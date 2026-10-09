using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using eldenRingApi.Contracts;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;

namespace eldenRingApi.Services;

public abstract class MongoCollectionReadService(IMongoDatabase database)
{
    protected async Task<EntityPage> GetPageAsync(string collectionName, CollectionQuery query, CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(collectionName);
        var total = await collection.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: cancellationToken);
        var filter = FilterDefinition<BsonDocument>.Empty;

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            filter = Builders<BsonDocument>.Filter.Regex("name", new BsonRegularExpression(Regex.Escape(query.Name), "i"));
        }

        var documents = await collection.Find(filter)
            .Skip(query.Page * query.Limit)
            .Limit(query.Limit)
            .ToListAsync(cancellationToken);
        return new EntityPage(total, documents.Select(ToApiDocument).ToList());
    }

    protected async Task<JsonObject?> GetByIdAsync(string collectionName, string id, CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(collectionName);
        var document = await collection.Find(Builders<BsonDocument>.Filter.Eq("_id", id)).FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : ToApiDocument(document);
    }

    private static JsonObject ToApiDocument(BsonDocument document)
    {
        var json = JsonNode.Parse(document.ToJson(new JsonWriterSettings { OutputMode = JsonOutputMode.RelaxedExtendedJson }))!.AsObject();
        var id = json["_id"];
        json.Remove("_id");
        json["id"] = id;
        return json;
    }
}
