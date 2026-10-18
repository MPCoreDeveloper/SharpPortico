# SharpPortico documentation — 1.5.0

Five documents, each answering a different question. Everything here describes **1.5.0**, the version in
`Directory.Build.props` and on nuget.org.

| Document | Answers |
| --- | --- |
| [README](../README.md) | what SharpPortico is, in one page: the pitch, the architecture, the mapping table, the CLI |
| [Developer guide](SharpPortico.md) | how to adopt it: packages, declaration, proxy mode, mapping rules, samples, configuration |
| [Reference](reference.md) | what exists: every option and its default, the generated surface member by member, the runtime contracts, the CLI, and how each claim is verified |
| [Presence](presence.md) | what a `nullable: true` property becomes: proto3 `optional`, `Has…`/`Clear…`, round-trip semantics, gateway behaviour, upgrading from 1.4.0 |
| [Diagnostics](diagnostics.md) | what `SP…` means: every id, its severity, what to do about it, and how to gate a build on it |
| [Changelog](../CHANGELOG.md) | what changed, release by release |

## The three claims, and where they are proved

| Claim | Proof |
| --- | --- |
| **Reflection-free and NativeAOT-safe** | `samples/NativeAotExample` is published as a native image in CI, the job refuses a publish that left a managed assembly behind, and it runs to `SMOKE TEST PASSED` |
| **The descriptor is real protobuf** | CI compiles every sample's `.proto` with `protoc` (`.github/scripts/check-protos.sh`) |
| **The package works as installed** | CI consumes the packed `.nupkg` from a throwaway project and runs the packed tool on both frameworks |

## Conventions in these documents

- **Code that appears here is output, not illustration**, wherever the text says so: the `.proto` blocks come from the
  generator, and the generated C# is the shape the emitter writes.
- **An option that does nothing is listed as doing nothing.** The reference has a table for properties that compile and
  are not read by this version, and the diagnostics page lists the ids that no code path raises.
- **Versions are checked, not remembered.** CI compares `SharpPorticoProductVersion` against every install line in these
  documents, so a guide cannot describe a package that is not the one being built.

## Samples

| Sample | Demonstrates |
| --- | --- |
| `samples/GrpcServerExample` | a generated server and client over `Grpc.Core`, end to end |
| `samples/MinimalApiExample` | the generated client behind ASP.NET Core minimal APIs |
| `samples/LegacyProxyExample` | proxy mode: cache hit, forced bypass, an outbound `X-Api-Key`, REST call counting |
| `samples/NativeAotExample` | the OpenAPI **3.1** contract — type arrays, `const`, tuples, `$defs`, webhooks — published as a native image |
