using System.Text.Json.Nodes;
using eldenRingApi.Contracts;
using eldenRingApi.Interfaces;
using MongoDB.Driver;

namespace eldenRingApi.Services;

public sealed class WeaponsService(IMongoDatabase database) : MongoCollectionReadService(database), IWeaponsService
{
    public Task<EntityPage> GetAllAsync(CollectionQuery query, CancellationToken cancellationToken) =>
        GetPageAsync("weapons", query, cancellationToken);

    public Task<JsonObject?> GetByIdAsync(string id, CancellationToken cancellationToken) =>
        base.GetByIdAsync("weapons", id, cancellationToken);
}
