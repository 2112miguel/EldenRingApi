namespace eldenRingApi.Services;

public sealed record AuthenticatedUser(string Email, IReadOnlyCollection<string> Roles);
