# 5. Options Reference

<p class="lead">Every option below is a named argument on `[OpenApiToGrpc]`. The table gives its default and
whether the generator reads it — because a switch that does nothing is worse than no switch at all, and this
manual says which is which.</p>

## Emission

| Option | Default | Read? | Effect |
| --- | --- | --- | --- |
| `EmitProtoFile` | `true` | yes | emits `{spec}.proto.cs`, the `{Service}Proto.Text` descriptor |
| `EmitClient` | `true` | yes | emits the convenience `{Service}Client`; the nested `Grpc.Core` client is always emitted. Turn `EmitDependencyInjection` off too when this is off, or the DI extension will not compile |
| `EmitServer` | `true` | yes | removes `{Service}Base`; the contract class still emits both `BindService` overloads, which take that type, so turning it off alone does not compile in 1.5.0 |
| `EmitDependencyInjection` | `true` | yes | emits `ServiceCollectionExtensions.AddSharpPortico{Service}()` |
| `EmitGeneratedCodeAttribute` | `true` | **no** | every generated class always carries `[GeneratedCode("SharpPortico", version)]`; enums do not |
| `UseFileScopedNamespaces` | `true` | **no** | generated code always uses a block-scoped `namespace { … }` |

The last two exist on the attribute and in `SharpPorticoOptions`, but no emitter consults them. Setting either
to `false` changes nothing.

## Streaming and pagination

| Option | Default | Read? | Effect |
| --- | --- | --- | --- |
| `RespectStreamingHints` | `true` | yes | honours `x-grpc-streaming` and the large-body heuristic |
| `LargePayloadStreamingThresholdBytes` | `1_000_000` | yes | a `POST` body estimated at or above this size becomes client-streaming |
| `DetectPagination` | `true` | yes | recognises pagination parameters and may switch an RPC to server-streaming |
| `PaginationPageParameter` | `"page"` | yes | parameter name treated as the page number |
| `PaginationLimitParameter` | `"limit"` | yes | parameter name treated as the page size |
| `PaginationCursorParameter` | `"cursor"` | yes | parameter name treated as a cursor; implies server-streaming |
| `PaginationNextPageTokenParameter` | `"next_page_token"` | yes | parameter name treated as a next-page token; implies server-streaming |

The parameter names are compared against the **proto** names of request fields, case-insensitively. A query
parameter `pageSize` therefore does not match `limit`; set `PaginationLimitParameter` to `"page_size"` if your
contract uses that spelling. Chapter 14 gives the full detection rules.

## Mapping and errors

| Option | Default | Read? | Effect |
| --- | --- | --- | --- |
| `MapEnumsToProtobufEnums` | `true` | **no** | enumerations are always protobuf enums; `false` does not turn them into C# enums in messages |
| `EmitDiagnosticsForUnmappableConstructs` | `true` | yes | reports `SP2000` for nullable arrays and nullable elements |
| `EmitGoogleRpcStatusWrapper` | `true` | yes | when an operation declares no successful response, emits a `{Method}Error` message with `code` and `message` |
| `Default4xxStatus` | `InvalidArgument` | **no** | unused; the gateway's HTTP-to-gRPC table is fixed (chapter 22) |
| `Default5xxStatus` | `Internal` | **no** | unused, for the same reason |

`EmitGoogleRpcStatusWrapper` does not wrap a response that exists; it covers an operation with no `200`,
`201`, `204` or `default` response at all. Turning it off leaves a response with only `has_value`.

## Authentication

| Option | Default | Read? | Effect |
| --- | --- | --- | --- |
| `GenerateAuthMetadataHelpers` | `true` | yes | emits `{Service}Auth` with a `Create…Metadata` method per security scheme |
| `GenerateAuthInterceptors` | `true` | yes | emits `{Service}AuthInterceptor` attaching the credential to every call shape |

Both are conditional on the document declaring `components.securitySchemes`. With no schemes, neither type is
generated regardless of the flags. The full behaviour is in chapter 21.

## Naming

| Option | Default | Read? | Effect |
| --- | --- | --- | --- |
| `ServiceNameSuffix` | `"Service"` | yes | appended to a derived service name when it does not already end with it |
| `MessageNaming` | `PascalCase` | **no** | message and field naming is fixed by the mapping rules in chapter 8 |
| `MethodNaming` | `PascalCase` | **no** | RPC names always come from `operationId` through PascalCase sanitising |
| `StripApiVersionFromServiceName` | `true` | **no** | a `v1`/`v2` segment is never stripped; name the service explicitly to remove it |

## Proxy

