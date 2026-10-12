using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using eldenRingApi.Contracts;
using eldenRingApi.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace eldenRingApi.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(IConfiguration configuration, IAuthService authService) : ControllerBase
{
    [AllowAnonymous, HttpPost("token")]
    public async Task<IActionResult> CreateToken([FromBody] AdminLoginRequest request)
    {
        var signingKey = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, error = "JWT authentication is not configured." });
        }

        var user = await authService.AuthenticateAsync(request.Email, request.Password, HttpContext.RequestAborted);
        if (user is null)
        {
            return Unauthorized(new { success = false });
        }

        var issuer = configuration["Jwt:Issuer"] ?? "elden-ring-api";
        var audience = configuration["Jwt:Audience"] ?? "elden-ring-admin";
        var expiresMinutes = int.TryParse(configuration["Jwt:ExpiresMinutes"], out var configuredMinutes) ? Math.Clamp(configuredMinutes, 5, 120) : 60;
        var now = DateTime.UtcNow;
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim> { new(ClaimTypes.Name, user.Email), new(ClaimTypes.Email, user.Email) };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        if (user.Roles.Contains("admin", StringComparer.OrdinalIgnoreCase)) claims.Add(new Claim("scope", "api.write"));
        var token = new JwtSecurityToken(issuer, audience,
            claims,
            now, now.AddMinutes(expiresMinutes), credentials);

        return Ok(new { success = true, accessToken = new JwtSecurityTokenHandler().WriteToken(token), tokenType = "Bearer", expiresAt = token.ValidTo });
    }
}
