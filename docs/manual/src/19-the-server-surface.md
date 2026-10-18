# 19. The Server Surface

<p class="lead">For each service the generator emits a contract class, a base class to implement, and — when
asked — DI registration and authentication helpers. This chapter covers how the pieces bind to a real server
and what an implementation is expected to override.</p>

## The contract class

`public static partial class PetstoreService` holds everything that describes the service:

```csharp
public const string ServiceFullName = "petstoreservice.PetstoreService";

public static readonly Grpc.Core.Method<ListPetsRequest, ListPetsResponse> Method_ListPets =
    new(Grpc.Core.MethodType.Unary, ServiceFullName, "ListPets",
        MarshallerFor<ListPetsRequest>(), MarshallerFor<ListPetsResponse>());

private static Grpc.Core.Marshaller<T> MarshallerFor<T>() where T : class, IMessage<T>, new()
    => Grpc.Core.Marshallers.Create<T>(serialize, deserialize);

public static ServerServiceDefinition BindService(PetstoreServiceBase serviceBase) { … }

public static void BindService(ServiceBinderBase serviceBinder, PetstoreServiceBase serviceImpl) { … }
```

The method name in the descriptor is the RPC name, not the C# method name: `ListPets`. The service name is
`{ProtoPackage}.{ServiceName}`. Both matter to a gRPC caller. The method name comes from the document; the
package and service name come from options (chapter 5).

`MarshallerFor` is built on `Grpc.Core.Marshallers.Create`, which takes a serialize and a deserialize delegate.
The generated delegates write to a `MemoryStream` through `CodedOutputStream` and read through
`CodedInputStream`; no reflection and no descriptor is involved (chapter 18). Each method descriptor is
`static readonly`, so the marshaller pair is created once per RPC at type initialization.

There are two `BindService` overloads because there are two server hosts:

| Overload | Used by |
| --- | --- |
| `BindService(PetstoreServiceBase)` → `ServerServiceDefinition` | `Grpc.Core.Server` |
| `BindService(ServiceBinderBase, PetstoreServiceBase)` → `void` | `grpc-dotnet`'s `MapGrpcService<PetstoreServiceBase>()` |

The base class carries `[BindServiceMethod(typeof(PetstoreService), "BindService")]`, which is how grpc-dotnet finds
the second overload. The first is what a `Grpc.Core` host calls:

```csharp
var server = new Server
{
    Services = { PetstoreService.BindService(new PetstoreServiceImpl()) },
    Ports = { new ServerPort("localhost", 50051, ServerCredentials.Insecure) }
};
server.Start();
```

## The base class

`PetstoreServiceBase` is abstract and has one virtual member per RPC, in the shape its kind requires:

| Kind | Member to override | Default |
| --- | --- | --- |
| unary | `Task<Resp> XAsync(Req request, ServerCallContext context)` | throws `Unimplemented` |
| server | `Task X(Req request, IServerStreamWriter<Resp> stream, ServerCallContext context)` | completes |
| client | `Task<Resp> X(IAsyncStreamReader<Req> stream, ServerCallContext context)` | throws `Unimplemented` |
| bidi | `Task X(IAsyncStreamReader<Req> in, IServerStreamWriter<Resp> out, ServerCallContext context)` | completes |

