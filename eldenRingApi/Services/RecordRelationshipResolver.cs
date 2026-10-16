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
    private static readonly SemaphoreSlim IndexBuildLock = new(1, 1);
    private static readonly string[] RelatedCollections =
    [
        "bosses", "creatures", "weapons", "ammos", "armors", "shields", "talismans",
        "items", "spirits", "ashes", "sorceries", "incantations", "locations", "classes"
    ];
    private static readonly Regex CurrencyPattern = new(@"^\s*(?:\d[\d\s,.]*|\?+)\s+runes?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex QuotedValuePattern = new("(?:\"(?<double>(?:\\\\.|[^\"])*)\"|'(?<single>(?:\\\\.|[^'])*)')", RegexOptions.CultureInvariant);

    public async Task PopulateAsync(IEnumerable<JsonObject> records, string collectionName, CancellationToken cancellationToken)
    {
        var index = await GetIndexAsync(cancellationToken);
        foreach (var record in records) Populate(record, collectionName, index);
    }

    public async Task PopulateAsync(JsonObject record, string collectionName, CancellationToken cancellationToken) =>
        Populate(record, collectionName, await GetIndexAsync(cancellationToken));

    public void Invalidate() => cache.Remove(CacheKey);

    private async Task<RelationshipIndex> GetIndexAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue<RelationshipIndex>(CacheKey, out var cached)) return cached!;

        await IndexBuildLock.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue<RelationshipIndex>(CacheKey, out cached)) return cached!;

            // Atlas free/shared tiers can have a small connection pool. Load one collection at a time
            // and ensure concurrent requests share the same index build.
            var records = new List<RelatedRecord>();
            foreach (var collection in RelatedCollections)
                records.AddRange(await LoadCollectionAsync(collection, cancellationToken));

            var guides = await LoadPublishedGuideSummariesAsync(cancellationToken);
            var index = new RelationshipIndex(records, guides);
            cache.Set(CacheKey, index, TimeSpan.FromMinutes(5));
            return index;
        }
        finally
        {
            IndexBuildLock.Release();
        }
    }

    private async Task<IEnumerable<RelatedRecord>> LoadCollectionAsync(string collectionName, CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(collectionName);
        var projection = Builders<BsonDocument>.Projection.Include("_id").Include("name").Include("image");
        var documents = await collection.Find(FilterDefinition<BsonDocument>.Empty)
            .Project<BsonDocument>(projection)
            .ToListAsync(cancellationToken);
        return documents
            .Where(document => document.TryGetValue("_id", out var id) && id.IsString && document.TryGetValue("name", out var name) && name.IsString)
            .Select(document => new RelatedRecord(
                collectionName,
                document["_id"].AsString,
                document["name"].AsString,
                document.TryGetValue("image", out var image) && image.IsString ? image.AsString : null));
    }

    private async Task<IEnumerable<GuideSummary>> LoadPublishedGuideSummariesAsync(CancellationToken cancellationToken)
    {
        var projection = Builders<BsonDocument>.Projection.Include("_id").Include("subject").Include("title").Include("status");
        var documents = await database.GetCollection<BsonDocument>("guides")
            .Find(Builders<BsonDocument>.Filter.Eq("status", "published"))
            .Project<BsonDocument>(projection)
            .ToListAsync(cancellationToken);
        return documents.Where(document =>
                document.TryGetValue("_id", out var id) && id.IsString &&
                document.TryGetValue("title", out var title) && title.IsString &&
                document.TryGetValue("subject", out var subject) && subject.IsBsonDocument &&
                subject.AsBsonDocument.TryGetValue("collection", out var collection) && collection.IsString &&
                subject.AsBsonDocument.TryGetValue("id", out var recordId) && recordId.IsString)
            .Select(document => new GuideSummary(document["_id"].AsString, document["subject"].AsBsonDocument["collection"].AsString, document["subject"].AsBsonDocument["id"].AsString, document["title"].AsString));
    }

    private static void Populate(JsonObject record, string collectionName, RelationshipIndex index)
    {
        var recordId = ReadString(record["id"]);
        record["guide"] = recordId is not null && index.TryFindGuide(collectionName, recordId, out var guide)
            ? new JsonObject { ["id"] = guide.Id, ["title"] = guide.Title, ["status"] = "published" }
            : null;

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

        if (collectionName.Equals("classes", StringComparison.OrdinalIgnoreCase))
            PopulateStartingEquipment(record, index);
    }

    private static void PopulateStartingEquipment(JsonObject record, RelationshipIndex index)
    {
        if (record["startingEquipment"] is not JsonObject groups) return;

        foreach (var group in groups)
        {
            if (group.Value is not JsonArray entries) continue;

            var populatedEntries = new JsonArray();
            foreach (var entry in entries)
            {
                if (entry is not JsonObject reference) continue;
                var collection = ReadString(reference["collection"]);
                var id = ReadString(reference["id"]);
                if (collection is null || id is null || !index.TryFindRecord(collection, id, out var related)) continue;

                var populated = ToJsonReference(related);
                if (reference["quantity"] is JsonValue quantity && quantity.TryGetValue<int>(out var amount))
                    populated["quantity"] = amount;
                populatedEntries.Add(populated);
            }
            groups[group.Key] = populatedEntries;
        }
    }

    private static JsonObject ToJsonReference(RelatedRecord record) => new()
    {
        ["id"] = record.Id,
        ["collection"] = record.Collection,
        ["name"] = record.Name,
        ["kind"] = "record",
        ["image"] = record.Image
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

    private sealed record RelatedRecord(string Collection, string Id, string Name, string? Image);
    private sealed record GuideSummary(string Id, string Collection, string RecordId, string Title);

    private sealed class RelationshipIndex(IEnumerable<RelatedRecord> records, IEnumerable<GuideSummary> guides)
    {
        private readonly Dictionary<string, RelatedRecord> _locations = records
            .Where(record => record.Collection == "locations")
            .GroupBy(record => Normalize(record.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, RelatedRecord> _records = records
            .GroupBy(record => Normalize(record.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, RelatedRecord> _recordsById = records
            .GroupBy(record => Key(record.Collection, record.Id), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, GuideSummary> _guides = guides
            .GroupBy(guide => Key(guide.Collection, guide.RecordId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        public bool TryFindLocation(string name, out RelatedRecord record) => _locations.TryGetValue(Normalize(name), out record!);
        public bool TryFindRecord(string name, out RelatedRecord record) => _records.TryGetValue(Normalize(name), out record!);
        public bool TryFindRecord(string collection, string id, out RelatedRecord record) => _recordsById.TryGetValue(Key(collection, id), out record!);
        public bool TryFindGuide(string collection, string id, out GuideSummary guide) => _guides.TryGetValue(Key(collection, id), out guide!);
        private static string Key(string collection, string id) => $"{collection.Trim().ToLowerInvariant()}:{id}";
    }
}
