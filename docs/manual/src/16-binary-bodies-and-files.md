# 16. Binary Bodies and Files

<p class="lead">OpenAPI describes binary payloads in several ways, and protobuf has exactly one type for them:
`bytes`. This chapter covers which source constructs become `bytes`, what a download looks like as a message,
and why binary payloads are the weakest part of proxy mode.</p>

## What becomes `bytes`

| OpenAPI | Proto |
| --- | --- |
| `type: string, format: binary` | `bytes` |
| `type: string, format: byte` | `bytes` (base64 in JSON already) |
| `type: string, contentEncoding: base64` (3.1) | `bytes` |
| a request body of `application/octet-stream` | `bytes body = 1` |
| a response of `application/octet-stream` | `bytes body = 1` |
| `type: string` with no format | `string` — not bytes |

The `format: binary` rule matters most. In 3.0, a file upload is usually described as
`type: string, format: binary`, which does **not** mean a string with strange characters; it means an octet
stream. Mapping it to `string` would force a UTF-8 assumption the contract never made. `sink.yaml` declares a
binary response and gets:

```protobuf
message UploadPhotoResponse
{
    bytes body = 1;
}
```

A 3.1 document may use `contentEncoding: base64` on a string. The downgrade step rewrites it to
`format: byte`, which the mapper maps to `bytes`, so the two versions converge (chapter 6).

## A binary response is a message

A successful `application/octet-stream` response does not become a bare `bytes` RPC return; it becomes a
message with one `bytes` field. That keeps the convention that every RPC returns a message, which is what the
generated client and server code assumes, and it leaves room for a future metadata field without changing the
RPC signature.

The consequence is that the consumer unwraps one level:

```csharp
var response = await client.UploadPhotoAsync(new UploadPhotoRequest { Id = id, Body = photo });
byte[] stored = response.Body.ToByteArray();
```

`Body` is Google.Protobuf's `ByteString`. `ToByteArray()` copies; `Span`/`Memory` access is available on
modern runtimes without the copy. The message does not carry the HTTP `Content-Type`, `Content-Disposition`,
`ETag` or the filename. A contract that needs them has to declare them as response headers and map them
separately — and response headers other than the success payload are not mapped at all, so in practice the
filename is lost.

## Uploads

A request body of `application/octet-stream` becomes `bytes body`. If the body is declared as a schema
instead — an object with a `content` property of `format: binary` — the message has a `bytes Content` field
inside a `<Method>Body` message. Both are single messages; neither is a stream.

A large binary body does **not** trigger the client-streaming heuristic. The estimate in chapter 13 counts 256
bytes for any media type without an object schema, which is below the default threshold regardless of the real
size, so an upload stays unary. Streaming an upload requires an explicit `x-grpc-streaming: client`, and then
each request message carries one `bytes` chunk that the service reassembles.

`multipart/form-data` is not special-cased. If the document gives it an object schema, the mapper builds that
message and the file part becomes whatever its schema says; if the document only names the media type without
a schema, no body field is emitted. There is no boundary parsing, no part names, and no filename.

## Proxy mode and binary payloads

The gateway serializes message bodies as JSON and sends them with `application/json`. Two consequences follow
from the generated code:

- an `application/octet-stream` body is a `bytes` field, not a message field, and the proxy only serializes a
  `body` field whose kind is `Message`. A binary request body is therefore **not sent**;
- a `bytes` field anywhere in a request message — a parameter or a body — makes the generated proxy reference
  a comparison that does not type-check, because the generated "is this set" test treats every non-string,
  non-bool field as numeric.

So a contract with binary request payloads can produce messages, a server and a client, but the generated proxy
for it will not compile or will drop the payload. Chapter 23 states the same limit from the runtime side. This
is a 1.5.0 boundary, not a design recommendation: a binary API needs a hand-written adapter or a gRPC-native
backend.

Binary **responses** are less affected in the descriptor but equally affected in the gateway: the legacy
service's octet stream arrives as an HTTP body, and the generated proxy has no `bytes` response path. The same
conclusion holds.

## When to use base64 instead

If the binary payload is small and the contract is JSON-shaped — an avatar thumbnail, a signature, a
certificate — declaring it as `type: string, format: byte` and sending base64 inside a JSON body keeps the
whole contract inside the parts of the pipeline that work. The cost is a third more bytes and an encode/decode
step; the benefit is that the proxy, the presence rules and the JSON helpers all behave as documented.

For large files, neither base64 nor the generated proxy is the right tool. Serve the bytes over HTTP and keep
the gRPC contract for metadata, or implement the file RPC by hand.
