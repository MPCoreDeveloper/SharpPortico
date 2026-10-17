using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Caching.Memory;

namespace SharpPortico.Proxy;

/// <summary>
/// Cache used by the generated proxy. Keys are stable strings (service+rpc+request-hash);
/// values are the serialized response payloads.
/// </summary>
/// <remarks>
/// The members are called <c>Get</c> and <c>Set</c> because those are the names
/// <see cref="IMemoryCache"/> uses for the same two operations, so a custom cache written against
/// this interface reads like one written against the BCL. Both are Visual Basic keywords, which is
/// what CA1716 reports: the rule exists so that an implementation in another language does not have
/// to escape the name it overrides. It does not apply here - every call site is C#, emitted by the
/// proxy generator or written in an application - and renaming a published interface member would
/// break each implementation already in the wild, which is a major version for a naming preference.
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1716:IdentifiersShouldNotMatchKeywords",
    Justification = "Get/Set mirror IMemoryCache and are only called from C#; renaming a published interface member is a breaking change.")]
public interface IProxyCache
{
    byte[]? Get(string key);
    void Set(string key, byte[] value, TimeSpan ttl);
}

/// <summary>
/// In-memory <see cref="IProxyCache"/> backed by <see cref="IMemoryCache"/>. Default for
/// single-instance deployments.
/// </summary>
public sealed class MemoryProxyCache : IProxyCache
{
    private readonly IMemoryCache _cache;

    public MemoryProxyCache(IMemoryCache cache) => _cache = cache;

    public byte[]? Get(string key) => _cache.TryGetValue(key, out byte[]? value) ? value : null;

    public void Set(string key, byte[] value, TimeSpan ttl)
    {
        _cache.Set(key, value, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
    }
}

/// <summary>
/// No-op cache — used when caching is disabled.
/// </summary>
public sealed class NullProxyCache : IProxyCache
{
    public static NullProxyCache Instance { get; } = new();

    public byte[]? Get(string key) => null;
    public void Set(string key, byte[] value, TimeSpan ttl) { }
}