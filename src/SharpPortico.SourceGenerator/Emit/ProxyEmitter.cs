using System.Linq;
using SharpPortico.Generator.Model;

namespace SharpPortico.Generator.Emit;

/// <summary>
/// Emits the gRPC-to-REST proxy ({Service}Proxy : ServiceBase) that forwards unary calls
/// to the legacy OpenAPI service using SharpPortico.Runtime.Proxy building blocks.
/// JSON payloads are handled by generated per-message readers/writers (no reflection; AOT-safe).
/// </summary>
internal static class ProxyEmitter
{
    public static void Emit(CodeWriter w, GrpcModel model, OpenApiWorkItem item)
    {
        var svc = model.Services[0];

        w.Line("[global::System.CodeDom.Compiler.GeneratedCode(\"SharpPortico\", \"1.0.0\")]");
        w.Block($"public sealed partial class {svc.Name}Proxy : {svc.Name}Base", () =>
        {
            EmitConstructor(w, svc);
            foreach (var rpc in svc.RpcMethods)
            {
                if (rpc.Kind == RpcKind.Unary) EmitUnary(w, model, rpc, svc);
            }
            EmitJsonHelpers(w, model);
        });
    }

    private static void EmitConstructor(CodeWriter w, ServiceModel svc)
    {
        w.Line("private readonly SharpPortico.Proxy.ProxyOptions _options;");
        w.Line("private readonly SharpPortico.Proxy.IRestClient _rest;");
        w.Line("private readonly SharpPortico.Proxy.IProxyCache? _cache;");
        w.Line("private readonly SharpPortico.Proxy.IKeyProvider? _keys;");
        w.Line("private readonly SharpPortico.Proxy.IClientKeyValidator? _validator;");
        w.Line("private readonly SharpPortico.Proxy.IProxyAuditLogger? _audit;");
        w.Line();
        w.Line($"public {svc.Name}Proxy(");
        w.Line("    SharpPortico.Proxy.ProxyOptions options,");
        w.Line("    SharpPortico.Proxy.IRestClient rest,");
        w.Line("    SharpPortico.Proxy.IProxyCache? cache = null,");
        w.Line("    SharpPortico.Proxy.IKeyProvider? keys = null,");
        w.Line("    SharpPortico.Proxy.IClientKeyValidator? validator = null,");
        w.Line("    SharpPortico.Proxy.IProxyAuditLogger? audit = null)");
        w.Line("{");
        w.Open();
        w.Line("_options = options ?? throw new global::System.ArgumentNullException(nameof(options));");
        w.Line("_rest = rest ?? throw new global::System.ArgumentNullException(nameof(rest));");
        w.Line("_cache = cache; _keys = keys; _validator = validator; _audit = audit;");
        w.Close();
        w.Line("}");
        w.Line();
    }

