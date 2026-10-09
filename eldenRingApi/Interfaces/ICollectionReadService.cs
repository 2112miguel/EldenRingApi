using System.Text.Json.Nodes;
using eldenRingApi.Contracts;

namespace eldenRingApi.Interfaces;

public interface ICollectionReadService
{
    Task<EntityPage> GetAllAsync(CollectionQuery query, CancellationToken cancellationToken);
    Task<JsonObject?> GetByIdAsync(string id, CancellationToken cancellationToken);
}
