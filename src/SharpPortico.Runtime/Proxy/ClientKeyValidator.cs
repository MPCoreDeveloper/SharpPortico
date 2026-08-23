using System;
using System.Collections.Generic;

namespace SharpPortico.Proxy;

/// <summary>
/// Validates client keys presented by gRPC callers. Keys are ULID-shaped (26 chars, Crockford
/// base32) so a key's issuance time is derivable. Backed by a configurable allowed-keys list.
/// </summary>
public interface IClientKeyValidator
{
    /// <summary>Returns true when the presented key is valid.</summary>
    bool IsValid(string? key);

    /// <summary>Optional display name/issuer hint for the key (for audit logging).</summary>
    string? Describe(string key);
}

/// <summary>
/// Validates ULID-shaped client keys against an allowed list. The list is supplied by the
/// host (config, Key Vault, DB) — never hardcoded. Accepts a custom <c>ulidFactory</c> hook so
/// the host can integrate Posseth.UlidFactory or any other ULID implementation.
/// </summary>
public sealed class UlidClientKeyValidator : IClientKeyValidator
{
    private readonly IReadOnlySet<string> _allowedKeys;
    private readonly Func<string?, bool>? _customValidate;

    public UlidClientKeyValidator(IEnumerable<string> allowedKeys, Func<string?, bool>? customValidate = null)
    {
        _allowedKeys = allowedKeys is null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(allowedKeys, StringComparer.Ordinal);
        _customValidate = customValidate;
    }

    public bool IsValid(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (_customValidate is not null) return _customValidate(key);
        if (!IsUlidShape(key)) return false;
        return _allowedKeys.Contains(key);
    }

    public string? Describe(string key)
    {
        // Try to derive the issuance timestamp from the ULID prefix (first 10 chars).
        if (key is { Length: >= 10 } && TryReadTimestamp(key.AsSpan(0, 10), out var ts))
        {
            return $"ulid:{ts:yyyy-MM-ddTHH:mm:ssZ}";
        }
        return null;
    }

    internal static bool IsUlidShape(string value)
    {
        // ULID = 26 characters from Crockford's base32 alphabet + a 48-bit millisecond
        // timestamp prefix. Structural check only; the allowed-list enforces authority.
        if (value.Length != 26) return false;
        foreach (var c in value)
        {
            var ok = c is >= '0' and <= '9'
                  or >= 'A' and <= 'Z'
                  or >= 'a' and <= 'z';
            if (!ok) return false;
            // Exclude I, L, O, U (Crockford base32 excludes them).
            if (c is 'I' or 'L' or 'O' or 'U' or 'i' or 'l' or 'o' or 'u') return false;
        }
        return true;
    }

    private static bool TryReadTimestamp(ReadOnlySpan<char> prefix, out DateTime timestamp)
    {
        // Decode the first 10 base32 chars into 7 bytes (48-bit timestamp + first byte).
        timestamp = default;
        ulong value = 0;
        foreach (var c in prefix)
        {
            var digit = DecodeBase32(c);
            if (digit < 0) return false;
            value = (value << 5) | (uint)digit;
        }
        var millis = (long)(value >> 8);
        if (millis < 0) return false;
        timestamp = DateTime.UnixEpoch.AddMilliseconds(millis);
        return true;
    }

    private static int DecodeBase32(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'Z' => c switch
        {
            'I' or 'L' or 'O' or 'U' => -1,
            _ => c - 'A' + 10
        },
        >= 'a' and <= 'z' => DecodeBase32(char.ToUpperInvariant(c)),
        _ => -1
    };
}