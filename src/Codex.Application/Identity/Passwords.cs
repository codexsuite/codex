using System.Security.Cryptography;
using Agoda.IoC.Core;
using Codex.Contracts.Errors;
using Microsoft.AspNetCore.Identity;

namespace Codex.Application.Identity;

// Password hashing (ASP.NET Core's PasswordHasher: PBKDF2, the format app_user.password_hash stores), the rule a new
// password must meet, and the one-time passwords an admin hands out.
[RegisterSingleton]
public sealed class Passwords
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    // No 0/O, 1/l/I: an admin reads a one-time password out or copies it by hand.
    private const string OneTimeAlphabet = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int OneTimeLength = 16;

    private readonly PasswordHasher<Passwords> _hasher = new();

    // Verifying against this when the email matches nobody keeps a sign-in as slow as one for a real user.
    private readonly Lazy<string> _decoy;

    public Passwords() => _decoy = new(() => Hash(RandomNumberGenerator.GetHexString(32)));

    public string Hash(string password) => _hasher.HashPassword(this, password);

    public bool Verify(string? hash, string password)
    {
        var result = _hasher.VerifyHashedPassword(this, hash ?? _decoy.Value, password);
        return hash is not null && result != PasswordVerificationResult.Failed;
    }

    public static void RequireAcceptable(string? password)
    {
        if (password is null || password.Length < MinLength)
        {
            throw ApiException.Validation($"The new password needs at least {MinLength} characters.");
        }
        if (password.Length > MaxLength)
        {
            throw ApiException.Validation($"The new password can have at most {MaxLength} characters.");
        }
    }

    public static string OneTime() => RandomNumberGenerator.GetString(OneTimeAlphabet, OneTimeLength);
}
