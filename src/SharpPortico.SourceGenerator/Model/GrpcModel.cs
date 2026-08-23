using System;
using System.Collections.Immutable;

namespace SharpPortico.Generator.Model;

/// <summary>
/// Immutable intermediate representation produced by the OpenAPI parser/mapper and
/// consumed by the emitters. Every collection is immutable so the incremental pipeline
/// can cache pipeline stages by value equality.
/// </summary>
public sealed record GrpcModel(
    string ServiceName,
    string Namespace,
    string ProtoPackage,
    string FileHintName,
    ImmutableArray<MessageModel> Messages,
    ImmutableArray<ServiceModel> Services,
    ImmutableArray<EnumModel> Enums,
    ImmutableArray<AuthSchemeModel> AuthSchemes);

public sealed record MessageModel(
    string Name,
    ImmutableArray<FieldModel> Fields,
    bool IsRequest = false,
    bool IsResponse = false)
{
    public int FieldCount => Fields.Length;
}

public sealed record FieldModel(
    string Name,
    string ProtoName,
    int Number,
    FieldKind Kind,
    string CsType,
    string? TypeName = null,
    bool IsRepeated = false);

public enum FieldKind
{
    String,
    Int32,
    Int64,
    UInt32,
    UInt64,
    Float,
    Double,
    Bool,
    Bytes,
    Enum,
    Message,
    Timestamp
}

public sealed record ServiceModel(
    string Name,
    ImmutableArray<RpcModel> RpcMethods);

public sealed record RpcModel(
    string Name,
    string OriginalPath,
    string HttpMethod,
    string RequestType,
    string ResponseType,
    RpcKind Kind,
    bool HasPagination,
    string? RequestStreamingMessage = null,
    string? ResponseStreamingMessage = null);

public enum RpcKind
{
    Unary,
    ServerStreaming,
    ClientStreaming,
    BidiStreaming
}

public sealed record EnumModel(string Name, ImmutableArray<EnumValueModel> Values);

public sealed record EnumValueModel(string Name, int Number);

public sealed record AuthSchemeModel(string Name, AuthKind Kind, string? HeaderName = null, string? QueryParameterName = null);

public enum AuthKind
{
    Bearer,
    ApiKey,
    OAuth2,
    Unknown
}

/// <summary>
/// Holds the raw content plus configuration of a single OpenAPI file to process.
/// Designed for cheap value comparison in the incremental pipeline.
/// </summary>
public sealed record OpenApiWorkItem(
    string FilePath,
    string HintName,
    string? ServiceName,
    string? NamespaceName,
    string Content,
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
    int LargePayloadStreamingThresholdBytes);
