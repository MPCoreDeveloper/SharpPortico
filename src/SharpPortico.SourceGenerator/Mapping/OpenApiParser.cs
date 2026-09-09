using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using SharpPortico.Generator.Model;

namespace SharpPortico.Generator.Mapping;

/// <summary>
/// Parses OpenAPI 3.0/3.1 documents (YAML/JSON) via Microsoft.OpenApi and maps them
/// to the immutable <see cref="GrpcModel"/> IR consumed by the emitters.
/// The mapping is pure and side-effect free so it can be cached by the incremental pipeline.
/// </summary>
internal static class OpenApiParser
{
    private const string StreamingHintExtension = "x-grpc-streaming";
    private const string BytesTypeName = "Google.Protobuf.ByteString";

    public static ParseResult ParseAndMap(OpenApiWorkItem item, ImmutableArray<AdditionalFileRequest> files, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var diags = ImmutableArray.CreateBuilder<GeneratorDiagnostic>();

        // 1) Resolve content (attribute requests carry it lazily)
        var content = ResolveContent(item, files);
        if (string.IsNullOrEmpty(content))
        {
            return ParseResult.Failure(item, new GeneratorDiagnostic(
                Diagnostics.Diagnostics.CannotParseOpenApiFile,
                new object[] { item.FilePath, "file not found in AdditionalFiles" }));
        }

        // 2) Parse. OpenApiStringReader parses OpenAPI 3.0/3.1 documents only.
        var (document, parseFailure) = TryParseDocument(content, item, diags);
        if (parseFailure is not null)
        {
            return ParseResult.Failure(item, parseFailure);
        }
        if (document is null || document.Info is null)
        {
            return ParseResult.Failure(item, new GeneratorDiagnostic(
                Diagnostics.Diagnostics.CannotParseOpenApiFile,
                new object[] { item.FilePath, "missing info/title section" }));
        }

        ct.ThrowIfCancellationRequested();

        // 3) Derive names
        var serviceName = DeriveServiceName(item, document);
        var ns = string.IsNullOrWhiteSpace(item.NamespaceName) ? serviceName : item.NamespaceName;
        var protoPackage = ns.ToLowerInvariant();

        // 4) Map schemas
        var schemaMapper = new SchemaMapper(document, ct);
        var allMessages = MapComponentSchemas(document, schemaMapper, ct);
        var allEnums = ImmutableArray.CreateBuilder<EnumModel>();
        allEnums.AddRange(schemaMapper.GetAllEnums());

        // 5) Map paths -> RPCs + request/response messages
        var rpcs = MapPathOperations(document, item, schemaMapper, allMessages, ct);

        // 6) Auth schemes
        var authSchemes = (item.GenerateAuthMetadataHelpers || item.GenerateAuthInterceptors)
            ? MapAuthSchemes(document)
            : ImmutableArray<AuthSchemeModel>.Empty;

        // 7) Proxy config (gRPC -> legacy REST gateway)
        var proxy = BuildProxyConfig(item, document);

        var model = new GrpcModel(
            serviceName,
            ns,
            protoPackage,
            item.HintName,
            allMessages.ToImmutable(),
            ImmutableArray.Create(new ServiceModel(serviceName, rpcs.ToImmutable())),
            allEnums.ToImmutable(),
            authSchemes,
            proxy);

        return ParseResult.Success(item, model, diags.ToImmutable());
    }

    private static string? ResolveContent(OpenApiWorkItem item, ImmutableArray<AdditionalFileRequest> files)
    {
        if (!string.IsNullOrEmpty(item.Content)) return item.Content;
        return ResolveContentFromFiles(item, files);
    }

    private static (OpenApiDocument? Document, GeneratorDiagnostic? Fatal) TryParseDocument(
        string content, OpenApiWorkItem item, ImmutableArray<GeneratorDiagnostic>.Builder diags)
    {
        try
        {
            var reader = new OpenApiStringReader();
            var document = reader.Read(content, out var readDiagnostic);
            if (readDiagnostic is not null && readDiagnostic.Errors.Count > 0)
            {
                var first = readDiagnostic.Errors[0];
                diags.Add(new GeneratorDiagnostic(
                    Diagnostics.Diagnostics.CannotParseOpenApiFile,
                    new object[] { item.FilePath, first.Message }));
            }
            return (document, null);
        }
        catch (Exception ex)
        {
            return (null, new GeneratorDiagnostic(
                Diagnostics.Diagnostics.CannotParseOpenApiFile,
                new object[] { item.FilePath, ex.Message }));
        }
    }

