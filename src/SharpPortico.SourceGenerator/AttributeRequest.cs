using System;
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
    string? AssemblyName,
    bool EmitProtoFile,
    bool EmitClient,
    bool EmitServer,
    bool EmitDependencyInjection,
    bool RespectStreamingHints,
    bool EmitDiagnosticsForUnmappableConstructs,
    bool GenerateAuthMetadataHelpers,
    bool GenerateAuthInterceptors,
    bool DetectPagination,
    string PaginationPageParameter,
    string PaginationLimitParameter,
    string PaginationCursorParameter,
    string PaginationNextPageTokenParameter,
    bool EmitGoogleRpcStatusWrapper,
    string ServiceNameSuffix,
    int LargePayloadStreamingThresholdBytes)
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

        var assemblyName = context.TargetSymbol.ContainingAssembly?.Name;

        bool GetBool(string name, bool fallback) { foreach (var n in attr.NamedArguments) if (n.Key == name && n.Value.Value is bool b) return b; return fallback; }
        string GetString(string name, string fallback) { foreach (var n in attr.NamedArguments) if (n.Key == name && n.Value.Value is string s) return s; return fallback; }
        int GetInt(string name, int fallback) { foreach (var n in attr.NamedArguments) if (n.Key == name && n.Value.Value is int i) return i; return fallback; }

        return new AttributeRequest(
            FilePath: file,
            ServiceName: serviceName,
            NamespaceName: namespaceName,
            AssemblyName: assemblyName,
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
            LargePayloadStreamingThresholdBytes: GetInt("LargePayloadStreamingThresholdBytes", 1000000));
    }

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
            EmitProtoFile,
            EmitClient,
            EmitServer,
            EmitDependencyInjection,
            RespectStreamingHints,
            EmitDiagnosticsForUnmappableConstructs,
            GenerateAuthMetadataHelpers,
            GenerateAuthInterceptors,
            DetectPagination,
            PaginationPageParameter,
            PaginationLimitParameter,
            PaginationCursorParameter,
            PaginationNextPageTokenParameter,
            EmitGoogleRpcStatusWrapper,
            ServiceNameSuffix,
            LargePayloadStreamingThresholdBytes);
    }
}

/// <summary>
/// A generation request derived from an <c>&lt;AdditionalFiles&gt;</c> YAML/JSON spec file.
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
