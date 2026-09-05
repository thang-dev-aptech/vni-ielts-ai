using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Vni.Ielts.Application.Identity;

namespace Vni.Ielts.Infrastructure.Security;

/// <summary>
/// Argon2id password hashing.
///
/// Argon2id rather than bcrypt or PBKDF2 because it is memory-hard: an
/// attacker with a GPU farm cannot trade memory for parallelism as cheaply.
/// The 2026 baseline in nfr.md names it directly.
///
/// The encoded format carries the parameters, so raising the cost later does
/// not invalidate existing hashes — an old hash still verifies with its own
/// parameters, and can be re-hashed on next successful login.
/// </summary>
public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    // OWASP's 2024 baseline for Argon2id: 19 MiB, 2 iterations, 1 degree of
    // parallelism. Memory is the expensive dimension for an attacker, so it is
    // the one to raise first if these are ever tuned up.
    private const int MemoryKib = 19456;
    private const int Iterations = 2;
    private const int Parallelism = 1;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, MemoryKib, Iterations, Parallelism, HashBytes);

        return string.Join(
            '$',
            string.Empty,
            "argon2id",
            "v=19",
            $"m={MemoryKib},t={Iterations},p={Parallelism}",
            Convert.ToBase64String(salt).TrimEnd('='),
            Convert.ToBase64String(hash).TrimEnd('='));
    }

    public bool Verify(string password, string hash)
    {
        if (!TryParse(hash, out var p))
            return false;

        var candidate = Derive(password, p.Salt, p.MemoryKib, p.Iterations, p.Parallelism, p.Hash.Length);

        // Constant-time. A plain sequence comparison short-circuits on the
        // first differing byte, which leaks how much of the hash matched.
        return CryptographicOperations.FixedTimeEquals(candidate, p.Hash);
    }

    private static byte[] Derive(
        string password, byte[] salt, int memoryKib, int iterations, int parallelism, int outputBytes)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKib,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon.GetBytes(outputBytes);
    }

    private readonly record struct Parsed(
        byte[] Salt, byte[] Hash, int MemoryKib, int Iterations, int Parallelism);

    private static bool TryParse(string encoded, out Parsed parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(encoded))
            return false;

        // $argon2id$v=19$m=...,t=...,p=...$<salt>$<hash>
        var parts = encoded.Split('$');
        if (parts.Length != 6 || parts[1] != "argon2id")
            return false;

        var settings = parts[3].Split(',');
        if (settings.Length != 3)
            return false;

        try
        {
            var memory = int.Parse(settings[0].AsSpan(2));
            var iterations = int.Parse(settings[1].AsSpan(2));
            var parallelism = int.Parse(settings[2].AsSpan(2));

            parsed = new Parsed(
                Convert.FromBase64String(Pad(parts[4])),
                Convert.FromBase64String(Pad(parts[5])),
                memory, iterations, parallelism);
            return true;
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
        {
            // A malformed stored hash must fail verification, not crash the
            // login endpoint — it is attacker-influenceable in the sense that
            // a corrupted record should not take the API down.
            return false;
        }
    }

    private static string Pad(string base64) =>
        base64.Length % 4 == 0 ? base64 : base64 + new string('=', 4 - base64.Length % 4);
}
