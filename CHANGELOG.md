# Changelog

All notable changes to SharpPortico are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.2-preview.1] - 2026-09-19

### Fixed
- **A document that fails to parse is refused with the reader's own complaint.** `SP1000` answered "missing
  info/title section" whenever the parsed document came back without an info section, and dropped the OpenAPI
  reader's own message on the floor. A document only reaches that state *because* the reader refused part of it,
  so the refusal now reports the first reader error and falls back to the old text only when the reader said
  nothing at all. The cost of the old behaviour is measured: a caller spent a long hunt through an eight-thousand
  line specification, reading the one section that was fine.

### Known limitations
- **A free-form object (`type: object` with `additionalProperties`) is still not mapped.** Such a property is
  dropped from the generated message without a diagnostic, so the consumer sees a compiler error in generated
  code rather than a refusal here. The protobuf answer is `google.protobuf.Struct`; until that lands, declare the
  field as `type: string` carrying JSON text — which is what the contract that found this does.

## [1.1.1] - 2026-09-13

### Fixed
- **The package now declares the dependency its own output needs.** The generated code registers the service
  with `IServiceCollection` by default, so a consumer needs
  `Microsoft.Extensions.DependencyInjection.Abstractions` in its compilation — and nothing declared it, because
  the generator itself does not use it. A consumer therefore compiled only when one of its other packages
  happened to bring it in transitively, which is exactly what made this survive: a probe that resolves floating
  package versions passes, and a consumer pinned to the versions this repository pins fails with `CS0234`.

### Added
- **CI consumes the packed package the way a stranger does, and the publish workflow refuses to push an
  artefact that fails that check.** This is the test that was missing rather than a bug in what was tested:
  the tests and samples here reference the generator as a *project*, with `OutputItemType="Analyzer"`, and
  NuGet's `analyzers/`-versus-`lib/` rule — the rule that decides whether a packaged generator runs at all —
  only exists on the package path. That is how `1.0.0` shipped a generator that never ran while every test in
  the repository was green. Both defects were found by consuming the package, and it is now a gate.


## [1.1.0] - 2026-09-13

The first release that works when it is referenced. `1.0.0` was published before the generator was placed
where the compiler looks for analyzers, so a `PackageReference` on it generated nothing; the fix went out as
`0.4.0-RC.1`, and a prerelease sorts *below* `1.0.0`, so no stable version contained it. This release is
that work as a stable one, on both supported runtimes, from `main`.

### Added
- **.NET 10 and .NET 11 from one package.** The generator is `netstandard2.0` — the TFM Roslyn loads
  analyzers on, so it runs in the compiler whatever the consumer targets — and it now ships with the
  helpers (`SharpPortico.Runtime`, one `lib/` folder per runtime) and the tool (`SharpPortico.Cli`, one
  `tools/` folder per runtime). The test suite runs on both frameworks, and both samples are built on both.
- **The generated contract is compiled in the test suite**, not only inspected. A text assertion cannot
  catch a contract that does not compile, which is a generator's most damaging failure: the consumer
  discovers it inside a generated file. The emitted sources are compiled against the framework and the
  packages they name — at the latest language version and at C# 14, which is what a .NET 10 consumer has.
- **ASP.NET Core hosting.** The generated server base carries
  `[BindServiceMethod(typeof({Service}), "BindService")]` and the contract a
  `BindService(ServiceBinderBase, {Service}Base)` overload, which is the shape grpc-dotnet's
  `BinderServiceModelProvider` looks for, so `MapGrpcService<T>` serves a SharpPortico contract next to
  another service. The `Grpc.Core` binder shape is unchanged.
- **`SharpPorticoServiceName` and `SharpPorticoNamespace` as MSBuild properties**, next to the per-file
  `SharpporticoServiceName` / `SharpporticoNamespace` metadata.
- **`SP2005` and `SP2006`** refuse a contract that cannot compile; **`SP1002`** reports an OpenAPI 3.1
  document being parsed as 3.0.

### Fixed
- **The generator is shipped where the compiler looks for it.** A `lib/` asset is never handed to the
  compiler as an analyzer, so the package generated nothing at all for a plain `PackageReference`; the
  generator now also ships in `analyzers/dotnet/cs`.
- **The configuration values reach the generator.** MSBuild surfaces a property or metadata to a generator
  only when it is declared compiler-visible, so the package now ships
  `build/SharpPortico.SourceGenerator.props`, which declares all four.
