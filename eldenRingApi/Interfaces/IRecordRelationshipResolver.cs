using System.Text.Json.Nodes;

namespace eldenRingApi.Interfaces;

public interface IRecordRelationshipResolver
{
    Task PopulateAsync(IEnumerable<JsonObject> records, CancellationToken cancellationToken);
    Task PopulateAsync(JsonObject record, CancellationToken cancellationToken);
    void Invalidate();
}
