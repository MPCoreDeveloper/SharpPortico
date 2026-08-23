# SharpPortico

**Incremental source generator: OpenAPI 3.0/3.1 → gRPC + protobuf + C# 14, with an optional gRPC↔REST proxy**

SharpPortico turns your OpenAPI spec (YAML or JSON) into production-ready gRPC services at compile time — zero reflection, NativeAOT-safe. Enable proxy mode to expose the same service to local gRPC clients while forwarding calls to a legacy REST API (with an X-Api-Key / OAuth2 outbound key and an optional response cache).

## Packages

| Package | What it provides |
| --- | --- |
| `SharpPortico.SourceGenerator` | The incremental generator (add as an analyzer) |
| `SharpPortico.Runtime` | Tiny AOT-safe runtime helpers incl. the proxy pipeline (`IRestClient`, `IProxyCache`, `IKeyProvider`, ...) |
| `SharpPortico.Cli` | `dotnet sharpPortico` CLI companion (preview/validation) |

## Quick start

**1. Add the generator to your project**

```xml
<ProjectReference Include="../../src/SharpPortico.SourceGenerator/SharpPortico.SourceGenerator.csproj"
                  ReferenceOutputAssembly="false"
                  OutputItemType="Analyzer"
                  PrivateAssets="all" />
```

**2. Declare the spec via an assembly attribute or `<AdditionalFiles>`**

```csharp
using SharpPortico;

[assembly: OpenApiToGrpc("openapi/users.yaml", "UserService", "MyApp.Generated")]
```

```xml
<ItemGroup>
  <AdditionalFiles Include="openapi/**/*.yaml" />
</ItemGroup>
```

**3. Use the generated client**

```csharp
using var channel = GrpcChannel.ForAddress("http://localhost:50051");
var client = UserServiceClient.Create(channel);

var user = await client.GetUserAsync(42);                 // scalar overload
var created = await client.CreateUserAsync(new CreateUserRequest { ... });
```

## Proxy mode (gRPC clients → legacy REST)

Enable it on the attribute, then host the generated `{Service}Proxy` in your gRPC server:

```csharp
[assembly: OpenApiToGrpc("openapi/users.yaml", "UserService", "MyApp.Generated",
    EnableProxyGeneration = true,
    ProxyBaseUrl = "https://corporate.example",
    ProxyApiKeyHeaderName = "X-Api-Key",
    ProxyCacheTtlSeconds = 60,
    ProxyClientKeyMode = ClientKeyMode.None)]
```

```csharp
var proxy = new UserServiceProxy(
    new ProxyOptions { BaseUrl = "https://corporate.example", ApiKeyHeaderName = "X-Api-Key" },
    new HttpRestClient(httpClient, options.BaseUrl),
    cache: new MemoryProxyCache(memoryCache));

server.Services.Add(UserService.BindService(proxy));
```

- **Cache**: GET responses are cached by default. Clients can bypass per call via metadata:
  ```csharp
  var headers = new Metadata { { "x-portico-bypass-cache", "true" } };
  var fresh = await client.GetUserAsync(new GetUserRequest { Id = 42 }, headers);
  ```
- **Client authentication** (`ProxyClientKeyMode`):
  - `None` — no inbound check.
  - `Forward` — the client key is forwarded 1:1 as the outbound API key.
  - `Own` — validate a ULID-shaped client key (`x-portico-key`) against an allowed list and use the configured outbound key.
- **Key storage** — the outbound key always comes from `IKeyProvider` (config, Key Vault, or delegate). Never hardcode secrets.
- **Audit** — set `ProxyAuditEnabled = true` (or inject `IProxyAuditLogger`) to log which client, RPC, cache-hit state and HTTP status per call.

## License

MIT — see [LICENSE](https://github.com/MPCoreDeveloper/SharpPortico/blob/main/LICENSE). Copyright (c) 2026 MPCoreDeveloper.