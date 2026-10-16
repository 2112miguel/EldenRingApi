using System.Text.Json.Nodes;
using eldenRingApi.Contracts;
using eldenRingApi.Interfaces;
using MongoDB.Driver;

namespace eldenRingApi.Services;

public sealed class ItemsService(
    IMongoDatabase database,
    IRecordRelationshipResolver relationshipResolver)
    : MongoCollectionReadService(database, relationshipResolver), IItemsService
{
    public Task<EntityPage> GetAllAsync(CollectionQuery query, CancellationToken ct) =>
        GetPageAsync("items", query, ct);

    public Task<JsonObject?> GetByIdAsync(string id, CancellationToken ct) =>
        base.GetByIdAsync("items", id, ct);
}