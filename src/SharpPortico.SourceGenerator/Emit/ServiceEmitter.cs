using System;
using System.Linq;
using System.Text;
using SharpPortico.Generator.Model;

namespace SharpPortico.Generator.Emit;

/// <summary>
/// Emits gRPC contract (Method/Marshaller/ServiceBinderBase), nested low-level client,
/// server base, C# 14 convenience client, DI extensions and auth metadata helpers.
/// All generated code is reflection-free and NativeAOT-safe.
/// </summary>
internal static class ServiceEmitter
{
    public static void Emit(CodeWriter w, GrpcModel model, OpenApiWorkItem item)
    {
        var svc = model.Services[0];

        // ---- Contract class (static descriptors + nested low-level client) ----
        w.Line("[global::System.CodeDom.Compiler.GeneratedCode(\"SharpPortico\", \"1.0.0\")]");
        w.Block($"public static partial class {svc.Name}", () =>
        {
            w.Line($"public const string ServiceFullName = \"{model.ProtoPackage}.{svc.Name}\";");
            w.Line();

            foreach (var rpc in svc.RpcMethods)
            {
                var methodKind = rpc.Kind switch
                {
                    RpcKind.ServerStreaming => "ServerStreaming",
                    RpcKind.ClientStreaming => "ClientStreaming",
                    RpcKind.BidiStreaming => "DuplexStreaming",
                    _ => "Unary"
                };
                w.Line($"public static readonly global::Grpc.Core.Method<{rpc.RequestType}, {rpc.ResponseType}> Method_{rpc.Name} =");
                w.Line($"    new(global::Grpc.Core.MethodType.{methodKind}, ServiceFullName, \"{rpc.Name}\",");
                w.Line($"        MarshallerFor<{rpc.RequestType}>(), MarshallerFor<{rpc.ResponseType}>());");
            }
            w.Line();

            w.Line("private static global::Grpc.Core.Marshaller<T> MarshallerFor<T>() where T : class, global::Google.Protobuf.IMessage<T>, new()");
            w.Line("    => global::Grpc.Core.Marshallers.Create<T>(");
            w.Line("        (T msg) =>");
            w.Line("        {");
            w.Line("            var ms = new global::System.IO.MemoryStream();");
            w.Line("            using var output = new global::Google.Protobuf.CodedOutputStream(ms, leaveOpen: true);");
            w.Line("            msg.WriteTo(output);");
            w.Line("            output.Flush();");
            w.Line("            return ms.ToArray();");
            w.Line("        },");
            w.Line("        (byte[] data) => { var m = new T(); m.MergeFrom(new global::Google.Protobuf.CodedInputStream(data)); return m; });");
            w.Line();

            w.Block($"public static global::Grpc.Core.ServerServiceDefinition BindService({svc.Name}Base serviceBase)", () =>
            {
                w.Line("return global::Grpc.Core.ServerServiceDefinition.CreateBuilder()");
                foreach (var rpc in svc.RpcMethods)
                {
                    var handler = rpc.Kind == RpcKind.Unary ? $"{rpc.Name}Handler" : rpc.Name;
                    w.Line($"    .AddMethod(Method_{rpc.Name}, serviceBase.{handler})");
                }
                w.Line("    .Build();");
            });
            w.Line();

            // Nested low-level client (ClientBase)
            w.Line($"public partial class {svc.Name}Client : global::Grpc.Core.ClientBase<{svc.Name}Client>");
            w.Line("{");
            w.Open();
            w.Line($"internal {svc.Name}Client(global::Grpc.Core.CallInvoker callInvoker) : base(callInvoker) {{ }}");
            w.Line($"internal {svc.Name}Client(global::Grpc.Net.Client.GrpcChannel channel) : base(channel.CreateCallInvoker()) {{ }}");
            w.Line($"protected {svc.Name}Client() : base() {{ }}");
            w.Line($"protected {svc.Name}Client(ClientBaseConfiguration configuration) : base(configuration) {{ }}");
            w.Line($"protected override {svc.Name}Client NewInstance(ClientBaseConfiguration configuration) => new(configuration);");
            w.Line();
            EmitCallMethods(w, svc, isNested: true);
            w.Close();
            w.Line("}");
        });
        w.Line();

        // ---- Server base ----
        if (item.EmitServer)
        {
            w.Line("[global::System.CodeDom.Compiler.GeneratedCode(\"SharpPortico\", \"1.0.0\")]");
            w.Block($"public abstract partial class {svc.Name}Base", () =>
            {
                foreach (var rpc in svc.RpcMethods)
                {
                    switch (rpc.Kind)
                    {
                        case RpcKind.Unary:
                            w.Line($"public virtual global::System.Threading.Tasks.Task<{rpc.ResponseType}> {rpc.Name}Async({rpc.RequestType} request, global::Grpc.Core.ServerCallContext context)");
                            w.Line("    => throw new global::Grpc.Core.RpcException(new global::Grpc.Core.Status(global::Grpc.Core.StatusCode.Unimplemented, \"\"));");
                            w.Line($"public global::System.Threading.Tasks.Task<{rpc.ResponseType}> {rpc.Name}Handler({rpc.RequestType} request, global::Grpc.Core.ServerCallContext context)");
                            w.Line($"    => {rpc.Name}Async(request, context);");
                            break;
                        case RpcKind.ServerStreaming:
                            w.Line($"public virtual global::System.Threading.Tasks.Task {rpc.Name}({rpc.RequestType} request, global::Grpc.Core.IServerStreamWriter<{rpc.ResponseType}> responseStream, global::Grpc.Core.ServerCallContext context)");
                            w.Line("    => global::System.Threading.Tasks.Task.CompletedTask;");
                            break;
                        case RpcKind.ClientStreaming:
                            w.Line($"public virtual global::System.Threading.Tasks.Task<{rpc.ResponseType}> {rpc.Name}(global::Grpc.Core.IAsyncStreamReader<{rpc.RequestType}> requestStream, global::Grpc.Core.ServerCallContext context)");
                            w.Line("    => throw new global::Grpc.Core.RpcException(new global::Grpc.Core.Status(global::Grpc.Core.StatusCode.Unimplemented, \"\"));");
                            break;
                        case RpcKind.BidiStreaming:
                            w.Line($"public virtual global::System.Threading.Tasks.Task {rpc.Name}(global::Grpc.Core.IAsyncStreamReader<{rpc.RequestType}> requestStream, global::Grpc.Core.IServerStreamWriter<{rpc.ResponseType}> responseStream, global::Grpc.Core.ServerCallContext context)");
                            w.Line("    => global::System.Threading.Tasks.Task.CompletedTask;");
                            break;
                    }
                    w.Line();
                }
            });
            w.Line();
        }

        // ---- C# 14 convenience client ----
        if (item.EmitClient)
        {
            w.Line("[global::System.CodeDom.Compiler.GeneratedCode(\"SharpPortico\", \"1.0.0\")]");
            w.Block($"public sealed partial class {svc.Name}Client(global::Grpc.Net.Client.GrpcChannel channel) : {svc.Name}.{svc.Name}Client(channel)", () =>
            {
                w.Line($"public static {svc.Name}Client Create(global::Grpc.Net.Client.GrpcChannel channel) => new(channel);");
                w.Line($"public static {svc.Name}Client Create(global::Grpc.Core.ChannelBase channel) => new(channel as global::Grpc.Net.Client.GrpcChannel ?? throw new global::System.NotSupportedException(\"GrpcChannel required\"));");
                w.Line();

                foreach (var rpc in svc.RpcMethods)
                {
                    if (rpc.Kind == RpcKind.Unary)
                    {
                        // request object overload (headers = per-call cache bypass / client key)
                        w.Line($"public async global::System.Threading.Tasks.Task<{rpc.ResponseType}> {rpc.Name}Async({rpc.RequestType} request, global::Grpc.Core.Metadata? headers = null, global::System.Threading.CancellationToken ct = default)");
                        w.Line($"    => await {rpc.Name}Async(request, headers, deadline: null, cancellationToken: ct);");
                        w.Line();

                        var single = SingleRequestField(model, rpc);
                        var resp = SingleResponseField(model, rpc);

                        if (single is not null)
                        {
                            w.Line($"public async global::System.Threading.Tasks.Task<{rpc.ResponseType}> {rpc.Name}Async({single.CsType} {SingleParamName(single)}, global::System.Threading.CancellationToken ct = default)");
                            w.Line($"    => await {rpc.Name}Async(new {rpc.RequestType} {{ {single.Name} = {SingleParamName(single)} }}, headers: null, ct);");
                            w.Line();
                        }

                        if (resp is not null)
                        {
                            w.Line($"public async global::System.Threading.Tasks.Task<{ElementType(resp)}> {rpc.Name}{resp.Name}Async({rpc.RequestType} request, global::System.Threading.CancellationToken ct = default)");
                            w.Line($"    => (await {rpc.Name}Async(request, headers: null, ct)).{resp.Name};");
                            w.Line();

                            if (single is not null)
                            {
                                w.Line($"public async global::System.Threading.Tasks.Task<{ElementType(resp)}> {rpc.Name}{resp.Name}Async({single.CsType} {SingleParamName(single)}, global::System.Threading.CancellationToken ct = default)");
                                w.Line($"    => await {rpc.Name}{resp.Name}Async(new {rpc.RequestType} {{ {single.Name} = {SingleParamName(single)} }}, ct);");
                                w.Line();
                            }
                        }
                    }
                    else if (rpc.Kind == RpcKind.ServerStreaming)
                    {
                        w.Line($"public global::Grpc.Core.AsyncServerStreamingCall<{rpc.ResponseType}> {rpc.Name}Stream({rpc.RequestType} request, global::System.Threading.CancellationToken ct = default)");
                        w.Line($"    => {rpc.Name}(request, cancellationToken: ct);");
                        w.Line();
                    }
                    else if (rpc.Kind == RpcKind.ClientStreaming)
                    {
                        w.Line($"public global::Grpc.Core.AsyncClientStreamingCall<{rpc.RequestType}, {rpc.ResponseType}> {rpc.Name}Stream(global::System.Threading.CancellationToken ct = default)");
                        w.Line($"    => {rpc.Name}(cancellationToken: ct);");
                        w.Line();
                    }
                    else
                    {
                        w.Line($"public global::Grpc.Core.AsyncDuplexStreamingCall<{rpc.RequestType}, {rpc.ResponseType}> {rpc.Name}Stream(global::System.Threading.CancellationToken ct = default)");
                        w.Line($"    => {rpc.Name}(cancellationToken: ct);");
                        w.Line();
                    }
                }
            });
            w.Line();
        }

        // ---- DI ----
        if (item.EmitDependencyInjection)
        {
            w.Line("[global::System.CodeDom.Compiler.GeneratedCode(\"SharpPortico\", \"1.0.0\")]");
            w.Block("public static class ServiceCollectionExtensions", () =>
            {
                w.Line($"public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddSharpPortico{svc.Name}(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
                w.Line("{");
                w.Open();
                w.Line("global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, provider =>");
                w.Line("{");
                w.Open();
                w.Line("var channel = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Grpc.Net.Client.GrpcChannel>(provider);");
                w.Line($"return new {svc.Name}Client(channel);");
                w.Close();
                w.Line("});");
                w.Line("return services;");
                w.Close();
                w.Line("}");
            });
            w.Line();
        }

        // ---- Auth metadata helpers ----
        if (item.GenerateAuthMetadataHelpers)
        {
            foreach (var scheme in model.AuthSchemes)
            {
                var suffix = SanitizeIdent(scheme.Name);
                w.Block($"public static partial class {svc.Name}Auth", () =>
                {
                    switch (scheme.Kind)
                    {
                        case AuthKind.Bearer:
                            w.Line("public static global::Grpc.Core.Metadata CreateBearerTokenMetadata(string token)");
                            w.Line("    => new() { { \"authorization\", $\"Bearer {token}\" } };");
                            break;
                        case AuthKind.ApiKey:
                            w.Line("public static global::Grpc.Core.Metadata CreateApiKeyMetadata(string key)");
                            w.Line($"    => new() {{ {{ \"{scheme.HeaderName ?? "x-api-key"}\", key }} }};");
                            break;
                        case AuthKind.OAuth2:
                            w.Line("public static global::Grpc.Core.Metadata CreateOAuth2Metadata(string accessToken)");
                            w.Line("    => new() { { \"authorization\", $\"Bearer {accessToken}\" } };");
                            break;
                        default:
                            w.Line($"public static global::Grpc.Core.Metadata Create{suffix}Metadata(string value)");
                            w.Line($"    => new() {{ {{ \"{suffix.ToLowerInvariant()}\", value }} }};");
                            break;
                    }
                });
                w.Line();
            }
        }
    }

    private static void EmitCallMethods(CodeWriter w, ServiceModel svc, bool isNested)
    {
        foreach (var rpc in svc.RpcMethods)
        {
            switch (rpc.Kind)
            {
                case RpcKind.Unary:
                    w.Line($"public virtual global::Grpc.Core.AsyncUnaryCall<{rpc.ResponseType}> {rpc.Name}Async({rpc.RequestType} request, global::Grpc.Core.Metadata? headers = null, global::System.DateTime? deadline = null, global::System.Threading.CancellationToken cancellationToken = default)");
                    w.Line($"    => {rpc.Name}Async(request, new global::Grpc.Core.CallOptions(headers, deadline, cancellationToken));");
                    w.Line($"public virtual global::Grpc.Core.AsyncUnaryCall<{rpc.ResponseType}> {rpc.Name}Async({rpc.RequestType} request, global::Grpc.Core.CallOptions options)");
                    w.Line($"    => CallInvoker.AsyncUnaryCall(Method_{rpc.Name}, null, options, request);");
                    break;
                case RpcKind.ServerStreaming:
                    w.Line($"public virtual global::Grpc.Core.AsyncServerStreamingCall<{rpc.ResponseType}> {rpc.Name}({rpc.RequestType} request, global::Grpc.Core.Metadata? headers = null, global::System.DateTime? deadline = null, global::System.Threading.CancellationToken cancellationToken = default)");
                    w.Line($"    => {rpc.Name}(request, new global::Grpc.Core.CallOptions(headers, deadline, cancellationToken));");
                    w.Line($"public virtual global::Grpc.Core.AsyncServerStreamingCall<{rpc.ResponseType}> {rpc.Name}({rpc.RequestType} request, global::Grpc.Core.CallOptions options)");
                    w.Line($"    => CallInvoker.AsyncServerStreamingCall(Method_{rpc.Name}, null, options, request);");
                    break;
                case RpcKind.ClientStreaming:
                    w.Line($"public virtual global::Grpc.Core.AsyncClientStreamingCall<{rpc.RequestType}, {rpc.ResponseType}> {rpc.Name}(global::Grpc.Core.Metadata? headers = null, global::System.DateTime? deadline = null, global::System.Threading.CancellationToken cancellationToken = default)");
                    w.Line($"    => {rpc.Name}(new global::Grpc.Core.CallOptions(headers, deadline, cancellationToken));");
                    w.Line($"public virtual global::Grpc.Core.AsyncClientStreamingCall<{rpc.RequestType}, {rpc.ResponseType}> {rpc.Name}(global::Grpc.Core.CallOptions options)");
                    w.Line($"    => CallInvoker.AsyncClientStreamingCall(Method_{rpc.Name}, null, options);");
                    break;
                case RpcKind.BidiStreaming:
                    w.Line($"public virtual global::Grpc.Core.AsyncDuplexStreamingCall<{rpc.RequestType}, {rpc.ResponseType}> {rpc.Name}(global::Grpc.Core.Metadata? headers = null, global::System.DateTime? deadline = null, global::System.Threading.CancellationToken cancellationToken = default)");
                    w.Line($"    => {rpc.Name}(new global::Grpc.Core.CallOptions(headers, deadline, cancellationToken));");
                    w.Line($"public virtual global::Grpc.Core.AsyncDuplexStreamingCall<{rpc.RequestType}, {rpc.ResponseType}> {rpc.Name}(global::Grpc.Core.CallOptions options)");
                    w.Line($"    => CallInvoker.AsyncDuplexStreamingCall(Method_{rpc.Name}, null, options);");
                    break;
            }
            w.Line();
        }
    }

    private static FieldModel? SingleRequestField(GrpcModel model, RpcModel rpc)
    {
        var msg = model.Messages.FirstOrDefault(m => m.Name == rpc.RequestType);
        var fields = msg?.Fields.Where(static f => !f.Name.StartsWith("_")).ToArray();
        return fields is { Length: 1 } ? fields[0] : null;
    }

    private static FieldModel? SingleResponseField(GrpcModel model, RpcModel rpc)
    {
        var msg = model.Messages.FirstOrDefault(m => m.Name == rpc.ResponseType);
        var fields = msg?.Fields.Where(static f => !f.Name.StartsWith("_")).ToArray();
        return fields is { Length: 1 } ? fields[0] : null;
    }

    private static string ElementType(FieldModel f)
    {
        if (f.IsRepeated)
        {
            var elem = f.Kind switch
            {
                FieldKind.Message => f.TypeName ?? "Google.Protobuf.IMessage",
                FieldKind.Enum => f.TypeName ?? "int",
                _ => f.CsType
            };
            return $"global::Google.Protobuf.Collections.RepeatedField<{elem}>";
        }
        return f.Kind switch
        {
            FieldKind.Message => f.TypeName ?? "Google.Protobuf.IMessage",
            FieldKind.Enum => f.TypeName ?? "int",
            _ => f.CsType
        };
    }

    private static string SingleParamName(FieldModel f)
        => char.ToLowerInvariant(f.Name[0]) + f.Name.Substring(1);

    private static string SanitizeIdent(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        }
        if (sb.Length == 0) sb.Append("Auth");
        var s = sb.ToString();
        return char.IsDigit(s[0]) ? "A" + s : s;
    }
}