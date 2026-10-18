# 27. The Generated Surface, Member by Member

<p class="lead">This is the reference page: what exists in the namespace after a build, what each member is for,
and which option switches it off. Signatures are shown as they are emitted, with the service name from
`petstore.yaml` (`PetstoreService`) as the example.</p>

## Messages and enums

| Member | Shape | Option |
| --- | --- | --- |
| one class per message | `public sealed partial class Pet : IMessage<Pet>` | always |
| one property per field | scalar, message, `RepeatedField<T>`, or presence-backed `optional` | always |
| `Parser`, `Descriptor`, `Clone`, `Equals`, `GetHashCode`, `ToString`, `WriteTo`, `CalculateSize`, `MergeFrom`, `IsInitialized` | `IMessage<T>` implementation | always |
| one enum per enumeration | `public enum StatusEnum { Available = 0, … }` | always |
| descriptor constant | `public static partial class PetstoreServiceProto { public const string Text = "…"; }` | `EmitProtoFile` |

`Descriptor` returns `null!`; chapter 18 explains what that costs. The descriptor text is LF-normalized and is
the same string `sharpportico generate --out` writes.

## The contract class

```csharp
public static partial class PetstoreService
{
    public const string ServiceFullName = "petstoreservice.PetstoreService";

    public static readonly Method<ListPetsRequest, ListPetsResponse> Method_ListPets;
    public static readonly Method<CreatePetRequest, CreatePetResponse> Method_CreatePet;
    public static readonly Method<GetPetRequest, GetPetResponse> Method_GetPet;

    public static ServerServiceDefinition BindService(PetstoreServiceBase serviceBase);
    public static void BindService(ServiceBinderBase binder, PetstoreServiceBase serviceImpl);

    public partial class PetstoreServiceClient : ClientBase<PetstoreServiceClient> { … }
}
```

The nested client is what `protoc` would call the stub. Its constructors are `internal`/`protected`; application
code uses the convenience client.

## The server base

```csharp
[BindServiceMethod(typeof(PetstoreService), "BindService")]
public abstract partial class PetstoreServiceBase
{
    public virtual Task<ListPetsResponse> ListPetsAsync(ListPetsRequest request, ServerCallContext context);
    public virtual Task<CreatePetResponse> CreatePetAsync(CreatePetRequest request, ServerCallContext context);
    public virtual Task<GetPetResponse> GetPetAsync(GetPetRequest request, ServerCallContext context);
}
```

Streaming kinds replace the unary shape with the matching `IServerStreamWriter`/`IAsyncStreamReader` parameters
(chapter 19). The unary shape also emits `XHandler` and `X` forwarding members; override `XAsync`.

## The convenience client

```csharp
public sealed partial class PetstoreServiceClient : PetstoreService.PetstoreServiceClient
{
    public static PetstoreServiceClient Create(GrpcChannel channel);
    public static PetstoreServiceClient Create(ChannelBase channel);       // throws unless GrpcChannel

    public Task<ListPetsResponse> ListPetsAsync(ListPetsRequest request, Metadata? headers = null,
        CancellationToken ct = default);
    public Task<CreatePetResponse> CreatePetAsync(CreatePetRequest request, Metadata? headers = null,
        CancellationToken ct = default);
    public Task<GetPetResponse> GetPetAsync(GetPetRequest request, Metadata? headers = null,
        CancellationToken ct = default);
    public Task<GetPetResponse> GetPetAsync(long petId, CancellationToken ct = default);
    public Task<Pet> GetPetDataAsync(GetPetRequest request, CancellationToken ct = default);
    public Task<Pet> GetPetDataAsync(long petId, CancellationToken ct = default);
}
```

The request-scalar and response-field overloads appear only when the request or response has exactly one field
(chapter 20). A streaming RPC gets `XStream(…)` returning the matching call object.

## Dependency injection

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSharpPorticoPetstoreService(this IServiceCollection services);
}
```

It registers one singleton `PetstoreServiceClient` built from a `GrpcChannel` already in the container. It does
not register a channel, a server or a proxy.

## Authentication

```csharp
public static partial class PetstoreServiceAuth
{
    public static Metadata CreateBearerTokenMetadata(string token);
    public static Metadata CreateApiKeyMetadata(string key);
}

public sealed class PetstoreServiceAuthInterceptor : Interceptor
{
    public PetstoreServiceAuthInterceptor(Func<string?> credentialFactory);
}
```

Both are emitted only when the document declares a security scheme and the corresponding option is on. The
interceptor attaches the credential to every call shape; the helpers are per-call (chapter 21).

## The proxy

```csharp
public sealed partial class PetstoreServiceProxy : PetstoreServiceBase
{
    public PetstoreServiceProxy(
        ProxyOptions options,
        IRestClient rest,
        IProxyCache? cache = null,
        IKeyProvider? keys = null,
        IClientKeyValidator? validator = null,
        IProxyAuditLogger? audit = null);
}
```

It overrides unary RPCs only. `EnableProxyGeneration` (default `false`) emits it, and the project must reference
`SharpPortico.Runtime` (chapters 22–23).

## Options that change the surface

| Option | Default | Effect on names or members |
| --- | --- | --- |
| `EmitProtoFile` | `true` | removes `{Service}Proto` |
| `EmitClient` | `true` | removes the convenience client; set `EmitDependencyInjection=false` too, because the DI extension constructs that client and will not compile without it |
| `EmitServer` | `true` | removes `{Service}Base`; the two `BindService` overloads remain and reference it, so the file does not compile with this switch off alone |
| `EmitDependencyInjection` | `true` | removes `ServiceCollectionExtensions` |
| `GenerateAuthMetadataHelpers` | `true` | removes `{Service}Auth` |
| `GenerateAuthInterceptors` | `true` | removes `{Service}AuthInterceptor` |
| `EnableProxyGeneration` | `false` | adds `{Service}Proxy` |
| `ServiceNameSuffix` | `Service` | changes every name in this chapter |
| `Namespace` / `ProtoPackage` | derived | changes the C# namespace and the proto package |