- **An unset configuration value no longer shadows a set one.** A declared metadata name is emitted on
  every item — empty when the item does not set it — so the per-file lookup succeeded with `""` and the
  project-wide properties were never read. Whitespace now counts as missing on both routes.
- **A message with two enum properties did not compile.** The deserialiser declares a local per enum field,
  and each `case` body was emitted without its own scope, so two enums in one message emitted the same
  local twice into one `switch` scope — `CS0128` in generated source, for a specification that maps
  cleanly and reports no diagnostics. Each case body now has its own scope, which is also what protoc
  emits.
- `Microsoft.Extensions.Caching.Memory` 8.0.0 → 8.0.1 (CVE-2024-43483).


## [0.4.0-RC.4] - 2026-09-13

### Fixed
- **A message with two enum properties did not compile.** The protobuf deserialiser declares a local per
  enum field, and each `case` body was emitted without its own scope, so two enums in one message emitted
  `var v` twice into the same `switch` scope — CS0128 (and CS0165 downstream) in generated source, for a
  specification that maps cleanly and reports no diagnostics. Each case body now has its own scope, which
  is also what protoc emits. A contract with several enum-typed properties is ordinary, so the failure
  looked like a typo in the consumer's code rather than a generator defect.

### Added
- **The generated contract is compiled in the test suite, not only inspected.** A text assertion cannot
  catch a contract that does not compile, which is a generator's most damaging failure: the consumer
  discovers it inside a generated file. `GeneratedCodeCompiler` compiles the emitted sources against the
  framework and the packages the output names, and `GeneratedCodeCompilationTests` runs it over the
  petstore contract and over a specification with two enum properties — the case above. The existing
  `Petstore_Generated_Code_Compiles_With_Real_References` now compiles instead of asserting the absence
  of diagnostics, which is what it always claimed to do.


## [0.4.0-RC.3] - 2026-09-13

### Fixed
- **An unset configuration value no longer shadows a set one.** Declaring a metadata name compiler-visible makes
  the compiler emit an entry for it on every item — with an *empty* value when the item does not set it — so the
  per-file lookup succeeded with `""` and the project-wide `SharpPorticoServiceName` / `SharpPorticoNamespace` were
  never read. A service was then named after an empty string: the generated file was `.g.cs`, its namespace the
  service's own name, and nothing the project configured had any effect. Whitespace now counts as missing on both
  routes.


## [0.4.0-RC.2] - 2026-09-13

### Added
- **`SharpPorticoServiceName` and `SharpPorticoNamespace` as project-wide MSBuild properties**, next to the
  per-file `SharpporticoServiceName` / `SharpporticoNamespace` metadata.

### Fixed
- **The service name and namespace can actually be configured now.** MSBuild hands a property or an item
  metadata to the compiler — and therefore to a source generator — only when it is declared compiler-visible,
  so neither the properties nor the per-file metadata ever reached the generator: a service was named after its
  specification's file name whatever the project said. The package now ships
  `build/SharpPortico.SourceGenerator.props`, which declares all four. 0.4.0-RC.1 added the property route on
  the generator side but shipped without the declaration, so nothing read it — the property would have
  appeared to work and done nothing.


## [0.4.0-RC.1] - 2026-09-13

### Added
- **ASP.NET Core hosting.** The generated server base carries
  `[BindServiceMethod(typeof({Service}), "BindService")]` and the contract a
  `BindService(ServiceBinderBase, {Service}Base)` overload, which is the shape grpc-dotnet's
  `BinderServiceModelProvider` looks for, so `MapGrpcService<T>` can serve a SharpPortico contract next to the
  existing `Grpc.Core` route. For a unary RPC the base also declares the RPC-named
  `{Rpc}({Request}, ServerCallContext)` that grpc-dotnet binds by; it forwards to the `{Rpc}Async` an
  implementation still overrides.
- **`SharpPorticoServiceName` and `SharpPorticoNamespace` MSBuild properties** — a spec declared with
  `<AdditionalFiles>` can now be named without per-file metadata, which not every SDK surfaces to analyzers.
- Diagnostics **SP1002** (an OpenAPI 3.1 document parsed as 3.0), **SP2005** (a message name produced twice) and
  **SP2006** (a property named like its schema).

