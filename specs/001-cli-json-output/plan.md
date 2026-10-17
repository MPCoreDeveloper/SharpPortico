# Implementation Plan: Machine-Readable Report for `generate`

**Branch**: `001-cli-json-output` | **Date**: 2026-10-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-cli-json-output/spec.md`

**Note**: This plan was produced by the `/speckit-plan` workflow; the artifacts below are the design, not the implementation.

## Summary

Add `--format json` to `sharpportico generate`. The command already computes everything a CI gate needs — the mapped model and the generator's own diagnostics — and prints it as aligned text to stdout and stderr. The plan projects that same result into one deterministic JSON document on stdout, leaves the default text path byte-identical, and keeps exit codes unchanged. No mapping, emitter, diagnostic or descriptor code is touched: the CLI stays a host over `OpenApiParser.ParseAndMap` and `ProtoEmitter.Emit`, which is what keeps the report and a build from disagreeing.

## Technical Context

**Language/Version**: C# 14 (`LangVersion=preview`), targeting `net10.0` and `net11.0` (`$(SharpPorticoNetTargetFrameworks)`)

**Primary Dependencies**: none added. `System.Text.Json` is part of the shared framework on both targets, so `Directory.Packages.props` is unchanged; `System.CommandLine` stays referenced as it is today (the command's arguments are parsed by hand in `Program.cs`).

**Storage**: N/A — reads one OpenAPI file, optionally writes one `.proto` file

**Testing**: xunit (`tests/SharpPortico.Tests`, multi-targeted to both runtimes); new tests call the CLI's internal entry point with injected writers, so no console redirection and no parallel-test interference

**Target Platform**: the `sharpportico` .NET tool on Windows, Linux and macOS

**Project Type**: CLI tool over the source generator's mapping pipeline

**Performance Goals**: the JSON path costs no more than 10% over the text path on the repository's large-spec fixture (SC-005)

**Constraints**: stdout holds exactly one document in JSON mode (FR-002); default output byte-identical (FR-013); exit codes unchanged (FR-010); document deterministic across operating systems (FR-011, SC-002); no new dependency (constitution V)

**Scale/Scope**: one command, one new option, four sample specs plus the large-spec fixture; no new project

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Gate for this feature | Verdict |
|---|---|---|
| I. The Generated Code Is the Product | `Emit/` and `Mapping/` are not modified; the CLI keeps calling `ProtoEmitter.Emit`, so the descriptor in the report and the descriptor in a build cannot diverge; a text-path snapshot proves no emitted artifact moved | PASS |
| II. Reflection-Free, AOT Claim Stated Precisely | The writer enumerates model members explicitly with `Utf8JsonWriter`; no reflection-based serialization, no `Activator`, no new package; no new AOT claim is made | PASS |
| III. Every Contract Decision Is Proven | Every FR maps to a test: shape, exit codes, stdout discipline, determinism, refusal, UTF-8, `--out` path; SC-004 is a byte comparison of the text path; the shape is published in `contracts/cli.md` | PASS |
| IV. Never Fail Silently | Diagnostics keep the generator's id, severity and message; a refused contract still produces the document plus exit code 1; warnings are never dropped or promoted | PASS |
| V. Compatibility, One Version, Dependency Discipline | Default output unchanged (PATCH-level compatibility for consumers); a `CHANGELOG.md` entry is required; the package version bump belongs to the release step, and no document that names the version is touched by this feature | PASS |

No violations and no complexity to justify. The re-check after Phase 1 is recorded at the end of `research.md`.

## Project Structure

### Documentation (this feature)

```text
specs/001-cli-json-output/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output — decisions and rejected alternatives
├── data-model.md        # Phase 1 output — report shape and invariants
├── quickstart.md        # Phase 1 output — runnable validation guide
├── contracts/
│   └── cli.md           # Phase 1 output — command surface and document contract
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/SharpPortico.Cli/
├── Program.cs                    # argument parsing, --format selection, writer injection
└── Reporting/
    ├── MappingReport.cs          # projection of GrpcModel + diagnostics into report values
    └── JsonReportWriter.cs       # deterministic UTF-8 document writer

tests/SharpPortico.Tests/
├── CliJsonReportTests.cs         # shape, severity, refusal, determinism, escaping, --out
├── CliTextOutputTests.cs         # default output unchanged (SC-004)
├── Infrastructure/CliHarness.cs  # runs Program.RunAsync with captured writers
├── Infrastructure/LargeSpecBuilder.cs   # shared 400-operation fixture (SC-005)
└── openapi/
    ├── petstore.report.txt       # golden text report (SC-004)
    └── petstore.report.json      # golden JSON report (SC-002, cross-OS)

src/SharpPortico.Cli/SharpPortico.Cli.csproj   # ProjectReference from tests + InternalsVisibleTo
docs/SharpPortico.md                            # §5 CLI: document the new option
src/SharpPortico.Cli/README.md                  # package readme: document the new option
CHANGELOG.md                                    # entry for the feature
```

**Structure Decision**: The repository is a single solution with `src/`, `tests/` and `samples/` (`SharpPortico.slnx`). No new project is introduced: the report is a projection of values the CLI already has, so it lives beside `Program.cs` in a `Reporting/` folder rather than in the generator (which must not learn about a CLI format) or in the runtime package (which is additive surface for consumers). `Program.cs` is split into a thin `Main` and an internal `RunAsync(args, stdout, stderr)` so tests exercise the real argument parsing and exit codes without touching `Console`.

## Complexity Tracking

> No Constitution Check violations. Nothing to justify.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |