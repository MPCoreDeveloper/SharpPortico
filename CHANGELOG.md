# Changelog

All notable changes to SharpPortico are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.0-preview.2] - 2026-10-06

### Fixed
- **A response the contract declares free-form is a `Struct`, not a placeholder message.** `1.2.0-preview.1` mapped
  `type: object` with `additionalProperties` - and a bare `{}` - to `google.protobuf.Struct` where the schema was a
  *property*, and left the same question unanswered where it was a *response*, an array's element or a request body:
  those positions built a message instead, and a message with no members is emitted as `bool has_value = 1`. So an
  operation whose answer is a document the contract does not describe - a `GET` serving its own OpenAPI document,
  declared free-form because describing an OpenAPI document inside itself is a self-reference - handed a consumer a
  wrapper that carries a boolean and nothing else. The question is now asked in one place
  (`SchemaMapper.MapArbitraryJson`) and answered the same way in each position: an arbitrary JSON value is
  `google.protobuf.Value`, a free-form object is `google.protobuf.Struct`, and the descriptor imports
  `google/protobuf/struct.proto` for a response exactly as it already did for a property. The tests assert the rule in
  each position it binds, not only the one that was noticed first, and the C# and the descriptor are both checked -
  the previous fix in this series was correct in one and not the other.
- **The client's convenience overload names a C# type, not the proto type.** The `{Operation}{Field}Async` overload
  that hands a response's single field back resolved that field's type a second time, and read its *proto* name for a
  message or an enum: the same string as the C# type for every message this generator declares itself - which is why
  it went unnoticed - and a type that does not exist for the ones it does not. Consuming the free-form response above
  failed the consumer's build with `CS0246: The type or namespace name 'google' could not be found` inside the
  generated file, from `Task<google.protobuf.Struct> GetOpenApiDataAsync(...)`. The overload now reads the same
  resolution the property, the comparator, the codec and the merge paths read.

