using System.Security.Cryptography;
using System.Text;
using System.Text.Unicode;
using DhurtaHost.Models;
using Konscious.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace DhurtaHost.Services;

public sealed class AuthService
{
    private const int AccessTokenMinutes = 15;
    private const int RefreshTokenDays = 7;

    private readonly DatabaseService _db;
    private SigningCredentials? _signingCredentials;
    private TokenValidationParameters? _tokenValidation;

    public AuthService(DatabaseService db)
    {
        _db = db;
    }

    public void Initialize()
    {
        var secretB64 = _db.GetConfig("jwt_secret")!;
        var keyBytes = Convert.FromBase64String(secretB64);
        var key = new SymmetricSecurityKey(keyBytes);
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        _tokenValidation = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }

    // ── Password hashing (Argon2id) ───────────────────────────────────────

    public string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = ComputeArgon2(Encoding.UTF8.GetBytes(password), salt);
        return $"$argon2id${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[1] != "argon2id") return false;
        var salt = Convert.FromBase64String(parts[2]);
        var expectedHash = Convert.FromBase64String(parts[3]);
        var actualHash = ComputeArgon2(Encoding.UTF8.GetBytes(password), salt);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static byte[] ComputeArgon2(byte[] password, byte[] salt)
    {
        using var argon2 = new Argon2id(password)
        {
            Salt = salt,
            DegreeOfParallelism = 1,
            MemorySize = 65536,   // 64 MB
            Iterations = 3
        };
        return argon2.GetBytes(32);
    }

    // ── Access keys ───────────────────────────────────────────────────────

    public (string plainKey, AccessKey record) GenerateAccessKey(string deviceId, string label, DateTime? expiresAt)
    {
        var rawBytes = RandomNumberGenerator.GetBytes(32);
        var plainKey = "dh_" + Base32Encode(rawBytes);   // 55-char key, human-readable prefix
        var keyHash = HashKeyMaterial(plainKey);

        var record = new AccessKey
        {
            Id = NewId(),
            DeviceId = deviceId,
            KeyHash = keyHash,
            Label = label,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
        };
        return (plainKey, record);
    }

    public bool VerifyAccessKey(string plainKey, AccessKey stored)
    {
        if (stored.IsRevoked) return false;
        if (stored.ExpiresAt.HasValue && stored.ExpiresAt.Value < DateTime.UtcNow) return false;
        var hash = HashKeyMaterial(plainKey);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(hash),
            Encoding.UTF8.GetBytes(stored.KeyHash));
    }

    private static string HashKeyMaterial(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(bytes);
    }

    // ── JWT tokens ────────────────────────────────────────────────────────

    public (string accessToken, string refreshToken, Session session) IssueTokens(
        User user, string deviceId, string clientIp)
    {
        var sessionId = NewId();
        var accessToken = CreateJwt(sessionId, user.Id, "access", TimeSpan.FromMinutes(AccessTokenMinutes));

        var refreshRaw = RandomNumberGenerator.GetBytes(32);
        var refreshToken = "rt_" + Convert.ToBase64String(refreshRaw);
        var refreshHash = HashKeyMaterial(refreshToken);

        var session = new Session
        {
            Id = sessionId,
            UserId = user.Id,
            DeviceId = deviceId,
            RefreshTokenHash = refreshHash,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(RefreshTokenDays),
            ClientIp = clientIp,
        };

        return (accessToken, refreshToken, session);
    }

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        if (_tokenValidation is null) return null;
        try
        {
            var handler = new JwtSecurityTokenHandler();
            return handler.ValidateToken(token, _tokenValidation, out _);
        }
        catch
        {
            return null;
        }
    }

    public string? GetSessionId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(JwtRegisteredClaimNames.Sub);

    public string? GetUserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue("uid");

    private string CreateJwt(string sessionId, string userId, string type, TimeSpan lifetime)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, sessionId),
            new Claim(JwtRegisteredClaimNames.Jti, NewId()),
            new Claim("uid", userId),
            new Claim("type", type),
        };
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime),
            signingCredentials: _signingCredentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    public static string NewId() =>
        Guid.NewGuid().ToString("N");

    private static string Base32Encode(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var sb = new System.Text.StringBuilder();
        int buffer = data[0], next = 1, bitsLeft = 8;
        while (bitsLeft > 0 || next < data.Length)
        {
            if (bitsLeft < 5)
            {
                if (next < data.Length) { buffer <<= 8; buffer |= data[next++]; bitsLeft += 8; }
                else { buffer <<= 5 - bitsLeft; bitsLeft = 5; }
            }
            bitsLeft -= 5;
            sb.Append(alphabet[(buffer >> bitsLeft) & 0x1F]);
        }
        return sb.ToString();
    }
}
