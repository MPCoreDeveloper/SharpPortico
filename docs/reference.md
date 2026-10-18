# Reference — 1.5.0

The complete inventory of what this version ships: the packages, the ways a specification can be declared, every option,
the generated surface, the runtime contracts and the CLI. Behaviour that needs an explanation rather than a listing lives
next to it: [mapping rules](SharpPortico.md#4-mapping-rules-summary), [presence](presence.md),
[diagnostics](diagnostics.md), [proxy mode](SharpPortico.md#3-proxy-mode--grpc-clients--legacy-rest).

---

## 1. Packages

| Package | Version | Contents | Target frameworks |
| --- | --- | --- | --- |
| `SharpPortico.SourceGenerator` | 1.5.0 | the incremental generator under `analyzers/dotnet/cs` (and `lib/netstandard2.0`, for hand-wiring), with the bundled OpenAPI parser beside it | `netstandard2.0` — what Roslyn loads an analyzer on |
| `SharpPortico.Runtime` | 1.5.0 | the AOT-safe runtime contracts the generated proxy links against | `net10.0`, `net11.0` |
| `SharpPortico.Cli` | 1.5.0 | `dotnet sharpportico generate` — mapping preview, descriptor writer, JSON report | `net10.0`, `net11.0` (packed as a tool) |

`SharpPortico.Abstractions` (`[OpenApiToGrpc]`, `SharpPorticoOptions`) is not a package of its own: it is compiled into
the analyzer package and shipped in `analyzers/dotnet/cs` beside the generator, so a consumer never adds it separately.

Generated code compiles on both supported runtimes and is written in C# 14. The generator is `netstandard2.0` because a
Roslyn analyzer has to be — which is also what makes it work in any consumer's compiler, whatever that consumer targets.

---

## 2. Declaring a specification

The document itself always comes from an **`AdditionalFiles`** item; the attribute configures what is generated from it.

```xml
<ItemGroup>
  <PackageReference Include="SharpPortico.SourceGenerator" Version="1.5.0" PrivateAssets="all" />
  <AdditionalFiles Include="openapi/**/*.yaml" />
</ItemGroup>
```

### Naming, three ways

| Where | Keys | Note |
| --- | --- | --- |
| MSBuild properties | `SharpPorticoServiceName`, `SharpPorticoNamespace` | project-wide, the simplest form |
| `AdditionalFiles` metadata | `SharpporticoServiceName`, `SharpporticoNamespace` | per file; the lower-case `s` is the package's, not a typo |
| assembly attribute | `[assembly: OpenApiToGrpc("openapi/users.yaml", "UserService", "MyApp.Generated")]` | repeatable, one per specification |

When an attribute and an `AdditionalFiles` item describe the same file (matched by file name), the **attribute wins** and
the file is not generated twice. A file declared only as an `AdditionalFiles` item is generated with defaults.

Naming defaults: the **service name** is the one the attribute names — or the MSBuild property, or the per-file metadata;
otherwise it is derived from the specification's file name, or from the first word of `info.title` when the file name
yields nothing. The `Service` suffix is appended when the name does not already end with it. The **namespace** defaults
to the service name — not to a fixed package namespace — and the **protobuf package** is that namespace in lower case.

The package's `build/SharpPortico.SourceGenerator.props` is what makes those two names and two metadata keys visible to
the compiler. A project does not import it by hand.

---

## 3. Options

Every option below is a property on `[OpenApiToGrpc]`. `SharpPorticoOptions` carries the same set as one object, for code
that reads them; it cannot be handed to the attribute, because an attribute argument has to be a compile-time constant.

### Generation

| Property | Default | Effect |
| --- | --- | --- |
| `EmitProtoFile` | `true` | emit the descriptor as `{Service}Proto.Text` |
| `EmitClient` | `true` | emit the typed client |
| `EmitServer` | `true` | emit the server base class |
| `EmitDependencyInjection` | `true` | emit the `IServiceCollection` extension |
| `ServiceNameSuffix` | `"Service"` | suffix appended to a derived service name |

### Mapping

| Property | Default | Effect |
| --- | --- | --- |
| `RespectStreamingHints` | `true` | honour `x-grpc-streaming` and the payload threshold |
| `LargePayloadStreamingThresholdBytes` | `1000000` | request size above which a POST becomes client-streaming |
| `DetectPagination` | `true` | detect page / limit / cursor / next-page-token parameters |
| `PaginationPageParameter` | `"page"` | page parameter name |
| `PaginationLimitParameter` | `"limit"` | limit parameter name |
| `PaginationCursorParameter` | `"cursor"` | cursor parameter name |
| `PaginationNextPageTokenParameter` | `"next_page_token"` | next-page token name |
| `EmitGoogleRpcStatusWrapper` | `true` | emit a `google.rpc.Status`-shaped error message per RPC |
| `EmitDiagnosticsForUnmappableConstructs` | `true` | report losses (`SP2000`) instead of mapping silently |

### Auth

| Property | Default | Effect |
| --- | --- | --- |
| `GenerateAuthMetadataHelpers` | `true` | `{Service}Auth.Create…Metadata(credential)` |
| `GenerateAuthInterceptors` | `true` | `{Service}AuthInterceptor`, which attaches the credential to every call |

### Proxy (gRPC → REST gateway)

| Property | Default | Effect |
| --- | --- | --- |
| `EnableProxyGeneration` | `false` | emit `{Service}Proxy : {Service}Base` |
| `ProxyBaseUrl` | `null` | legacy base URL; `servers[0].url` when unset |
| `ProxyApiKeyHeaderName` | `"X-Api-Key"` | outbound key header |
| `ProxyCacheTtlSeconds` | `60` | cache TTL for GET responses |
| `ProxyBypassCacheMetadataKey` | `"x-portico-bypass-cache"` | per-call cache bypass |
| `ProxyClientKeyHeaderName` | `"x-portico-key"` | inbound client-key header |
| `ProxyClientKeyMode` | `None` | `None`, `Forward`, `Own` |
| `ProxyAuditEnabled` | `false` | log one audit entry per call |

### Declared, but not read by 1.5.0

These properties compile and are accepted, and the generator does not look at them: what they would configure is fixed.
They are listed here rather than quietly described as working, because a consumer who sets one should know that nothing
changed.

| Property | Fixed behaviour instead |
| --- | --- |
| `EmitGeneratedCodeAttribute` | `[GeneratedCode("SharpPortico", "1.5.0")]` is always emitted |
| `UseFileScopedNamespaces` | generated C# always uses a block-scoped `namespace { … }` |
| `MessageNaming` | message members are always `PascalCase` |
| `MethodNaming` | RPC methods are always `PascalCase` |
| `MapEnumsToProtobufEnums` | enumerations are always emitted as protobuf enums |
| `StripApiVersionFromServiceName` | a derived name is the title's first word, Pascal-cased, plus the suffix — a version segment does not survive that regardless |
| `Default4xxStatus` | the gateway maps concrete statuses itself (400/422 → `InvalidArgument`, 401/403 → `Unauthenticated`, …) |
| `Default5xxStatus` | as above; 5xx → `Internal` |

---

## 4. The generated surface

Two files per specification, both named after it: `{spec}.g.cs` for the C#, `{spec}.proto.cs` for the descriptor.

| Type | Shape | Members |
| --- | --- | --- |
| `{Service}` | `static partial class` | `ServiceFullName`, one `Method_{Rpc}` per RPC, `BindService({Service}Base)` |
| `{Service}.{Service}Client` | nested, `Grpc.Core.ClientBase<T>` | `{Rpc}Async(request, headers, deadline, ct)` and `{Rpc}Async(request, CallOptions)` |
| `{Service}Base` | `abstract partial class` | `virtual {Rpc}Async(request, ServerCallContext)` (throws `Unimplemented`), `{Rpc}Handler` |
| `{Service}Client` | `sealed partial class`, primary constructor over `GrpcChannel` | `Create(GrpcChannel)`, `Create(ChannelBase)`, `{Rpc}Async` with `Task<TResponse>`, a scalar overload per path parameter, and `{Rpc}DataAsync` / `{Rpc}ItemsAsync` for the response's `data` / `items` |
| `ServiceCollectionExtensions` | `static class` | `AddSharpPortico{Service}()` — registers the client from the container's `GrpcChannel` |
| `{Service}Auth` | `static partial class` | `Create…Metadata(credential)` per security scheme |
| `{Service}AuthInterceptor` | `sealed class`, `Grpc.Core.Interceptors.Interceptor` | one override per call shape, attaching the credential |
| `{Service}Proto` | `static partial class` | `const string Text` — the descriptor, LF-normalised, exactly as `protoc` receives it |
| `{Service}Proxy` | `sealed partial class : {Service}Base` (proxy mode only) | constructor over `ProxyOptions`, `IRestClient`, and optional cache, keys, validator, audit |
| messages / enums | `sealed partial class` / `enum` | below |

Every message implements `Google.Protobuf.IMessage<T>` by hand — no reflection, no `Activator` — and carries:

| Member | Notes |
| --- | --- |
| `static MessageParser<T> Parser` | for gRPC marshalling |
| one property per field | `{ get; set; }`; `RepeatedField<T>` for `repeated`; message fields default to `null!` |
| `Has{X}` / `Clear{X}()` | only for a nullable scalar — see [presence](presence.md) |
| `bool IsInitialized` | always `true`: proto3 has no required fields |
| `T Clone()` | copies only the fields that were set |
| `bool Equals(T?)`, `Equals(object?)`, `GetHashCode()` | presence-aware for `optional` fields |
| `string ToString()` | protobuf text format |
| `int CalculateSize()`, `void WriteTo(CodedOutputStream)` | hand-written wire encoding |
| `void MergeFrom(T)`, `void MergeFrom(CodedInputStream)` | merge and decode |

`[GeneratedCode("SharpPortico", "1.5.0")]` is stamped on every generated type, so committed output or a stack trace
names the generator version that produced it.

---

## 5. Runtime contracts (`SharpPortico.Runtime`)

Only a project that hosts a generated `{Service}Proxy` needs this package; the generated client and server use
`Grpc.Core.Api` / `Grpc.Net.Client` and `Google.Protobuf` directly.

| Type | Kind | Purpose |
| --- | --- | --- |
| `RestRequest` / `RestResponse` | readonly record structs | the verb, path, headers and body a proxy call sends, and the materialised answer it gets back |
| `IRestClient` | interface | `RestResponse Send(RestRequest request)` — the whole transport, one call at a time |
| `HttpRestClient` | sealed class | the `HttpClient`-backed implementation |
| `IProxyCache` | interface | `Get` / `Set` a cached response by key |
| `MemoryProxyCache` | sealed class | `IMemoryCache`-backed implementation |
| `NullProxyCache` | sealed class | no caching, for a cache that must be switched off |
| `IKeyProvider` | interface | resolve an outbound credential by name; keys are never hardcoded |
| `ConfigurationKeyProvider` | sealed class | reads `IConfiguration` |
| `DelegateKeyProvider` | sealed class | any `Func<string, string?>` — Key Vault, a secret store, a test |
| `CompositeKeyProvider` | sealed class | first provider that resolves wins |
| `IClientKeyValidator` / `UlidClientKeyValidator` | interface / sealed class | inbound client-key check for `ClientKeyMode.Own` |
| `IProxyAuditLogger` / `DefaultAuditLogger` / `AuditEntry` | interface / sealed class / record struct | one entry per call: service, RPC, client-key id, peer, cache hit, status, timestamp |
| `ProxyOptions` | sealed record | `BaseUrl`, `ApiKeyHeaderName`, `ClientKeyHeaderName`, `ClientKeyMode`, `BypassCacheMetadataKey`, `CacheTtl`, `CacheReadsOnly` |
| `ClientKeyMode` | enum | `None`, `Forward`, `Own` |
| `ClientIdentity` | readonly record struct | `KeyId` and `RemoteAddress`, as the gateway reads them from a call |
| `ProxyContext` | static class | `ParseBypass(metadata, key)`, `ReadIdentity(callContext, clientKeyHeader)` |
| `ProxyRestException` | sealed class | a non-success REST response, as an exception |
| `GrpcStatusMapper` | static class | REST status → gRPC status (`400`/`422` → `InvalidArgument`, `401`/`403` → `Unauthenticated`, `404` → `NotFound`, `408`/`504` → `DeadlineExceeded`, `409` → `AlreadyExists`, `429` → `ResourceExhausted`, `500`/`502` → `Internal`, `503` → `Unavailable`, `501`/`505` → `Unimplemented`, anything else → `Unknown`) |
| `ProxyServiceCollectionExtensions` | static class | `AddSharpPorticoProxyCore(options)`, `AddSharpPorticoKeyProvider(…)`, `AddSharpPorticoClientKeys(…)` |

---

## 6. CLI

```bash
dotnet tool install -g SharpPortico.Cli --version 1.5.0

sharpportico generate SPEC [--out DIR|FILE] [--service-name NAME] [--namespace NS] [--format text|json]
```

| Option | Effect |
| --- | --- |
| `--out DIR\|FILE` | write the descriptor — a directory gets `<spec>.proto` |
| `--service-name NAME` | base name of the generated service (`Service` is appended when absent) |
| `--namespace NS` | namespace of the generated C# (defaults to the service name) |
| `--format text\|json` | `text` (default) prints the summary; `json` prints one document on stdout |

| Exit code | Meaning |
| --- | --- |
| `0` | the specification mapped; stdout holds the summary or the document |
| `1` | the specification was refused — a diagnostic with `severity: "error"`; the document is still written in `--format json` |
| `2` | the command line was wrong, or the spec file does not exist — nothing was read |

In `--format text`, a warning goes to standard error and the summary to standard output, so a pipeline can capture the
summary without the noise. In `--format json`, standard output holds the document and nothing else.

The document has seven members:

| Member | Meaning |
| --- | --- |
| `formatVersion` | the shape of the document; `1` today, bumped only when a reader would misread it |
| `spec` | the path exactly as it was typed |
| `service` | `name`, `namespace`, `package`; **omitted** when the contract was refused |
| `services` | each mapped service with its `rpcs` (`name`, `httpMethod`, `path`, `kind`); `[]` when nothing mapped |
| `counts` | `messages` and `enums`; `0`/`0` on a refusal rather than omitted |
| `diagnostics` | every diagnostic: `id`, `severity`, `message`; `[]` when there were none |
| `descriptor` | the path `--out` wrote, present only after it was written |

The bytes are a compatibility surface: UTF-8 without a byte-order mark, two-space indentation, `\n` line endings, exactly
one trailing newline, and identical between runs and between operating systems.
`tests/SharpPortico.Tests/openapi/petstore.report.json` pins the document and a Linux CI step regenerates and diffs it.

---

## 7. Compatibility and verification

| Claim | Where it is checked |
| --- | --- |
| the generator loads where Roslyn loads it (`netstandard2.0`) and generates for both runtimes | CI builds the solution and runs the suite on `net10.0` **and** `net11.0` |
| a stranger can consume the package (analyzer discovered from `analyzers/dotnet/cs`, nothing added by hand) | `.github/scripts/consume-package.sh` installs the packed `.nupkg` into a throwaway project and builds it |
| the tool package installs, runs, and answers on both frameworks | `.github/scripts/check-tool-package.sh` runs `tools/net10.0` and `tools/net11.0` by name |
| every descriptor is valid protobuf | `.github/scripts/check-protos.sh` compiles each sample's with `protoc` |
| the generated code is reflection-free | the `native-aot` job publishes the 3.1 sample as a native image, refuses a publish that left a managed assembly behind, and runs it to `SMOKE TEST PASSED` |
| the JSON report is byte-identical across platforms | CI regenerates it on Linux and diffs it against the committed golden file |
| the documented version is the packaged version | CI greps `Directory.Build.props` and every install line against it |

`optional` fields need `protoc` 3.15 or newer — the first release that accepts proto3 presence without an experimental
flag. Everything else about the descriptor is plain proto3.