    private static ImmutableArray<MessageModel>.Builder MapComponentSchemas(
        OpenApiDocument document, SchemaMapper schemaMapper, CancellationToken ct)
    {
        var allMessages = ImmutableArray.CreateBuilder<MessageModel>();
        if (document.Components?.Schemas is not { } schemas) return allMessages;

        foreach (var kvpS in schemas.Where(static s => s.Key is not null))
        {
            var name = kvpS.Key;
            var schema = kvpS.Value;
            ct.ThrowIfCancellationRequested();
            if (SchemaMapper.IsEnum(schema))
            {
                // Register with the SchemaMapper so GetAllEnums below reports it exactly once.
                schemaMapper.MapEnum(name, schema);
            }
            else
            {
                var msg = schemaMapper.MapSchemaToMessage(name, schema, isRequest: false, isResponse: false);
                if (msg is not null) allMessages.Add(msg);
            }
        }
        return allMessages;
    }

    private static ImmutableArray<RpcModel>.Builder MapPathOperations(
        OpenApiDocument document, OpenApiWorkItem item, SchemaMapper schemaMapper,
        ImmutableArray<MessageModel>.Builder messages, CancellationToken ct)
    {
        var rpcs = ImmutableArray.CreateBuilder<RpcModel>();
        var operIndex = 0;
        if (document.Paths is not { } paths) return rpcs;

        foreach (var kvp0 in paths)
        {
            var path = kvp0.Key;
            var pathItem = kvp0.Value;
            if (pathItem is null || pathItem.Operations is null) continue;
            foreach (var kvp1 in pathItem.Operations)
            {
                ct.ThrowIfCancellationRequested();
                var rpc = MapOperation(path, kvp1.Key, kvp1.Value, item, schemaMapper, messages, ref operIndex);
                if (rpc is not null) rpcs.Add(rpc);
            }
        }
        return rpcs;
    }

    private static RpcModel? MapOperation(
        string path,
        OperationType method,
        OpenApiOperation op,
        OpenApiWorkItem item,
        SchemaMapper schemaMapper,
        ImmutableArray<MessageModel>.Builder messages,
        ref int operIndex)
    {
        var httpMethod = method.ToString().ToUpperInvariant();
        var opId = string.IsNullOrWhiteSpace(op.OperationId) ? "Op" + operIndex++ + "_" + httpMethod : op.OperationId;
        var methodName = SanitizePascal(opId);
        if (methodName.Length == 0) methodName = httpMethod + "Operation";

        // Streaming hint: x-grpc-streaming extension (values: unary|client|server|bidi)
        var streamingHint = GetStreamingHint(op);
        var kind = RpcKind.Unary;
        var requestStreams = false;
        var responseStreams = false;
        ApplyStreamingHint(item, streamingHint, ref kind, ref requestStreams, ref responseStreams);

        // Request message: parameters (path/query/header) + body
        var requestFields = ImmutableArray.CreateBuilder<FieldModel>();
        var paramIndex = 0;
        MapRequestParameters(op, item, schemaMapper, requestFields, ref paramIndex);
        var bodyField = MapRequestBodyField(op, schemaMapper, messages, paramIndex, methodName);
        if (bodyField is not null) requestFields.Add(bodyField);
        ApplyPayloadStreamingHint(op, item, httpMethod, streamingHint, ref kind, ref requestStreams);

        if (requestFields.Count == 0)
        {
            requestFields.Add(new FieldModel("_HasValue", "has_value", 1, FieldKind.Bool, "bool", null, false));
        }

        var requestMsgName = methodName + "Request";
        messages.Add(new MessageModel(requestMsgName, requestFields.ToImmutable(), IsRequest: true, IsResponse: false));

        // Response message
        var responseFields = ImmutableArray.CreateBuilder<FieldModel>();
        MapResponseFields(op, item, schemaMapper, messages, methodName, responseFields);

        if (responseFields.Count == 0)
        {
            responseFields.Add(new FieldModel("_HasValue", "has_value", 1, FieldKind.Bool, "bool", null, false));
        }

        var responseMsgName = methodName + "Response";
        messages.Add(new MessageModel(responseMsgName, responseFields.ToImmutable(), IsRequest: false, IsResponse: true));

        // Pagination detection: page/limit/cursor/next_page_token
        var hasPagination = DetectPagination(item, requestFields, ref kind, ref responseStreams, streamingHint);

        return new RpcModel(
            methodName,
            path,
            httpMethod,
            requestMsgName,
            responseMsgName,
            kind,
            hasPagination,
            requestStreams ? requestMsgName : null,
            responseStreams ? responseMsgName : null);
    }

