namespace eldenRingApi.Contracts;

public sealed record UpdateAccountRequest(string CurrentPassword, string? NewEmail, string? NewPassword);
