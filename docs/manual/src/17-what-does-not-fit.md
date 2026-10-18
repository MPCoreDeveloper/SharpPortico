# 17. What Does Not Fit

<p class="lead">A mapping manual that only lists what maps is a sales brochure. This chapter is the ledger of
everything the generator reads and discards, everything it cannot express, and the handful of cases where the
generated code does not compile at all.</p>

## Document-level constructs

| OpenAPI | Status | Consequence |
| --- | --- | --- |
| `info.title`, `info.version` | ignored | the protobuf `package` comes from `ProtoPackage`, not from the document |
| `servers` | ignored | endpoints are configured by the channel or by `HttpRestClient`; the generator emits no address |
| `tags` | ignored | no grouping metadata; a service is one service |
| `security`, `components.securitySchemes` | partly | schemes become metadata helpers and an interceptor (chapter 21); requirements and scopes are ignored |
| `components.responses` | partly | visible through `$ref` when a success response points at one; headers are ignored |
| `components.parameters` | yes | resolved through `$ref` like any other object |
| `components.headers` | ignored | response headers are not mapped |
| `components.links` | ignored | no relationship between responses |
| `components.callbacks` | ignored | callbacks are not operations with paths in the document model |
| `externalDocs` | ignored | documentation only |
| `webhooks` (3.1) | mapped with a loss | merged into `paths` as a `POST` operation (chapter 6) |

## Operation-level constructs

| Construct | Status |
| --- | --- |
| `operationId` | RPC name (chapter 8) |
| `summary`, `description` | ignored |
| `tags`, `deprecated`, `externalDocs` | ignored |
| `parameters` | request fields (chapter 14) |
| `requestBody` | one body field (chapter 14) |
| `responses` | one success payload; others ignored |
| `security` | ignored per operation; every declared scheme is attached if auth code is enabled |
| `callbacks` | ignored |
| `servers` (operation-level) | ignored |
| `x-grpc-streaming` | streaming kind (chapter 13) |
| other `x-` extensions | ignored |

The `deprecated` flag deserves a note. gRPC has an `option deprecated = true` on services, methods and fields,
and the generator does not emit it. A deprecated operation becomes an ordinary RPC, indistinguishable from a
supported one. If that matters, the contract should not carry the operation at all, or the descriptor should be
annotated by hand.

## Schema-level constructs

