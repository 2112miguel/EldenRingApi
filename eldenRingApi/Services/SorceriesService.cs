using System.Text.Json.Nodes;
using eldenRingApi.Contracts;
using eldenRingApi.Interfaces;
using MongoDB.Driver;
namespace eldenRingApi.Services;
public sealed class SorceriesService(IMongoDatabase database) : MongoCollectionReadService(database), ISorceriesService { public Task<EntityPage> GetAllAsync(CollectionQuery query, CancellationToken ct) => GetPageAsync("sorceries", query, ct); public Task<JsonObject?> GetByIdAsync(string id, CancellationToken ct) => base.GetByIdAsync("sorceries", id, ct); }
