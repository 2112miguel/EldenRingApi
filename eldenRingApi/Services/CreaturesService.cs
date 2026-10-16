using System.Text.Json.Nodes;
using eldenRingApi.Contracts;
using eldenRingApi.Interfaces;
using MongoDB.Driver;

namespace eldenRingApi.Services;

public sealed class CreaturesService(
    IMongoDatabase database,
    IRecordRelationshipResolver relationshipResolver)
    : MongoCollectionReadService(database, relationshipResolver), ICreaturesService
{
    public Task<EntityPage> GetAllAsync(CollectionQuery query, CancellationToken ct) =>
        GetPageAsync("creatures", query, ct);

    public Task<JsonObject?> GetByIdAsync(string id, CancellationToken ct) =>
        base.GetByIdAsync("creatures", id, ct);
}