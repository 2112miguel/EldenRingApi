using System.Text.Json.Nodes;
using eldenRingApi.Contracts;
using eldenRingApi.Interfaces;
using MongoDB.Driver;
namespace eldenRingApi.Services;
public sealed class IncantationsService(IMongoDatabase database, IRecordRelationshipResolver relationshipResolver) : MongoCollectionReadService(database, relationshipResolver), IIncantationsService { public Task<EntityPage> GetAllAsync(CollectionQuery query, CancellationToken ct) => GetPageAsync("incantations", query, ct); public Task<JsonObject?> GetByIdAsync(string id, CancellationToken ct) => base.GetByIdAsync("incantations", id, ct); }
