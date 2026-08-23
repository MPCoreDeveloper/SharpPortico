# SharpPortico.SourceGenerator

![SharpPortico](https://raw.githubusercontent.com/MPCoreDeveloper/SharpPortico/main/docs/assets/SharpPortico.jpg)

**Incremental source generator: OpenAPI 3.0/3.1 → gRPC + protobuf + C# 14.**

SharpPortico is a compile-time `IIncrementalGenerator`. Declare your OpenAPI spec (YAML or JSON) with an `[assembly: OpenApiToGrpc]` attribute or an MSBuild `<AdditionalFiles>` item, and it generates production-quality gRPC services, hand-written protobuf messages, typed C# 14 clients and DI wiring — zero reflection, NativeAOT-safe. Enable **proxy mode** and it also emits a `{Service}Proxy` that turns the service into a gRPC↔REST gateway for legacy APIs.

Add it to your project as an analyzer:

```xml
<PackageReference Include="SharpPortico.SourceGenerator" Version="1.0.0"
                  ReferenceOutputAssembly="false"
                  OutputItemType="Analyzer"
                  PrivateAssets="all" />
```

```csharp
using SharpPortico;

[assembly: OpenApiToGrpc("openapi/users.yaml", "UserService", "MyApp.Generated")]
```

📖 **Full documentation, mapping rules & samples:** [SharpPortico on GitHub](https://github.com/MPCoreDeveloper/SharpPortico#readme)

## License

MIT — see [LICENSE](https://github.com/MPCoreDeveloper/SharpPortico/blob/main/LICENSE). Copyright (c) 2026 MPCoreDeveloper.
