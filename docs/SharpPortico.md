# SharpPortico — Developer Guide

SharpPortico is an **incremental source generator** (C# 14, .NET 10) that converts OpenAPI 3.0/3.1 specs (YAML/JSON) into production-ready **gRPC services, protobuf messages and modern C# clients** at compile time. It is fully **reflection-free and NativeAOT-safe**.

The optional **proxy mode** turns SharpPortico into a gRPC↔REST gateway: local .NET apps call a generated gRPC service, while the server forwards to a legacy REST API (with X-Api-Key / OAuth2 outbound auth and an optional response cache).

---

## 1. NuGet packages

| Package | What it contains |
| --- | --- |
| `SharpPortico.SourceGenerator` | The generator (reference as an Analyzer) |
| `SharpPortico.Runtime` | AOT-safe runtime helpers incl. the proxy pipeline (`IRestClient`, `IProxyCache`, `IKeyProvider`, `IClientKeyValidator`, audit) |
| `SharpPortico.Cli` | `dotnet sharpPortico` CLI (spec preview/validation) |

> All packages carry the SharpPortico logo icon and a short NuGet readme (see `src/*/README.md`). Current version: **0.2.0** — see [CHANGELOG](../CHANGELOG.md).

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
| arrays | `repeated` |
| enums | protobuf enums |
| auth | metadata helpers + interceptor-ready |
| pagination | `page/limit/cursor/next_page_token` detection |

---

## 5. CLI

```bash
dotnet run --project src/SharpPortico.Cli -- generate openapi/petstore.yaml --out out/
```

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

---

## 7. Configuration knobs (attribute)

`EmitProtoFile`, `EmitClient`, `EmitServer`, `EmitDependencyInjection`, `RespectStreamingHints`, `DetectPagination`, `GenerateAuthMetadataHelpers`, `GenerateAuthInterceptors`, `EnableProxyGeneration`, `ProxyBaseUrl`, `ProxyApiKeyHeaderName`, `ProxyCacheTtlSeconds`, `ProxyBypassCacheMetadataKey`, `ProxyClientKeyHeaderName`, `ProxyClientKeyMode`, `ProxyAuditEnabled`.

---

## License

MIT — see `LICENSE`.