### Changed
- **The version is `1.3.0-preview.2`, and every document that names it names this one.** The three `dotnet tool
  install` lines (`README.md`, `docs/SharpPortico.md`, and the CLI's own package readme), the developer guide's
  current-version line and the two package readmes, which CI checks against `Directory.Build.props`.

## [1.3.0-preview.1] - 2026-09-30

### Changed
- **Every dependency is on its newest release except the three that cannot be, and those say why.**
  `Google.Protobuf` 3.36.2, `Grpc.Core.Api` / `Grpc.Net.Client` / `Grpc.AspNetCore` 2.84.0, the
  `Microsoft.Extensions.*` line from 8.0.x to 10.0.12, `System.Text.Json` 10.0.12, `System.CommandLine` 2.0.12,
  `Microsoft.NET.Test.Sdk` 18.10.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 4.0.0 and `coverlet.collector`
  10.1.0 are the newest releases the projects can consume; two of the `NU1510` warnings the build carried are gone
  as a result, because a reference the shared framework already provides is one an up-to-date version no longer
  claims to supply. Three are held on purpose, with the reason stated in `Directory.Packages.props` beside the
  version: `Microsoft.OpenApi` 1.6.31 (the last 1.6 release - 2.x/3.x replace the object model the whole mapping
  layer is written against, so that move belongs to `Mapping/`, not to a version bump), `SharpYaml` 2.1.5 (the
  last 2.x release - 3.x's `netstandard2.0` asset depends on `System.Text.Json`, `System.Buffers` and
  `System.Collections.Immutable`, none of which the analyzer package ships, so a compiler running on .NET
  Framework could not load it on the 3.1 path) and `Grpc.Core` 2.46.6, which is that package's final release. The
  new set was verified by a full build, the 56 tests on `net10.0` and `net11.0`, the `protoc` step over every
  sample's descriptor, and a `win-x64` NativeAOT publish whose native image runs the smoke test.
- **The version is `1.3.0-preview.1`, and every document that names it names this one.** The three
  `dotnet tool install` lines (`README.md`, `docs/SharpPortico.md`, and the CLI's own package readme) now pin the
  version, because a bare `dotnet tool install -g SharpPortico.Cli` resolves the last *stable* release - 1.1.1 -
  and would hand a reader a tool without the 3.1 mapping these pages describe. CI's version step checks those
  three lines against `Directory.Build.props` along with the developer guide and the two package readmes, so an
  install line cannot drift either.

### Fixed
- **The generated C# for a free-form object did not compile.** `1.2.0-preview.1` mapped `type: object` with
  `additionalProperties` to `google.protobuf.Struct` in the descriptor and in the property's declared type, but three
  places behind them still named the *protobuf* type where C# was expected: the merge path emitted
  `new google.protobuf.Struct()`, and the repeated path emitted `FieldCodec<google.protobuf.Struct>` beside
  `FieldCodec.ForMessage(…, google.protobuf.Struct.Parser)`. A consumer compiling such a contract got
  `CS0246: The type or namespace name 'google' could not be found` from inside a generated file. The element type is
  now resolved in one place, which also fixes the repeated property, which had been wrapped twice
  (`RepeatedField<RepeatedField<Struct>>`) - a declaration that contains the string the old test asserted on, and is
  not what a consumer can use.
- **The descriptor imports the file that declares a well-known type.** A `.proto` that names
  `google.protobuf.Struct` without `import "google/protobuf/struct.proto";` does not compile, which is what the
  `1.2.0-preview.1` entry recorded as a known limitation. The imports are now derived from the model: only the ones
  the contract's own messages use, alphabetically ordered, ahead of the first definition - so a contract that names no
  well-known type still carries none.
- **Generated proxy code no longer emits unreachable code.** The JSON serializer's separator was folded at generation
  time into `if (!true) sb.Append(',');`, so every consumer of proxy mode built against a `CS0162` warning and could
  not build at all under `TreatWarningsAsErrors`. The first field now simply has no separator, which is what the
  generated text already said it meant.
- **An apiKey credential travels under a lowercase metadata key.** gRPC metadata keys are lowercase, and the generated
  helper emitted the contract's own spelling (`X-API-Key`) while the scheme's own name was already lowercased in the
  fallback case. Metadata keys are case-insensitive on the wire, so this corrects what the generated code claims rather
  than changing what a server receives.
- **The version a reader is told to expect is the version that is built.** The README badge said `1.1.1` while the
  packages were `1.2.0-preview.1`, and the developer guide said `0.2.0` and ".NET 10" while the product ships on
  net10.0 and net11.0. The badge now comes from NuGet itself, so it cannot drift, and CI fails when the documented
  version and `Directory.Build.props` disagree.

### Added
- **`GenerateAuthInterceptors` generates an interceptor.** The option defaulted to `true` and nothing read it: only the
  auth metadata helpers were emitted, and a helper only works at a call site that remembers to pass its result - the
  credential goes missing on the one call that forgot it. A contract that declares a security scheme now also gets
  `{Service}AuthInterceptor`, a `Grpc.Core.Interceptors.Interceptor` that attaches the credential to every shape of
  call (unary, server streaming, client streaming, duplex and blocking unary), copies the caller's own metadata rather
  than replacing it, and sends the call unauthenticated when the credential factory returns nothing. The credential
  comes from a `Func<string?>` invoked per call, so a rotated token is picked up without rebuilding the interceptor,
  and two schemes that share a header produce one entry rather than two.
- **The proxy sample is built, and its generated code is inspected.** `samples/LegacyProxyExample` was in the
  repository but in no solution and in no CI job, so the one sample that exercises proxy mode - and the only sample
  whose contract declares a security scheme - had never been compiled. CI now builds it, emits the generated sources
  to disk, and fails on a compiler warning inside them and on a folded `!true` / `!false`, which is the shape the
  `CS0162` bug above had.
- **A NativeAOT sample publishes a 3.1 contract as a native image and runs it.** `samples/NativeAotExample` (in the
  solution and in CI) generates from an OpenAPI 3.1 document that uses the constructs the 3.0 reader cannot read - a
  `type` array, a `const`, `prefixItems`, `contentEncoding: base64`, a numeric `exclusiveMinimum`, a `$defs`
  reference, a `webhook` and a `jsonSchemaDialect` - publishes it with `PublishAot`, and then drives the generated
  `Grpc.Core` server and the generated client from one process: register, read back, list, retire, and a round trip
  through the 3.1-only properties, each asserted. It exits non-zero on the first failure, so the sample is a test.
  CI's `native-aot` job publishes `win-x64`, fails when a managed `NativeAotExample.dll` sits beside the native
  executable - the shape a publish that quietly fell back to IL leaves behind - and then runs the executable and
  requires exit `0` with `SMOKE TEST PASSED` in the output.
- **Every sample's descriptor is compiled by `protoc` in CI.** The descriptor is emitted as a string const that no
  compiler reads, so a `.proto` that names an undeclared type or omits an import builds green - which is exactly how
  the missing `google/protobuf/struct.proto` import above reached a consumer. `.github/scripts/check-protos.sh` runs
  the CLI over each sample's spec, writes the descriptor to its own temp directory, and compiles it with
  `protoc --descriptor_set_out`, failing on the first error; a missing CLI or include directory fails the step rather
  than skipping it. The check was planted with both defects (an unknown type, a missing import) and rejects each. A
  new `.gitattributes` pins `*.sh` to LF, because a shell script that reaches a runner with CRLF endings dies on
  `set -euo pipefail` before it checks anything.

## [1.2.0-preview.1] - 2026-09-19

### Added
- **A free-form object (`type: object` with `additionalProperties`, or any object that declares no properties) maps to
  `google.protobuf.Struct`.** This was the one gap that forced a contract to carry JSON as a string: a recorded payload
  said "any JSON object" and got back a placeholder message whose only member was `_HasValue`, which is not what the
  contract said and not something a consumer can use. It holds for an array element too (`repeated …Struct`), in the
  generated C# and in the descriptor. `Struct` is protobuf's own answer for "any JSON object", so a consumer receives a
  real value with a type it already has.

### Known limitations
- **No import is emitted for a well-known type.** A `.proto` that names `google.protobuf.Struct` (or `Timestamp`) without
  `import "google/protobuf/struct.proto";` does not compile. The gap predates this mapping - the descriptor emitter
  writes `syntax`, `package`, `option`, then declarations, and nothing anywhere emits an import - so it is recorded here
  rather than papered over by a test that would pass while the file is wrong. The generated C# is unaffected.
- **A free-form object is not mapped to a typed `map<…>`.** `additionalProperties: { type: string }` is a statement about
  a value type, and `Struct` is looser than that. `Struct` is chosen deliberately as the honest general answer until a
  contract needs the stricter mapping.

## [1.1.4-preview.1] - 2026-09-19

### Fixed
- **A colliding inline enumeration is named after its members instead of being refused.** `1.1.3-preview.1` refused the
  case with `SP2007`, which was the wrong call: a contract that already declares a one-member `state` (the answer to a
  purge) beside the artifact lifecycle's (`pending`, `committed`, `purged`) is a legitimate contract, and refusing it
  breaks working deployments to fix a bug that was latent in them. The first enumeration keeps the name its property
  implies; a second one with different members is registered as `{Name}{Members}`, which is deterministic, collision
  free while the members differ, and says what the type is. `SP2007` remains for the case where even that name is
  taken by a third member set, which is a contract that cannot be resolved mechanically.

## [1.1.3-preview.1] - 2026-09-19

### Fixed
- **An enumeration's identity is its name *and* its members.** An inline enumeration is named after the property that
  declares it, and the mapper kept one model per name — so a second `state` with different members silently inherited
  the first one's members. Found by consuming the package: an artifact lifecycle (`pending`, `committed`, `purged`) and
  a session lifecycle (`open`, `closed`, `expired`) both declare `state`, and the session's members landed nowhere.
  Nothing failed until a consumer referenced them, and then it failed inside generated source. Identical member sets
  stay one shared type, which is what a contract means when the same vocabulary appears in several schemas; different
  member sets under one name are refused with `SP2007`, which names both member sets and says what to do instead.

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
