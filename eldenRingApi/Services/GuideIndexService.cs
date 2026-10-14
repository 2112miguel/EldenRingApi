using MongoDB.Bson;
using MongoDB.Driver;

namespace eldenRingApi.Services;

/// <summary>Ensures one guide is associated with each catalogue record, while allowing standalone guides.</summary>
public sealed class GuideIndexService(IMongoDatabase database) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>("guides");
        var keys = Builders<BsonDocument>.IndexKeys.Ascending("subject.collection").Ascending("subject.id");
        var indexes = await (await collection.Indexes.ListAsync(cancellationToken)).ToListAsync(cancellationToken);
        var existing = indexes.FirstOrDefault(index => index.GetValue("name", string.Empty).AsString == "unique_guide_subject");
        if (existing is not null && (!existing.TryGetValue("sparse", out var sparse) || !sparse.ToBoolean()))
            await collection.Indexes.DropOneAsync("unique_guide_subject", cancellationToken);

        await collection.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(keys, new CreateIndexOptions
        {
            Unique = true,
            Name = "unique_guide_subject",
            Sparse = true
        }), cancellationToken: cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
