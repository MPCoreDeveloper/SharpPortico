# 20. The Client Surface

<p class="lead">There are two clients per service: a low-level one that mirrors what `protoc` generates, and a
convenience one that removes the request wrapper when the contract allows it. Both are in the same file and
both use the same method descriptors.</p>

## The low-level client

The nested `PetService.PetServiceClient` derives from `Grpc.Core.ClientBase<T>` and has one method per RPC in
the shape its kind requires:

```csharp
public virtual AsyncUnaryCall<ListPetsResponse> ListPetsAsync(
    ListPetsRequest request, Metadata? headers = null, DateTime? deadline = null,
    CancellationToken cancellationToken = default)
    => ListPetsAsync(request, new CallOptions(headers, deadline, cancellationToken));

public virtual AsyncUnaryCall<ListPetsResponse> ListPetsAsync(
    ListPetsRequest request, CallOptions options)
    => CallInvoker.AsyncUnaryCall(Method_ListPets, null, options, request);
```

Every kind follows this pair: an overload that builds `CallOptions` from named arguments, and one that takes
`CallOptions` whole. Streaming methods have the same pair with the matching call type and, for client and bidi
streaming, no request argument:

| Kind | Call type | Method shape |
| --- | --- | --- |
| unary | `AsyncUnaryCall<Resp>` | `XAsync(Req, …)` |
| server | `AsyncServerStreamingCall<Resp>` | `X(Req, …)` |
| client | `AsyncClientStreamingCall<Req, Resp>` | `X(…)` |
| bidi | `AsyncDuplexStreamingCall<Req, Resp>` | `X(…)` |

The `CallOptions` overload is the one to use when a deadline, a custom `WriteOptions` or per-call credentials
have to be precise. The named-argument overload covers the common case.

The constructors are `internal` or `protected`:

```csharp
internal PetServiceClient(CallInvoker callInvoker) : base(callInvoker) { }
internal PetServiceClient(GrpcChannel channel) : base(channel.CreateCallInvoker()) { }
```

so application code reaches the client through the convenience type's `Create` methods, and a test can reach it
through a subclass that exposes the protected constructor with its own `CallInvoker`. That is why the
`protected PetServiceClient(ClientBaseConfiguration)` constructor exists: `ClientBase<T>` needs it to build the
per-call instance.

## The convenience client

`public sealed partial class PetServiceClient : PetService.PetServiceClient` is the type most call sites use:

```csharp
using var channel = GrpcChannel.ForAddress("http://localhost:50051");
var client = PetServiceClient.Create(channel);

var pet = await client.GetPetAsync(1);                 // long → GetPetResponse
var name = await client.GetPetDataAsync(1);            // long → Pet
```

`Create` has two overloads: one takes a `GrpcChannel`, the other a `ChannelBase`. The second throws
`NotSupportedException` unless the argument is a `GrpcChannel`, because the generated constructors need one.
That is a deliberate restriction: a `CallInvoker`-based test channel does not go through `Create`.

For each unary RPC the convenience client emits up to four overloads, depending on the request and response
shapes:

| Condition | Overload |
| --- | --- |
| always | `Task<Resp> XAsync(Req request, Metadata? headers = null, CancellationToken ct = default)` |
| request has exactly one field | `Task<Resp> XAsync(T value, CancellationToken ct = default)` |
| response has exactly one field | `Task<T> X{Field}Async(Req request, CancellationToken ct = default)` |
| both | `Task<T> X{Field}Async(T value, CancellationToken ct = default)` |

"Exactly one field" excludes the synthetic `has_value` member, so an empty request does not produce a
zero-argument overload. The scalar overload's parameter name is the field name with a lower-case first letter
(`PetId` → `petId`). The response overload's name is the RPC name plus the field name (`GetPetDataAsync`), not a
generic `GetAsync`. A repeated response field still counts as one field: the overload returns the whole
`RepeatedField<T>`, not one element.