    private static void ApplyStreamingHint(
        OpenApiWorkItem item, string? streamingHint,
        ref RpcKind kind, ref bool requestStreams, ref bool responseStreams)
    {
        if (!item.RespectStreamingHints || streamingHint is null) return;
        switch (streamingHint)
        {
            case "client": kind = RpcKind.ClientStreaming; requestStreams = true; break;
            case "server": kind = RpcKind.ServerStreaming; responseStreams = true; break;
            case "bidi": kind = RpcKind.BidiStreaming; requestStreams = true; responseStreams = true; break;
            default: kind = RpcKind.Unary; break;
        }
    }

    private static void MapRequestParameters(
        OpenApiOperation op, OpenApiWorkItem item, SchemaMapper schemaMapper,
        ImmutableArray<FieldModel>.Builder requestFields, ref int paramIndex)
    {
        if (op.Parameters is not { } parameters) return;
        foreach (var p in parameters)
        {
            if (p.Schema is null) continue;
            var (_, field) = SchemaMapper.MapParameter(schemaMapper, p, item, ref paramIndex);
            if (field is not null) requestFields.Add(field);
        }
    }

    private static FieldModel? MapRequestBodyField(
        OpenApiOperation op, SchemaMapper schemaMapper,
        ImmutableArray<MessageModel>.Builder messages, int paramIndex, string methodName)
    {
        // Request body: nested message or bytes for application/octet-stream
        if (op.RequestBody?.Content is not { Count: > 0 }) return null;

        var mediaTypes = op.RequestBody.Content;
        if (mediaTypes.ContainsKey("application/octet-stream"))
        {
            return new FieldModel("Body", "body", paramIndex + 1, FieldKind.Bytes, BytesTypeName, BytesTypeName, false);
        }

        var firstSchema = mediaTypes.Values.FirstOrDefault(static m => m.Schema is not null)?.Schema;
        if (firstSchema is not { } bodySchema) return null;

        var refName = SchemaMapper.ResolveSchemaName(bodySchema);
        if (refName is not null)
        {
            return new FieldModel("Body", "body", paramIndex + 1, FieldKind.Message, refName, refName, false);
        }

        var nested = schemaMapper.MapSchemaToMessage(methodName + "Body", bodySchema, isRequest: false, isResponse: false);
        if (nested is null) return null;
        messages.Add(nested);
        return new FieldModel("Body", "body", paramIndex + 1, FieldKind.Message, nested.Name, nested.Name, false);
    }

    private static void ApplyPayloadStreamingHint(
        OpenApiOperation op, OpenApiWorkItem item, string httpMethod,
        string? streamingHint, ref RpcKind kind, ref bool requestStreams)
    {
        // Large payload + POST -> client-streaming option when hint absent
        if (item.RespectStreamingHints && streamingHint is null
            && httpMethod == "POST"
            && op.RequestBody is not null
            && EstimatePayloadSize(op.RequestBody) >= item.LargePayloadStreamingThresholdBytes)
        {
            kind = RpcKind.ClientStreaming;
            requestStreams = true;
        }
    }

    private static void MapResponseFields(
        OpenApiOperation op, OpenApiWorkItem item, SchemaMapper schemaMapper,
        ImmutableArray<MessageModel>.Builder messages, string methodName,
        ImmutableArray<FieldModel>.Builder responseFields)
    {
        var successResponse = ResolveSuccessResponse(op);
        if (successResponse?.Content is { Count: > 0 })
        {
            MapSuccessResponse(successResponse, schemaMapper, messages, methodName, responseFields);
        }
        else if (successResponse is null && item.EmitGoogleRpcStatusWrapper)
        {
            // No success response -> google.rpc.Status-shaped error wrapper
            var statusName = methodName + "Error";
            var statusFields = ImmutableArray.Create<FieldModel>(
                new FieldModel("Code", "code", 1, FieldKind.Int32, "int", null, false),
                new FieldModel("Message", "message", 2, FieldKind.String, "string", null, false));
            messages.Add(new MessageModel(statusName, statusFields, IsRequest: false, IsResponse: true));
            responseFields.Add(new FieldModel("Error", "error", 1, FieldKind.Message, statusName, statusName, false));
        }
    }

