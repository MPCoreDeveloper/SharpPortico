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

    public static ParseResult ParseAndMap(OpenApiWorkItem item, ImmutableArray<AdditionalFileRequest> files, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var diags = ImmutableArray.CreateBuilder<GeneratorDiagnostic>();

        // 1) Resolve content (attribute requests carry it lazily)
        var content = item.Content;
        if (string.IsNullOrEmpty(content))
        {
            content = ResolveContentFromFiles(item, files);
            if (content is null)
            {
                return ParseResult.Failure(item, new GeneratorDiagnostic(
                    Diagnostics.Diagnostics.CannotParseOpenApiFile,
                    new object[] { item.FilePath, "file not found in AdditionalFiles" }));
            }
        }

        // 2) Parse. OpenApiStringReader parses OpenAPI 3.0/3.1 documents only.
        OpenApiDocument document;
        try
        {
            var reader = new OpenApiStringReader();
            document = reader.Read(content, out var readDiagnostic);
            if (readDiagnostic != null && readDiagnostic.Errors.Count > 0)
            {
                var first = readDiagnostic.Errors[0];
                diags.Add(new GeneratorDiagnostic(
                    Diagnostics.Diagnostics.CannotParseOpenApiFile,
                    new object[] { item.FilePath, first.Message }));
            }
        }
        catch (Exception ex)
        {
            return ParseResult.Failure(item, new GeneratorDiagnostic(
                Diagnostics.Diagnostics.CannotParseOpenApiFile,
                new object[] { item.FilePath, ex.Message }));
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
        var ns = string.IsNullOrWhiteSpace(item.NamespaceName)
            ? serviceName
            : item.NamespaceName!;
        var protoPackage = ns.ToLowerInvariant();

        // 4) Map schemas
        var schemaMapper = new SchemaMapper(document, ct);
        var allMessages = ImmutableArray.CreateBuilder<MessageModel>();
        var allEnums = ImmutableArray.CreateBuilder<EnumModel>();

        if (document.Components?.Schemas is { } schemas)
        {
            foreach (var kvpS in schemas.Where(static s => s.Key != null))
            {
                var name = kvpS.Key!;
                var schema = kvpS.Value;
                ct.ThrowIfCancellationRequested();
                if (schemaMapper.IsEnum(schema))
                {
                    // Register with the SchemaMapper so AllEnums below reports it exactly once.
                    schemaMapper.MapEnum(name, schema);
                }
                else
                {
                    var msg = schemaMapper.MapSchemaToMessage(name, schema, isRequest: false, isResponse: false);
                    if (msg is not null) allMessages.Add(msg);
                }
            }
        }

        // Collect every enum registered during mapping (component enums AND inline enums
        // created on the fly, e.g. Pet.status -> StatusEnum). The SchemaMapper deduplicates
        // by name so each enum is emitted exactly once.
        foreach (var e in schemaMapper.AllEnums)
        {
            allEnums.Add(e);
        }

        // 5) Map paths -> RPCs + request/response messages
        var rpcs = ImmutableArray.CreateBuilder<RpcModel>();
        var operIndex = 0;

        if (document.Paths is { } paths)
        {
            foreach (var kvp0 in paths)
            {
                var path = kvp0.Key;
                var pathItem = kvp0.Value;
                if (pathItem is null || pathItem.Operations is null) continue;
                foreach (var kvp1 in pathItem.Operations)
                {
                    ct.ThrowIfCancellationRequested();
                    var rpc = MapOperation(path, kvp1.Key, kvp1.Value, item, schemaMapper, allMessages, diags, ref operIndex);
                    if (rpc is not null) rpcs.Add(rpc);
                }
            }
        }

        // 6) Auth schemes
        var authSchemes = ImmutableArray<AuthSchemeModel>.Empty;
        if (item.GenerateAuthMetadataHelpers || item.GenerateAuthInterceptors)
        {
            authSchemes = MapAuthSchemes(document);
        }

        var model = new GrpcModel(
            serviceName,
            ns,
            protoPackage,
            item.HintName,
            allMessages.ToImmutable(),
            ImmutableArray.Create(new ServiceModel(serviceName, rpcs.ToImmutable())),
            allEnums.ToImmutable(),
            authSchemes);

        return ParseResult.Success(item, model, diags.ToImmutable());
    }

    private static string? ResolveContentFromFiles(OpenApiWorkItem item, ImmutableArray<AdditionalFileRequest> files)
    {
        var target = NormalizePath(item.FilePath);
        foreach (var f in files)
        {
            if (string.Equals(NormalizePath(f.Path), target, StringComparison.OrdinalIgnoreCase))
                return f.Content;
        }
        // Fallback: match by file name only (e.g. attribute says "users.yaml", file is "openapi/users.yaml")
        var name = System.IO.Path.GetFileName(target);
        foreach (var f in files)
        {
            if (string.Equals(System.IO.Path.GetFileName(f.Path), name, StringComparison.OrdinalIgnoreCase))
                return f.Content;
        }
        return null;
    }

    private static string NormalizePath(string p)
        => p.Replace('\\', '/').TrimStart('.', '/');

    private static string DeriveServiceName(OpenApiWorkItem item, OpenApiDocument document)
    {
        if (!string.IsNullOrWhiteSpace(item.ServiceName)) return item.ServiceName!;
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

    private static RpcModel? MapOperation(
        string path,
        OperationType method,
        OpenApiOperation op,
        OpenApiWorkItem item,
        SchemaMapper schemaMapper,
        ImmutableArray<MessageModel>.Builder messages,
        ImmutableArray<GeneratorDiagnostic>.Builder diags,
        ref int operIndex)
    {
        var httpMethod = method.ToString().ToUpperInvariant();
        var opId = string.IsNullOrWhiteSpace(op.OperationId) ? $"Op{operIndex++}_{httpMethod}" : op.OperationId;
        var methodName = SanitizePascal(opId);
        if (methodName.Length == 0) methodName = httpMethod + "Operation";

        // Streaming hint: x-grpc-streaming extension (values: unary|client|server|bidi)
        var streamingHint = GetStreamingHint(op);
        RpcKind kind = RpcKind.Unary;
        bool requestStreams = false, responseStreams = false;
        if (item.RespectStreamingHints && streamingHint is not null)
        {
            switch (streamingHint)
            {
                case "client": kind = RpcKind.ClientStreaming; requestStreams = true; break;
                case "server": kind = RpcKind.ServerStreaming; responseStreams = true; break;
                case "bidi": kind = RpcKind.BidiStreaming; requestStreams = responseStreams = true; break;
                default: kind = RpcKind.Unary; break;
            }
        }

        // Request message: parameters (path/query/header) + body
        var requestFields = ImmutableArray.CreateBuilder<FieldModel>();
        var paramIndex = 0;
        if (op.Parameters is { } parameters)
        {
            foreach (var p in parameters)
            {
                if (p.Schema is null) continue;
                var (_, field) = schemaMapper.MapParameter(p, item, ref paramIndex);
                if (field is not null) requestFields.Add(field);
            }
        }

        // Request body: nested message or bytes for application/octet-stream
        var hasBody = op.RequestBody?.Content is { Count: > 0 };
        if (hasBody)
        {
            var mediaTypes = op.RequestBody.Content;
            if (mediaTypes.ContainsKey("application/octet-stream"))
            {
                requestFields.Add(new FieldModel(
                    "Body", "body", ++paramIndex, FieldKind.Bytes, "Google.Protobuf.ByteString", "Google.Protobuf.ByteString", false));
            }
            else
            {
                var firstSchema = mediaTypes.Values.FirstOrDefault(static m => m.Schema is not null)?.Schema;
                if (firstSchema is { } bodySchema)
                {
                    var refName = schemaMapper.ResolveSchemaName(bodySchema);
                    if (refName is not null)
                    {
                        requestFields.Add(new FieldModel(
                            "Body", "body", ++paramIndex, FieldKind.Message, refName, refName, false));
                    }
                    else
                    {
                        var nested = schemaMapper.MapSchemaToMessage(methodName + "Body", bodySchema, isRequest: false, isResponse: false);
                        if (nested is not null)
                        {
                            messages.Add(nested);
                            requestFields.Add(new FieldModel(
                                "Body", "body", ++paramIndex, FieldKind.Message, nested.Name, nested.Name, false));
                        }
                    }
                }
            }

            // Large payload + POST -> client-streaming option when hint absent
            if (item.RespectStreamingHints && streamingHint is null
                && httpMethod == "POST"
                && EstimatePayloadSize(op.RequestBody) >= item.LargePayloadStreamingThresholdBytes)
            {
                kind = RpcKind.ClientStreaming;
                requestStreams = true;
            }
        }

        if (requestFields.Count == 0)
        {
            requestFields.Add(new FieldModel("_HasValue", "has_value", 1, FieldKind.Bool, "bool", null, false));
        }

        var requestMsgName = methodName + "Request";
        messages.Add(new MessageModel(requestMsgName, requestFields.ToImmutable(), IsRequest: true, IsResponse: false));

        // Response message
        var responseFields = ImmutableArray.CreateBuilder<FieldModel>();
        var respIndex = 0;
        var successResponse = ResolveSuccessResponse(op);
        if (successResponse?.Content is { Count: > 0 })
        {
            if (successResponse.Content.ContainsKey("application/octet-stream"))
            {
                responseFields.Add(new FieldModel(
                    "Body", "body", ++respIndex, FieldKind.Bytes, "Google.Protobuf.ByteString", "Google.Protobuf.ByteString", false));
            }
            else
            {
                var schema = successResponse.Content.Values.FirstOrDefault(static m => m.Schema is not null)?.Schema;
                if (schema is { } respSchema)
                {
                    if (respSchema.Type == "array")
                    {
                        // Array response -> repeated field on the response wrapper.
                        // Element type: $ref name when available; otherwise an inline nested message.
                        var items = respSchema.Items;
                        if (items?.Reference is { Id: { } elemRefId })
                        {
                            var elemName = SchemaMapper.SanitizePascal(elemRefId);
                            var isEnum = schemaMapper.IsEnum(items);
                            responseFields.Add(new FieldModel(
                                "Items", "items", ++respIndex,
                                isEnum ? FieldKind.Enum : FieldKind.Message, elemName, elemName, IsRepeated: true));
                        }
                        else if (items is not null)
                        {
                            var elemName = schemaMapper.InlineArrayElementName(respSchema, methodName + "Item");
                            var nested = schemaMapper.MapSchemaToMessage(elemName, items, isRequest: false, isResponse: true);
                            if (nested is not null && !messages.Any(m => m.Name == nested.Name))
                            {
                                messages.Add(nested);
                            }
                            responseFields.Add(new FieldModel(
                                "Items", "items", ++respIndex, FieldKind.Message, elemName, elemName, IsRepeated: true));
                        }
                    }
                    else
                    {
                        var refName = schemaMapper.ResolveSchemaName(respSchema);
                        if (refName is not null)
                        {
                            responseFields.Add(new FieldModel(
                                "Data", "data", ++respIndex, FieldKind.Message, refName, refName, false));
                        }
                        else
                        {
                            // Inline object response -> nested message named <Method>Data so it
                            // does not collide with the <Method>Response wrapper message.
                            var nested = schemaMapper.MapSchemaToMessage(methodName + "Data", respSchema, isRequest: false, isResponse: true);
                            if (nested is not null)
                            {
                                messages.Add(nested);
                                responseFields.Add(new FieldModel(
                                    "Data", "data", ++respIndex, FieldKind.Message, nested.Name, nested.Name, false));
                            }
                        }
                    }
                }
            }
        }
        else if (successResponse is null)
        {
            // No success response -> google.rpc.Status-shaped error wrapper
            if (item.EmitGoogleRpcStatusWrapper)
            {
                var statusName = methodName + "Error";
                var statusFields = ImmutableArray.Create<FieldModel>(
                    new FieldModel("Code", "code", 1, FieldKind.Int32, "int", null, false),
                    new FieldModel("Message", "message", 2, FieldKind.String, "string", null, false));
                messages.Add(new MessageModel(statusName, statusFields, IsRequest: false, IsResponse: true));
                responseFields.Add(new FieldModel(
                    "Error", "error", ++respIndex, FieldKind.Message, statusName, statusName, false));
            }
        }

        if (responseFields.Count == 0)
        {
            responseFields.Add(new FieldModel("_HasValue", "has_value", 1, FieldKind.Bool, "bool", null, false));
        }

        var responseMsgName = methodName + "Response";
        messages.Add(new MessageModel(responseMsgName, responseFields.ToImmutable(), IsRequest: false, IsResponse: true));

        // Pagination detection: page/limit/cursor/next_page_token
        bool hasPagination = false;
        if (item.DetectPagination)
        {
            var paramNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in requestFields)
            {
                if (f.ProtoName is { Length: > 0 }) paramNames.Add(f.ProtoName);
            }
            hasPagination = paramNames.Contains(item.PaginationPageParameter)
                || paramNames.Contains(item.PaginationLimitParameter)
                || paramNames.Contains(item.PaginationCursorParameter)
                || paramNames.Contains(item.PaginationNextPageTokenParameter);
            if (hasPagination && streamingHint is null)
            {
                // token/pagination -> server-streaming for cursor-style, unary otherwise
                if (paramNames.Contains(item.PaginationCursorParameter) || paramNames.Contains(item.PaginationNextPageTokenParameter))
                {
                    kind = RpcKind.ServerStreaming;
                    responseStreams = true;
                }
            }
        }

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

    private static string? GetStreamingHint(OpenApiOperation op)
    {
        if (op.Extensions is null) return null;
        if (op.Extensions.TryGetValue(StreamingHintExtension, out var ext) && ext is Microsoft.OpenApi.Any.OpenApiString str)
            return str.Value;
        return null;
    }

    private static long EstimatePayloadSize(OpenApiRequestBody body)
    {
        long total = 0;
        if (body.Content is null) return 0;
        foreach (var kvp2 in body.Content)
        {
            var media = kvp2.Value;
            if (media?.Schema?.Type == "object" && media.Schema.Properties is { } props)
                total += props.Count * 64L;
            else
                total += 256;
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

    private static ImmutableArray<AuthSchemeModel> MapAuthSchemes(OpenApiDocument document)
    {
        if (document.Components?.SecuritySchemes is not { Count: > 0 }) return ImmutableArray<AuthSchemeModel>.Empty;
        var builder = ImmutableArray.CreateBuilder<AuthSchemeModel>();
        foreach (var kvp3 in document.Components.SecuritySchemes)
        {
            var name = kvp3.Key;
            var scheme = kvp3.Value;
            if (scheme is null) continue;

            if (scheme.Type == SecuritySchemeType.Http && string.Equals(scheme.Scheme, "bearer", StringComparison.OrdinalIgnoreCase))
            {
                builder.Add(new AuthSchemeModel(name, AuthKind.Bearer));
            }
            else if (scheme.Type == SecuritySchemeType.ApiKey)
            {
                var inHeader = scheme.In == ParameterLocation.Header;
                var inQuery = scheme.In == ParameterLocation.Query;
                builder.Add(new AuthSchemeModel(
                    name,
                    AuthKind.ApiKey,
                    HeaderName: inHeader ? scheme.Name : null,
                    QueryParameterName: inQuery ? scheme.Name : null));
            }
            else if (scheme.Type == SecuritySchemeType.OAuth2 || scheme.Type == SecuritySchemeType.OpenIdConnect)
            {
                builder.Add(new AuthSchemeModel(name, AuthKind.OAuth2));
            }
            else
            {
                builder.Add(new AuthSchemeModel(name, AuthKind.Unknown));
            }
        }
        return builder.ToImmutable();
    }
}