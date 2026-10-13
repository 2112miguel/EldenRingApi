using System.Text.Json.Nodes;
using eldenRingApi.Contracts;
using eldenRingApi.Interfaces;
using MongoDB.Driver;
namespace eldenRingApi.Services;
public sealed class TalismansService(IMongoDatabase database, IRecordRelationshipResolver relationshipResolver) : MongoCollectionReadService(database, relationshipResolver), ITalismansService { public Task<EntityPage> GetAllAsync(CollectionQuery query, CancellationToken ct) => GetPageAsync("talismans", query, ct); public Task<JsonObject?> GetByIdAsync(string id, CancellationToken ct) => base.GetByIdAsync("talismans", id, ct); }
