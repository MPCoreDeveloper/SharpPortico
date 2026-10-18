# 14. Parameters, Bodies and Pagination

<p class="lead">A request message is assembled from three sources: parameters, the request body, and the
pagination heuristic. This chapter describes the order, the shapes, and the places where HTTP information has
nowhere to go.</p>

## Parameters become fields

Every parameter with a schema is mapped exactly like a property: same scalar table, same array rule, same
`$ref` handling, same presence rule. The parameter's location (`path`, `query`, `header`, `cookie`) does not
change the type.

`edge.yaml` declares a header, a cookie and a query parameter:

```yaml
parameters:
  - name: X-Trace-Id
    in: header
    schema: { type: string }
  - name: session
    in: cookie
    schema: { type: string }
  - name: URL
    in: query
    schema: { type: string }
```

```protobuf
message Op0POSTRequest
{
    string x_trace_id = 1;
    string session = 2;
    string u_r_l = 3;
}
```

The original location is not recorded in the descriptor. A consumer that needs to know whether `session` was
a cookie or a header reads the OpenAPI document; the generated client simply has a field.

Two parameter details are ignored:

- **`required`**: a missing parameter is the field's default. There is no validation, and no presence unless
  the schema says `nullable`.
- **`style`, `explode`, `allowEmptyValue`, `example`**: serialization and documentation hints, not types.

A parameter array becomes a repeated field, which is the closest protobuf equivalent to a repeated query
parameter. How the legacy service expects it encoded (`ids=1&ids=2` versus `ids=1,2`) is not represented; the
gateway does not send a repeated parameter at all, and a top-level repeated scalar makes its generated file fail
to compile (chapters 22 and 23).

## The request body

The body is mapped after the parameters, so its field number is `parameterCount + 1`.

| Request body content | Field |
| --- | --- |
| any `application/octet-stream` entry | `bytes body` |
| first entry whose schema is a `$ref` | `<ReferencedMessage> body` |
| first entry whose schema is a free-form object | `google.protobuf.Struct body` |
| first entry whose schema is an arbitrary JSON value | `google.protobuf.Value body` |
| first entry with an inline object schema | `<Method>Body body` |
| first entry with an inline array schema | `<Method>Body body`, and that message has `repeated <Element> values` |
| first entry with a scalar schema | `<Method>Body body`, and that message has `<scalar> value` |

The `application/octet-stream` check runs first and is not order-dependent. Everything else takes the first
media type entry that has a schema; a document listing `application/json` and `application/xml` therefore
uses the JSON one if it appears first. The others are ignored, rather than represented as alternatives.

`required: true` on the body does not make the field mandatory. A message field is always optional on the
wire; the generated server sees `null` when no body was sent, which is the check `PetServiceImpl` performs:

```csharp
if (request.Body is null) throw new RpcException(new Status(StatusCode.InvalidArgument, "body required"));
```

## No parameters and no body

An operation with nothing to send still needs a request message. The mapper inserts a single member:

```protobuf
message GetThingByIdRequest
{
    bool has_value = 1;
}
```

The field exists so that the message is not empty, and so a caller can distinguish a request that carried
`has_value = true` from one that carried nothing — though in practice the distinction is unused. A response
with no declared content gets the same member.

## Pagination detection

`DetectPagination` (default `true`) looks at the **proto names** of the request fields after they are mapped:

```text
page, limit, cursor, next_page_token
```

The names are configurable through `PaginationPageParameter`, `PaginationLimitParameter`,
`PaginationCursorParameter` and `PaginationNextPageTokenParameter`. Matching is case-insensitive, which
matters because proto names are lower-case: `nextPageToken` maps to `next_page_token` and matches the default.

Three outcomes follow:

| Fields present | Result |
| --- | --- |
| `page` and/or `limit` | `RpcModel.HasPagination = true`; kind unchanged |
| `cursor` or `next_page_token` | `HasPagination = true`; kind becomes server-streaming unless an explicit hint exists |
| none | no change |

The distinction is intentional. A page/limit API returns a bounded page and expects the caller to ask again;
that is still one request and one response, so it stays unary. A cursor or token API is naturally a feed;
server-streaming lets one call deliver successive pages. The heuristic cannot know whether the service
actually supports that, so an explicit `x-grpc-streaming: unary` is the way to keep a cursor-based operation
unary.

Pagination detection does **not** change any message. There is no synthesized `next_page_token` field, no
page wrapper, and no automatic loop. It marks the RPC and may change its kind; the implementation still
decides what a page means.

## Why detection is name-based

The alternative is to infer pagination from response shape: an object with `items` and `total`, or a field
called `next`. That would fire on any list response and produce streaming RPCs nobody asked for. A name is a
claim the contract makes deliberately; a shape is a coincidence. The mapping follows the claim and gives you
four properties to rename if your contract uses different words.

## Order and numbers

Field numbers are assigned in the order the mapper adds fields:

1. parameters, in the order they appear in the operation's `parameters` array;
2. the body, last.

That order is stable for a given document, and it is part of the wire contract. Reordering parameters in the
OpenAPI document renumbers the fields, which breaks compatibility with any consumer built against the previous
descriptor. Treat parameter order as you would treat a `.proto` field number: append, do not rearrange.

The mapper does not deduplicate parameter names. Two parameters called `id` — one in the path, one in the
query — produce two fields named `Id` with consecutive numbers, and the generated C# does not compile. Rename
one in the document; the mapping cannot choose which one the caller meant.

## What HTTP adds that protobuf cannot

A request message is a value. HTTP requests have properties a value does not:

| HTTP feature | In the generated contract |
| --- | --- |
| path template | not present; the proxy keeps it on the RPC model |
| query string encoding | not present; repeated fields have no encoding convention |
| header names | only as field names in the message, without the header semantics |
| cookies | ordinary fields; no cookie jar |
| content negotiation | not present; the first media type was chosen at generation time |
| `Content-Length`, `Transfer-Encoding` | transport concerns, handled by gRPC |
| conditional requests (`If-Match`, …) | ordinary string fields if declared; no semantics |

When a contract depends on one of these, the gRPC consumer has to reconstruct the convention. That is a
legitimate design: the contract says what the values are, and the transport is gRPC's. It becomes a problem
only when the legacy service's behaviour depends on the convention itself, which is the case proxy mode
exists to handle.

