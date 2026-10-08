# SharpPortico — Developer Guide

SharpPortico is an **incremental source generator** (C# 14, running on .NET 10 and .NET 11) that converts OpenAPI 3.0/3.1 specs (YAML/JSON) into production-ready **gRPC services, protobuf messages and modern C# clients** at compile time. It is fully **reflection-free and NativeAOT-safe**.

The optional **proxy mode** turns SharpPortico into a gRPC↔REST gateway: local .NET apps call a generated gRPC service, while the server forwards to a legacy REST API (with X-Api-Key / OAuth2 outbound auth and an optional response cache).

---

## 1. NuGet packages

| Package | What it contains |
| --- | --- |
| `SharpPortico.SourceGenerator` | The generator (reference as an Analyzer) |
| `SharpPortico.Runtime` | AOT-safe runtime helpers incl. the proxy pipeline (`IRestClient`, `IProxyCache`, `IKeyProvider`, `IClientKeyValidator`, audit) |
| `SharpPortico.Cli` | `dotnet sharpportico generate` — spec preview/validation: what the mapping makes of a spec, and the `.proto` descriptor a build of it would embed |

> All packages carry the SharpPortico logo icon and a short NuGet readme (see `src/*/README.md`). Current version: **1.3.0-preview.2**, defined once in `Directory.Build.props` (`SharpPorticoProductVersion`) — see [CHANGELOG](../CHANGELOG.md).

---

## 2. Quick start — generate a gRPC service

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\SharpPortico.SourceGenerator\SharpPortico.SourceGenerator.csproj"
                    ReferenceOutputAssembly="false"
                    OutputItemType="Analyzer"
                    PrivateAssets="all" />
  <Analyzer Include="$(SharpPorticoGeneratorOutDir)Microsoft.OpenApi.dll" />
  <Analyzer Include="$(SharpPorticoGeneratorOutDir)Microsoft.OpenApi.Readers.dll" />
  <Analyzer Include="$(SharpPorticoGeneratorOutDir)SharpYaml.dll" />
  <Analyzer Include="$(SharpPorticoGeneratorOutDir)SharpPortico.Abstractions.dll" />
</ItemGroup>
```

Declare the spec **either** via an assembly attribute **or** `<AdditionalFiles>`:

```csharp
// AssemblyInfo.cs
using SharpPortico;

[assembly: OpenApiToGrpc("openapi/users.yaml", "UserService", "MyApp.Generated")]
```

```xml
<ItemGroup>
  <AdditionalFiles Include="openapi/**/*.yaml" />
</ItemGroup>
```

Use the generated client:

```csharp
using var channel = GrpcChannel.ForAddress("http://localhost:50051");
var client = UserServiceClient.Create(channel);

var user = await client.GetUserAsync(42);                     // scalar overload
var created = await client.CreateUserAsync(new CreateUserRequest { ... });
```

Generated per spec `users.yaml` (namespace `MyApp.Generated`):

- `Users.g.cs` — protobuf messages, `UserService` contract, `UserServiceBase`, `UserServiceClient`, DI
- `Users.proto.cs` — the `.proto` descriptor (const string)

---

## 3. Proxy mode — gRPC clients → legacy REST

### 3.1 Enable + host

```csharp
[assembly: OpenApiToGrpc("openapi/users.yaml", "UserService", "MyApp.Generated",
    EnableProxyGeneration = true,
    ProxyBaseUrl = "https://corporate.example",
    ProxyApiKeyHeaderName = "X-Api-Key",
    ProxyCacheTtlSeconds = 60,
    ProxyClientKeyMode = ClientKeyMode.None,
    ProxyAuditEnabled = true)]
```

```csharp
// Host the generated proxy in your gRPC server
var options = new ProxyOptions
{
    BaseUrl = "https://corporate.example",
    ApiKeyHeaderName = "X-Api-Key",
    CacheTtl = TimeSpan.FromSeconds(60),
    ClientKeyMode = ClientKeyMode.None
};

using var http = new HttpClient { BaseAddress = new Uri(options.BaseUrl) };
var rest = new HttpRestClient(http, options.BaseUrl);

server.Services.Add(UserService.BindService(
    new UserServiceProxy(options, rest, cache: new MemoryProxyCache(memoryCache), keys: keyProvider)));
```

### 3.2 Caching + per-call bypass

GET responses are cached (respecting `CacheTtl`). Clients can bypass per call:

```csharp
using var headers = new Metadata { { "x-portico-bypass-cache", "true" } };
var fresh = await client.GetUserAsync(new GetUserRequest { Id = 42 }, headers);
```

### 3.3 Client authentication (`ProxyClientKeyMode`)

- `None` — no inbound check.
- `Forward` — the client key (`x-portico-key`) is forwarded 1:1 as the outbound API key.
- `Own` — validate a ULID-shaped client key against an allowed list (`UlidClientKeyValidator` / `IClientKeyValidator`), then use the configured outbound key.

### 3.4 Keys are never hardcoded

Outbound keys always come from `IKeyProvider`:

```csharp
// config-backed
services.AddSingleton<IKeyProvider>(_ => new ConfigurationKeyProvider(configuration));

// Key Vault / custom
services.AddSingleton<IKeyProvider>(_ =>
    new DelegateKeyProvider(name => secretClient.GetSecret(name).Value.Value));

// composite (first hit wins)
services.AddSingleton<IKeyProvider>(_ =>
    new CompositeKeyProvider(configProvider, vaultProvider));
```

### 3.5 Audit logging

Set `ProxyAuditEnabled = true` (or inject `IProxyAuditLogger`). The default logs: **service, RPC, client key id, peer, cache-hit, HTTP status, timestamp**.

---

## 4. Mapping rules (summary)

| OpenAPI | gRPC / protobuf |
| --- | --- |
| paths + HTTP verb | Unary RPC (streaming via `x-grpc-streaming` or large POST) |
| path/query/header params | Single `*Request` message |
| body | Nested message; `application/octet-stream` → `bytes` |
| response | `*Response` message (+ google.rpc.Status-shaped error wrapper) |
| components/schemas | `message` definitions (`$ref`, `allOf`, `oneOf`/`anyOf`) |
| free-form object (`additionalProperties`) | `google.protobuf.Struct`, with the descriptor importing `google/protobuf/struct.proto` |
| arrays | `repeated` |
| enums | protobuf enums |
| auth | metadata helpers + a `{Service}AuthInterceptor` client interceptor that attaches the credential to every call |
| pagination | `page/limit/cursor/next_page_token` detection |
| 3.1 `type` array (`["string", "null"]`) | the one type the members name, or `google.protobuf.Value` when several do |
| 3.1 `const` | the one-member enumeration the value already is |
| 3.1 tuple (`prefixItems`) | `repeated` — the fixed length and the per-position types are reported, not carried |
| 3.1 `contentEncoding: base64` | `bytes` |
| 3.1 numeric `exclusiveMinimum` / `exclusiveMaximum` | the 3.0 `minimum`/`maximum` + flag pair (protobuf carries no bound) |
| 3.1 `$defs` reference | the definition, hoisted to where the parser resolves it |
| 3.1 `webhooks` | an RPC like every other operation — a webhook is a call the API makes to *you* |

A 3.1 document is rewritten to the 3.0 form the bundled parser reads before it is parsed, and nothing in that rewrite is silent: `SP1002` says the document was read that way, and `SP1003` names each construct and what it became. A 3.0 document is left untouched, byte for byte. `x-sharpportico-json-value: true` on a schema asks for `google.protobuf.Value` where the mapping cannot infer the JSON type.

---

## 5. CLI

```bash
# What the mapping makes of the spec: service, namespace, package, RPC surface, message/enum counts, diagnostics
dotnet run --project src/SharpPortico.Cli --framework net10.0 -- generate openapi/petstore.yaml

# And the descriptor the generator would embed, as a file
dotnet run --project src/SharpPortico.Cli --framework net10.0 -- generate openapi/petstore.yaml --out out/
```

| Option | Effect |
| --- | --- |
| `--out DIR\|FILE` | Write the descriptor — a directory gets `<spec>.proto` |
| `--service-name NAME` | Base name of the generated service (`Service` is appended when absent; defaults to the spec's file name) |
| `--namespace NS` | Namespace of the generated C#, as the declaration's third argument (defaults to the service name) |

The descriptor comes from `ProtoEmitter`, the emitter the generator itself calls, and is LF-normalized exactly as the generated `{Service}Proto.Text` const holds it, so the file and a build of the same spec cannot disagree. The diagnostics are the generator's own, with the ids and messages the compiler reports. That makes the CLI a check as well as a preview: CI runs it over every sample spec and compiles each descriptor with `protoc` (`.github/scripts/check-protos.sh`), which is the only check for what the C# compiler never reads — a name `protoc` rejects, or an import left out of the descriptor.

The `--framework` is only needed because the CLI multi-targets (`net10.0` / `net11.0`); `dotnet tool install -g SharpPortico.Cli --version 1.3.0-preview.2` gives you `sharpportico generate` without it. The version is pinned because a bare install resolves the last *stable* release, which predates the 3.1 mapping above.

---

## 6. Samples

- **GrpcServerExample** — plain unary gRPC server + client (petstore).
- **MinimalApiExample** — ASP.NET Minimal API over the generated client.
- **LegacyProxyExample** — mock legacy REST (X-Api-Key check + call counter), generated proxy, cache hit + bypass demo:

```
gRPC server listening on 50053
GetPet(1)          -> Rex   (REST calls: 2)
GetPet(1) cached   -> Rex   (REST calls: 2)   <- cache hit
GetPet(1) bypass   -> Rex   (REST calls: 3)   <- forced fresh
ListPets           -> 2 pets (REST calls: 4)
CreatePet          -> id=3 Luna (REST calls: 4)
TOTAL REST calls: 4 (expected 4)
```

- **NativeAotExample** — the OpenAPI **3.1** sample, and the AOT proof: a 3.1 contract that uses the constructs the 3.0 reader cannot read (`type` arrays, `const`, `prefixItems`, `contentEncoding: base64`, a numeric `exclusiveMinimum`, `$defs`, `webhooks`, `jsonSchemaDialect`), published as a **native image** and driven end to end in one process — the generated `Grpc.Core` server and the generated client:

```bash
dotnet publish samples/NativeAotExample -c Release -f net10.0 -r win-x64
```

```
FleetService (NativeAOT)
dynamic code:      compiled=False supported=False
server:            listening on <port assigned by the OS>
RegisterVehicle    -> id=1 callsign=Atlas kind=Truck year=2026 window=day-shift
GetVehicle(1)      -> callsign=Atlas labels=2 bounds=2 telemetry=StringValue
ListVehicles       -> 1 vehicle(s)
OnVehicleRetired   -> callsign=Atlas response=True
Round-trip OK      -> callsign=Atlas kind=Truck manifest=fleet region=eu-west
SMOKE TEST PASSED
```

The `dynamic code` line is what the runtime reports, and `PublishAot` clears that feature switch whether or not a publish ran, so it proves nothing by itself. The proof is the publish: CI's `native-aot` job fails when a managed `NativeAotExample.dll` sits beside the native executable — the shape a publish that quietly fell back to IL leaves behind — then runs the executable and fails unless it exits `0` with `SMOKE TEST PASSED`.

---

## 7. Configuration knobs (attribute)

`EmitProtoFile`, `EmitClient`, `EmitServer`, `EmitDependencyInjection`, `RespectStreamingHints`, `DetectPagination`, `GenerateAuthMetadataHelpers`, `GenerateAuthInterceptors`, `EnableProxyGeneration`, `ProxyBaseUrl`, `ProxyApiKeyHeaderName`, `ProxyCacheTtlSeconds`, `ProxyBypassCacheMetadataKey`, `ProxyClientKeyHeaderName`, `ProxyClientKeyMode`, `ProxyAuditEnabled`.

---

## License

MIT — see `LICENSE`.