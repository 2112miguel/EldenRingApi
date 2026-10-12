using eldenRingApi.Services;

namespace eldenRingApi.Interfaces;

public interface IAuthService
{
    Task<AuthenticatedUser?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken);
}
