using System.Text.Json.Nodes;

namespace eldenRingApi.Interfaces;

public interface IRecordRelationshipResolver
{
    Task PopulateAsync(IEnumerable<JsonObject> records, string collectionName, CancellationToken cancellationToken);
    Task PopulateAsync(JsonObject record, string collectionName, CancellationToken cancellationToken);
    void Invalidate();
}
