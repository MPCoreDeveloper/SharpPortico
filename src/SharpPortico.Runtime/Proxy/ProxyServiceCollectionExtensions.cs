using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace SharpPortico.Proxy;

/// <summary>
/// Registers the shared proxy infrastructure (HTTP client, key provider, cache, validator,
/// audit logger). Generated code calls <c>AddSharpPorticoProxyCore</c> from its type-safe
/// <c>AddSharpPortico{Service}Proxy</c> extension.
/// </summary>
public static class ProxyServiceCollectionExtensions
{
    /// <summary>Registers shared SharpPortico proxy services.</summary>
    public static IServiceCollection AddSharpPorticoProxyCore(this IServiceCollection services, ProxyOptions options)
    {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (options is null) throw new ArgumentNullException(nameof(options));

        services.AddHttpClient();
        services.AddMemoryCache();

        services.AddSingleton(options);

        // Key provider: config-backed by default; replaced by the host for Key Vault.
        services.TryRegisterKeyProvider();

        // Cache: memory by default (singleton so generated proxies share it).
        services.AddSingleton<SharpPortico.Proxy.IProxyCache, SharpPortico.Proxy.MemoryProxyCache>();

        // Audit logger: ILogger-backed default (no-op when logging disabled).
        services.AddSingleton<SharpPortico.Proxy.IProxyAuditLogger, SharpPortico.Proxy.DefaultAuditLogger>();

        return services;
    }

    /// <summary>Registers a config-backed key provider when none is registered yet.</summary>
    public static IServiceCollection AddSharpPorticoKeyProvider(this IServiceCollection services,
        Func<string?, string?> keyResolver)
    {
        if (keyResolver is null) throw new ArgumentNullException(nameof(keyResolver));
        services.AddSingleton<SharpPortico.Proxy.IKeyProvider>(_ => new DelegateKeyProvider(keyResolver));
        return services;
    }

    /// <summary>Registers a ULID client-key validator from an allowed-keys enumerable.</summary>
    public static IServiceCollection AddSharpPorticoClientKeys(this IServiceCollection services,
        IEnumerable<string> allowedKeys, Func<string?, bool>? customValidate = null)
    {
        if (allowedKeys is null) throw new ArgumentNullException(nameof(allowedKeys));
        services.AddSingleton<SharpPortico.Proxy.IClientKeyValidator>(_ =>
            new UlidClientKeyValidator(allowedKeys, customValidate));
        return services;
    }

    private static void TryRegisterKeyProvider(this IServiceCollection services)
    {
        // Config-backed key provider registered lazily; hosts may override it
        // later via AddSharpPorticoKeyProvider.
        services.AddSingleton<SharpPortico.Proxy.IKeyProvider>(sp =>
        {
            var config = sp.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            return config is not null
                ? new SharpPortico.Proxy.ConfigurationKeyProvider(config)
                : new SharpPortico.Proxy.DelegateKeyProvider(_ => null);
        });
    }
}