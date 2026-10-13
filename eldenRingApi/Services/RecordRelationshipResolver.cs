using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using eldenRingApi.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Bson;
using MongoDB.Driver;

namespace eldenRingApi.Services;

/// <summary>Converts legacy location/drop text into safe, navigable API references.</summary>
public sealed class RecordRelationshipResolver(IMongoDatabase database, IMemoryCache cache) : IRecordRelationshipResolver
{
    private const string CacheKey = "record-relationship-index";
    private static readonly string[] RelatedCollections =
    [
        "bosses", "creatures", "weapons", "ammos", "armors", "shields", "talismans",
        "items", "spirits", "ashes", "sorceries", "incantations", "locations"
    ];
    private static readonly Regex CurrencyPattern = new(@"^\s*(?:\d[\d\s,.]*|\?+)\s+runes?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex QuotedValuePattern = new("(?:\"(?<double>(?:\\\\.|[^\"])*)\"|'(?<single>(?:\\\\.|[^'])*)')", RegexOptions.CultureInvariant);

    public async Task PopulateAsync(IEnumerable<JsonObject> records, CancellationToken cancellationToken)
    {
        var index = await GetIndexAsync(cancellationToken);
        foreach (var record in records) Populate(record, index);
    }

    public async Task PopulateAsync(JsonObject record, CancellationToken cancellationToken) =>
        Populate(record, await GetIndexAsync(cancellationToken));

    public void Invalidate() => cache.Remove(CacheKey);

    private async Task<RelationshipIndex> GetIndexAsync(CancellationToken cancellationToken)
    {
        var cached = await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            var indexes = await Task.WhenAll(RelatedCollections.Select(collection => LoadCollectionAsync(collection, cancellationToken)));
            return new RelationshipIndex(indexes.SelectMany(values => values));
        });
        return cached!;
    }

    private async Task<IEnumerable<RelatedRecord>> LoadCollectionAsync(string collectionName, CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(collectionName);
        var projection = Builders<BsonDocument>.Projection.Include("_id").Include("name");
        var documents = await collection.Find(FilterDefinition<BsonDocument>.Empty)
            .Project<BsonDocument>(projection)
            .ToListAsync(cancellationToken);
        return documents
            .Where(document => document.TryGetValue("_id", out var id) && id.IsString && document.TryGetValue("name", out var name) && name.IsString)
            .Select(document => new RelatedRecord(collectionName, document["_id"].AsString, document["name"].AsString));
    }

    private static void Populate(JsonObject record, RelationshipIndex index)
    {
        if (!record.ContainsKey("guideUrl"))
            record["guideUrl"] = ReadString(record["fextralifePageUrl"]);

        if (record.TryGetPropertyValue("location", out var locationValue))
        {
            var locationName = ReadString(locationValue);
            record["location"] = locationName is not null && index.TryFindLocation(locationName, out var location)
                ? ToJsonReference(location)
                : null;
        }

        if (record.TryGetPropertyValue("drops", out var dropsValue))
        {
            var references = new JsonArray();
            foreach (var dropName in ReadDropNames(dropsValue))
            {
                if (CurrencyPattern.IsMatch(dropName))
                {
                    references.Add(new JsonObject { ["collection"] = "items", ["name"] = dropName, ["kind"] = "currency" });
                }
                else if (index.TryFindRecord(dropName, out var drop))
                {
                    references.Add(ToJsonReference(drop));
                }
            }
            record["drops"] = references;
        }
    }

    private static JsonObject ToJsonReference(RelatedRecord record) => new()
    {
        ["id"] = record.Id,
        ["collection"] = record.Collection,
        ["name"] = record.Name,
        ["kind"] = "record"
    };

    private static string? ReadString(JsonNode? node) => node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var value) ? value : null;

    private static IEnumerable<string> ReadDropNames(JsonNode? node)
    {
        if (node is JsonArray array)
            return array.Select(ReadString).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim());

        var text = ReadString(node)?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return [];
        if (!text.StartsWith('[') || !text.EndsWith(']')) return [text];

        var matches = QuotedValuePattern.Matches(text);
        return matches.Count == 0
            ? []
            : matches.Select(match => match.Groups["double"].Success ? match.Groups["double"].Value : match.Groups["single"].Value)
                .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim());
    }

    private static string Normalize(string value) => Regex.Replace(value.Trim(), "\\s+", " ");

    private sealed record RelatedRecord(string Collection, string Id, string Name);

    private sealed class RelationshipIndex(IEnumerable<RelatedRecord> records)
    {
        private readonly Dictionary<string, RelatedRecord> _locations = records
            .Where(record => record.Collection == "locations")
            .GroupBy(record => Normalize(record.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, RelatedRecord> _records = records
            .GroupBy(record => Normalize(record.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        public bool TryFindLocation(string name, out RelatedRecord record) => _locations.TryGetValue(Normalize(name), out record!);
        public bool TryFindRecord(string name, out RelatedRecord record) => _records.TryGetValue(Normalize(name), out record!);
    }
}
