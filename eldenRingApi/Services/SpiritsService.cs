using System.Text.Json.Nodes;
using eldenRingApi.Contracts;
using eldenRingApi.Interfaces;
using MongoDB.Driver;
namespace eldenRingApi.Services;
public sealed class SpiritsService(IMongoDatabase database, IRecordRelationshipResolver relationshipResolver) : MongoCollectionReadService(database, relationshipResolver), ISpiritsService { public Task<EntityPage> GetAllAsync(CollectionQuery query, CancellationToken ct) => GetPageAsync("spirits", query, ct); public Task<JsonObject?> GetByIdAsync(string id, CancellationToken ct) => base.GetByIdAsync("spirits", id, ct); }
