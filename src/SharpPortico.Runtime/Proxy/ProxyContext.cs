using System;
using System.Collections.Generic;
using System.Threading;
using Grpc.Core;

namespace SharpPortico.Proxy;

/// <summary>Identifies the calling client (from gRPC metadata) and the proxy call context.</summary>
public readonly record struct ClientIdentity(string? KeyId, string? RemoteAddress);

/// <summary>Configuration for the generated proxy call pipeline.</summary>
public sealed record ProxyOptions
{
    /// <summary>Base URL of the legacy REST service (e.g. https://corporate.poland.example).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Header carrying the outbound API key (default X-Api-Key).</summary>
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";

    /// <summary>gRPC metadata entry for the inbound client key (default x-portico-key).</summary>
    public string ClientKeyHeaderName { get; set; } = "x-portico-key";

    /// <summary>
    /// Inbound mode: Forward = pass the client key as the outbound X-Api-Key;
    /// Own = validate a Portico key and use <see cref="IKeyProvider"/> outbound.
    /// </summary>
    public ClientKeyMode ClientKeyMode { get; set; } = ClientKeyMode.None;

    /// <summary>gRPC metadata entry for the per-call cache bypass flag (default x-portico-bypass-cache).</summary>
    public string BypassCacheMetadataKey { get; set; } = "x-portico-bypass-cache";

    /// <summary>Default response cache TTL (default 60 s).</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>When true, only GET/read operations are cached.</summary>
    public bool CacheReadsOnly { get; set; } = true;
}

public enum ClientKeyMode
{
    /// <summary>No inbound client authentication.</summary>
    None = 0,

    /// <summary>Forward the client key 1:1 as the outbound API key.</summary>
    Forward = 1,

    /// <summary>Validate a Portico (ULID) client key and use the configured outbound key.</summary>
    Own = 2
}

/// <summary>Parses the gRPC call context into proxy inputs (bypass flag, client identity).</summary>
public static class ProxyContext
{
    public static DateTime? ParseBypass(Metadata metadata, string key)
    {
        foreach (var entry in metadata)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)
                && bool.TryParse(entry.Value, out var bypass) && bypass)
            {
                return DateTime.UtcNow;
            }
        }
        return null;
    }

    public static ClientIdentity ReadIdentity(ServerCallContext context, string clientKeyHeader)
    {
        string? key = null;
        foreach (var entry in context.RequestHeaders)
        {
            if (string.Equals(entry.Key, clientKeyHeader, StringComparison.OrdinalIgnoreCase))
            {
                key = entry.Value;
                break;
            }
        }
        return new ClientIdentity(key, context.Peer);
    }
}