using System;

namespace SharpPortico;

/// <summary>
/// Instructs the SharpPortico source generator to turn an OpenAPI 3.0/3.1 specification
/// (YAML or JSON) into a gRPC service, protobuf messages and a modern C# 14 client
/// at compile time.
/// </summary>
/// <remarks>
/// The file is resolved relative to the project directory. Multiple attributes may be
/// added to generate multiple services. The attribute is an alternative to declaring
/// the file in the project's AdditionalFiles items; when both are present,
/// AdditionalFiles wins and this attribute is used to customize generation.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class OpenApiToGrpcAttribute : Attribute
{
    /// <summary>
    /// Creates a new <see cref="OpenApiToGrpcAttribute"/>.
    /// </summary>
    /// <param name="openApiFile">
    /// Path to the OpenAPI specification, relative to the project directory,
    /// e.g. <c>"openapi/users.yaml"</c>.
    /// </param>
    /// <param name="serviceName">
    /// Optional generated service name (e.g. <c>"UserService"</c>). When omitted the name is
    /// derived from the file name or the OpenAPI <c>info.title</c>.
    /// </param>
    /// <param name="namespaceName">
    /// Optional root namespace for generated code. When omitted,
    /// <c>SharpPortico.Generated</c> is used.
    /// </param>
    public OpenApiToGrpcAttribute(string openApiFile, string? serviceName = null, string? namespaceName = null)
    {
        OpenApiFile = openApiFile ?? throw new ArgumentNullException(nameof(openApiFile));
        ServiceName = serviceName;
        NamespaceName = namespaceName;
    }

    /// <summary>Path to the OpenAPI specification, relative to the project directory.</summary>
    public string OpenApiFile { get; }

    /// <summary>Optional generated service name.</summary>
    public string? ServiceName { get; }

    /// <summary>Optional root namespace for generated code.</summary>
    public string? NamespaceName { get; }

    /// <summary>When <c>true</c> (default), a <c>*.proto</c> file is emitted alongside the generated code.</summary>
    public bool EmitProtoFile { get; set; } = true;

    /// <summary>When <c>true</c> (default), a strongly-typed C# 14 client is emitted.</summary>
    public bool EmitClient { get; set; } = true;

    /// <summary>When <c>true</c> (default), a server base class is emitted.</summary>
    public bool EmitServer { get; set; } = true;

    /// <summary>When <c>true</c> (default), DI extension methods are emitted.</summary>
    public bool EmitDependencyInjection { get; set; } = true;

    // ---- Mapping options (flattened for attribute-argument compatibility) ----
    // Note: custom class objects are not valid attribute argument types in C#,
    // so options are exposed here as individual value-type properties. The generator
    // aggregates them into a SharpPorticoOptions instance.

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

    // ---- Proxy options (gRPC -> legacy REST gateway) ----

    /// <summary>When <c>true</c>, a REST proxy (<c>{Service}Proxy : ServiceBase</c>) is
    /// generated that forwards gRPC calls to the legacy OpenAPI service over HTTP.</summary>
    public bool EnableProxyGeneration { get; set; }

    /// <summary>Base URL of the legacy REST service. When omitted, the first value of the
    /// OpenAPI <c>servers[].url</c> is used.</summary>
    public string? ProxyBaseUrl { get; set; }

    /// <summary>Outbound API-key header name (default <c>X-Api-Key</c>).</summary>
    public string ProxyApiKeyHeaderName { get; set; } = "X-Api-Key";

    /// <summary>Default response cache TTL in seconds (default 60).</summary>
    public int ProxyCacheTtlSeconds { get; set; } = 60;

    /// <summary>gRPC metadata key used for the per-call cache bypass (default <c>x-portico-bypass-cache</c>).</summary>
    public string ProxyBypassCacheMetadataKey { get; set; } = "x-portico-bypass-cache";

    /// <summary>gRPC metadata key carrying the client key (default <c>x-portico-key</c>).</summary>
    public string ProxyClientKeyHeaderName { get; set; } = "x-portico-key";

    /// <summary>Inbound client-key mode: <see cref="ClientKeyMode"/>. Default None.</summary>
    public ClientKeyMode ProxyClientKeyMode { get; set; } = ClientKeyMode.None;

    /// <summary>When <c>true</c>, the generated proxy logs an audit entry per call.</summary>
    public bool ProxyAuditEnabled { get; set; }
}
