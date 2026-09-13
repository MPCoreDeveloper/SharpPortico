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

    // ---- Parse, continued (1000) ----

    /// <summary>SP1002: an OpenAPI 3.1 document was parsed as 3.0.</summary>
    public static DiagnosticDescriptor OpenApi31ParsedAs30 { get; } = new(
        id: "SP1002",
        title: "OpenAPI 3.1 parsed as 3.0",
        messageFormat: "SharpPortico parsed '{0}' as OpenAPI 3.0: the bundled parser (Microsoft.OpenApi 1.6.x) does "
            + "not read a 3.1 document. Everything 3.0 and 3.1 share maps normally; constructs only 3.1 adds - a "
            + "type array, prefixItems, webhooks, jsonSchemaDialect, $defs - are not mapped",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The declared version is rewritten to 3.0 before parsing, so a 3.0-compatible 3.1 document maps as it stands.");

    // ---- Mapping, continued (2000) ----

    /// <summary>SP2005: one message name would be emitted twice, which cannot compile.</summary>
    public static DiagnosticDescriptor DuplicateMessageName { get; } = new(
        id: "SP2005",
        title: "Message name produced twice",
        messageFormat: "SharpPortico cannot map '{0}': the message name '{1}' is produced twice - an operation "
            + "derives its messages as '{{OperationId}}Request' and '{{OperationId}}Response', so a component "
            + "schema of that name collides. Rename one of the two",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Two different messages with one name would land in one file, which does not compile.");

    /// <summary>SP2006: a property would generate a member named like its enclosing type, which cannot compile.</summary>
    public static DiagnosticDescriptor MemberNameEqualsType { get; } = new(
        id: "SP2006",
        title: "Property name equals its schema name",
        messageFormat: "SharpPortico cannot map '{0}': property '{1}' of schema '{2}' would generate a member with "
            + "the same name as its enclosing type, which C# forbids (CS0542). Rename the property or the schema",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A generated member may not share its name with the type that contains it.");


}