    private static void MapSuccessResponse(
        OpenApiResponse successResponse, SchemaMapper schemaMapper,
        ImmutableArray<MessageModel>.Builder messages, string methodName,
        ImmutableArray<FieldModel>.Builder responseFields)
    {
        if (successResponse.Content.ContainsKey("application/octet-stream"))
        {
            responseFields.Add(new FieldModel("Body", "body", 1, FieldKind.Bytes, BytesTypeName, BytesTypeName, false));
            return;
        }

        var schema = successResponse.Content.Values.FirstOrDefault(static m => m.Schema is not null)?.Schema;
        if (schema is null) return;

        if (schema.Type == "array")
        {
            MapArrayResponse(schema, schemaMapper, messages, methodName, responseFields);
            return;
        }

        var refName = SchemaMapper.ResolveSchemaName(schema);
        if (refName is not null)
        {
            responseFields.Add(new FieldModel("Data", "data", 1, FieldKind.Message, refName, refName, false));
            return;
        }

        // Inline object response -> nested message named <Method>Data so it
        // does not collide with the <Method>Response wrapper message.
        var nested = schemaMapper.MapSchemaToMessage(methodName + "Data", schema, isRequest: false, isResponse: true);
        if (nested is not null)
        {
            messages.Add(nested);
            responseFields.Add(new FieldModel("Data", "data", 1, FieldKind.Message, nested.Name, nested.Name, false));
        }
    }

    private static void MapArrayResponse(
        OpenApiSchema responseSchema, SchemaMapper schemaMapper,
        ImmutableArray<MessageModel>.Builder messages, string methodName,
        ImmutableArray<FieldModel>.Builder responseFields)
    {
        // Array response -> repeated field on the response wrapper.
        // Element type: $ref name when available; otherwise an inline nested message.
        var items = responseSchema.Items;
        if (items?.Reference is { Id: { } elemRefId })
        {
            var refElemName = SchemaMapper.SanitizePascal(elemRefId);
            var isEnum = SchemaMapper.IsEnum(items);
            responseFields.Add(new FieldModel(
                "Items", "items", 1, isEnum ? FieldKind.Enum : FieldKind.Message, refElemName, refElemName, IsRepeated: true));
            return;
        }
        if (items is null) return;

        var elemName = SchemaMapper.InlineArrayElementName(responseSchema, methodName + "Item");
        var nested = schemaMapper.MapSchemaToMessage(elemName, items, isRequest: false, isResponse: true);
        if (nested is not null && !messages.Any(m => m.Name == nested.Name))
        {
            messages.Add(nested);
        }
        responseFields.Add(new FieldModel(
            "Items", "items", 1, FieldKind.Message, elemName, elemName, IsRepeated: true));
    }

    private static bool DetectPagination(
        OpenApiWorkItem item, ImmutableArray<FieldModel>.Builder requestFields,
        ref RpcKind kind, ref bool responseStreams, string? streamingHint)
    {
        if (!item.DetectPagination) return false;

        var paramNames = new HashSet<string>(
            requestFields.Select(static f => f.ProtoName).Where(static n => n is { Length: > 0 }),
            StringComparer.OrdinalIgnoreCase);

        var hasPagination = paramNames.Contains(item.PaginationPageParameter)
            || paramNames.Contains(item.PaginationLimitParameter)
            || paramNames.Contains(item.PaginationCursorParameter)
            || paramNames.Contains(item.PaginationNextPageTokenParameter);

        // cursor-style pagination -> server-streaming; otherwise keep the current RPC kind
        if (hasPagination && streamingHint is null
            && (paramNames.Contains(item.PaginationCursorParameter)
                || paramNames.Contains(item.PaginationNextPageTokenParameter)))
        {
            kind = RpcKind.ServerStreaming;
            responseStreams = true;
        }
        return hasPagination;
    }

    private static string? ResolveContentFromFiles(OpenApiWorkItem item, ImmutableArray<AdditionalFileRequest> files)
    {
        var target = NormalizePath(item.FilePath);
        var exact = files.FirstOrDefault(
            f => string.Equals(NormalizePath(f.Path), target, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.Content;

        // Fallback: match by file name only (e.g. attribute says "users.yaml", file is "openapi/users.yaml")
        var name = System.IO.Path.GetFileName(target);
        var byName = files.FirstOrDefault(
            f => string.Equals(System.IO.Path.GetFileName(f.Path), name, StringComparison.OrdinalIgnoreCase));
        return byName?.Content;
    }

    private static string NormalizePath(string p)
        => p.Replace('\\', '/').TrimStart('.', '/');

    private static string DeriveServiceName(OpenApiWorkItem item, OpenApiDocument document)
    {
        if (!string.IsNullOrWhiteSpace(item.ServiceName)) return item.ServiceName;
        var stem = item.HintName;
        var title = document.Info.Title;
        if (!string.IsNullOrWhiteSpace(title) && (stem == "Service" || string.IsNullOrEmpty(stem)))
        {
            stem = SanitizePascal(title.Split(' ')[0]);
        }
        var baseName = SanitizePascal(stem);
        if (baseName.Length == 0) baseName = "Service";
        return baseName.EndsWith(item.ServiceNameSuffix, StringComparison.Ordinal)
            ? baseName
            : baseName + item.ServiceNameSuffix;
    }

    private static string SanitizePascal(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var chars = new char[input.Length];
        var n = 0;
        var upper = true;
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                chars[n++] = upper ? char.ToUpperInvariant(c) : c;
                upper = false;
            }
            else
            {
                upper = true;
            }
        }
        var result = new string(chars, 0, n);
        if (result.Length == 0 || char.IsDigit(result[0])) result = "N" + result;
        return result;
    }

