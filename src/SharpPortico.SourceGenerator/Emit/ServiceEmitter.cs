using System.Linq;
using System.Text;
using SharpPortico.Generator.Model;

namespace SharpPortico.Generator.Emit;

/// <summary>
/// Emits gRPC contract (Method/Marshaller/ServiceBinderBase), nested low-level client,
/// server base, C# 14 convenience client, DI extensions and auth metadata helpers plus the
/// client interceptor that attaches them.
/// All generated code is reflection-free and NativeAOT-safe.
/// </summary>
internal static class ServiceEmitter
{
    private const string GeneratedCodeAttribute =
        "[global::System.CodeDom.Compiler.GeneratedCode(\"SharpPortico\", \"1.0.0\")]";

    public static void Emit(CodeWriter w, GrpcModel model, OpenApiWorkItem item)
    {
        var svc = model.Services[0];
        EmitContractClass(w, model, svc);
        if (item.EmitServer) EmitServerBase(w, svc);
        if (item.EmitClient) EmitConvenienceClient(w, model, svc);
        if (item.EmitDependencyInjection) EmitDiExtensions(w, svc);
        if (item.GenerateAuthMetadataHelpers) EmitAuthHelpers(w, model, svc);
        if (item.GenerateAuthInterceptors) EmitAuthInterceptor(w, model, svc);
    }

    /// <summary>A contract name as a gRPC metadata key. gRPC requires metadata keys to be lowercase.</summary>
    private static string MetadataKey(string name) => name.ToLowerInvariant();

    /// <summary>
    /// The metadata entries the contract's security schemes call for: the key each credential travels under,
    /// deduplicated across schemes, plus the C# expression that produces its value from a credential.
    /// </summary>
    private static System.Collections.Generic.List<(string Name, string Value)> AuthHeaders(GrpcModel model)
    {
        var headers = new System.Collections.Generic.List<(string Name, string Value)>();
        var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var scheme in model.AuthSchemes)
        {
            string name;
            string value;

            switch (scheme.Kind)
            {
                case AuthKind.Bearer:
                case AuthKind.OAuth2:
                    // Both kinds of token travel in the standard authorization header.
                    name = "authorization";
                    value = "\"Bearer \" + credential";
                    break;
                case AuthKind.ApiKey:
                    // An apiKey scheme that travels in the query string has no header of its own, so its
                    // parameter name is the only thing the metadata can be keyed by.
                    name = scheme.HeaderName ?? scheme.QueryParameterName ?? scheme.Name;
                    value = "credential";
                    break;
                default:
                    name = scheme.Name;
                    value = "credential";
                    break;
            }

            if (name.Length > 0 && seen.Add(name))
            {
                headers.Add((MetadataKey(name), value));
            }
        }