| Construct | Status | Notes |
| --- | --- | --- |
| `pattern`, `minLength`, `maxLength`, `minimum`, `maximum` | ignored | no validation is generated |
| `multipleOf`, `minItems`, `maxItems`, `uniqueItems` | ignored | same |
| `minProperties`, `maxProperties` | ignored | same |
| `readOnly`, `writeOnly` | ignored | one message serves both directions |
| `default` | ignored | protobuf's zero value is the only default |
| `example`, `examples` | ignored | documentation |
| `title`, `description` | ignored | no comments in the `.proto` |
| `deprecated` | ignored | no field option |
| `discriminator` | ignored | `oneOf` is flattened (chapter 9) |
| `xml`, `externalDocs` | ignored | media-specific |
| `nullable` (3.0) | mapped | becomes `optional` (chapter 12) |
| `format` | partly | only the formats in chapter 7 change the type; unknown formats are documentation |
| `additionalProperties: true` on `type: object` | mapped | `google.protobuf.Struct` in a field position (chapter 15) |
| `additionalProperties: <schema>` | ignored | the value type is not read; an object with no `properties` becomes `Struct` (chapter 10) |
| `additionalProperties: false` | ignored | the message is still open: unknown fields are skipped on read, and the flag is not enforced |
| an inline object under a property (including a property's array `items`) | **not emitted** | the mapper derives a nested message name, but 1.5.0 does not add that message to the model, so the descriptor and C# reference an undefined type; use a named component and `$ref` |

The `additionalProperties: false` row is worth stating plainly: the generator does not produce a closed message.
`additionalProperties: false` is a promise about the document, not about the wire, and the mapping does not
pretend otherwise. Nor does the generated reader preserve unknown fields — it skips them — so a consumer that
round-trips a message built by a newer contract loses the fields it did not know. This differs from `protoc`'s
generated code, which keeps them in an `UnknownFieldSet`.

The inline-object row is a different kind of entry: it is an emitter defect, not a deliberate loss. A named
component under `components/schemas` is emitted and can be referenced; an inline object under a property (or as
that property's array `items`) is named by the mapper but never reaches `GrpcModel.Messages`. The generated
field still points at that name, so the C# and the descriptor fail to resolve it. The workaround is to declare
the object once as a component and use `$ref` at every position where it appears.

## The losses that change behaviour

Most of the table above is metadata. Four entries change what a caller can do:

1. **One success response only.** If a `200` returns a `Pet` and a `202` returns a `Job`, the response message
   carries `Pet data` and the job is unreachable. The `202` is not an error; it is simply not mapped.
2. **Error bodies are unreachable.** A `400` with `application/problem+json` does not become a message.
   Failures arrive as `RpcException` with a status code, without the problem document.
3. **No unions.** `oneOf` and `anyOf` collapse to one message with a `kind` string (chapter 9). A caller cannot
   pattern-match on which variant it received.
4. **Free-form fields break the proxy.** A `Struct` or `Value` field is fine in the descriptor and in the
   generated messages, but the generated gateway has no serializer for it (chapter 15). The proxy file does not
   compile.

The first three are contract design decisions a reviewer can see and accept. The fourth is a defect in the
current release, and it should be treated as one.

## What the 3.1 rewrite reports

The downgrade step is the one place where the generator can see that it changed the contract, so it is the one
place that warns. `SP1002` is informational: it names the constructs the rewrite had to touch. Each construct
that was mapped with a loss — a multi-type array collapsed to `google.protobuf.Value`, a tuple collapsed to a
`repeated` field, a webhook moved into `paths`, a non-base64 `contentEncoding` removed, a `jsonSchemaDialect`
dropped — is a `SP1003` warning that says what was lost and why.

Those warnings are the closest thing to a loss report for 3.1 input. For 3.0 input there is no equivalent,
because nothing is rewritten: `discriminator`, `additionalProperties: false` and ignored headers are dropped
silently. The tables above are the only complete record.

## A worked example: `sink.yaml`

`sink.yaml` is the fixture that exercises mapping breadth, not a realistic API. It declares, among other
things, free-form objects, a union, binary payloads and recursion. Generated from it:

- `labels` (`additionalProperties: { type: string }`) and `meta` (`additionalProperties: true`) both become
  `google.protobuf.Struct`; the declared value type of `labels` is not represented;
- `anything: {}` becomes a `string`, because an empty schema has no type and `string` is the fallback;
- `both: oneOf: [string, integer]` also becomes a `string`; a property-level union is not the JSON-value marker,
  which only the 3.1 rewrite adds;
- `blob` (`format: byte`) and `photo` (`format: binary`) become `bytes`, and the octet-stream response becomes
  `bytes body`;
- `child` and `children` refer back to `Widget`, which is legal in both models and produces a recursive message.

The `.proto` compiles and the messages are usable. The proxy for the same document does not compile, because of
the `Struct` fields. That is the shape of the current boundary: the contract side is broadly faithful,
the gateway side is narrow.

## How to decide whether a loss matters

Three questions, in order:

1. **Does a caller need the discarded information to use the service correctly?** Status codes, error bodies
   and variant discriminators usually do. `summary` and `tags` do not.
2. **Can the information be recovered from the request or response values?** A `next` link inside the body can
   stand in for response headers; a `kind` field can stand in for a discriminator if the producer fills it in.
3. **Is the workaround inside the contract?** If yes, change the document — add the fields, split the
   operation, declare explicit error messages. If no, the gRPC contract cannot express it, and the honest
   choice is to keep that operation on REST or to write the gRPC service by hand.

The generator's job is to make the first question answerable. It does that by being predictable: the same
document always produces the same descriptor, and every departure from the document is one of the rules in
this part.

