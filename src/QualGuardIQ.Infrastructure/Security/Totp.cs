using System.Security.Cryptography;
using System.Text;

namespace QualGuardIQ.Infrastructure.Security;

/// <summary>
/// Time-based one-time passwords (RFC 6238 over RFC 4226 HOTP): HMAC-SHA1, 30-second steps, 6 digits — the settings every
/// common authenticator app uses. Secrets are 20 random bytes shown as Base32 (RFC 4648) for manual entry.
/// </summary>
public static class Totp
{
    public const int StepSeconds = 30;
    public const int Digits = 6;

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

    public static long TimeStep(DateTimeOffset at) => at.ToUnixTimeSeconds() / StepSeconds;

    /// <summary>HOTP value for a counter (RFC 4226 section 5.3), with the requested number of digits.</summary>
    public static string Code(byte[] secret, long step, int digits = Digits, HashAlgorithmName? algorithm = null)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var alg = algorithm ?? HashAlgorithmName.SHA1;
        byte[] hash = alg == HashAlgorithmName.SHA256 ? HMACSHA256.HashData(secret, counter)
            : alg == HashAlgorithmName.SHA512 ? HMACSHA512.HashData(secret, counter)
            : HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        var mod = 1;
        for (var i = 0; i < digits; i++) mod *= 10;
        return (binary % mod).ToString().PadLeft(digits, '0');
    }

    /// <summary>
    /// Finds the time step a code belongs to within the allowed skew, or null. Steps at or before <paramref name="lastUsedStep"/>
    /// are refused so a code (or an older one) cannot be replayed.
    /// </summary>
    public static long? Match(byte[] secret, string code, DateTimeOffset now, int skewSteps, long? lastUsedStep)
    {
        var c = Normalize(code);
        if (c.Length != Digits) return null;
        var current = TimeStep(now);
        long? found = null;
        for (var d = -skewSteps; d <= skewSteps; d++)
        {
            var step = current + d;
            // compare every candidate in constant time; keep scanning so timing does not reveal which step matched
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(secret, step)), Encoding.ASCII.GetBytes(c)) && found is null) found = step;
        }
        if (found is { } f && lastUsedStep is { } last && f <= last) return null;
        return found;
    }

    public static string Normalize(string? code) => new((code ?? "").Where(char.IsDigit).ToArray());

    public static string OtpAuthUri(string issuer, string account, byte[] secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={Base32.Encode(secret)}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
}

/// <summary>RFC 4648 Base32 without padding (the form authenticator apps accept).</summary>
public static class Base32
{
    const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] Decode(string text)
    {
        var s = text.Trim().TrimEnd('=').Replace(" ", "").Replace("-", "").ToUpperInvariant();
        var bytes = new List<byte>(s.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var ch in s)
        {
            var v = Alphabet.IndexOf(ch);
            if (v < 0) throw new FormatException("Not a Base32 value.");
            buffer = (buffer << 5) | v; bits += 5;
            if (bits >= 8) { bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF)); bits -= 8; }
        }
        return [.. bytes];
    }

    /// <summary>Key in groups of four for reading aloud or typing ("ABCD EFGH …").</summary>
    public static string Grouped(string key) => string.Join(' ', Enumerable.Range(0, (key.Length + 3) / 4).Select(i => key.Substring(i * 4, Math.Min(4, key.Length - i * 4))));
}
