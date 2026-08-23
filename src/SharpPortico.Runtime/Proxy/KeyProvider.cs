using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace SharpPortico.Proxy;

/// <summary>
/// Supplies the outbound API key used to authenticate against the legacy OpenAPI service.
/// Implementations must never hardcode secrets — config, Key Vault, or a secret store is expected.
/// </summary>
public interface IKeyProvider
{
    /// <summary>Returns the API key for the given header name, or null when unavailable.</summary>
    string? GetApiKey(string headerName);
}

/// <summary>
/// Reads the API key from <see cref="IConfiguration"/>. The configuration path is
/// SharpPortico:Proxy:ApiKeys under the header name (or a single fallback key).
/// </summary>
public sealed class ConfigurationKeyProvider : IKeyProvider
{
    private readonly IConfiguration _config;

    public ConfigurationKeyProvider(IConfiguration config) => _config = config;

    public string? GetApiKey(string headerName)
    {
        var key = _config[$"SharpPortico:Proxy:ApiKeys:{headerName}"];
        if (!string.IsNullOrWhiteSpace(key)) return key;
        return _config["SharpPortico:Proxy:ApiKey"];
    }
}

/// <summary>
/// Decorator that resolves keys through a delegate — perfect for Azure Key Vault integration
/// (e.g. <c>new DelegateKeyProvider(name => secretClient.GetSecret(name).Value.Value)</c>).
/// </summary>
public sealed class DelegateKeyProvider : IKeyProvider
{
    private readonly Func<string, string?> _get;

    public DelegateKeyProvider(Func<string, string?> get) => _get = get;

    public string? GetApiKey(string headerName) => _get(headerName);
}

/// <summary>Aggregates several providers and returns the first non-null key.</summary>
public sealed class CompositeKeyProvider : IKeyProvider
{
    private readonly IReadOnlyList<IKeyProvider> _providers;

    public CompositeKeyProvider(params IKeyProvider[] providers) => _providers = providers ?? Array.Empty<IKeyProvider>();

    public string? GetApiKey(string headerName)
    {
        foreach (var p in _providers)
        {
            var key = p.GetApiKey(headerName);
            if (!string.IsNullOrWhiteSpace(key)) return key;
        }
        return null;
    }
}