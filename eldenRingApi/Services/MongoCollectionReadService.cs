using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using eldenRingApi.Contracts;
using eldenRingApi.Interfaces;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;

namespace eldenRingApi.Services;

public abstract class MongoCollectionReadService(IMongoDatabase database, IRecordRelationshipResolver relationshipResolver)
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
        var items = documents.Select(ToApiDocument).ToList();
        await relationshipResolver.PopulateAsync(items, collectionName, cancellationToken);
        return new EntityPage(total, items);
    }

    protected async Task<JsonObject?> GetByIdAsync(string collectionName, string id, CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(collectionName);
        var document = await collection.Find(Builders<BsonDocument>.Filter.Eq("_id", id)).FirstOrDefaultAsync(cancellationToken);
        if (document is null) return null;
        var item = ToApiDocument(document);
        await relationshipResolver.PopulateAsync(item, collectionName, cancellationToken);
        return item;
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
