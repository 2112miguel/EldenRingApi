using eldenRingApi.Services;

namespace eldenRingApi.Interfaces;

public interface IAuthService
{
    Task<AuthenticatedUser?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken);
    Task<AccountUpdateResult> UpdateAccountAsync(string currentEmail, string currentPassword, string? newEmail, string? newPassword, CancellationToken cancellationToken);
}
