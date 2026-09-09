using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpPortico.Generator.Model;

namespace SharpPortico.Generator;

/// <summary>
/// A generation request derived from an <c>[OpenApiToGrpc(...)]</c> assembly attribute.
/// </summary>
internal sealed record AttributeRequest(
    string FilePath,
    string? ServiceName,
    string? NamespaceName,
    ImmutableDictionary<string, object?> NamedValues)
{
    /// <summary>
    /// Reads the attribute data into a strongly-typed request. Returns <c>null</c>
    /// when the attribute does not carry a usable file path.
    /// </summary>
    public static AttributeRequest? From(GeneratorAttributeSyntaxContext context, System.Threading.CancellationToken ct)
    {
        var attrs = context.Attributes;
        if (attrs.Length == 0) return null;

        var attr = attrs[0];
        string? file = null;
        string? serviceName = null;
        string? namespaceName = null;

        if (attr.ConstructorArguments.Length > 0)
        {
            var arg = attr.ConstructorArguments[0];
            if (arg.Value is string s && !string.IsNullOrWhiteSpace(s)) file = s;
        }
        if (attr.ConstructorArguments.Length > 1 && attr.ConstructorArguments[1].Value is string svc)
            serviceName = svc;
        if (attr.ConstructorArguments.Length > 2 && attr.ConstructorArguments[2].Value is string ns)
            namespaceName = ns;

        if (file is null) return null;

        var named = ImmutableDictionary.CreateBuilder<string, object?>(StringComparer.Ordinal);
        foreach (var n in attr.NamedArguments)
        {
            named[n.Key] = n.Value.Value;
        }

        return new AttributeRequest(file, serviceName, namespaceName, named.ToImmutable());
    }

    private bool GetBool(string name, bool fallback)
        => NamedValues.TryGetValue(name, out var v) && v is bool b ? b : fallback;

    private string GetString(string name, string fallback)
        => NamedValues.TryGetValue(name, out var v) && v is string s ? s : fallback;

    private int GetInt(string name, int fallback)
        => NamedValues.TryGetValue(name, out var v) && v is int i ? i : fallback;

    private SharpPortico.ClientKeyMode GetEnum(string name, SharpPortico.ClientKeyMode fallback)
        => NamedValues.TryGetValue(name, out var v) && v is int i ? (SharpPortico.ClientKeyMode)i : fallback;

    /// <summary>
    /// Converts this attribute request into a pipeline work item. Content is resolved
    /// lazily by the parser from the compilation's additional files.
    /// </summary>
    public OpenApiWorkItem? ToWorkItem()
    {
        var fileName = System.IO.Path.GetFileNameWithoutExtension(FilePath);
        var hint = string.IsNullOrWhiteSpace(fileName) ? "Service" : fileName;

        return new OpenApiWorkItem(
            FilePath,
            hint,
            ServiceName,
            NamespaceName,
            Content: string.Empty,
            EmitProtoFile: GetBool("EmitProtoFile", true),
            EmitClient: GetBool("EmitClient", true),
            EmitServer: GetBool("EmitServer", true),
            EmitDependencyInjection: GetBool("EmitDependencyInjection", true),
            RespectStreamingHints: GetBool("RespectStreamingHints", true),
            EmitDiagnosticsForUnmappableConstructs: GetBool("EmitDiagnosticsForUnmappableConstructs", true),
            GenerateAuthMetadataHelpers: GetBool("GenerateAuthMetadataHelpers", true),
            GenerateAuthInterceptors: GetBool("GenerateAuthInterceptors", true),
            DetectPagination: GetBool("DetectPagination", true),
            PaginationPageParameter: GetString("PaginationPageParameter", "page"),
            PaginationLimitParameter: GetString("PaginationLimitParameter", "limit"),
            PaginationCursorParameter: GetString("PaginationCursorParameter", "cursor"),
            PaginationNextPageTokenParameter: GetString("PaginationNextPageTokenParameter", "next_page_token"),
            EmitGoogleRpcStatusWrapper: GetBool("EmitGoogleRpcStatusWrapper", true),
            ServiceNameSuffix: GetString("ServiceNameSuffix", "Service"),
            LargePayloadStreamingThresholdBytes: GetInt("LargePayloadStreamingThresholdBytes", 1000000),
            EnableProxyGeneration: GetBool("EnableProxyGeneration", false),
            ProxyBaseUrl: GetString("ProxyBaseUrl", string.Empty),
            ProxyApiKeyHeaderName: GetString("ProxyApiKeyHeaderName", "X-Api-Key"),
            ProxyCacheTtlSeconds: GetInt("ProxyCacheTtlSeconds", 60),
            ProxyBypassCacheMetadataKey: GetString("ProxyBypassCacheMetadataKey", "x-portico-bypass-cache"),
            ProxyClientKeyHeaderName: GetString("ProxyClientKeyHeaderName", "x-portico-key"),
            ProxyClientKeyMode: (int)GetEnum("ProxyClientKeyMode", SharpPortico.ClientKeyMode.None),
            ProxyAuditEnabled: GetBool("ProxyAuditEnabled", false));
    }
}

/// <summary>
/// A generation request derived from an <c><AdditionalFiles></c> YAML/JSON spec file.
/// </summary>
internal sealed record AdditionalFileRequest(
    string Path,
    string ServiceName,
    string? NamespaceName,
    string Content)
{
    /// <summary>Converts this file request into a pipeline work item.</summary>
    public OpenApiWorkItem ToWorkItem()
        => new(
            FilePath: Path,
            HintName: ServiceName,
            ServiceName: null,
            NamespaceName: NamespaceName,
            Content: Content,
            EmitProtoFile: true,
            EmitClient: true,
            EmitServer: true,
            EmitDependencyInjection: true,
            RespectStreamingHints: true,
            EmitDiagnosticsForUnmappableConstructs: true,
            GenerateAuthMetadataHelpers: true,
            GenerateAuthInterceptors: true,
            DetectPagination: true,
            PaginationPageParameter: "page",
            PaginationLimitParameter: "limit",
            PaginationCursorParameter: "cursor",
            PaginationNextPageTokenParameter: "next_page_token",
            EmitGoogleRpcStatusWrapper: true,
            ServiceNameSuffix: "Service",
            LargePayloadStreamingThresholdBytes: 1000000);
}