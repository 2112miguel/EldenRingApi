using System.Text.Json.Nodes;
using eldenRingApi.Contracts;
using eldenRingApi.Interfaces;
using MongoDB.Driver;

namespace eldenRingApi.Services;

public sealed class BossesService(IMongoDatabase database, IRecordRelationshipResolver relationshipResolver) : MongoCollectionReadService(database, relationshipResolver), IBossesService
{
    public Task<EntityPage> GetAllAsync(CollectionQuery query, CancellationToken cancellationToken) =>
        GetPageAsync("bosses", query, cancellationToken);

    public Task<JsonObject?> GetByIdAsync(string id, CancellationToken cancellationToken) =>
        base.GetByIdAsync("bosses", id, cancellationToken);
}