    private static string? GetStreamingHint(OpenApiOperation op)
    {
        if (op.Extensions is null) return null;
        if (op.Extensions.TryGetValue(StreamingHintExtension, out var ext) && ext is Microsoft.OpenApi.Any.OpenApiString str)
        {
            return str.Value;
        }
        return null;
    }

    private static string? ResolveBaseUrl(OpenApiDocument document)
        => document.Servers?.FirstOrDefault()?.Url ?? string.Empty;

    private static long EstimatePayloadSize(OpenApiRequestBody body)
    {
        long total = 0;
        if (body.Content is null) return 0;
        foreach (var kvp2 in body.Content)
        {
            var media = kvp2.Value;
            if (media?.Schema?.Type == "object" && media.Schema.Properties is { } props)
            {
                total += props.Count * 64L;
            }
            else
            {
                total += 256;
            }
        }
        return total;
    }

    private static OpenApiResponse? ResolveSuccessResponse(OpenApiOperation op)
    {
        if (op.Responses is null || op.Responses.Count == 0) return null;
        foreach (var code in new[] { "200", "201", "204" })
        {
            if (op.Responses.TryGetValue(code, out var r)) return r;
        }
        return op.Responses.TryGetValue("default", out var def) ? def : null;
    }

    private static ProxyConfigModel? BuildProxyConfig(OpenApiWorkItem item, OpenApiDocument document)
    {
        if (!item.EnableProxyGeneration) return null;
        var baseUrl = !string.IsNullOrWhiteSpace(item.ProxyBaseUrl) ? item.ProxyBaseUrl : ResolveBaseUrl(document);
        return new ProxyConfigModel(
            Enabled: true,
            BaseUrl: baseUrl,
            ApiKeyHeaderName: item.ProxyApiKeyHeaderName,
            CacheTtlSeconds: item.ProxyCacheTtlSeconds,
            BypassCacheMetadataKey: item.ProxyBypassCacheMetadataKey,
            ClientKeyHeaderName: item.ProxyClientKeyHeaderName,
            ClientKeyMode: item.ProxyClientKeyMode,
            AuditEnabled: item.ProxyAuditEnabled);
    }

    private static ImmutableArray<AuthSchemeModel> MapAuthSchemes(OpenApiDocument document)
    {
        if (document.Components?.SecuritySchemes is not { Count: > 0 }) return ImmutableArray<AuthSchemeModel>.Empty;
        var builder = ImmutableArray.CreateBuilder<AuthSchemeModel>();
        foreach (var kvp3 in document.Components.SecuritySchemes)
        {
            var scheme = kvp3.Value;
            if (scheme is not null)
            {
                builder.Add(MapAuthScheme(kvp3.Key, scheme));
            }
        }
        return builder.ToImmutable();
    }

    private static AuthSchemeModel MapAuthScheme(string name, OpenApiSecurityScheme scheme)
    {
        if (scheme.Type == SecuritySchemeType.Http && string.Equals(scheme.Scheme, "bearer", StringComparison.OrdinalIgnoreCase))
        {
            return new AuthSchemeModel(name, AuthKind.Bearer);
        }
        if (scheme.Type == SecuritySchemeType.ApiKey)
        {
            var inHeader = scheme.In == ParameterLocation.Header;
            var inQuery = scheme.In == ParameterLocation.Query;
            return new AuthSchemeModel(
                name,
                AuthKind.ApiKey,
                HeaderName: inHeader ? scheme.Name : null,
                QueryParameterName: inQuery ? scheme.Name : null);
        }
        if (scheme.Type == SecuritySchemeType.OAuth2 || scheme.Type == SecuritySchemeType.OpenIdConnect)
        {
            return new AuthSchemeModel(name, AuthKind.OAuth2);
        }
        return new AuthSchemeModel(name, AuthKind.Unknown);
    }
}