The `headers` parameter is per-call metadata. It is also the only way to bypass the proxy's response cache on a
single call (chapter 22), which is why it appears on the request-object overload rather than being folded into
`CallOptions`.

For streaming RPCs the convenience client adds one method per RPC, named `XStream`:

```csharp
var call = client.ListEventsStream(new ListEventsRequest());
await foreach (var page in call.ResponseStream.ReadAllAsync())
{
    // one response message per iteration
}
```

The `XStream` method returns the same call object the low-level method returns; it exists so a call site can
use one client type for every kind without remembering which method carries the `Async` suffix.

## The channel is yours

Nothing in the generated client chooses an address. The `servers` section of the OpenAPI document is ignored
(chapter 17), and there is no generated default endpoint. The channel is where transport decisions live:

```csharp
using var channel = GrpcChannel.ForAddress("https://api.example.com", new GrpcChannelOptions
{
    MaxReceiveMessageSize = 16 * 1024 * 1024,
    MaxSendMessageSize = 16 * 1024 * 1024,
    HttpHandler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true }
});
```

A `GrpcChannel` is thread-safe, long-lived and expensive to build; create one per process or per target and
share it. `PetServiceClient` itself is stateless apart from its `CallInvoker`, so a single instance per channel
is enough. The DI extension registers exactly that: one singleton client over the resolved channel.

The generated convenience client does **not** set a deadline, and its overloads do not take one. A call with
no deadline waits until the transport gives up. For anything user-facing, use the low-level client, which does
expose `deadline`:

```csharp
var lowLevel = new PetService.PetServiceClient(channel.CreateCallInvoker());
await lowLevel.GetPetAsync(new GetPetRequest { PetId = 1 },
    deadline: DateTime.UtcNow.AddSeconds(5));
```

The convenience overloads cannot forward a deadline; use the low-level client when it has to be per-call, or
configure a default with an interceptor.

## Failures

Every call can throw `RpcException`. The useful members are `StatusCode`, `Detail` (the message the server
sent) and `Trailers`. A client-side mapping from status code to application error belongs in one place in your
code, not at every call site:

```csharp
try
{
    return await client.GetPetAsync(id);
}
catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
{
    return null;
}
```

`CancellationToken` cancellation surfaces as `RpcException` with `StatusCode.Cancelled`, not as
`OperationCanceledException`. That is a gRPC convention, not a generator choice, and it surprises callers who
are used to `HttpClient`.

The client cannot recover the HTTP status code or the error body the legacy service returned. The gateway maps
some of that into gRPC status codes (chapter 22), but the mapping is one-way and lossy.

## Interceptors

A client interceptor is a `Grpc.Core.Interceptors.Interceptor`, and it attaches at a `CallInvoker`:

```csharp
var invoker = channel.Intercept(new LoggingInterceptor());
var client = new PetService.PetServiceClient(invoker);   // low-level client
```

The nested client's `CallInvoker` constructor is `internal`, and the generated file is compiled into your
assembly, so this is legal from your own code. It is also the reason the constructor is not `public`: the
intended entry point is `Create`.

The convenience client cannot wrap an intercepted invoker, because `Create` accepts only a `GrpcChannel` and a
channel has no interceptor list. If a project needs an interceptor on every call, use the nested client (or
write a small subclass-free factory of your own). The generated auth interceptor is written for exactly this
slot and handles all five call shapes (chapter 21).

## What the client does not do

- **No retries or hedging.** gRPC's retry policy is channel configuration; the generated code does not add one.
- **No serialization of query strings.** The client speaks gRPC, not REST. Even a contract generated from a
  document full of `GET`s sends one unary call with a request message.
- **No caching.** Only the proxy has a response cache, and only for `GET`-shaped calls.
- **No discovery.** No reflection, no service registry. The address is the channel's.

The distinction is worth keeping in mind for the next part: a generated client talks to a generated server,
or to any other gRPC implementation of the same contract. It does not talk to the REST service directly. The
proxy exists to bridge that last gap.

