using System.Text.Json.Nodes;
using eldenRingApi.Contracts;
using eldenRingApi.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;

namespace eldenRingApi.Controllers;

[ApiController, Route("api/guides")]
public sealed class GuidesController(IMongoDatabase database, IRecordRelationshipResolver relationshipResolver) : ControllerBase
{
    private static readonly HashSet<string> ContentCollections = new(StringComparer.OrdinalIgnoreCase)
    { "ammos", "armors", "ashes", "bosses", "classes", "creatures", "incantations", "items", "locations", "npcs", "shields", "sorceries", "spirits", "talismans", "weapons" };
    private static readonly HashSet<string> SupplementTypes = new(StringComparer.OrdinalIgnoreCase)
    { "tip", "warning", "mechanic", "lore", "npc-note", "checklist", "external-link" };

    [HttpGet("{collection}/{recordId}")]
    public async Task<IActionResult> GetPublished(string collection, string recordId, CancellationToken cancellationToken)
    {
        var guide = await Guides.Find(PublishedSubjectFilter(collection, recordId)).FirstOrDefaultAsync(cancellationToken);
        return guide is null ? NotFound(new { success = false }) : Ok(new { success = true, data = await ToEnrichedJsonAsync(guide, cancellationToken) });
    }

    [HttpGet("{guideId}")]
    public async Task<IActionResult> GetPublishedById(string guideId, CancellationToken cancellationToken)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", guideId),
            Builders<BsonDocument>.Filter.Eq("status", "published"));
        var guide = await Guides.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return guide is null ? NotFound(new { success = false }) : Ok(new { success = true, data = await ToEnrichedJsonAsync(guide, cancellationToken) });
    }

    [HttpGet]
    public async Task<IActionResult> GetPublishedList([FromQuery] string? status = "published", [FromQuery] int page = 0, [FromQuery] int limit = 12, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(status, "published", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { success = false, error = "Only published guides are available publicly." });
        if (page is < 0 or > 1_000_000) return BadRequest(new { success = false, error = "Page must be between 0 and 1000000." });
        if (limit is < 1 or > 100) return BadRequest(new { success = false, error = "Limit must be between 1 and 100." });

        var filter = Builders<BsonDocument>.Filter.Eq("status", "published");
        var total = await Guides.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        var guides = await Guides.Find(filter)
            .Sort(Builders<BsonDocument>.Sort.Descending("updatedAt"))
            .Skip(page * limit)
            .Limit(limit)
            .Project<BsonDocument>(Builders<BsonDocument>.Projection.Include("_id").Include("title").Include("summary").Include("status").Include("subject").Include("media").Include("updatedAt"))
            .ToListAsync(cancellationToken);

        var data = new List<object>();
        foreach (var guide in guides)
        {
            JsonObject? subjectPayload = null;
            if (guide.TryGetValue("subject", out var subjectValue) && subjectValue.IsBsonDocument)
            {
                var subject = subjectValue.AsBsonDocument;
                var collection = subject["collection"].AsString;
                var recordId = subject["id"].AsString;
                subjectPayload = new JsonObject { ["collection"] = collection, ["id"] = recordId };
                await EnrichReferenceAsync(subjectPayload, cancellationToken);
            }

            data.Add(new
            {
                id = guide["_id"].AsString,
                title = guide.GetValue("title", string.Empty).AsString,
                summary = guide.TryGetValue("summary", out var summary) && summary.IsString ? summary.AsString : null,
                status = "published",
                subject = subjectPayload,
                media = ToJsonArray(guide.GetValue("media", new BsonArray())),
                coverImage = GetCoverImage(guide),
                updatedAt = guide.GetValue("updatedAt", BsonDateTime.Create(DateTime.UnixEpoch)).ToUniversalTime()
            });
        }

        return Ok(new { success = true, page, limit, total, data });
    }

    // Drafts deliberately have no public route. This route is for the specialized admin editor.
    [Authorize(Policy = "AdminWrite"), HttpGet("admin/{id}")]
    public async Task<IActionResult> GetForAdmin(string id, CancellationToken cancellationToken)
    {
        var guide = await Guides.Find(Builders<BsonDocument>.Filter.Eq("_id", id)).FirstOrDefaultAsync(cancellationToken);
        return guide is null ? NotFound(new { success = false }) : Ok(new { success = true, data = ToJson(guide) });
    }

    [Authorize(Policy = "AdminWrite"), HttpGet("admin")]
    public async Task<IActionResult> GetForAdminList([FromQuery] string? status = "draft", [FromQuery] int page = 0, [FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        status = (status ?? "draft").Trim().ToLowerInvariant();
        if (status is not ("draft" or "published")) return BadRequest(new { success = false, error = "Status must be draft or published." });
        if (page is < 0 or > 1_000_000) return BadRequest(new { success = false, error = "Page must be between 0 and 1000000." });
        if (limit is < 1 or > 100) return BadRequest(new { success = false, error = "Limit must be between 1 and 100." });

        var filter = Builders<BsonDocument>.Filter.Eq("status", status);
        var total = await Guides.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        var guides = await Guides.Find(filter)
            .Sort(Builders<BsonDocument>.Sort.Descending("updatedAt"))
            .Skip(page * limit)
            .Limit(limit)
            .Project<BsonDocument>(Builders<BsonDocument>.Projection.Include("_id").Include("title").Include("status").Include("subject").Include("updatedAt"))
            .ToListAsync(cancellationToken);

        var data = new List<object>();
        foreach (var guide in guides)
        {
            object? subjectPayload = null;
            if (guide.TryGetValue("subject", out var subjectValue) && subjectValue.IsBsonDocument)
            {
                var subject = subjectValue.AsBsonDocument;
                var collection = subject["collection"].AsString;
                var recordId = subject["id"].AsString;
                var record = await database.GetCollection<BsonDocument>(collection)
                    .Find(Builders<BsonDocument>.Filter.Eq("_id", recordId))
                    .Project<BsonDocument>(Builders<BsonDocument>.Projection.Include("name"))
                    .FirstOrDefaultAsync(cancellationToken);
                subjectPayload = new { collection, id = recordId, name = record?.GetValue("name", string.Empty).AsString ?? string.Empty };
            }
            data.Add(new
            {
                id = guide["_id"].AsString,
                title = guide.GetValue("title", string.Empty).AsString,
                status = guide.GetValue("status", string.Empty).AsString,
                subject = subjectPayload,
                updatedAt = guide.GetValue("updatedAt", BsonDateTime.Create(DateTime.UnixEpoch)).ToUniversalTime()
            });
        }

        return Ok(new { success = true, page, limit, total, data });
    }

    [Authorize(Policy = "AdminWrite"), HttpPost]
    public async Task<IActionResult> Create([FromBody] GuideRequest request, CancellationToken cancellationToken)
    {
        var error = await ValidateAsync(request, null, cancellationToken);
        if (error is not null) return BadRequest(new { success = false, error });
        var id = ObjectId.GenerateNewId().ToString();
        var document = ToDocument(id, request, null);
        await Guides.InsertOneAsync(document, cancellationToken: cancellationToken);
        relationshipResolver.Invalidate();
        var resourceUrl = request.Subject is null
            ? $"/api/guides/admin/{id}"
            : $"/api/guides/{request.Subject.Collection!.ToLowerInvariant()}/{request.Subject.Id}";
        return Created(resourceUrl, new { success = true, id, data = await ToEnrichedJsonAsync(document, cancellationToken) });
    }

    [Authorize(Policy = "AdminWrite"), HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] GuideRequest request, CancellationToken cancellationToken)
    {
        var existing = await Guides.Find(Builders<BsonDocument>.Filter.Eq("_id", id)).FirstOrDefaultAsync(cancellationToken);
        if (existing is null) return NotFound(new { success = false });
        var error = await ValidateAsync(request, id, cancellationToken);
        if (error is not null) return BadRequest(new { success = false, error });
        var replacement = ToDocument(id, request, existing.GetValue("createdAt", BsonDateTime.Create(DateTime.UtcNow)));
        await Guides.ReplaceOneAsync(Builders<BsonDocument>.Filter.Eq("_id", id), replacement, cancellationToken: cancellationToken);
        relationshipResolver.Invalidate();
        return Ok(new { success = true, id, data = await ToEnrichedJsonAsync(replacement, cancellationToken) });
    }

    private IMongoCollection<BsonDocument> Guides => database.GetCollection<BsonDocument>("guides");
    private static FilterDefinition<BsonDocument> PublishedSubjectFilter(string collection, string id) => Builders<BsonDocument>.Filter.And(
        Builders<BsonDocument>.Filter.Eq("subject.collection", collection.ToLowerInvariant()), Builders<BsonDocument>.Filter.Eq("subject.id", id), Builders<BsonDocument>.Filter.Eq("status", "published"));

    private async Task<string?> ValidateAsync(GuideRequest request, string? currentGuideId, CancellationToken cancellationToken)
    {
        if (request.Subject is not null &&
            (request.Subject.Collection is null || request.Subject.Id is null || !ContentCollections.Contains(request.Subject.Collection) || string.IsNullOrWhiteSpace(request.Subject.Id)))
            return "Subject must contain a valid collection and id when provided.";
        if (string.IsNullOrWhiteSpace(request.Title)) return "A title is required.";
        var status = (request.Status ?? "draft").Trim().ToLowerInvariant();
        if (status is not ("draft" or "published")) return "Status must be draft or published.";
        if (request.Sections?.GroupBy(section => section.Order).Any(group => group.Key < 1 || group.Count() > 1) == true) return "Section order values must be positive and unique.";
        if (request.Sections?.Any(section => section.Subsections?.GroupBy(subsection => subsection.Order).Any(group => group.Key < 1 || group.Count() > 1) == true) == true)
            return "Subsection order values must be positive and unique within a section.";
        if (request.Sections?.Any(section => section.Subsections?.Any(subsection => string.IsNullOrWhiteSpace(subsection.Title) || string.IsNullOrWhiteSpace(subsection.Content)) == true) == true)
            return "Every subsection requires title and content.";
        if (request.Supplements?.Any(supplement =>
                string.IsNullOrWhiteSpace(supplement.Id) ||
                string.IsNullOrWhiteSpace(supplement.Title) ||
                string.IsNullOrWhiteSpace(supplement.Content) ||
                string.IsNullOrWhiteSpace(supplement.Type) ||
                !SupplementTypes.Contains(supplement.Type)) == true)
            return "Every supplement requires id, title, content, and a valid type.";
        if (request.Supplements?.GroupBy(supplement => supplement.Id!, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1) == true)
            return "Supplement id values must be unique within a guide.";
        var references = (request.Subject is null ? Enumerable.Empty<GuideReference>() : new[] { request.Subject })
            .Concat((request.Sections ?? []).SelectMany(section => section.References ?? []))
            .Concat((request.Sections ?? []).SelectMany(section => section.Subsections ?? []).SelectMany(subsection => subsection.References ?? []))
            .Concat((request.Supplements ?? []).SelectMany(supplement => supplement.References ?? []));
        foreach (var reference in references)
        {
            if (reference.Collection is null || reference.Id is null || !ContentCollections.Contains(reference.Collection) || string.IsNullOrWhiteSpace(reference.Id)) return "Each reference requires a valid collection and id.";
            var exists = await database.GetCollection<BsonDocument>(reference.Collection.ToLowerInvariant()).Find(Builders<BsonDocument>.Filter.Eq("_id", reference.Id)).AnyAsync(cancellationToken);
            if (!exists) return $"Referenced record was not found: {reference.Collection}/{reference.Id}.";
        }
        if (request.Subject is not null)
        {
            var duplicateFilter = Builders<BsonDocument>.Filter.And(Builders<BsonDocument>.Filter.Eq("subject.collection", request.Subject.Collection!.ToLowerInvariant()), Builders<BsonDocument>.Filter.Eq("subject.id", request.Subject.Id));
            if (currentGuideId is not null) duplicateFilter &= Builders<BsonDocument>.Filter.Ne("_id", currentGuideId);
            if (await Guides.Find(duplicateFilter).AnyAsync(cancellationToken)) return "A guide already exists for this record.";
        }
        return null;
    }

    private static BsonDocument ToDocument(string id, GuideRequest request, BsonValue? createdAt) => new()
    {
        { "_id", id }, { "schemaVersion", 1 },
        { "subject", request.Subject is null ? BsonNull.Value : ReferenceDocument(request.Subject) }, { "title", request.Title!.Trim() },
        { "summary", request.Summary is null ? BsonNull.Value : new BsonString(request.Summary) }, { "status", (request.Status ?? "draft").Trim().ToLowerInvariant() },
        { "sections", new BsonArray((request.Sections ?? []).OrderBy(section => section.Order).Select(section => new BsonDocument
            {
                { "order", section.Order }, { "title", string.IsNullOrWhiteSpace(section.Title) ? BsonNull.Value : new BsonString(section.Title.Trim()) },
                { "content", string.IsNullOrWhiteSpace(section.Content) ? BsonNull.Value : new BsonString(section.Content.Trim()) },
                { "references", new BsonArray((section.References ?? []).Select(ReferenceDocument)) },
                { "subsections", new BsonArray((section.Subsections ?? []).OrderBy(subsection => subsection.Order).Select(subsection => new BsonDocument
                    {
                        { "order", subsection.Order }, { "title", subsection.Title! }, { "content", subsection.Content! },
                        { "references", new BsonArray((subsection.References ?? []).Select(ReferenceDocument)) }
                    })) }
            })) },
        { "supplements", new BsonArray((request.Supplements ?? []).Select(supplement => new BsonDocument
            {
                { "id", supplement.Id!.Trim() }, { "type", supplement.Type!.Trim().ToLowerInvariant() },
                { "title", supplement.Title!.Trim() }, { "content", supplement.Content!.Trim() },
                { "image", string.IsNullOrWhiteSpace(supplement.Image) ? BsonNull.Value : new BsonString(supplement.Image.Trim()) },
                { "references", new BsonArray((supplement.References ?? []).Select(ReferenceDocument)) }
            })) },
        { "media", new BsonArray((request.Media ?? []).Select(media => new BsonDocument { { "type", media.Type ?? "image" }, { "url", media.Url ?? string.Empty }, { "alt", media.Alt ?? string.Empty } })) },
        { "createdAt", createdAt ?? BsonDateTime.Create(DateTime.UtcNow) }, { "updatedAt", DateTime.UtcNow }
    };

    private static BsonDocument ReferenceDocument(GuideReference reference) => new() { { "collection", reference.Collection!.Trim().ToLowerInvariant() }, { "id", reference.Id!.Trim() } };

    private async Task<JsonObject> ToEnrichedJsonAsync(BsonDocument document, CancellationToken cancellationToken)
    {
        var guide = ToJson(document);
        await EnrichReferenceAsync(guide["subject"] as JsonObject, cancellationToken);

        if (guide["sections"] is JsonArray sections)
            foreach (var section in sections.OfType<JsonObject>())
            {
                await EnrichReferencesAsync(section["references"] as JsonArray, cancellationToken);
                if (section["subsections"] is JsonArray subsections)
                    foreach (var subsection in subsections.OfType<JsonObject>())
                        await EnrichReferencesAsync(subsection["references"] as JsonArray, cancellationToken);
            }
        if (guide["supplements"] is JsonArray supplements)
            foreach (var supplement in supplements.OfType<JsonObject>())
                await EnrichReferencesAsync(supplement["references"] as JsonArray, cancellationToken);

        guide["coverImage"] = GetCoverImage(document);
        return guide;
    }

    private async Task EnrichReferencesAsync(JsonArray? references, CancellationToken cancellationToken)
    {
        if (references is null) return;
        foreach (var reference in references.OfType<JsonObject>())
            await EnrichReferenceAsync(reference, cancellationToken);
    }

    private async Task EnrichReferenceAsync(JsonObject? reference, CancellationToken cancellationToken)
    {
        if (reference is null ||
            reference["collection"] is not JsonValue collectionValue ||
            reference["id"] is not JsonValue idValue ||
            !collectionValue.TryGetValue<string>(out var collection) ||
            !idValue.TryGetValue<string>(out var id) ||
            string.IsNullOrWhiteSpace(collection) || string.IsNullOrWhiteSpace(id) ||
            !ContentCollections.Contains(collection)) return;

        var record = await database.GetCollection<BsonDocument>(collection.ToLowerInvariant())
            .Find(Builders<BsonDocument>.Filter.Eq("_id", id))
            .Project<BsonDocument>(Builders<BsonDocument>.Projection.Include("name").Include("image"))
            .FirstOrDefaultAsync(cancellationToken);
        reference["name"] = record is not null && record.TryGetValue("name", out var name) && name.IsString ? name.AsString : null;
        reference["image"] = record is not null && record.TryGetValue("image", out var image) && image.IsString ? image.AsString : null;
    }

    private static string? GetCoverImage(BsonDocument guide)
    {
        if (!guide.TryGetValue("media", out var media) || !media.IsBsonArray) return null;
        return media.AsBsonArray
            .Where(value => value.IsBsonDocument)
            .Select(value => value.AsBsonDocument.GetValue("url", BsonNull.Value))
            .FirstOrDefault(url => url.IsString && !string.IsNullOrWhiteSpace(url.AsString))?.AsString;
    }

    private static JsonArray ToJsonArray(BsonValue value) => JsonNode.Parse(value.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson }))!.AsArray();
    private static JsonObject ToJson(BsonDocument document)
    {
        var json = JsonNode.Parse(document.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson }))!.AsObject();
        json["id"] = json["_id"]?.DeepClone();
        json.Remove("_id");
        SetIsoDate(json, document, "createdAt");
        SetIsoDate(json, document, "updatedAt");
        return json;
    }

    private static void SetIsoDate(JsonObject json, BsonDocument document, string field)
    {
        if (document.TryGetValue(field, out var value) && value.IsValidDateTime)
            json[field] = value.ToUniversalTime().ToString("O");
    }
}
