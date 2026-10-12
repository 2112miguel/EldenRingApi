using System.Globalization;
using System.Text;
using CsvHelper;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

var options = MigrationOptions.Parse(args);
if (!string.IsNullOrWhiteSpace(options.UpdatesFile))
{
    return await ApplyUpdatesAsync(options);
}

if (string.IsNullOrWhiteSpace(options.SourceDirectory) || !Directory.Exists(options.SourceDirectory))
{
    Console.Error.WriteLine("Uso: dotnet run -- --source <directorio-csv> [--apply] [--database eldenring]");
    return 1;
}

var documentsByCollection = new Dictionary<string, List<BsonDocument>>(StringComparer.OrdinalIgnoreCase);
var errors = new List<string>();
foreach (var specification in ImportSpecifications.All)
{
    var path = Path.Combine(options.SourceDirectory, specification.FileName);
    if (!File.Exists(path))
    {
        errors.Add($"No se encontró {specification.FileName}.");
        continue;
    }

    var documents = ReadCsv(path, specification, errors);
    documentsByCollection[specification.CollectionName] = documents;
    Console.WriteLine($"{specification.FileName}: {documents.Count} documentos válidos.");
}

if (errors.Count > 0)
{
    Console.Error.WriteLine($"Se encontraron {errors.Count} errores. No se escribió nada en MongoDB.");
    foreach (var error in errors.Take(20)) Console.Error.WriteLine($"- {error}");
    return 1;
}

var catalogs = BuildCatalogs(documentsByCollection);
Console.WriteLine($"Catálogos derivados: {catalogs.Count}.");
if (!options.Apply)
{
    Console.WriteLine("Dry-run terminado. No se escribió ningún documento. Agrega --apply para ejecutar la migración.");
    return 0;
}

if (string.IsNullOrWhiteSpace(options.ConnectionString))
{
    Console.Error.WriteLine("Falta MONGODB_CONNECTION_STRING o --connection-string.");
    return 1;
}

var database = new MongoClient(options.ConnectionString).GetDatabase(options.DatabaseName);
foreach (var (collectionName, documents) in documentsByCollection)
{
    var collection = database.GetCollection<BsonDocument>(collectionName);
    var operations = documents.Select(document => new ReplaceOneModel<BsonDocument>(
        Builders<BsonDocument>.Filter.Eq("_id", document["_id"]), document)
    { IsUpsert = true });
    await collection.BulkWriteAsync(operations, new BulkWriteOptions { IsOrdered = false });
    Console.WriteLine($"{collectionName}: {documents.Count} documentos insertados o actualizados.");
}

var catalogCollection = database.GetCollection<BsonDocument>("catalogs");
foreach (var catalog in catalogs)
{
    await catalogCollection.ReplaceOneAsync(Builders<BsonDocument>.Filter.Eq("_id", catalog["_id"]), catalog, new ReplaceOptions { IsUpsert = true });
}
Console.WriteLine("Migración terminada correctamente.");
return 0;

static async Task<int> ApplyUpdatesAsync(MigrationOptions options)
{
    if (!File.Exists(options.UpdatesFile))
    {
        Console.Error.WriteLine("No se encontró el archivo de actualizaciones.");
        return 1;
    }

    if (string.IsNullOrWhiteSpace(options.ConnectionString))
    {
        Console.Error.WriteLine("Falta MONGODB_CONNECTION_STRING o --connection-string.");
        return 1;
    }

    var updates = JArray.Parse(await File.ReadAllTextAsync(options.UpdatesFile));
    var allowedCollections = ImportSpecifications.All.Select(specification => specification.CollectionName).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var requested = updates.Select(token =>
    {
        var collection = token.Value<string>("collection") ?? throw new FormatException("Actualización sin collection.");
        var id = token.Value<string>("id") ?? throw new FormatException("Actualización sin id.");
        var set = token["set"] as JObject ?? throw new FormatException("Actualización sin set.");
        if (!allowedCollections.Contains(collection)) throw new FormatException($"Colección no permitida: {collection}.");
        if (!set.Properties().Any()) throw new FormatException($"Actualización vacía para {collection}/{id}.");
        return new PendingUpdate(collection, id, set);
    }).ToList();

    var database = new MongoClient(options.ConnectionString).GetDatabase(options.DatabaseName);
    foreach (var group in requested.GroupBy(update => update.Collection, StringComparer.OrdinalIgnoreCase))
    {
        var collection = database.GetCollection<BsonDocument>(group.Key);
        var ids = group.Select(update => update.Id).Distinct(StringComparer.Ordinal).ToList();
        var found = await collection.CountDocumentsAsync(Builders<BsonDocument>.Filter.In("_id", ids));
        if (found != ids.Count)
        {
            Console.Error.WriteLine($"{group.Key}: se esperaban {ids.Count} IDs y MongoDB encontró {found}. No se aplicó ningún cambio.");
            return 1;
        }
    }

    var modified = 0L;
    foreach (var group in requested.GroupBy(update => update.Collection, StringComparer.OrdinalIgnoreCase))
    {
        var collection = database.GetCollection<BsonDocument>(group.Key);
        var operations = group.Select(update =>
        {
            var definitions = update.Set.Properties().Select(property => Builders<BsonDocument>.Update.Set(property.Name, ToBsonValue(property.Value))).ToList();
            return new UpdateOneModel<BsonDocument>(Builders<BsonDocument>.Filter.Eq("_id", update.Id), Builders<BsonDocument>.Update.Combine(definitions));
        });
        var result = await collection.BulkWriteAsync(operations, new BulkWriteOptions { IsOrdered = true });
        modified += result.ModifiedCount;
        Console.WriteLine($"{group.Key}: {result.MatchedCount} coincidencias, {result.ModifiedCount} modificados.");
    }
    Console.WriteLine($"Actualización terminada: {requested.Count} documentos solicitados, {modified} modificados.");
    return 0;
}