    private static void EmitUnary(CodeWriter w, GrpcModel model, RpcModel rpc, ServiceModel svc)
    {
        var reqMsg = model.Messages.First(m => m.Name == rpc.RequestType);
        var respMsg = model.Messages.First(m => m.Name == rpc.ResponseType);

        w.Line($"public override async global::System.Threading.Tasks.Task<{rpc.ResponseType}> {rpc.Name}Async({rpc.RequestType} request, global::Grpc.Core.ServerCallContext context)");
        w.Line("{");
        w.Open();

        w.Line($"var isRead = string.Equals(\"{rpc.HttpMethod}\", \"GET\", global::System.StringComparison.OrdinalIgnoreCase);");
        w.Line("var identity = SharpPortico.Proxy.ProxyContext.ReadIdentity(context, _options.ClientKeyHeaderName);");
        w.Line("var bypass = SharpPortico.Proxy.ProxyContext.ParseBypass(context.RequestHeaders, _options.BypassCacheMetadataKey) is not null;");
        w.Line("var outboundKey = default(string);");
        w.Line("if (_options.ClientKeyMode == global::SharpPortico.Proxy.ClientKeyMode.Forward)");
        w.Line("{ outboundKey = identity.KeyId; }");
        w.Line("else if (_options.ClientKeyMode == global::SharpPortico.Proxy.ClientKeyMode.Own)");
        w.Line("{");
        w.Open();
        w.Line("if (_validator is null || !_validator.IsValid(identity.KeyId))");
        w.Line($"    throw new global::Grpc.Core.RpcException(new global::Grpc.Core.Status(global::Grpc.Core.StatusCode.Unauthenticated, \"invalid client key\"));");
        w.Line("outboundKey = _keys is not null ? _keys.GetApiKey(_options.ApiKeyHeaderName) : null;");
        w.Close();
        w.Line("}");
        w.Line("else");
        w.Line("{ outboundKey = _keys is not null ? _keys.GetApiKey(_options.ApiKeyHeaderName) : null; }");
        w.Line();

        w.Line($"var path = \"{rpc.OriginalPath}\";");
        w.Line("var pathParams = new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.Ordinal);");
        w.Line("var queryParams = new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.Ordinal);");
        w.Line("var headers = new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.Ordinal);");
        w.Line("if (outboundKey is not null) headers[_options.ApiKeyHeaderName] = outboundKey;");
        w.Line();

        foreach (var f in reqMsg.Fields)
        {
            if (f.Name.StartsWith("_") || f.Kind == FieldKind.Message) continue;
            // Every set scalar is registered both as a path param (case-insensitively
            // substituted by HttpRestClient) and as a query param; parameters absent
            // from the OpenAPI path template are ignored by the REST client.
            w.Line($"if ({IsSet(f)})");
            w.Line("{");
            w.Open();
            w.Line($"pathParams[\"{f.Name}\"] = {ToStringCall(f)};");
            w.Line($"queryParams[\"{f.Name}\"] = {ToStringCall(f)};");
            w.Close();
            w.Line("}");
        }

        var bodyField = reqMsg.Fields.FirstOrDefault(f => f.Name == "Body" && f.Kind == FieldKind.Message);
        w.Line("var body = default(byte[]);");
        if (bodyField is not null)
        {
            var bodyMsg = model.Messages.First(m => m.Name == bodyField.TypeName);
            w.Line($"if (request.Body is not null) body = global::System.Text.Encoding.UTF8.GetBytes(Serialize{camel(bodyMsg.Name)}(request.Body));");
        }
        w.Line();

        // Cache key: service/rpc + path (+ query string). Stable, reflection-free.
        w.Line($"var cacheKey = \"{svc.Name}/{rpc.Name}/\" + path;");
        w.Line("foreach (var kv in queryParams)");
        w.Line("{");
        w.Open();
        w.Line("cacheKey += \",\" + kv.Key + \"=\" + kv.Value;");
        w.Close();
        w.Line("}");
        w.Line("var cacheHit = false;");
        w.Line("var cached = !bypass && isRead && _cache is not null ? _cache.Get(cacheKey) : null;");
        w.Line("if (cached is not null)");
        w.Line("{");
        w.Open();
        w.Line("cacheHit = true;");
        w.Line($"var hit = new {rpc.ResponseType}();");
        w.Line($"Parse{camel(respMsg.Name)}(cached, hit);");
        w.Line("LogAudit(context, identity, \"" + rpc.Name + "\", cacheHit);");
        w.Line("return hit;");
        w.Close();
        w.Line("}");
        w.Line();

        w.Line("var restResponse = default(SharpPortico.Proxy.RestResponse);");
        w.Line("try");
        w.Line("{");
        w.Open();
        w.Line($"restResponse = _rest.Send(new global::SharpPortico.Proxy.RestRequest(\"{rpc.HttpMethod}\", path, pathParams, queryParams, headers, body, \"application/json\"));");
        w.Close();
        w.Line("}");
        w.Line("catch (global::System.Exception ex)");
        w.Line("{");
        w.Open();
        w.Line("throw new global::Grpc.Core.RpcException(new global::Grpc.Core.Status(global::Grpc.Core.StatusCode.Internal, ex.Message));");
        w.Close();
        w.Line("}");
        w.Line();

        w.Line("LogAudit(context, identity, \"" + rpc.Name + "\", cacheHit);");
        w.Line("if (restResponse.StatusCode < 200 || restResponse.StatusCode >= 300)");
        w.Line("{");
        w.Open();
        w.Line("var status = global::SharpPortico.Proxy.GrpcStatusMapper.ToGrpcStatusCode(restResponse.StatusCode);");
        w.Line($"throw new global::Grpc.Core.RpcException(new global::Grpc.Core.Status((global::Grpc.Core.StatusCode)status, global::System.Text.Encoding.UTF8.GetString(restResponse.Body)));");
        w.Close();
        w.Line("}");
        w.Line($"var result = new {rpc.ResponseType}();");
        w.Line($"Parse{camel(respMsg.Name)}(restResponse.Body, result);");
        w.Line("if (isRead && !bypass && _cache is not null) _cache.Set(cacheKey, restResponse.Body, _options.CacheTtl);");
        w.Line("return result;");
        w.Close();
        w.Line("}");
        w.Line();
    }

    private static void LogAuditHelper(CodeWriter w)
    {
        w.Line("private void LogAudit(global::Grpc.Core.ServerCallContext context, global::SharpPortico.Proxy.ClientIdentity identity, string rpcName, bool cacheHit)");
        w.Line("{");
        w.Open();
        w.Line("if (_audit is not null)");
        w.Line("    _audit.Log(new global::SharpPortico.Proxy.AuditEntry(context.Method, rpcName, identity.KeyId, identity.RemoteAddress, cacheHit, global::System.DateTime.UtcNow, null));");
        w.Close();
        w.Line("}");
    }

