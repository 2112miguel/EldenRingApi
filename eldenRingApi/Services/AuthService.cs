using eldenRingApi.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace eldenRingApi.Services;

public sealed class AuthService(IMongoDatabase database) : IAuthService
{
    private readonly IMongoCollection<BsonDocument> _users = database.GetCollection<BsonDocument>("users");

    public async Task<AuthenticatedUser?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return null;

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await _users.Find(Builders<BsonDocument>.Filter.Eq("_id", normalizedEmail))
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null || !user.GetValue("isActive", false).ToBoolean()) return null;

        var passwordHash = user.GetValue("passwordHash", BsonNull.Value);
        if (!passwordHash.IsString || !BCrypt.Net.BCrypt.Verify(password, passwordHash.AsString)) return null;

        var roles = user.GetValue("roles", new BsonArray()).AsBsonArray
            .Where(role => role.IsString)
            .Select(role => role.AsString)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new AuthenticatedUser(normalizedEmail, roles);
    }
}
