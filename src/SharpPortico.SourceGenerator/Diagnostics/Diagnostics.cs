using Microsoft.CodeAnalysis;

namespace SharpPortico.Generator.Diagnostics;

/// <summary>
/// Central registry of SharpPortico generator diagnostics.
/// IDs use the SP prefix (1000-range: parse, 2000-range: mapping, 3000-range: emit).
/// </summary>
internal static class Diagnostics
{
    private const string Category = "SharpPortico";

    // ---- Parse (1000) ----

    /// <summary>SP1000: the OpenAPI file could not be read or is not valid YAML/JSON.</summary>
    public static DiagnosticDescriptor CannotParseOpenApiFile { get; } = new(
        id: "SP1000",
        title: "Could not parse OpenAPI file",
        messageFormat: "SharpPortico could not parse OpenAPI file '{0}': {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The referenced file is missing, empty, or not valid YAML/JSON.");

    /// <summary>SP1001: the parsed document is not an OpenAPI 3.x document.</summary>
    public static DiagnosticDescriptor NotOpenApi3 { get; } = new(
        id: "SP1001",
        title: "Document is not OpenAPI 3.x",
        messageFormat: "SharpPortico expected an OpenAPI 3.0/3.1 document but '{0}' declares '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Only OpenAPI 3.0 and 3.1 specifications are supported.");

    // ---- Mapping (2000) ----

    /// <summary>SP2000: an OpenAPI construct could not be mapped faithfully.</summary>
    public static DiagnosticDescriptor UnmappableConstruct { get; } = new(
        id: "SP2000",
        title: "OpenAPI construct could not be mapped",
        messageFormat: "SharpPortico could not map '{0}' ({1}): {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The construct differs from gRPC/protobuf semantics and was mapped to the closest equivalent or skipped.");

    /// <summary>SP2001: HTTP method is not supported by gRPC mapping.</summary>
    public static DiagnosticDescriptor UnsupportedHttpMethod { get; } = new(
        id: "SP2001",
        title: "Unsupported HTTP method",
        messageFormat: "SharpPortico does not support the '{0}' operation '{1}' ({2})",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>SP2002: an OpenAPI schema used an unsupported composition type.</summary>
    public static DiagnosticDescriptor UnsupportedSchemaComposition { get; } = new(
        id: "SP2002",
        title: "Unsupported schema composition",
        messageFormat: "SharpPortico could not map schema '{0}': {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>SP2003: a circular reference could not be resolved.</summary>
    public static DiagnosticDescriptor CircularReference { get; } = new(
        id: "SP2003",
        title: "Circular reference detected",
        messageFormat: "SharpPortico detected a circular reference while resolving '{0}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>SP2004: authentication scheme is not mapped to a gRPC interceptor.</summary>
    public static DiagnosticDescriptor UnsupportedAuthScheme { get; } = new(
        id: "SP2004",
        title: "Unsupported authentication scheme",
        messageFormat: "SharpPortico does not generate an interceptor for auth scheme '{0}': {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // ---- Emit (3000) ----

    /// <summary>SP3000: internal failure while emitting generated code.</summary>
    public static DiagnosticDescriptor EmitFailed { get; } = new(
        id: "SP3000",
        title: "SharpPortico emit failure",
        messageFormat: "SharpPortico failed to emit generated code for '{0}': {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
