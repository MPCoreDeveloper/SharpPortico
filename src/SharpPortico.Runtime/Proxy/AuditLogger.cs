using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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

public sealed partial class DefaultAuditLogger : IProxyAuditLogger
{
    private readonly ILogger _logger;

    public DefaultAuditLogger(ILogger<DefaultAuditLogger> logger) => _logger = logger;

    [SuppressMessage(
        "Performance",
        "CA1873:Avoid potentially expensive logging",
        Justification = "CA1873 only recognises an IsEnabled guard in front of ILogger.Log*; the guard inside this method already skips the single int?.ToString() when Information is filtered out.")]
    public void Log(AuditEntry entry)
    {
        // Levels are filtered before the arguments are built: without this guard every call pays
        // for a ToString that a disabled logger would throw away.
        if (!_logger.IsEnabled(LogLevel.Information)) return;

        ProxyCallLogged(
            _logger,
            entry.ServiceName,
            entry.RpcName,
            entry.ClientKeyId ?? "-",
            entry.ClientAddress ?? "-",
            entry.CacheHit,
            entry.HttpStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "-",
            entry.TimestampUtc);
    }

    // Source-generated: the template is parsed once instead of on every call, and the arguments
    // are passed as typed parameters instead of a params array (CA1848/CA1873). The placeholder
    // names and the template text are unchanged because log sinks parse "service=", "cacheHit="
    // and "at=" out of this line. EventId stays 0, as it was for the hand-written LogInformation
    // call: left out, the generator would derive a hash of the method name instead.
    [LoggerMessage(
        EventId = 0,
        Level = LogLevel.Information,
        Message = "[portico-audit] service={Service} rpc={Rpc} client={ClientKey} peer={Peer} cacheHit={CacheHit} http={HttpStatus} at={At:O}")]
    private static partial void ProxyCallLogged(
        ILogger logger,
        string service,
        string rpc,
        string clientKey,
        string peer,
        bool cacheHit,
        string httpStatus,
        DateTime at);
}