static BsonValue ToBsonValue(JToken token) => BsonDocument.Parse($"{{\"value\":{token.ToString(Formatting.None)}}}")["value"];

static List<BsonDocument> ReadCsv(string path, ImportSpecification specification, List<string> errors)
{
    var documents = new List<BsonDocument>();
    using var reader = new StreamReader(path, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
    using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
    csv.Read();
    csv.ReadHeader();
    var headers = csv.HeaderRecord ?? [];
    if (!headers.Contains("id", StringComparer.OrdinalIgnoreCase))
    {
        errors.Add($"{specification.FileName} no contiene la columna id.");
        return documents;
    }

    while (csv.Read())
    {
        var row = csv.Context.Parser?.Row ?? 0;
        var id = csv.GetField("id")?.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            errors.Add($"{specification.FileName}, fila {row}: id vacío.");
            continue;
        }

        var document = new BsonDocument("_id", id);
        foreach (var header in headers.Where(h => !h.Equals("id", StringComparison.OrdinalIgnoreCase)))
        {
            try { document[header] = ConvertValue(csv.GetField(header)?.Trim(), header, specification); }
            catch (Exception exception) { errors.Add($"{specification.FileName}, fila {row}, columna {header}: {exception.Message}"); }
        }
        documents.Add(document);
    }
    return documents;
}

static BsonValue ConvertValue(string? value, string field, ImportSpecification specification)
{
    if (string.IsNullOrWhiteSpace(value)) return BsonNull.Value;
    if (specification.StructuredFields.Contains(field))
    {
        // Algunos CSV usan literales de Python para datos desconocidos; BSON los representa como null.
        var normalizedValue = value
            .Replace("None", "null", StringComparison.Ordinal)
            .Replace("NaN", "null", StringComparison.Ordinal);
        var token = JToken.Parse(normalizedValue);
        return BsonDocument.Parse($"{{\"value\":{token.ToString(Formatting.None)}}}")["value"];
    }
    if (specification.IntegerFields.Contains(field))
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) throw new FormatException($"'{value}' no es un entero válido.");
        return number;
    }
    if (specification.DecimalFields.Contains(field))
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) throw new FormatException($"'{value}' no es un número válido.");
        return number;
    }
    return value;
}

static List<BsonDocument> BuildCatalogs(IReadOnlyDictionary<string, List<BsonDocument>> documentsByCollection)
{
    var catalogs = new List<BsonDocument>();
    foreach (var specification in ImportSpecifications.All)
    {
        if (!documentsByCollection.TryGetValue(specification.CollectionName, out var documents)) continue;
        foreach (var field in specification.CatalogFields)
        {
            var values = documents.Where(d => d.TryGetValue(field, out var value) && value.IsString && !string.IsNullOrWhiteSpace(value.AsString))
                .Select(d => d[field].AsString).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Select(value => (BsonValue)value);
            catalogs.Add(new BsonDocument { { "_id", $"{specification.CollectionName}:{field}" }, { "collection", specification.CollectionName }, { "field", field }, { "values", new BsonArray(values) } });
        }
    }
    return catalogs;
}

sealed record ImportSpecification(string FileName, string CollectionName, HashSet<string> StructuredFields, HashSet<string> IntegerFields, HashSet<string> DecimalFields, string[] CatalogFields);

static class ImportSpecifications
{
    static HashSet<string> F(params string[] fields) => new(fields, StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> None = F();
    public static readonly ImportSpecification[] All =
    [
        new("weapons.csv", "weapons", F("attack", "defence", "scalesWith", "requiredAttributes"), None, F("weight"), ["category"]),
        new("talismans.csv", "talismans", None, None, None, []), new("spirits.csv", "spirits", None, None, None, []),
        new("sorceries.csv", "sorceries", F("requires"), F("cost", "slots"), None, ["type"]),
        new("shields.csv", "shields", F("attack", "defence", "scalesWith", "requiredAttributes"), None, F("weight"), ["category"]),
        new("npcs.csv", "npcs", None, None, None, ["role"]), new("locations.csv", "locations", None, None, None, ["region"]),
        new("items.csv", "items", None, None, None, ["type"]), new("incantations.csv", "incantations", F("requires"), F("cost", "slots"), None, ["type"]),
        new("creatures.csv", "creatures", F("drops"), None, None, []), new("classes.csv", "classes", F("stats"), None, None, []),
        new("bosses.csv", "bosses", F("drops"), None, None, ["region"]), new("ashes.csv", "ashes", None, None, None, ["affinity"]),
        new("armors.csv", "armors", F("dmgNegation", "resistance"), None, F("weight"), ["category"]), new("ammos.csv", "ammos", F("attackPower"), None, None, ["type"])
    ];
}

sealed record PendingUpdate(string Collection, string Id, JObject Set);

sealed record MigrationOptions(string? SourceDirectory, bool Apply, string DatabaseName, string? ConnectionString, string? UpdatesFile)
{
    public static MigrationOptions Parse(string[] arguments)
    {
        string? source = null; string? updatesFile = null; var apply = false; var database = "eldenring"; var connection = Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING");
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] == "--apply") apply = true;
            else if (arguments[i] == "--source" && i + 1 < arguments.Length) source = arguments[++i];
            else if (arguments[i] == "--database" && i + 1 < arguments.Length) database = arguments[++i];
            else if (arguments[i] == "--connection-string" && i + 1 < arguments.Length) connection = arguments[++i];
            else if (arguments[i] == "--updates-file" && i + 1 < arguments.Length) updatesFile = arguments[++i];
        }
        return new MigrationOptions(source, apply, database, connection, updatesFile);
    }
}
