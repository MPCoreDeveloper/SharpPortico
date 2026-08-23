# SharpPortico.Runtime

![SharpPortico](https://raw.githubusercontent.com/MPCoreDeveloper/SharpPortico/main/docs/assets/SharpPortico.jpg)

**Tiny AOT-safe runtime helpers used by SharpPortico-generated gRPC code.**

Contains the runtime contracts and implementations the generated proxy code links against:

- `IRestClient` / `HttpRestClient` — gRPC→REST forwarding
- `IProxyCache` / `MemoryProxyCache` — response caching with per-call bypass
- `IKeyProvider` (config, Key Vault, delegate, composite) — API keys are never hardcoded
- `IClientKeyValidator` / `UlidClientKeyValidator` — inbound client-key checks
- `IProxyAuditLogger` — per-call audit logging

```xml
<PackageReference Include="SharpPortico.Runtime" Version="1.0.0" />
```

Reference this package in any project that hosts a generated `{Service}Proxy` (proxy mode).

📖 **Full documentation & proxy-mode guide:** [SharpPortico on GitHub](https://github.com/MPCoreDeveloper/SharpPortico#readme)

## License

MIT — see [LICENSE](https://github.com/MPCoreDeveloper/SharpPortico/blob/main/LICENSE). Copyright (c) 2026 MPCoreDeveloper.
