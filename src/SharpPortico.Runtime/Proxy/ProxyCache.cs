using System;
using Microsoft.Extensions.Caching.Memory;

namespace SharpPortico.Proxy;

/// <summary>
/// Cache used by the generated proxy. Keys are stable strings (service+rpc+request-hash);
/// values are the serialized response payloads.
/// </summary>
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