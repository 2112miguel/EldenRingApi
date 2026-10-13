namespace eldenRingApi.Services;

public sealed record AccountUpdateResult(bool Succeeded, string? Error, AuthenticatedUser? User)
{
    public static AccountUpdateResult Failure(string error) => new(false, error, null);
    public static AccountUpdateResult Success(AuthenticatedUser user) => new(true, null, user);
}