        return headers;
    }

    private static void EmitContractClass(CodeWriter w, GrpcModel model, ServiceModel svc)
    {
        // ---- Contract class (static descriptors + nested low-level client) ----
        w.Line(GeneratedCodeAttribute);
        w.Block($"public static partial class {svc.Name}", () =>
        {
            w.Line($"public const string ServiceFullName = \"{model.ProtoPackage}.{svc.Name}\";");
            w.Line();
            EmitMethodDescriptors(w, svc);
            w.Line();
            EmitMarshaller(w);
            w.Line();
            EmitBindService(w, svc);
            w.Line();
            EmitNestedClient(w, svc);
        });
        w.Line();
    }

    private static void EmitMethodDescriptors(CodeWriter w, ServiceModel svc)
    {
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
    }

    private static void EmitMarshaller(CodeWriter w)
    {
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
    }

    private static void EmitBindService(CodeWriter w, ServiceModel svc)
    {
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

        // The same binding in the shape grpc-dotnet's binder asks for, which is what makes the service
        // hostable by MapGrpcService on ASP.NET Core and not only by the Grpc.Core server. The base
        // class carries [BindServiceMethod] pointing here; the service instance is always null on that
        // path, because grpc-dotnet resolves the implementation per call and binds by method name.
        w.Block($"public static void BindService(global::Grpc.Core.ServiceBinderBase serviceBinder, {svc.Name}Base serviceImpl)", () =>
        {
            foreach (var rpc in svc.RpcMethods)
            {
                var delegateType = rpc.Kind switch
                {
                    RpcKind.ServerStreaming => "ServerStreamingServerMethod",
                    RpcKind.ClientStreaming => "ClientStreamingServerMethod",
                    RpcKind.BidiStreaming => "DuplexStreamingServerMethod",
                    _ => "UnaryServerMethod"
                };

                var handler = rpc.Kind == RpcKind.Unary ? $"{rpc.Name}Async" : rpc.Name;

                w.Line($"serviceBinder.AddMethod(Method_{rpc.Name}, serviceImpl == null");
                w.Line($"    ? null");
                w.Line($"    : new global::Grpc.Core.{delegateType}<{rpc.RequestType}, {rpc.ResponseType}>(serviceImpl.{handler}));");
            }
        });
    }

    private static void EmitNestedClient(CodeWriter w, ServiceModel svc)
    {
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
        EmitCallMethods(w, svc);
        w.Close();
        w.Line("}");
    }

    private static void EmitServerBase(CodeWriter w, ServiceModel svc)
    {
        // ---- Server base ----
        // The attribute is how grpc-dotnet's binder finds the binder method: it walks base types looking
        // for [BindServiceMethod] and then takes the named method from the type it points at.
        w.Line(GeneratedCodeAttribute);
        w.Line($"[global::Grpc.Core.BindServiceMethod(typeof({svc.Name}), \"BindService\")]");
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
                        // grpc-dotnet binds a handler by the RPC's own name and signature, so the base
                        // carries one under that name too. It forwards to Async, which stays the method an
                        // implementation overrides, and the Grpc.Core path keeps using the Handler shim.
                        w.Line($"public virtual global::System.Threading.Tasks.Task<{rpc.ResponseType}> {rpc.Name}({rpc.RequestType} request, global::Grpc.Core.ServerCallContext context)");
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

    private static void EmitConvenienceClient(CodeWriter w, GrpcModel model, ServiceModel svc)
    {
        // ---- C# 14 convenience client ----
        w.Line(GeneratedCodeAttribute);
        w.Block($"public sealed partial class {svc.Name}Client(global::Grpc.Net.Client.GrpcChannel channel) : {svc.Name}.{svc.Name}Client(channel)", () =>
        {
            w.Line($"public static {svc.Name}Client Create(global::Grpc.Net.Client.GrpcChannel channel) => new(channel);");
            w.Line($"public static {svc.Name}Client Create(global::Grpc.Core.ChannelBase channel) => new(channel as global::Grpc.Net.Client.GrpcChannel ?? throw new global::System.NotSupportedException(\"GrpcChannel required\"));");
            w.Line();

            foreach (var rpc in svc.RpcMethods)
            {
                if (rpc.Kind == RpcKind.Unary)
                {
                    EmitUnaryConvenienceMethods(w, model, rpc);
                }
                else
                {
                    EmitStreamingConvenienceMethod(w, rpc);
                }
            }
        });
        w.Line();
    }

    private static void EmitUnaryConvenienceMethods(CodeWriter w, GrpcModel model, RpcModel rpc)
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

    private static void EmitStreamingConvenienceMethod(CodeWriter w, RpcModel rpc)
    {
        if (rpc.Kind == RpcKind.ServerStreaming)
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

    private static void EmitDiExtensions(CodeWriter w, ServiceModel svc)
    {
        // ---- DI ----
        w.Line(GeneratedCodeAttribute);
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

    private static void EmitAuthHelpers(CodeWriter w, GrpcModel model, ServiceModel svc)
    {
        // ---- Auth metadata helpers ----
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
                        w.Line($"    => new() {{ {{ \"{MetadataKey(scheme.HeaderName ?? "x-api-key")}\", key }} }};");
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

    /// <summary>
    /// Emits the client interceptor that attaches the contract's credentials to every outbound call.
    /// </summary>
    /// <remarks>
    /// A metadata helper only works at a call site that remembers to pass its result. An interceptor attaches
    /// the credential whatever the call site looks like, which is what <c>GenerateAuthInterceptors</c> promises,
    /// and it is emitted whenever the contract declares a security scheme.
    /// </remarks>
    private static void EmitAuthInterceptor(CodeWriter w, GrpcModel model, ServiceModel svc)
    {
        var headers = AuthHeaders(model);
        if (headers.Count == 0)
        {
            return;
        }

        // The doc comment has to precede the attribute: between an attribute and its declaration the compiler
        // reports it as a stray XML comment (CS1587).
        w.Line($"/// <summary>Attaches the auth metadata of the {svc.Name} contract to every outbound call.</summary>");
        w.Line(GeneratedCodeAttribute);
        w.Block($"public sealed class {svc.Name}AuthInterceptor : global::Grpc.Core.Interceptors.Interceptor", () =>
        {
            w.Line("private readonly global::System.Func<string?> _credentialFactory;");
            w.Line();
            w.Line("/// <summary>Creates the interceptor.</summary>");
            w.Line("/// <param name=\"credentialFactory\">Supplies the credential for each call, so a rotated token is");
            w.Line("/// picked up without rebuilding the interceptor. Returning null or the empty string sends the call");
            w.Line("/// without credentials.</param>");
            w.Line($"public {svc.Name}AuthInterceptor(global::System.Func<string?> credentialFactory)");
            w.Line("    => _credentialFactory = credentialFactory ?? throw new global::System.ArgumentNullException(nameof(credentialFactory));");
            w.Line();
            // Every shape of call has to carry the credential, or the one call kind left out fails only for
            // whoever happens to use it.
            w.Line("/// <inheritdoc />");
            w.Line("public override global::Grpc.Core.AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(");
            w.Line("    TRequest request, global::Grpc.Core.Interceptors.ClientInterceptorContext<TRequest, TResponse> context, global::Grpc.Core.Interceptors.Interceptor.AsyncUnaryCallContinuation<TRequest, TResponse> continuation)");
            w.Line("    => continuation(request, WithCredential(context));");
            w.Line();
            w.Line("/// <inheritdoc />");
            w.Line("public override global::Grpc.Core.AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(");
            w.Line("    TRequest request, global::Grpc.Core.Interceptors.ClientInterceptorContext<TRequest, TResponse> context, global::Grpc.Core.Interceptors.Interceptor.AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)");
            w.Line("    => continuation(request, WithCredential(context));");
            w.Line();
            w.Line("/// <inheritdoc />");
            w.Line("public override global::Grpc.Core.AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(");
            w.Line("    global::Grpc.Core.Interceptors.ClientInterceptorContext<TRequest, TResponse> context, global::Grpc.Core.Interceptors.Interceptor.AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)");
            w.Line("    => continuation(WithCredential(context));");
            w.Line();
            w.Line("/// <inheritdoc />");
            w.Line("public override global::Grpc.Core.AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(");
            w.Line("    global::Grpc.Core.Interceptors.ClientInterceptorContext<TRequest, TResponse> context, global::Grpc.Core.Interceptors.Interceptor.AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)");
            w.Line("    => continuation(WithCredential(context));");
            w.Line();
            w.Line("/// <inheritdoc />");
            w.Line("public override TResponse BlockingUnaryCall<TRequest, TResponse>(");
            w.Line("    TRequest request, global::Grpc.Core.Interceptors.ClientInterceptorContext<TRequest, TResponse> context, global::Grpc.Core.Interceptors.Interceptor.BlockingUnaryCallContinuation<TRequest, TResponse> continuation)");
            w.Line("    => continuation(request, WithCredential(context));");
            w.Line();
            w.Line("/// <summary>Returns the call context with the contract's credentials attached.</summary>");
            w.Line("/// <remarks>The caller's own metadata is copied rather than replaced, so per-call metadata survives.</remarks>");
            w.Line("private global::Grpc.Core.Interceptors.ClientInterceptorContext<TRequest, TResponse> WithCredential<TRequest, TResponse>(");
            w.Line("    global::Grpc.Core.Interceptors.ClientInterceptorContext<TRequest, TResponse> context)");
            w.Line("    where TRequest : class");
            w.Line("    where TResponse : class");
            w.Line("{");
            w.Open();
            w.Line("var credential = _credentialFactory();");
            w.Line("if (global::System.String.IsNullOrEmpty(credential)) return context;");
            w.Line();
            w.Line("var headers = new global::Grpc.Core.Metadata();");
            w.Line("if (context.Options.Headers is not null)");
            w.Line("{");
            w.Open();
            w.Line("foreach (var existing in context.Options.Headers) headers.Add(existing);");
            w.Close();
            w.Line("}");
            w.Line();
            foreach (var header in headers)
            {
                w.Line($"headers.Add(\"{header.Name}\", {header.Value});");
            }
            w.Line();
            w.Line("return new global::Grpc.Core.Interceptors.ClientInterceptorContext<TRequest, TResponse>(");
            w.Line("    context.Method, context.Host!, context.Options.WithHeaders(headers));");
            w.Close();
            w.Line("}");
        });
        w.Line();
    }

    private static void EmitCallMethods(CodeWriter w, ServiceModel svc)
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
        var fields = msg?.Fields.Where(static f => f.Name.Length > 0 && f.Name[0] != '_').ToArray();
        return fields is { Length: 1 } ? fields[0] : null;
    }

    private static FieldModel? SingleResponseField(GrpcModel model, RpcModel rpc)
    {
        var msg = model.Messages.FirstOrDefault(m => m.Name == rpc.ResponseType);
        var fields = msg?.Fields.Where(static f => f.Name.Length > 0 && f.Name[0] != '_').ToArray();
        return fields is { Length: 1 } ? fields[0] : null;
    }

    /// <summary>
    /// The C# type a convenience overload hands back for a response's field: the field's own type, resolved exactly
    /// as the message declaration resolves it.
    /// </summary>
    /// <remarks>
    /// Resolved here as well until it was not: this used to name the *proto* type for a message or an enum field,
    /// which is the same string as the C# type for every message this generator declares itself and a type that does
    /// not exist for the ones it does not - so a free-form response emitted a convenience overload returning
    /// `Task&lt;google.protobuf.Struct&gt;`, and the consumer's build failed on a generated file with `CS0246`.
    /// A repeated field is handed back whole, as its property is declared.
    /// </remarks>
    /// <param name="f">The field.</param>
    /// <returns>The C# type of it.</returns>
    private static string ElementType(FieldModel f) =>
        f.IsRepeated
            ? $"global::Google.Protobuf.Collections.RepeatedField<{MessageEmitter.FieldPropertyType(f)}>"
            : MessageEmitter.FieldPropertyType(f);

    private static string SingleParamName(FieldModel f)
        => char.ToLowerInvariant(f.Name[0]) + f.Name.Substring(1);

    private static string SanitizeIdent(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Where(static ch => char.IsLetterOrDigit(ch)))
        {
            sb.Append(c);
        }
        if (sb.Length == 0) sb.Append("Auth");
        var s = sb.ToString();
        return char.IsDigit(s[0]) ? "A" + s : s;
    }
}