using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;

namespace eldenRingApi.Controllers;

/// <summary>Admin-only partial updates for existing game content.</summary>
[ApiController]
[Route("api")]
[Authorize(Policy = "AdminWrite")]
public sealed class ContentUpdatesController(IMongoDatabase database) : ControllerBase
{
    private static readonly HashSet<string> AllowedCollections = new(StringComparer.OrdinalIgnoreCase)
    {
        "ammos", "armors", "ashes", "bosses", "classes", "creatures", "incantations",
        "items", "locations", "npcs", "shields", "sorceries", "spirits", "talismans", "weapons"
    };

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
            if (string.IsNullOrWhiteSpace(field) || field is "id" or "_id" || field.Contains('.') || field.StartsWith('$'))
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

        return Ok(new { success = true, id, modified = result.ModifiedCount > 0, updatedFields = changes.Select(field => field.Key) });
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
}
