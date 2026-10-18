# 22. The Gateway: gRPC in Front of REST

<p class="lead">Proxy mode generates a `{Service}Proxy : {Service}Base` that answers gRPC calls by calling the
legacy REST service the document describes. It is the only generated type that makes an HTTP request, and the
only one whose behaviour depends on options read at run time rather than at build time.</p>

## Enabling it

`EnableProxyGeneration` (default `false`) adds one file value: the proxy class. It needs the
`SharpPortico.Runtime` package, which supplies `ProxyOptions`, `IRestClient`, the cache, the key provider and the
error mapper. Nothing is auto-registered; the host builds the object graph, as the sample does:

```csharp
var options = new ProxyOptions
{
    BaseUrl = "http://localhost:5099",
    ApiKeyHeaderName = "X-Api-Key",
    CacheTtl = TimeSpan.FromSeconds(60),
    ClientKeyMode = ClientKeyMode.None
};

var rest = new HttpRestClient(httpClient, options.BaseUrl);
var proxy = new PetServiceProxy(options, rest,
    cache: cache, keys: keys, validator: validator, audit: audit);

var server = new Server { Services = { PetService.BindService(proxy) }, … };
```

`ProxyOptions.BaseUrl` is **not read by the generated proxy**. The address lives in the `HttpRestClient`
constructor, which is why the sample passes the same string twice. Treat `BaseUrl` as documentation on the options
object, not as the switch that moves the gateway.

The build-time proxy options follow the same rule. `EnableProxyGeneration` decides whether the proxy file is
emitted; `ProxyBaseUrl`, `ProxyApiKeyHeaderName`, `ProxyCacheTtlSeconds`, `ProxyBypassCacheMetadataKey`,
`ProxyClientKeyHeaderName`, `ProxyClientKeyMode` and `ProxyAuditEnabled` are accepted but not read by the
emitter. The generated proxy takes the matching values from the `ProxyOptions` instance, and auditing happens
when an `IProxyAuditLogger` is supplied (chapter 5).

`SharpPortico.Proxy.ProxyServiceCollectionExtensions.AddSharpPorticoProxyCore(options)` registers an
`IHttpClientFactory`, an `IMemoryCache`, the options, a configuration-backed `IKeyProvider`, the default cache and
the audit logger. It does not register the generated proxy or `IRestClient`; those are yours.

## One call, step by step

For a unary RPC the generated override does the following, in order:

1. **Classifies the call.** `isRead` is true only when the original HTTP method was `GET`. Everything else is a
   write for caching purposes, including `HEAD`.
2. **Reads identity.** The client key is read from the gRPC metadata key named by `ClientKeyHeaderName`
   (`x-portico-key` by default), and the peer address from `ServerCallContext.Peer`.
3. **Chooses the outbound key.** `ClientKeyMode.None` uses `IKeyProvider`; `Forward` sends the inbound key as the
   outbound API key; `Own` validates the inbound key with `IClientKeyValidator` and then uses `IKeyProvider`.
   An invalid key in `Own` mode throws `Unauthenticated`.
4. **Builds the path.** The original OpenAPI path template is a literal in the generated code.
5. **Adds every set scalar request field** to both a path-parameter dictionary and a query-parameter dictionary.
6. **Serializes the body**, if the request has a `Body` message field, to UTF-8 JSON.
7. **Looks in the cache** for a read when a cache is installed and the bypass flag is absent.
8. **Sends** a `RestRequest(method, path, pathParams, queryParams, headers, body, "application/json")`.
9. **Maps a non-2xx status** to a gRPC status and throws `RpcException` with the response body as the message.
10. **Parses the 2xx body** into the response message, caches it if the call was a read, and returns.

The `headers` dictionary contains exactly one entry: the outbound API key, when there is one. No inbound gRPC
metadata is forwarded.

## What gets sent

The request the legacy service sees is not the request the OpenAPI document describes. Three rules explain
almost all of the difference:

- **Every singular scalar field is a query parameter.** Path parameters are also added to the path, so a `GET
  /pets/{petId}` with `PetId = 7` becomes `/pets/7?PetId=7`. A server that ignores unknown query parameters is
  fine; a strict one is not.