| Option | Default | Read? | Effect |
| --- | --- | --- | --- |
| `EnableProxyGeneration` | `false` | yes | emits `{Service}Proxy : {Service}Base` |
| `ProxyBaseUrl` | `servers[0].url`, else empty | **no** | resolved into the internal proxy model, but no emitter reads it; the address is supplied to `HttpRestClient` (chapter 22) |
| `ProxyApiKeyHeaderName` | `"X-Api-Key"` | **no** | accepted, but the generated proxy reads `ProxyOptions.ApiKeyHeaderName` at run time |
| `ProxyCacheTtlSeconds` | `60` | **no** | accepted, but the generated proxy reads `ProxyOptions.CacheTtl` at run time |
| `ProxyBypassCacheMetadataKey` | `"x-portico-bypass-cache"` | **no** | accepted, but the generated proxy reads `ProxyOptions.BypassCacheMetadataKey` at run time |
| `ProxyClientKeyHeaderName` | `"x-portico-key"` | **no** | accepted, but the generated proxy reads `ProxyOptions.ClientKeyHeaderName` at run time |
| `ProxyClientKeyMode` | `ClientKeyMode.None` | **no** | accepted, but the generated proxy reads `ProxyOptions.ClientKeyMode` at run time |
| `ProxyAuditEnabled` | `false` | **no** | accepted, but auditing is controlled by whether an `IProxyAuditLogger` is passed to the proxy constructor |

Only `EnableProxyGeneration` changes what the build emits. The other proxy properties are carried into the
internal model but not consulted by the emitter; the generated proxy reads the equivalent properties from the
`ProxyOptions` instance it is given at run time. Chapter 22 shows the object graph.

## One attribute, many options

Named arguments are the only way to set these; the attribute constructor takes just the three strings.

```csharp
[assembly: OpenApiToGrpc(
    "openapi/fleet.yaml",
    "FleetService",
    "MyApp.Fleet.Generated",
    RespectStreamingHints = true,
    LargePayloadStreamingThresholdBytes = 4_000_000,
    DetectPagination = true,
    PaginationLimitParameter = "page_size",
    EmitDiagnosticsForUnmappableConstructs = true,
    GenerateAuthMetadataHelpers = true,
    GenerateAuthInterceptors = true,
    EnableProxyGeneration = true)]
```

`ProxyClientKeyMode` is of type `SharpPortico.ClientKeyMode`, but it is not read either; the generated proxy uses
`ProxyOptions.ClientKeyMode` at run time. The other enum-valued properties (`Default4xxStatus`, `Default5xxStatus`,
`MessageNaming`, `MethodNaming`) are accepted by the compiler and are also not read, as noted above.

## `SharpPorticoOptions` is a mirror, not a second input

`src/SharpPortico.Abstractions/SharpPorticoOptions.cs` declares a class with the same properties and the same
defaults, plus `SharpPorticoOptions.Default`. It exists so code that manipulates options can do so as one
object, because an attribute argument cannot be a class instance.

The generator does **not** read it. A project that constructs `SharpPorticoOptions` and expects the build to
change is disappointed: only the attribute's named arguments reach the pipeline. The class carries a doc
comment saying exactly this, and the generator's internal `OpenApiWorkItem` is the single source of truth for
defaults.

## Defaults as a work item

The effective configuration for a build is the `OpenApiWorkItem`. Its defaults are visible in
`AttributeRequest.ToWorkItem()` and `AdditionalFileRequest.ToWorkItem()`:

| Setting | Default in both routes |
| --- | --- |
| `RespectStreamingHints` | `true` |
| `LargePayloadStreamingThresholdBytes` | `1_000_000` |
| `EmitDiagnosticsForUnmappableConstructs` | `true` |
| `GenerateAuthMetadataHelpers` | `true` |
| `GenerateAuthInterceptors` | `true` |
| `DetectPagination` | `true` |
| page / limit / cursor / next token | `page` / `limit` / `cursor` / `next_page_token` |
| `EmitGoogleRpcStatusWrapper` | `true` |
| `ServiceNameSuffix` | `Service` |
| `EnableProxyGeneration` | `false` |
| `ProxyCacheTtlSeconds` | `60` |
| `ProxyClientKeyMode` | `None` |
| `ProxyAuditEnabled` | `false` |

The `AdditionalFiles` route has no way to override these; it exists to make a contract generate with sane
defaults in a project that only wants the contract. Anything else is an attribute.

## What to check before adding an option

1. **Is it in the "Read? yes" list?** If not, changing it is a no-op; the behaviour is fixed by the mapping.
2. **Does the document support it?** Auth options do nothing without `securitySchemes`; proxy options do
   nothing without `EnableProxyGeneration`.
3. **Is the name a proto name?** Pagination matching happens after field naming, so it compares
   `next_page_token`, not `nextPageToken`.
4. **Does the option change types?** Almost none do. The type of a field is the mapping's decision (Part III);
   options affect RPC kinds, emitted extras and the gateway, not scalar choices.

The next chapter is the one input distinction that does change types: whether the document declares OpenAPI
3.0 or 3.1.