    private static void EmitJsonHelpers(CodeWriter w, GrpcModel model)
    {
        LogAuditHelper(w);

        foreach (var msg in model.Messages)
        {
            w.Line($"private static string Serialize{camel(msg.Name)}({msg.Name} msg)");
            w.Line("{");
            w.Open();
            w.Line("var sb = new global::System.Text.StringBuilder();");
            w.Line("sb.Append('{');");
            var first = true;
            foreach (var f in msg.Fields)
            {
                w.Line($"if (!{first.ToString().ToLowerInvariant()}) sb.Append(',');");
                w.Line($"sb.Append(\"\\\"{f.ProtoName}\\\":\");");
                EmitFieldAppend(w, f, model);
                first = false;
            }
            w.Line("sb.Append('}');");
            w.Line("return sb.ToString();");
            w.Close();
            w.Line("}");

            w.Line($"private static void Parse{camel(msg.Name)}Element(global::System.Text.Json.JsonElement el, {msg.Name} msg)");
            w.Line("{");
            w.Open();
            w.Line("if (el.ValueKind == global::System.Text.Json.JsonValueKind.Null) return;");
            foreach (var f in msg.Fields)
            {
                w.Line($"if (el.ValueKind == global::System.Text.Json.JsonValueKind.Object && el.TryGetProperty(\"{f.ProtoName}\", out var _{f.Name.ToLowerInvariant()}))");
                w.Open();
                EmitFieldRead(w, f, model);
                w.Close();
            }
            EmitRootFallback(w, msg, model);
            w.Close();
            w.Line("}");

            w.Line($"private static void Parse{camel(msg.Name)}(byte[] body, {msg.Name} msg)");
            w.Line("{");
            w.Open();
            w.Line("if (body.Length == 0) return;");
            w.Line("using var doc = global::System.Text.Json.JsonDocument.Parse(body);");
            w.Line($"Parse{camel(msg.Name)}Element(doc.RootElement, msg);");
            w.Close();
            w.Line("}");
            w.Line();
        }
    }

    private static void EmitFieldAppend(CodeWriter w, FieldModel f, GrpcModel model)
    {
        if (f.IsRepeated)
        {
            w.Line("sb.Append('[');");
            var first = "first" + f.Name;
            w.Line($"var {first} = true;");
            if (f.Kind == FieldKind.Message)
            {
                w.Line($"foreach (var item in msg.{f.Name}) {{ if (!{first}) sb.Append(','); {first} = false; sb.Append(Serialize{camel(f.TypeName!)}(item)); }}");
            }
            else
            {
                w.Line($"foreach (var item in msg.{f.Name}) {{ if (!{first}) sb.Append(','); {first} = false; {EmitRepeatedAppend(f)} }}");
            }
            w.Line("sb.Append(']');");
            return;
        }

        switch (f.Kind)
        {
            case FieldKind.String:
                w.Line($"sb.Append('\\\"').Append(msg.{f.Name}).Append('\\\"');");
                break;
            case FieldKind.Bool:
                w.Line("sb.Append(msg." + f.Name + " ? \"true\" : \"false\");");
                break;
            case FieldKind.Enum:
                w.Line($"sb.Append((int)msg.{f.Name});");
                break;
            case FieldKind.Message:
                w.Line($"if (msg.{f.Name} is not null) sb.Append(Serialize{camel(f.TypeName!)}(msg.{f.Name})); else sb.Append(\"null\");");
                break;
            default:
                w.Line($"sb.Append(global::System.Globalization.CultureInfo.InvariantCulture, $\"{{msg.{f.Name}}}\");");
                break;
        }
    }

    private static string EmitRepeatedAppend(FieldModel f) => f.Kind switch
    {
        FieldKind.String => "sb.Append('\\\"').Append(item).Append('\\\"');",
        FieldKind.Bool => "sb.Append(item ? \"true\" : \"false\");",
        FieldKind.Enum => "sb.Append((int)item);",
        _ => "sb.Append(global::System.Globalization.CultureInfo.InvariantCulture, $\"{item}\");"
    };

