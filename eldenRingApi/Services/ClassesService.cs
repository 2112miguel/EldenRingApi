using System.Text.Json.Nodes;
using eldenRingApi.Contracts;
using eldenRingApi.Interfaces;
using MongoDB.Driver;
namespace eldenRingApi.Services;
public sealed class ClassesService(IMongoDatabase database) : MongoCollectionReadService(database), IClassesService { public Task<EntityPage> GetAllAsync(CollectionQuery query, CancellationToken ct) => GetPageAsync("classes", query, ct); public Task<JsonObject?> GetByIdAsync(string id, CancellationToken ct) => base.GetByIdAsync("classes", id, ct); }
