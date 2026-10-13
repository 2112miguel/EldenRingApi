using MongoDB.Bson;
using MongoDB.Driver;

namespace eldenRingApi.Services;

/// <summary>Creates the first administrator only when BootstrapAdmin settings are explicitly provided.</summary>
public sealed class BootstrapAdminService(IConfiguration configuration, IMongoDatabase database, ILogger<BootstrapAdminService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var email = configuration["BootstrapAdmin:Email"];
        var password = configuration["BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(password)) return;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Bootstrap administrator was skipped because both BootstrapAdmin settings are required.");
            return;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var users = database.GetCollection<BsonDocument>("users");
        var exists = await users.Find(Builders<BsonDocument>.Filter.Eq("_id", normalizedEmail)).AnyAsync(cancellationToken);
        if (exists) return;

        var user = new BsonDocument
        {
            { "_id", normalizedEmail },
            { "email", normalizedEmail },
            { "passwordHash", BCrypt.Net.BCrypt.HashPassword(password) },
            { "roles", new BsonArray { "admin" } },
            { "isActive", true },
            { "tokenVersion", 0 },
            { "createdAt", DateTime.UtcNow }
        };
        await users.InsertOneAsync(user, cancellationToken: cancellationToken);
        logger.LogInformation("Bootstrap administrator created for {Email}.", normalizedEmail);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