### Fixed
- **The NuGet package now actually runs the generator.** A package's `lib/` assets are never handed to the
  compiler as analyzers — not even with `OutputItemType="Analyzer"` — so a plain
  `<PackageReference Include="SharpPortico.SourceGenerator" />` generated nothing at all. The generator now ships
  under `analyzers/dotnet/cs` as well as `lib/netstandard2.0`; a project uses one route or the other, never both.
- **An OpenAPI 3.1 document is parsed instead of refused.** `Microsoft.OpenApi` 1.6.x rejects `openapi: 3.1.0`
  outright ("OpenAPI specification version '3.1.0' is not supported") although the mapping covers 3.0 and 3.1
  alike. The declared version is rewritten to 3.0 before parsing, with SP1002 saying exactly what that means:
  everything the two versions share maps, 3.1-only keywords (`prefixItems`, a `type` array, `webhooks`, `$defs`)
  do not yet.
- **A contract that cannot compile is refused with a reason** (SP2005, SP2006) instead of being emitted and left
  for the consumer to diagnose from generated source.
- The large-spec fixture declared a path parameter its path did not contain; now that the driver surfaces real
  diagnostics, its `Assert.Empty(result.Diagnostics)` caught it.

### Security
- **`Microsoft.Extensions.Caching.Memory` 8.0.0 → 8.0.1**, clearing the high-severity advisory
  [CVE-2024-43483](https://github.com/advisories/GHSA-qj66-m88j-hmgj) that reached consumers through
  `SharpPortico.Runtime`.

### Changed
- The test driver reports the diagnostics the pipeline actually produced (its `RunResult.Diagnostics` used to be
  empty whatever happened) and adds a non-throwing `TryRun` for specifications that have to be refused.
- Samples declare `IsPackable=false`, so `dotnet pack` at the repository root no longer fails on a sample that
  has no README to pack.


## [0.2.0] - 2026-09-09

### Fixed
- **Code quality** — resolved all 89 open SonarCloud issues (including the 3 issues flagged on the PR review):
  - **S3776 ×9** — cognitive-complexity refactors across `MessageEmitter`, `ServiceEmitter`, `ProtoEmitter`, `ProxyEmitter`, `OpenApiParser`, `SchemaMapper` and `SharpPorticoGenerator`. Generated code output stays **byte-for-byte identical** (SHA-256 verified on all samples, incl. proxy mode).
  - **S2365 ×2** — `SchemaMapper.AllMessages` / `AllEnums` collection-copying properties converted to `GetAllMessages()` / `GetAllEnums()` methods.
  - **S1854 ×9** — removed dead `++paramIndex` / `++respIndex` assignments in the OpenAPI parser.
  - **S8970 ×20 / S8969 ×2 / S1125 ×2** — removed redundant null-forgiving operators and unnecessary boolean literals.
  - **S1172 ×6 / S107 / S1481 ×2 / S1905 ×3** — removed unused method parameters (incl. the 3 flagged on the PR), slimmer helper signatures (≤ 7 params), removed unused locals and redundant casts.
  - **S2325 ×8 / S3267 ×7 / S1192 ×7 / S6610 ×4 / S1066 ×2 / S2681 / S2589 / S127 / S125** — static members, LINQ loop simplifications, named constants, single-char comparisons, merged nested `if`s, inline `if` braces, duplicated condition, loop-counter hygiene and dead comments.

### Changed
- CLI `--out` argument parsing no longer mutates its loop counter.
- Named constants introduced for repeated literals (streaming keyword, `Google.Protobuf.ByteString`, `GeneratedCode` attribute, proxy `request.` prefix, `X-Api-Key` header, schema type names).

## [0.1.0] - 2026-08-23

### Added
- Initial release: OpenAPI 3.0/3.1 → gRPC + protobuf + C# 14 incremental source generator (net10, C# 14, NativeAOT-safe).
- Hand-written `IMessage<T>` protobuf messages (reflection-free), gRPC contract, server base, typed C# 14 client and DI extensions.
- gRPC → REST **proxy mode** — response cache with per-call bypass, ULID client-key validation, `IKeyProvider` (config / Key Vault / delegate / composite) and audit logging.
- `dotnet sharpportico` CLI preview/validation tool.
- Developer guide (`docs/SharpPortico.md`), samples (gRPC server, Minimal API, legacy proxy) and xunit test suite (snapshot, mapping, performance).
