using System;

namespace SharpPortico;

/// <summary>
/// Fine-grained mapping options for the SharpPortico generator.
/// Consumable as an attribute named argument, e.g.
/// <c>[OpenApiToGrpc("users.yaml", Options = new SharpPorticoOptions { DetectPagination = true })]</c>.
/// </summary>
public sealed class SharpPorticoOptions
{
    /// <summary>Default options used when no explicit options are supplied.</summary>
    public static SharpPorticoOptions Default { get; } = new();

    /// <summary>When <c>false</c>, unary RPCs are always generated even when a large
    /// payload or an <c>x-grpc-streaming</c> hint is present.</summary>
    public bool RespectStreamingHints { get; set; } = true;

    /// <summary>Payload size in bytes above which a request body is treated as large and
    /// eligible for client-streaming (when <see cref="RespectStreamingHints"/> is enabled).</summary>
    public int LargePayloadStreamingThresholdBytes { get; set; } = 1_000_000;

    /// <summary>When <c>false</c>, OpenAPI enums are emitted as plain C# <c>enum</c> values
    /// in messages instead of protobuf enums.</summary>
    public bool MapEnumsToProtobufEnums { get; set; } = true;

    /// <summary>When <c>true</c>, constructs that cannot be mapped faithfully are reported
    /// as generator warnings instead of being silently dropped.</summary>
    public bool EmitDiagnosticsForUnmappableConstructs { get; set; } = true;

    /// <summary>When <c>true</c>, metadata helpers for Bearer / API-Key / OAuth2 are generated
    /// from the OpenAPI <c>securitySchemes</c> section.</summary>
    public bool GenerateAuthMetadataHelpers { get; set; } = true;

    /// <summary>When <c>true</c>, gRPC call interceptors for authentication are generated.</summary>
    public bool GenerateAuthInterceptors { get; set; } = true;

    /// <summary>When <c>true</c>, page/limit/cursor/next_page_token parameters are detected and
    /// mapped to streaming or token-based RPCs.</summary>
    public bool DetectPagination { get; set; } = true;

    /// <summary>Name of the parameter treated as the page number.</summary>
    public string PaginationPageParameter { get; set; } = "page";

    /// <summary>Name of the parameter treated as the page size.</summary>
    public string PaginationLimitParameter { get; set; } = "limit";

    /// <summary>Name of the parameter treated as the pagination cursor.</summary>
    public string PaginationCursorParameter { get; set; } = "cursor";

    /// <summary>Name of the parameter treated as the next-page token.</summary>
    public string PaginationNextPageTokenParameter { get; set; } = "next_page_token";

    /// <summary>When <c>true</c>, error responses are wrapped so the RPC returns
    /// <c>google.rpc.Status</c>-shaped messages for failed calls.</summary>
    public bool EmitGoogleRpcStatusWrapper { get; set; } = true;

    /// <summary>Default gRPC status code used for 4xx HTTP responses.</summary>
    public GrpcStatusCodeMapping Default4xxStatus { get; set; } = GrpcStatusCodeMapping.InvalidArgument;

    /// <summary>Default gRPC status code used for 5xx HTTP responses.</summary>
    public GrpcStatusCodeMapping Default5xxStatus { get; set; } = GrpcStatusCodeMapping.Internal;

    /// <summary>Naming convention applied to generated message and field names.</summary>
    public NamingConvention MessageNaming { get; set; } = NamingConvention.PascalCase;

    /// <summary>Naming convention applied to generated RPC method names.</summary>
    public NamingConvention MethodNaming { get; set; } = NamingConvention.PascalCase;

    /// <summary>Suffix appended to derived service names (e.g. <c>"Service"</c> → <c>UserService</c>).</summary>
    public string ServiceNameSuffix { get; set; } = "Service";

    /// <summary>When <c>true</c>, a trailing <c>v1</c>/<c>v2</c> segment in the OpenAPI title or
    /// path is stripped from the generated C# service name.</summary>
    public bool StripApiVersionFromServiceName { get; set; } = true;

    /// <summary>When <c>true</c>, an <see cref="System.CodeDom.Compiler.GeneratedCodeAttribute"/>
    /// is applied to generated types.</summary>
    public bool EmitGeneratedCodeAttribute { get; set; } = true;

    /// <summary>When <c>true</c>, generated C# uses file-scoped namespaces.</summary>
    public bool UseFileScopedNamespaces { get; set; } = true;
}

/// <summary>gRPC status codes used when mapping HTTP error responses.</summary>
public enum GrpcStatusCodeMapping
{
    Ok = 0,
    Cancelled = 1,
    Unknown = 2,
    InvalidArgument = 3,
    DeadlineExceeded = 4,
    NotFound = 5,
    AlreadyExists = 6,
    PermissionDenied = 7,
    ResourceExhausted = 8,
    FailedPrecondition = 9,
    Aborted = 10,
    OutOfRange = 11,
    Unimplemented = 12,
    Internal = 13,
    Unavailable = 14,
    DataLoss = 15,
    Unauthenticated = 16
}

/// <summary>Naming conventions applied to generated identifiers.</summary>
public enum NamingConvention
{
    /// <summary>PascalCase (C# convention).</summary>
    PascalCase = 0,

    /// <summary>camelCase.</summary>
    CamelCase = 1,

    /// <summary>snake_case (protobuf convention).</summary>
    SnakeCase = 2,

    /// <summary>Preserve the original OpenAPI identifier verbatim.</summary>
    Original = 3
}