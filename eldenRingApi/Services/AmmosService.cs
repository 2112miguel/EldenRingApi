using System.Text.Json.Nodes;
using eldenRingApi.Contracts;
using eldenRingApi.Interfaces;
using MongoDB.Driver;

namespace eldenRingApi.Services;

public sealed class AmmosService(
    IMongoDatabase database,
    IRecordRelationshipResolver relationshipResolver)
    : MongoCollectionReadService(database, relationshipResolver), IAmmosService
{
    public Task<EntityPage> GetAllAsync(CollectionQuery query, CancellationToken ct) =>
        GetPageAsync("ammos", query, ct);

    public Task<JsonObject?> GetByIdAsync(string id, CancellationToken ct) =>
        base.GetByIdAsync("ammos", id, ct);
}