    private static void EmitRootFallback(CodeWriter w, MessageModel msg, GrpcModel model)
    {
        var nonSentinel = msg.Fields.Where(f => !f.Name.StartsWith("_")).ToList();

        var singleMsg = nonSentinel.Count == 1 && nonSentinel[0].Kind == FieldKind.Message && !nonSentinel[0].IsRepeated
            ? nonSentinel[0]
            : null;
        if (singleMsg is not null)
        {
            w.Line($"if (el.ValueKind == global::System.Text.Json.JsonValueKind.Object && msg.{singleMsg.Name} is null)");
            w.Open();
            w.Line($"{{ var n = new {singleMsg.TypeName}(); Parse{camel(singleMsg.TypeName!)}Element(el, n); msg.{singleMsg.Name} = n; }}");
            w.Close();
            return;
        }

        var singleRepeatedMsg = nonSentinel.Count == 1 && nonSentinel[0].IsRepeated && nonSentinel[0].Kind == FieldKind.Message
            ? nonSentinel[0]
            : null;
        if (singleRepeatedMsg is not null)
        {
            w.Line($"if (el.ValueKind == global::System.Text.Json.JsonValueKind.Array)");
            w.Open();
            w.Line($"foreach (var itemEl in el.EnumerateArray()) {{ var n = new {singleRepeatedMsg.TypeName}(); Parse{camel(singleRepeatedMsg.TypeName!)}Element(itemEl, n); msg.{singleRepeatedMsg.Name}.Add(n); }}");
            w.Close();
        }
    }

    private static void EmitFieldRead(CodeWriter w, FieldModel f, GrpcModel model)
    {
        var prop = "msg." + f.Name;
        var e = "_" + f.Name.ToLowerInvariant();

        if (f.IsRepeated)
        {
            w.Line($"if ({e}.ValueKind == global::System.Text.Json.JsonValueKind.Array)");
            w.Open();
            if (f.Kind == FieldKind.Message)
            {
                w.Line($"foreach (var itemEl in {e}.EnumerateArray()) {{ var item = new {f.TypeName}(); Parse{camel(f.TypeName!)}Element(itemEl, item); {prop}.Add(item); }}");
            }
            else
            {
                w.Line($"foreach (var itemEl in {e}.EnumerateArray()) {{ {prop}.Add({ScalarFromElement(f, "itemEl")}); }}");
            }
            w.Close();
            return;
        }

        switch (f.Kind)
        {
            case FieldKind.String:
                w.Line($"{prop} = {e}.ValueKind == global::System.Text.Json.JsonValueKind.Null ? string.Empty : {e}.GetString() ?? string.Empty;");
                break;
            case FieldKind.Message:
                w.Line($"if ({e}.ValueKind != global::System.Text.Json.JsonValueKind.Null) {{ var nested = new {f.TypeName}(); Parse{camel(f.TypeName!)}Element({e}, nested); {prop} = nested; }}");
                break;
            case FieldKind.Int32:
                w.Line($"{prop} = {e}.GetInt32();");
                break;
            case FieldKind.Int64:
                w.Line($"{prop} = {e}.GetInt64();");
                break;
            case FieldKind.UInt32:
                w.Line($"{prop} = {e}.GetUInt32();");
                break;
            case FieldKind.UInt64:
                w.Line($"{prop} = {e}.GetUInt64();");
                break;
            case FieldKind.Float:
                w.Line($"{prop} = {e}.GetSingle();");
                break;
            case FieldKind.Double:
                w.Line($"{prop} = {e}.GetDouble();");
                break;
            case FieldKind.Bool:
                w.Line($"{prop} = {e}.GetBoolean();");
                break;
            case FieldKind.Enum:
                w.Line($"{prop} = ({f.TypeName}){e}.GetInt32();");
                break;
            case FieldKind.Bytes:
                w.Line($"{prop} = global::Google.Protobuf.ByteString.CopyFrom({e}.GetBytesFromBase64());");
                break;
        }
    }

    private static string ScalarFromElement(FieldModel f, string el) => f.Kind switch
    {
        FieldKind.String => $"{el}.ValueKind == global::System.Text.Json.JsonValueKind.Null ? string.Empty : {el}.GetString() ?? string.Empty",
        FieldKind.Int32 => $"{el}.GetInt32()",
        FieldKind.Int64 => $"{el}.GetInt64()",
        FieldKind.UInt32 => $"{el}.GetUInt32()",
        FieldKind.UInt64 => $"{el}.GetUInt64()",
        FieldKind.Float => $"{el}.GetSingle()",
        FieldKind.Double => $"{el}.GetDouble()",
        FieldKind.Bool => $"{el}.GetBoolean()",
        FieldKind.Enum => $"({f.TypeName}){el}.GetInt32()",
        _ => $"{el}.GetString() ?? string.Empty"
    };

    private static string IsSet(FieldModel f) => f.Kind switch
    {
        FieldKind.String => "request." + f.Name + ".Length != 0",
        FieldKind.Bool => "request." + f.Name,
        _ => "request." + f.Name + " != 0"
    };

    private static string ToStringCall(FieldModel f) => f.Kind switch
    {
        FieldKind.String => "request." + f.Name,
        _ => "global::System.Convert.ToString(request." + f.Name + ", global::System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty"
    };

    private static string camel(string s) => char.ToLowerInvariant(s[0]) + s.Substring(1);
}