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
        return new AuthenticatedUser(normalizedEmail, roles, GetTokenVersion(user));
    }

    public async Task<AccountUpdateResult> UpdateAccountAsync(string currentEmail, string currentPassword, string? newEmail, string? newPassword, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentEmail) || string.IsNullOrWhiteSpace(currentPassword))
            return AccountUpdateResult.Failure("Current password is required.");

        var normalizedCurrentEmail = currentEmail.Trim().ToLowerInvariant();
        var user = await _users.Find(Builders<BsonDocument>.Filter.Eq("_id", normalizedCurrentEmail))
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null || !user.GetValue("isActive", false).ToBoolean() ||
            !user.GetValue("passwordHash", BsonNull.Value).IsString ||
            !BCrypt.Net.BCrypt.Verify(currentPassword, user["passwordHash"].AsString))
            return AccountUpdateResult.Failure("Current password is incorrect.");

        var hasNewEmail = !string.IsNullOrWhiteSpace(newEmail);
        var hasNewPassword = !string.IsNullOrWhiteSpace(newPassword);
        if (!hasNewEmail && !hasNewPassword)
            return AccountUpdateResult.Failure("Provide a new email or a new password.");

        var normalizedNewEmail = hasNewEmail ? newEmail!.Trim().ToLowerInvariant() : normalizedCurrentEmail;
        if (!IsValidEmail(normalizedNewEmail))
            return AccountUpdateResult.Failure("New email is invalid.");
        if (hasNewPassword && newPassword!.Length < 12)
            return AccountUpdateResult.Failure("New password must contain at least 12 characters.");

        var roles = user.GetValue("roles", new BsonArray()).AsBsonArray
            .Where(role => role.IsString).Select(role => role.AsString)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextTokenVersion = GetTokenVersion(user) + 1;
        var passwordHash = hasNewPassword ? BCrypt.Net.BCrypt.HashPassword(newPassword!) : user["passwordHash"].AsString;

        if (normalizedNewEmail == normalizedCurrentEmail)
        {
            var update = Builders<BsonDocument>.Update
                .Set("passwordHash", passwordHash)
                .Set("tokenVersion", nextTokenVersion)
                .Set("updatedAt", DateTime.UtcNow);
            await _users.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", normalizedCurrentEmail), update, cancellationToken: cancellationToken);
            return AccountUpdateResult.Success(new AuthenticatedUser(normalizedCurrentEmail, roles, nextTokenVersion));
        }

        using var session = await _users.Database.Client.StartSessionAsync(cancellationToken: cancellationToken);
        session.StartTransaction();
        try
        {
            var emailTaken = await _users.Find(session, Builders<BsonDocument>.Filter.Eq("_id", normalizedNewEmail))
                .AnyAsync(cancellationToken);
            if (emailTaken)
            {
                await session.AbortTransactionAsync(cancellationToken);
                return AccountUpdateResult.Failure("That email is already in use.");
            }

            var replacement = user.DeepClone().AsBsonDocument;
            replacement["_id"] = normalizedNewEmail;
            replacement["email"] = normalizedNewEmail;
            replacement["passwordHash"] = passwordHash;
            replacement["tokenVersion"] = nextTokenVersion;
            replacement["updatedAt"] = DateTime.UtcNow;
            await _users.InsertOneAsync(session, replacement, cancellationToken: cancellationToken);
            await _users.DeleteOneAsync(session, Builders<BsonDocument>.Filter.Eq("_id", normalizedCurrentEmail), null, cancellationToken);
            await session.CommitTransactionAsync(cancellationToken);
            return AccountUpdateResult.Success(new AuthenticatedUser(normalizedNewEmail, roles, nextTokenVersion));
        }
        catch (MongoException)
        {
            await session.AbortTransactionAsync(cancellationToken);
            return AccountUpdateResult.Failure("The account could not be updated. Try again.");
        }
    }

    private static int GetTokenVersion(BsonDocument user) =>
        user.TryGetValue("tokenVersion", out var tokenVersion) && tokenVersion.IsNumeric ? tokenVersion.ToInt32() : 0;

    private static bool IsValidEmail(string email)
    {
        try { return new System.Net.Mail.MailAddress(email).Address == email; }
        catch (FormatException) { return false; }
    }
}
