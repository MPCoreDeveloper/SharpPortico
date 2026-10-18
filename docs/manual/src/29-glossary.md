# 29. Glossary

<p class="lead">Terms as this manual uses them. Where a term has a narrower meaning here than in the wider
ecosystem, the definition says so.</p>

**AdditionalFiles** — the MSBuild item group a source generator reads. One entry per OpenAPI document, with
metadata for the service name and options (chapter 4).

**AOT (NativeAOT)** — publishing a .NET program as a native executable with no runtime code generation. The
generated messages implement serialization by hand so the result needs no reflection (chapters 1 and 18).

**allOf** — JSON Schema composition meaning "all of these". Merged into one message, in order, with duplicate
properties kept (chapter 9).

**base class** — `{Service}Base`, the abstract class a server implements. It carries `[BindServiceMethod]` and
one virtual member per RPC (chapter 19).

**BindService** — the generated static method that binds a base-class instance to a server: one overload returns
a `ServerServiceDefinition`, the other registers the implementation through a `ServiceBinderBase`
(chapter 19).

**body message** — the request message's field that carries the HTTP request body. Named `Body`; its type is
the body schema, or a generated `<Method>Body` wrapper (chapter 14).

**channel** — `GrpcChannel`, the client-side connection and HTTP/2 transport. The generated client does not
create one; the application does (chapter 20).

**client-streaming** — an RPC where the client sends a stream and the server answers once. Produced by
`x-grpc-streaming: client` or the large-POST heuristic (chapter 13).

**contract class** — `{Service}`, the static class holding `ServiceFullName`, the `Method_*` descriptors, the
marshallers, `BindService` and the nested low-level client (chapter 19).

**descriptor** — here, the `.proto` text in `{Service}Proto.Text`, not a `FileDescriptor` object. The
generated messages return `null` from `Descriptor` (chapters 3 and 18).

**DG (dependency injection)** — the generated `AddSharpPortico{Service}()` extension registers the convenience
client from an existing `GrpcChannel`; nothing else (chapters 19 and 27).

**enum** — a protobuf enumeration generated from a JSON Schema `enum`. Members are sanitised to PascalCase, and
the first member must be zero for `protoc` to accept the descriptor (chapter 11).

**grpc-dotnet** — the ASP.NET Core gRPC host (`AddGrpc`, `MapGrpcService`). It uses the
`ServiceBinderBase` overload of `BindService` (chapter 19).

**Grpc.Core** — the native gRPC library (`Server`, `Channel`, `ClientBase`). The generated contract and nested
client target it directly (chapters 19 and 20).

**hint name** — the file stem used for the descriptor class and the generated file name. It comes from the
`AdditionalFiles` path, not from `info.title` (chapter 4).

**IMessage&lt;T&gt;** — the Google.Protobuf interface with `MergeFrom`, `WriteTo`, `CalculateSize` and a
`Descriptor`. The generated messages implement it without a base class (chapter 18).

**mapping** — the whole transform from OpenAPI to the intermediate model. Not a synonym for serialization;
the gateway's JSON mapping is a separate, runtime concern (chapters 7–17 and 23).

**optional** — the proto3 label that gives a scalar explicit presence. Emitted for a nullable scalar, with
`HasX`/`ClearX` members (chapter 12).

**package** — the protobuf package in the descriptor, derived from the service name or set by `ProtoPackage`.
It is part of the wire identity (chapter 8).

**pagination** — detection of `page`/`limit`/`cursor`/`next_page_token` parameters by name. A cursor or token
turns the RPC into server-streaming; page/limit alone does not (chapter 13).

**presence** — whether a field was set, distinct from its value. Only `optional` scalars, message fields and
`bytes` can express it (chapter 12).

**proxy** — `{Service}Proxy`, the generated `{Service}Base` that answers gRPC by calling the legacy REST
service. Unary only (chapters 22 and 23).

**repeated** — protobuf's sequence type, generated for a JSON array. It has no null and no per-element presence
(chapter 10).

**request wrapper** — `{Method}Request`, the message an RPC accepts. Always generated, even when it has only
`has_value` (chapters 13 and 14).

**response wrapper** — `{Method}Response`, the message an RPC returns. Its success payload is `Data`, `Items`,
or `Body`, depending on the shape (chapter 13).

**RPC kind** — unary, server-streaming, client-streaming or bidi-streaming. Chosen by the `x-grpc-streaming`
extension, the large-POST heuristic, or pagination (chapter 13).

**schema mapper** — `SchemaMapper`, the component that turns an OpenAPI schema into a message, enum or field
model. It owns types, shapes and presence (chapters 7–12).

**service** — one generated gRPC service per document. The document's `tags` do not split it (chapter 13).

**source generator** — the Roslyn incremental generator that runs during compilation and writes the C# files.
It is not a CLI and does not invoke `protoc` (chapter 1).

**Struct** — `google.protobuf.Struct`, the well-known type for "any JSON object". Used for a free-form object
in a field position; it has no proxy serializer (chapters 9, 15 and 23).

**unary** — one request, one response. The default kind and the only kind the proxy overrides (chapters 13
and 22).

**Value** — `google.protobuf.Value`, the well-known type for any JSON value. Used for multi-type unions and
tuples; like `Struct`, it has no proxy serializer (chapters 6 and 23).

**well-known types** — protobuf's predefined messages (`Struct`, `Value`, `Timestamp`, `Duration`, `Any`,
`Empty`, wrappers). The descriptor imports the ones the model uses (chapter 15).

**wire tag** — the field number and wire type byte that precede a value on the wire. The generator emits it as
a `const uint Tag_N_Name` per field (chapter 18).

**x-grpc-streaming** — the operation-level extension that selects an RPC kind. Values are case-sensitive:
`unary`, `client`, `server`, `bidi`; anything else is unary (chapter 13).
