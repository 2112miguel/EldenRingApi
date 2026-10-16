using System.Text.Json.Nodes;
using eldenRingApi.Contracts;
using eldenRingApi.Interfaces;
using MongoDB.Driver;

namespace eldenRingApi.Services;

public sealed class LocationsService(
    IMongoDatabase database,
    IRecordRelationshipResolver relationshipResolver)
    : MongoCollectionReadService(database, relationshipResolver), ILocationsService
{
    public Task<EntityPage> GetAllAsync(CollectionQuery query, CancellationToken ct) =>
        GetPageAsync("locations", query, ct);

    public Task<JsonObject?> GetByIdAsync(string id, CancellationToken ct) =>
        base.GetByIdAsync("locations", id, ct);
}