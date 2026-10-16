using System.Text.Json.Nodes;
using eldenRingApi.Contracts;
using eldenRingApi.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;

namespace eldenRingApi.Controllers;

[ApiController]
[Route("api/classes")]
[Authorize(Policy = "AdminWrite")]
public sealed class ClassProfilesController(IMongoDatabase database, IRecordRelationshipResolver relationshipResolver) : ControllerBase
{
    private static readonly HashSet<string> EquipmentCollections = new(StringComparer.OrdinalIgnoreCase)
    {
        "weapons", "shields", "armors", "ammos", "sorceries", "incantations", "items"
    };

    [HttpPost("profile")]
    public async Task<IActionResult> Create([FromBody] ClassProfileRequest request, CancellationToken cancellationToken)
    {
        var error = await ValidateAsync(request, requireNameAndStats: true, cancellationToken);
        if (error is not null) return BadRequest(new { success = false, error });

        var id = ObjectId.GenerateNewId().ToString();
        var document = BuildDocument(id, request, null);
        await Classes.InsertOneAsync(document, cancellationToken: cancellationToken);
        relationshipResolver.Invalidate();
        return Created($"/api/classes/{id}", new { success = true, id, data = ToApiDocument(document) });
    }

    [HttpPatch("{id}/profile")]
    public async Task<IActionResult> Update(string id, [FromBody] ClassProfileRequest request, CancellationToken cancellationToken)
    {
        var existing = await Classes.Find(Builders<BsonDocument>.Filter.Eq("_id", id)).FirstOrDefaultAsync(cancellationToken);
        if (existing is null) return NotFound(new { success = false, error = "Document not found." });
        if (request.Stats is null && request.StartingEquipment is null)
            return BadRequest(new { success = false, error = "Provide stats and/or startingEquipment." });

        var error = await ValidateAsync(request, requireNameAndStats: false, cancellationToken);
        if (error is not null) return BadRequest(new { success = false, error });

        var updates = new List<UpdateDefinition<BsonDocument>>
        {
            Builders<BsonDocument>.Update.Set("updatedAt", DateTime.UtcNow)
        };
        if (request.Stats is not null) updates.Add(Builders<BsonDocument>.Update.Set("stats", StatsDocument(request.Stats)));
        if (request.StartingEquipment is not null) updates.Add(Builders<BsonDocument>.Update.Set("startingEquipment", EquipmentDocument(request.StartingEquipment)));

        await Classes.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", id), Builders<BsonDocument>.Update.Combine(updates), cancellationToken: cancellationToken);
        return Ok(new { success = true, id, updatedFields = updates.Count - 1 });
    }

    private IMongoCollection<BsonDocument> Classes => database.GetCollection<BsonDocument>("classes");

    private async Task<string?> ValidateAsync(ClassProfileRequest request, bool requireNameAndStats, CancellationToken cancellationToken)
    {
        if (requireNameAndStats && string.IsNullOrWhiteSpace(request.Name)) return "A class name is required.";
        if (requireNameAndStats && request.Stats is null) return "Stats are required.";
        if (request.Stats is not null && !AreValid(request.Stats)) return "Every stat must be a positive integer.";
        if (request.StartingEquipment is null) return null;

        foreach (var (group, entries) in request.StartingEquipment)
        {
            if (string.IsNullOrWhiteSpace(group) || entries is null) return "Each equipment group must be valid.";
            foreach (var entry in entries)
            {
                if (entry.Collection is null || entry.Id is null || !EquipmentCollections.Contains(entry.Collection) || string.IsNullOrWhiteSpace(entry.Id) || entry.Quantity < 1)
                    return "Every equipment reference requires a valid collection, id, and positive quantity.";
                var exists = await database.GetCollection<BsonDocument>(entry.Collection.ToLowerInvariant())
                    .Find(Builders<BsonDocument>.Filter.Eq("_id", entry.Id))
                    .AnyAsync(cancellationToken);
                if (!exists) return $"Starting equipment was not found: {entry.Collection}/{entry.Id}.";
            }
        }
        return null;
    }

    private static bool AreValid(StartingClassStats stats) =>
        stats.Level > 0 && stats.Vigor > 0 && stats.Mind > 0 && stats.Endurance > 0 &&
        stats.Strength > 0 && stats.Dexterity > 0 && stats.Intelligence > 0 && stats.Faith > 0 && stats.Arcane > 0;

    private static BsonDocument BuildDocument(string id, ClassProfileRequest request, BsonValue? createdAt) => new()
    {
        { "_id", id }, { "name", request.Name!.Trim() },
        { "description", string.IsNullOrWhiteSpace(request.Description) ? BsonNull.Value : new BsonString(request.Description.Trim()) },
        { "image", string.IsNullOrWhiteSpace(request.Image) ? BsonNull.Value : new BsonString(request.Image.Trim()) },
        { "stats", StatsDocument(request.Stats!) },
        { "startingEquipment", EquipmentDocument(request.StartingEquipment ?? []) },
        { "isBaseGameStartingClass", request.IsBaseGameStartingClass ?? false },
        { "contentSource", request.ContentSource ?? "" }, { "gameVersion", request.GameVersion ?? "" },
        { "createdAt", createdAt ?? BsonDateTime.Create(DateTime.UtcNow) }, { "updatedAt", DateTime.UtcNow }
    };

    private static BsonDocument StatsDocument(StartingClassStats stats) => new()
    {
        { "level", stats.Level }, { "vigor", stats.Vigor }, { "mind", stats.Mind }, { "endurance", stats.Endurance },
        { "strength", stats.Strength }, { "dexterity", stats.Dexterity }, { "intelligence", stats.Intelligence }, { "faith", stats.Faith }, { "arcane", stats.Arcane }
    };

    private static BsonDocument EquipmentDocument(Dictionary<string, List<StartingEquipmentReference>> equipment) =>
        new(equipment.Select(group => new BsonElement(
            group.Key,
            new BsonArray(group.Value.Select(entry => new BsonDocument
            {
                { "collection", entry.Collection!.Trim().ToLowerInvariant() },
                { "id", entry.Id!.Trim() },
                { "quantity", entry.Quantity }
            })))));

    private static JsonObject ToApiDocument(BsonDocument document)
    {
        var json = JsonNode.Parse(document.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson }))!.AsObject();
        json["id"] = json["_id"]?.DeepClone();
        json.Remove("_id");
        return json;
    }
}