The unary shape also carries two forwarding members: `XHandler` (used by the `Grpc.Core` binding) and `X`
itself (used when grpc-dotnet binds by the RPC's own name). Both forward to `XAsync`, so overriding `XAsync` is
enough; overriding `X` would be bypassed by the `Grpc.Core` path.

The defaults are deliberate. A unary RPC that was never implemented should fail loudly, because a caller
expects a response. A streaming RPC that was never implemented should end cleanly, because an empty stream is a
valid answer and because a server-streaming handler that throws `Unimplemented` after the first message is
harder to diagnose than a stream that simply ends.

The `PetServiceImpl` in the sample overrides three unary methods and nothing else:

```csharp
public override Task<GetPetResponse> GetPetAsync(GetPetRequest request, ServerCallContext context)
{
    if (!_pets.TryGetValue(request.PetId, out var pet))
        throw new RpcException(new Status(StatusCode.NotFound, $"pet {request.PetId} not found"));
    return Task.FromResult(new GetPetResponse { Data = pet });
}
```

## Errors and the call context

A handler reports failure by throwing `RpcException` with a `Status`. That is the only error channel; there is
no error return type and no error field on a response message. `StatusCode.InvalidArgument`,
`NotFound`, `PermissionDenied`, `FailedPrecondition` and `Internal` are the usual choices; the HTTP status the
OpenAPI document declared does not choose one for you (chapter 22 maps in the other direction, gateway only).

`ServerCallContext` provides:

- `CancellationToken` — cancelled when the client cancels or the deadline expires; pass it to your I/O;
- `RequestHeaders` and `ResponseTrailers` — the gRPC metadata, including the auth headers in chapter 21;
- `Peer`, `Host`, `Method` and `Deadline` — transport information;
- `WriteResponseHeadersAsync` — for server-streaming handlers that need to send metadata before the first
  message.

For server-streaming, write to `IServerStreamWriter<Resp>` and return when the stream is done. For
client-streaming, `IAsyncStreamReader<Req>.MoveNext()` until it returns false, then return one response. None of
these helpers buffer the stream; backpressure is the transport's.

## Hosting on ASP.NET Core

The `ServiceBinderBase` overload is what `MapGrpcService` uses:

```csharp
builder.Services.AddGrpc();
builder.Services.AddScoped<PetstoreServiceBase, PetstoreServiceImpl>();

var app = builder.Build();
app.MapGrpcService<PetstoreServiceBase>();
```

`AddGrpc` supplies the HTTP/2 endpoint and the marshallers; the generated binder supplies the methods. Any
dependency injection the implementation needs arrives through its constructor, because grpc-dotnet resolves the
service type per call from the request scope.

The generated `ServiceCollectionExtensions.AddSharpPorticoPetstoreService()` is for the **client** side only. It
registers a singleton `PetstoreServiceClient` built from a `GrpcChannel` that the application has already
registered:

```csharp
builder.Services.AddSingleton(GrpcChannel.ForAddress("https://localhost:5001"));
builder.Services.AddSharpPorticoPetstoreService();
```

There is no generated server registration, because the server type is your implementation, not a generated
one. A `AddSharpPorticoPetstoreService` that guessed a lifetime for `PetstoreServiceImpl` would be wrong for half its
users.

## Threading and lifetime

A handler may be called concurrently. The base class holds no state, and neither do the generated messages, so
concurrency safety is entirely your implementation's concern. Both the `Grpc.Core` and grpc-dotnet hosts invoke
handlers on the thread pool and do not serialize calls to one service instance.

The sample sidesteps the question by using a `Dictionary<long, Pet>` guarded by nothing — fine for one client,
wrong for a server. A real implementation needs a concurrent collection or a lock, and a scoped service
registered as scoped should not be stored in a singleton.

Server-streaming and bidi handlers stay alive for the duration of the stream. They must observe
`context.CancellationToken`, or a client that disconnects leaves a loop writing to a dead stream until the next
write fails.

## What the server surface does not include

- **No HTTP transcoding.** `MapGrpcService` serves gRPC (and gRPC-Web if configured); it does not serve REST.
  The OpenAPI paths are not routes on this server. For that, see the proxy in part V.
- **No authorization policy.** The auth helpers generate metadata and an interceptor for the client; the server
  sees headers and decides. Chapter 21 covers what is and is not generated.
- **No health, reflection or metrics service.** Add `Grpc.HealthCheck`, `Grpc.AspNetCore.Server.Reflection`
  (which cannot work without descriptors, chapter 18) or OpenTelemetry yourself.
- **No interceptors.** A server interceptor is registered with the host, not generated; the generated binder
  does not stand in the way of one.

The split is intentional: the generator owns the contract, and the host owns the process. The next chapter is
the other side of the same split, on the client.

