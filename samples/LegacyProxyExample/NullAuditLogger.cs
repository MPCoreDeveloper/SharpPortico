using SharpPortico.Proxy;

namespace SharpPortico.Samples.LegacyProxyExample;

/// <summary>No-op audit logger used by the legacy REST proxy demo.</summary>
internal sealed class NullAuditLogger : IProxyAuditLogger
{
    public void Log(AuditEntry entry) { }
}