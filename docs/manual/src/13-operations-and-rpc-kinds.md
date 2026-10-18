# 13. Operations and RPC Kinds

<p class="lead">Every operation under `paths` becomes one RPC. Most are unary; a hint or a size heuristic can
make one streaming. This chapter states how an operation is chosen, named, typed and given its kind.</p>

## One operation, one RPC

`MapPathOperations` walks `document.Paths` in document order, then each path item's operations, and calls
`MapOperation` for every one. No verb is skipped: `GET`, `POST`, `PUT`, `PATCH`, `DELETE`, `HEAD`, `OPTIONS`
and `TRACE` all become RPCs. The HTTP method is recorded on the RPC model (`HttpMethod`) and used by the CLI
report and the gateway, not by the protobuf descriptor.

The descriptor carries only the RPC name and the message types:

```protobuf
service PetService
{
    rpc ListPets (ListPetsRequest) returns (ListPetsResponse);
    rpc CreatePet (CreatePetRequest) returns (CreatePetResponse);
    rpc GetPet (GetPetRequest) returns (GetPetResponse);
}
```

There is no `google.api.http` annotation and no path in the descriptor. A consumer that needs the REST
mapping reads the OpenAPI document or the CLI report; the generated C# method descriptors expose the service
name and method name only.

## Naming

The RPC name is `SanitizePascal(operationId)` (chapter 8). Without an `operationId` the name is
`Op{index}_{METHOD}`, sanitised: the first operation in `edge.yaml` has no id, and the result is `Op0POST`.

The two wrapper messages are named `{Rpc}Request` and `{Rpc}Response` in every case, including streaming. A
streaming RPC still has one request type and one response type; `stream` changes how many messages travel,
not which messages they are.

## The four kinds

| Kind | `.proto` | Trigger |
| --- | --- | --- |
| unary | `rpc X (Req) returns (Resp);` | default |
| server-streaming | `rpc X (Req) returns (stream Resp);` | `x-grpc-streaming: server`, or a cursor/next-token parameter |
| client-streaming | `rpc X (stream Req) returns (Resp);` | `x-grpc-streaming: client`, or a large `POST` body |
| bidirectional | `rpc X (stream Req) returns (stream Resp);` | `x-grpc-streaming: bidi` |

The extension is read from the operation:

```yaml
post:
  operationId: uploadReadings
  x-grpc-streaming: client
```

Values are matched case-sensitively against `client`, `server` and `bidi`; anything else, including `unary`,
sets the kind to unary. `RespectStreamingHints = false` ignores the extension entirely, which is the switch to
reach for when a legacy document carries the annotation for another tool.

## The large-body heuristic

With `RespectStreamingHints` on and no explicit hint, a `POST` whose request body is estimated at or above
`LargePayloadStreamingThresholdBytes` (default `1_000_000`) becomes **client-streaming**.

The estimate is deliberately crude, because the generator never sees a payload:

```csharp
foreach (var media in body.Content.Values)
{
    total += media?.Schema?.Type == "object" && media.Schema.Properties is { } props
        ? props.Count * 64L
        : 256;
}
```

An object body is assumed to be 64 bytes per property; anything else is 256 bytes. A body with 16,000
properties crosses the default threshold. In practice the heuristic fires rarely, and when it does the
document's shape — not a measured size — is the reason. If client-streaming is wrong for an operation, set
`x-grpc-streaming: unary` explicitly or turn `RespectStreamingHints` off.

Only `POST` is considered. A large `PUT` or `PATCH` stays unary, because those verbs usually describe a
single replacement rather than a feed.

## Pagination changes the kind

When `DetectPagination` is on and the request contains a field named like the cursor or next-page-token
parameter, a unary RPC becomes **server-streaming** unless the document already supplied a streaming hint:

```yaml
parameters:
  - name: cursor
    in: query
    schema: { type: string }
```

```protobuf
rpc ListEvents (ListEventsRequest) returns (stream ListEventsResponse);
```

A page/limit pair does **not** change the kind; it only marks the RPC as paginated, which the proxy and
report can use. Chapter 14 covers the detection rules and why the cursor is treated differently.

## Request and response shape

The request message contains, in order:

1. one field per path, query and header parameter, in the order the operation declares them;
2. the request body, as `body`, when there is one.

The response message contains the success response's payload:

| Success response | Response message |
| --- | --- |
| `$ref` schema | `{ <Message> data = 1; }` |
| inline object | `{ <Method>Data data = 1; }` |
| array | `{ repeated <Element> items = 1; }` |
| `application/octet-stream` | `{ bytes body = 1; }` |
| no content | `{ bool has_value = 1; }` |
| no success response at all | `{ <Method>Error error = 1; }` when `EmitGoogleRpcStatusWrapper` is on |

"Success" means the first of `200`, `201`, `204` or `default`, in that order. A `204` with no content is a
legal response and produces `has_value`; it is not an error. An operation with no response section at all is
the case the error wrapper exists for.

## Streaming on the client

The nested `Grpc.Core` client exposes the four shapes with the usual signatures:

| Kind | Client method |
| --- | --- |
| unary | `AsyncUnaryCall<Resp> XAsync(Req request, …)` |
| server | `AsyncServerStreamingCall<Resp> X(Req request, …)` |
| client | `AsyncClientStreamingCall<Req, Resp> X(…)` |
| bidi | `AsyncDuplexStreamingCall<Req, Resp> X(…)` |

Only the unary method has `Async` in its name; the streaming methods are named after the RPC, which is what
`Grpc.Core`'s own generated clients do. The convenience client adds `XStream(…)` methods that return the same
call objects.

The server base is shaped by the kind:

| Kind | Method to override |
| --- | --- |
| unary | `Task<Resp> XAsync(Req request, ServerCallContext context)` (plus `XHandler` and `X`) |
| server | `Task X(Req request, IServerStreamWriter<Resp> responseStream, ServerCallContext context)` |
| client | `Task<Resp> X(IAsyncStreamReader<Req> requestStream, ServerCallContext context)` |
| bidi | `Task X(IAsyncStreamReader<Req> requestStream, IServerStreamWriter<Resp> responseStream, ServerCallContext context)` |

Server-streaming and bidi defaults complete without writing anything, rather than throwing `Unimplemented`,
because a stream that ends immediately is a valid empty implementation. Client-streaming and unary throw
`Unimplemented`, so a forgotten override fails loudly.

## What an operation loses

- **The HTTP method and path.** They are not in the descriptor. A gRPC consumer cannot tell `GET /pets` from
  `POST /pets` if both were mapped with the same request and response types.
- **Status codes.** Success status is not represented; error statuses become `RpcException` at run time, and
  the mapping from HTTP to gRPC exists only in the gateway (chapter 22).
- **Headers as a group.** A header parameter becomes an ordinary request field. Reserved headers such as
  `Authorization` are not treated specially by the mapping; authentication is a separate concern (chapter 21).
- **Media types.** Only the first media type with a schema is read; `application/json` and
  `application/octet-stream` get special treatment, and the rest are ignored (chapter 15).
- **Operation metadata.** `summary`, `description`, `tags`, `deprecated` and `externalDocs` are not emitted.

These are not oversights to work around; they are the boundary between an HTTP contract and a protobuf one.
The next chapter goes one level down, into the parameters and bodies that fill the request message.

