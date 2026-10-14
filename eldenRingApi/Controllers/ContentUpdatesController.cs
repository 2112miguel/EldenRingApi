using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Claims;
using eldenRingApi.Contracts;
using eldenRingApi.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;

namespace eldenRingApi.Controllers;

/// <summary>Admin-only creation and partial updates for game content.</summary>
[ApiController]
[Route("api")]
[Authorize(Policy = "AdminWrite")]
public sealed class ContentUpdatesController(IMongoDatabase database, IRecordRelationshipResolver relationshipResolver) : ControllerBase
{
    private static readonly HashSet<string> AllowedCollections = new(StringComparer.OrdinalIgnoreCase)
    {
        "ammos", "armors", "ashes", "bosses", "classes", "creatures", "incantations",
        "items", "locations", "npcs", "shields", "sorceries", "spirits", "talismans", "weapons"
    };

    [HttpPost("{collection}")]
    public async Task<IActionResult> Create(string collection, [FromBody] JsonObject? payload, CancellationToken cancellationToken)
    {
        if (!AllowedCollections.Contains(collection))
            return NotFound(new { success = false, error = "Collection not found." });
        if (payload is null || payload.Count == 0)
            return BadRequest(new { success = false, error = "Provide the record fields to create." });
        if (payload.ContainsKey("id") || payload.ContainsKey("_id"))
            return BadRequest(new { success = false, error = "The server generates the id." });
        if (payload.ContainsKey("location") || payload.ContainsKey("drops"))
            return BadRequest(new { success = false, error = "location and drops are managed through relationship endpoints." });

        var document = BsonDocument.Parse(payload.ToJsonString());
        if (!document.TryGetValue("name", out var name) || !name.IsString || string.IsNullOrWhiteSpace(name.AsString))
            return BadRequest(new { success = false, error = "A non-empty name is required." });
        if (HasUnsafeFieldName(document))
            return BadRequest(new { success = false, error = "Field names cannot contain '.' or begin with '$'." });

        var id = ObjectId.GenerateNewId().ToString();
        document.InsertAt(0, new BsonElement("_id", id));
        document["createdAt"] = DateTime.UtcNow;
        document["updatedAt"] = DateTime.UtcNow;

        await database.GetCollection<BsonDocument>(collection.ToLowerInvariant())
            .InsertOneAsync(document, cancellationToken: cancellationToken);
        relationshipResolver.Invalidate();

        var result = ToApiDocument(document);
        await relationshipResolver.PopulateAsync(result, collection, cancellationToken);
        return Created($"/api/{collection.ToLowerInvariant()}/{id}", new { success = true, id, data = result });
    }

    [HttpPatch("{collection}/{id}")]
    public async Task<IActionResult> Update(string collection, string id, [FromBody] JsonObject? changes, CancellationToken cancellationToken)
    {
        if (!AllowedCollections.Contains(collection))
            return NotFound(new { success = false, error = "Collection not found." });

        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { success = false, error = "An id is required." });

        if (changes is null || changes.Count == 0)
            return BadRequest(new { success = false, error = "Provide at least one scalar field to update." });

        var updates = new List<UpdateDefinition<BsonDocument>>();
        foreach (var (field, value) in changes)
        {
            if (string.IsNullOrWhiteSpace(field) || field is "id" or "_id" or "location" or "drops" || field.Contains('.') || field.StartsWith('$'))
                return BadRequest(new { success = false, error = $"'{field}' cannot be updated." });

            if (value is JsonArray or JsonObject)
                return BadRequest(new { success = false, error = $"'{field}' is a list or object. Only scalar fields can be updated." });

            updates.Add(Builders<BsonDocument>.Update.Set(field, ToBsonValue(value)));
        }