- **Names are C# field names.** The query key is `PetId`, not `petId` or `pet_id`. The path substitution is
  case-insensitive, so `{petId}` is filled correctly, but the duplicate query entry carries the C# spelling.
- **Only a `Body` message field is a body.** A body that mapped to a scalar (`bytes body`) has no serialization
  path, and a non-optional `bytes` field also makes the file fail to compile (see below). A request with path and
  body fields sends the body as `application/json` regardless of the media type the contract declared.

Parameters that came from headers are query parameters here, because the parser does not record where a
parameter came from (chapter 14) and the proxy has nothing to decide with. Message-valued fields are skipped entirely: they are neither path, query nor body unless the field is the
body itself. Repeated scalar fields are not skipped, but the generated `IsSet` test compares the collection
with `0`, so a proxy for such a request does not compile; chapter 23 records that failure.

## What comes back

Any 2xx is success, whatever the document's success code was. The body is parsed as JSON even when the response
`Content-Type` says otherwise. A 204 or an empty body leaves the response message at its defaults, which is why
the empty-response `has_value` member exists.

A non-2xx becomes `RpcException` with a mapped code and the raw response body as `Detail`:

| HTTP | gRPC |
| --- | --- |
| 400, 422 | `InvalidArgument` (3) |
| 401, 403 | `Unauthenticated` (16) |
| 404 | `NotFound` (5) |
| 408, 504 | `DeadlineExceeded` (4) |
| 409 | `AlreadyExists` (6) |
| 429 | `ResourceExhausted` (8) |
| 500, 502 | `Internal` (13) |
| 503 | `Unavailable` (14) |
| 501, 505 | `Unimplemented` (12) |
| anything else | `Unknown` (2) |

`403` mapping to `Unauthenticated` rather than `PermissionDenied` is the mapper's choice, not gRPC's
convention. A `405`, `410` or `418` is `Unknown`. The error body is plain text in `Detail`; a structured problem
document is not parsed.

## The cache

The cache is keyed by `{Service}/{Rpc}/{path}` followed by every query entry in insertion order. It stores the
raw response bytes and returns them without calling the legacy service. Only `GET`-shaped RPCs are cached; the
`CacheReadsOnly` option is declared but not consulted, so setting it to `false` does not enable write caching.

A call bypasses the cache when the gRPC metadata entry named by `BypassCacheMetadataKey` (`x-portico-bypass-cache`
by default) is `true`. That is the per-call escape hatch:

```csharp
var headers = new Metadata { { "x-portico-bypass-cache", "true" } };
await client.GetPetAsync(new GetPetRequest { PetId = 1 }, headers);
```

There is no invalidation on writes, no variation by client identity, no `Vary` handling and no negative caching.
A write through the gateway leaves every cached read stale until its TTL expires. For a contract with more than
trivial write traffic, install `NullProxyCache` or keep the TTL very short.

## What the gateway cannot do

- **Streaming.** Only unary RPCs are overridden. A server-streaming RPC falls back to the base implementation,
  which completes an empty stream; client- and bidi-streaming RPCs throw `Unimplemented`. Because pagination can
  turn a list operation into server streaming (chapter 13), a paginated contract may produce a gateway method
  that silently returns nothing.
- **Non-JSON payloads.** `application/octet-stream`, `multipart/form-data` and form-encoded bodies are not
  handled. Binary request fields are skipped, binary response fields are not parsed.
- **Header parameters.** They become query parameters. An API that requires `X-Tenant` as a header will not see
  it.
- **Cancellation and deadlines.** `HttpRestClient.Send` is synchronous and passes `CancellationToken.None`. A
  client that cancels a gRPC call does not cancel the outbound HTTP request; the `HttpClient.Timeout` is the only
  limit.
- **Free-form and binary fields.** `Struct`, `Value` and non-optional `bytes` fields make the generated proxy
  file fail to compile, for the reasons in chapters 15 and 16.

These are not configuration gaps. They are the edges of what a proxy generated from a lossy contract can know.
Chapter 23 is the JSON mapping in detail, including the one defect that affects otherwise supported contracts.

