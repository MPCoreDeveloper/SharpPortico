# Changelog

All notable changes to SharpPortico are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