        var mongoCollection = database.GetCollection<BsonDocument>(collection.ToLowerInvariant());
        var result = await mongoCollection.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", id),
            Builders<BsonDocument>.Update.Combine(updates),
            cancellationToken: cancellationToken);

        if (result.MatchedCount == 0)
            return NotFound(new { success = false, error = "Document not found." });

        if (changes.ContainsKey("name")) relationshipResolver.Invalidate();
        return Ok(new { success = true, id, modified = result.ModifiedCount > 0, updatedFields = changes.Select(field => field.Key) });
    }

    [HttpDelete("{collection}/{id}")]
    [EnableRateLimiting("record-delete")]
    public async Task<IActionResult> Delete(string collection, string id, [FromBody] DeleteRecordRequest? request, CancellationToken cancellationToken)
    {
        if (!AllowedCollections.Contains(collection))
            return NotFound(new { success = false, error = "Collection not found." });
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { success = false, error = "An id is required." });

        var mongoCollection = database.GetCollection<BsonDocument>(collection.ToLowerInvariant());
        var record = await mongoCollection.Find(Builders<BsonDocument>.Filter.Eq("_id", id)).FirstOrDefaultAsync(cancellationToken);
        if (record is null)
            return NotFound(new { success = false, error = "Document not found." });

        var expectedConfirmation = $"DELETE {record.GetValue("name", string.Empty).AsString} [{id}]";
        if (!string.Equals(request?.Confirmation, expectedConfirmation, StringComparison.Ordinal))
            return BadRequest(new { success = false, error = "Confirmation must exactly match the requested record." });

        var normalizedCollection = collection.ToLowerInvariant();
        var referenceFilter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.And(Builders<BsonDocument>.Filter.Eq("subject.collection", normalizedCollection), Builders<BsonDocument>.Filter.Eq("subject.id", id)),
            Builders<BsonDocument>.Filter.And(Builders<BsonDocument>.Filter.Eq("sections.references.collection", normalizedCollection), Builders<BsonDocument>.Filter.Eq("sections.references.id", id)),
            Builders<BsonDocument>.Filter.And(Builders<BsonDocument>.Filter.Eq("sections.subsections.references.collection", normalizedCollection), Builders<BsonDocument>.Filter.Eq("sections.subsections.references.id", id)),
            Builders<BsonDocument>.Filter.And(Builders<BsonDocument>.Filter.Eq("supplements.references.collection", normalizedCollection), Builders<BsonDocument>.Filter.Eq("supplements.references.id", id)));
        if (await database.GetCollection<BsonDocument>("guides").Find(referenceFilter).AnyAsync(cancellationToken))
            return Conflict(new { success = false, error = "This record is referenced by a guide and cannot be deleted." });

        var deletedBy = User.FindFirstValue(ClaimTypes.Email) ?? "unknown";
        var archive = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId().ToString() },
            { "originalCollection", normalizedCollection },
            { "originalId", id },
            { "deletedAt", DateTime.UtcNow },
            { "deletedBy", deletedBy },
            { "record", record.DeepClone().AsBsonDocument }
        };
        await database.GetCollection<BsonDocument>("deleted_records").InsertOneAsync(archive, cancellationToken: cancellationToken);

        var result = await mongoCollection.DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", id), cancellationToken);
        if (result.DeletedCount != 1)
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, error = "The record was archived but could not be deleted. No retry was performed." });

        relationshipResolver.Invalidate();
        return Ok(new { success = true, id, collection = normalizedCollection, deleted = true, archived = true });
    }

    private static BsonValue ToBsonValue(JsonNode? value)
    {
        if (value is null) return BsonNull.Value;

        var element = JsonSerializer.Deserialize<JsonElement>(value.ToJsonString());
        return element.ValueKind switch
        {
            JsonValueKind.String => new BsonString(element.GetString()!),
            JsonValueKind.True => BsonBoolean.True,
            JsonValueKind.False => BsonBoolean.False,
            JsonValueKind.Number when element.TryGetInt32(out var intValue) => new BsonInt32(intValue),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => new BsonInt64(longValue),
            JsonValueKind.Number => new BsonDouble(element.GetDouble()),
            JsonValueKind.Null => BsonNull.Value,
            _ => throw new InvalidOperationException("Only scalar JSON values are supported.")
        };
    }

    private static bool HasUnsafeFieldName(BsonValue value) => value switch
    {
        BsonDocument document => document.Elements.Any(element => element.Name.Contains('.') || element.Name.StartsWith('$') || HasUnsafeFieldName(element.Value)),
        BsonArray array => array.Any(HasUnsafeFieldName),
        _ => false
    };

    private static JsonObject ToApiDocument(BsonDocument document)
    {
        var json = JsonNode.Parse(document.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson }))!.AsObject();
        var id = json["_id"];
        json.Remove("_id");
        json["id"] = id;
        return json;
    }
}
