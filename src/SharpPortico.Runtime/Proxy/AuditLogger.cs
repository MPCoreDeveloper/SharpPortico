using System;
using Microsoft.Extensions.Logging;

namespace SharpPortico.Proxy;

/// <summary>Audit entry describing one proxied gRPC->REST call.</summary>
public readonly record struct AuditEntry(
    string ServiceName,
    string RpcName,
    string? ClientKeyId,
    string? ClientAddress,
    bool CacheHit,
    DateTime TimestampUtc,
    int? HttpStatusCode);

/// <summary>
/// Optional audit logger. The generated proxy reports every call; a default implementation
/// writes to <see cref="ILogger"/>. Swap for SIEM event sinks by implementing this interface.
/// </summary>
public interface IProxyAuditLogger
{
    void Log(AuditEntry entry);
}

public sealed class DefaultAuditLogger : IProxyAuditLogger
{
    private readonly ILogger _logger;

    public DefaultAuditLogger(ILogger<DefaultAuditLogger> logger) => _logger = logger;

    public void Log(AuditEntry entry)
    {
        _logger.LogInformation(
            "[portico-audit] service={Service} rpc={Rpc} client={ClientKey} peer={Peer} cacheHit={CacheHit} http={HttpStatus} at={At:O}",
            entry.ServiceName, entry.RpcName, entry.ClientKeyId ?? "-", entry.ClientAddress ?? "-",
            entry.CacheHit, entry.HttpStatusCode?.ToString() ?? "-", entry.TimestampUtc);
    }
}