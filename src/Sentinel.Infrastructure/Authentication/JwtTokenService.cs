
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Sentinel.Api.Authentication;

public sealed class JwtTokenService
{
    private readonly IConfiguration _configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public (string Token, int ExpiresIn) CreateToken(
        string accountId)
    {
        var issuer = _configuration["Auth:Issuer"]!;
        var audience = _configuration["Auth:Audience"]!;
        var signingKey = Convert.FromBase64String(
            _configuration["Auth:JwtSigningKey"]!);

        var lifetime = _configuration
            .GetValue<int>("Auth:AccessTokenMinutes");

        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(lifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, accountId),
            new(JwtRegisteredClaimNames.Jti,
                Guid.NewGuid().ToString("N"))
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(signingKey),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        return (
            new JwtSecurityTokenHandler().WriteToken(token),
            lifetime * 60
        );
    }
}
