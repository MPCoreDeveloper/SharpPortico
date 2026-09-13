# SharpPortico.SourceGenerator

![SharpPortico](https://raw.githubusercontent.com/MPCoreDeveloper/SharpPortico/main/docs/assets/SharpPortico.jpg)

**Incremental source generator: OpenAPI 3.0/3.1 → gRPC + protobuf + C# 14.**

SharpPortico is a compile-time `IIncrementalGenerator`. Declare your OpenAPI spec (YAML or JSON) with an `[assembly: OpenApiToGrpc]` attribute or an MSBuild `<AdditionalFiles>` item, and it generates production-quality gRPC services, hand-written protobuf messages, typed C# 14 clients and DI wiring — zero reflection, NativeAOT-safe. Enable **proxy mode** and it also emits a `{Service}Proxy` that turns the service into a gRPC↔REST gateway for legacy APIs.

Add the spec to your project as an `AdditionalFiles` item — that is where the content comes from — and the
generator runs on every build:

```xml
<ItemGroup>
  <PackageReference Include="SharpPortico.SourceGenerator" Version="0.4.0-RC.2"
                    PrivateAssets="all" />
  <AdditionalFiles Include="openapi/**/*.yaml" />
</ItemGroup>
```

Name the service and its namespace with MSBuild properties (per-file `SharpporticoServiceName` /
`SharpporticoNamespace` metadata works too, where the SDK surfaces it to analyzers):

```xml
<PropertyGroup>
  <SharpPorticoServiceName>UserService</SharpPorticoServiceName>
  <SharpPorticoNamespace>MyApp.Generated</SharpPorticoNamespace>
</PropertyGroup>
```

The `[OpenApiToGrpc]` attribute configures the same generation when you reference `SharpPortico.Abstractions` as a
project: it carries the mapping options (streaming hints, pagination, proxy mode), and the specification still has
to be an `AdditionalFiles` item.

> Do not also add the generator by hand with `<Analyzer Include="...lib\netstandard2.0\SharpPortico.SourceGenerator.dll" />`:
> the package ships it under both `analyzers/dotnet/cs` and `lib`, and using both routes runs it twice.

📖 **Full documentation, mapping rules & samples:** [SharpPortico on GitHub](https://github.com/MPCoreDeveloper/SharpPortico#readme)

## License

MIT — see [LICENSE](https://github.com/MPCoreDeveloper/SharpPortico/blob/main/LICENSE). Copyright (c) 2026 MPCoreDeveloper.
