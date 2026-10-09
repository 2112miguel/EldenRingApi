using System.Text.Json.Nodes;

namespace eldenRingApi.Contracts;

public sealed record EntityPage(long Total, IReadOnlyList<JsonObject> Items);
