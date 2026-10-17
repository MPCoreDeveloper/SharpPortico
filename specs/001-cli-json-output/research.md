# Phase 0 Research: Machine-Readable Report for `generate`

This feature is additive to a command that already computes everything it needs. The research therefore concentrates on the three places where a mistake is expensive: the option surface, the serialization (determinism and dependencies), and how the existing text path and diagnostics are preserved.

## R1 — Option surface

**Decision**: `--format <text|json>`, default `text`, compared case-insensitively; any other value is a usage error (exit 2) reported on stderr with the usage line.

**Rationale**: The value names the thing being chosen, so a third format later is a value rather than a new flag. A default keeps FR-013 honest: the text path is what runs when nobody asks.

**Alternatives considered**:

- `--json` as a boolean — reads well today, but cannot express a third format, and the command already has `--out`, so a bare switch next to value-taking options is the odd one out.
- A separate `report` command — would need its own parsing and its own path to `ParseAndMap`, which is exactly the drift the CLI exists to avoid.
- An environment variable — invisible in `--help`, and CI logs would not show which format produced a document.

## R2 — Serialization

**Decision**: `Utf8JsonWriter` (System.Text.Json, part of the shared framework on `net10.0` and `net11.0`) writing to a `MemoryStream` with `Indented = true`, two-space indent, `NewLine = "\n"`, then decoded as UTF-8 and written to stdout without a BOM, followed by a single LF.

**Rationale**: Fields are written in call order, so the document's byte order is a property of the code rather than of a serializer's reflection order. The API is reflection-free, which is the same property the product claims for generated code (constitution II). `NewLine = "\n"` and an explicit LF make SC-002 (byte-identical across operating systems) true rather than hoped for. No package reference changes, so `Directory.Packages.props` and `Directory.Build.props` are untouched (constitution V).

**Alternatives considered**:

- `JsonSerializer` over POCOs — property order is a convention, not a contract, and the serializer is reflection-based unless a source-generated context is added.
- A source-generated `JsonSerializerContext` — the cleanest use of `JsonSerializer`, but it adds build plumbing for a document with roughly twenty fields and makes the order an artifact of declaration order plus options.
- Hand-built strings (`StringBuilder`) — re-implements escaping, which is the one thing the platform's writer gets right for free (quotes, newlines, non-ASCII diagnostic messages).

## R3 — Where diagnostics go in JSON mode

**Decision**: In JSON mode, diagnostics appear only inside the document; stderr is reserved for usage and I/O failures.

**Rationale**: FR-002 and FR-003. A consumer that reads stdout must not have to filter lines, and a gate that reads stderr must not see text it might mistake for the report. The severity and message are in the document, so nothing is lost.

**Alternatives considered**:

- Write them to both streams — duplicates every warning and invites two sources of truth.
- Keep text diagnostics on stderr and omit them from the document — a gate could not see the reason for a refusal, and constitution IV (never fail silently) would be violated in spirit.

## R4 — A refused contract still reports

**Decision**: When the mapping is refused, the JSON document is still written, containing the refusal diagnostics, and the process exits 1.

**Rationale**: The diagnostics are the value in that run; a gate needs the identifier, not just the exit code. The current pipeline returns `result.Diagnostics` before the `IsSuccess` branch, so nothing has to be recomputed.

**Alternatives considered**:

- Emit nothing and rely on the exit code — a CI log then explains nothing, and the text mode already reports the reason.

## R5 — Determinism

**Decision**: No timestamps, no machine names, no environment values. `spec` is the path exactly as typed; `descriptor.path` is the path the text mode already prints. Numbers are written invariantly; ordering follows the model (services as declared, RPCs as declared, diagnostics in report order); the document ends with one LF.

**Rationale**: FR-011 and SC-002. A report that changes without the contract changing is worse than no report, because a reviewer learns to ignore the diff.

**Alternatives considered**:

- Include a generation timestamp — useful for logs, fatal for diffing, and the CI job already timestamps its own output.
- Sort RPCs alphabetically — stable, but hides the declaration order a reader compares against the spec.

## R6 — Testability of a `Main`

**Decision**: Split `Program` into a thin `Main` that passes `Console.Out`/`Console.Error` and an internal `RunAsync(string[] args, TextWriter stdout, TextWriter stderr)` that holds the current logic. Expose internals to the test assembly with the SDK's `<InternalsVisibleTo Include="SharpPortico.Tests" />` item.

**Rationale**: Tests then exercise the real argument parsing, the real exit codes and the real streams, with no global console state, so they stay parallel-safe. It is also the smallest change to `Program.cs`: the existing body moves, it is not rewritten.

**Alternatives considered**:

- Launch the built tool as a process — closest to a user, but slow, path-dependent across two target frameworks, and awkward to assert on with captured output.
- `Console.SetOut` in tests — global state shared by parallel tests; the failure mode is intermittent, which is the worst kind.
- Make `Program` public — widens the tool's surface for no reason; `InternalsVisibleTo` says exactly who may look inside.

## R7 — Preserving the text path

**Decision**: Move the existing print block verbatim into `WriteTextReport(...)` and pin it with a golden file (`tests/SharpPortico.Tests/openapi/petstore.report.txt`), compared after normalizing CRLF to LF.

**Rationale**: FR-013 and SC-004. The golden file turns "output unchanged" into a test rather than a claim, and it fails loudly if a future change reorders a line — which is exactly the regression a script consuming the text output would suffer.

**Alternatives considered**:

- Assert individual substrings — passes while the format quietly changes.
- Compare against the previous release's binary in CI — real, but adds a package download and a version pin to the build.

## R8 — `--out` and the document

**Decision**: Write the descriptor first, then the document, so a write failure produces stderr plus exit 1 and never a document claiming a path that does not exist. On success the document carries `descriptor.path`.

**Rationale**: The edge case in the spec and FR-012. The text mode already writes the descriptor after printing the summary; for JSON mode the order is inverted because a partial or lying document is worse than no document.

**Alternatives considered**:

- Report the intended path before writing — the document would claim something that may not exist.

## R9 — Format version

**Decision**: `formatVersion: 1`, independent of the package version; bumped only when a consumer would have to change.

**Rationale**: FR-004. The package version changes on every release, including releases that do not touch the document; tying the two would train consumers to ignore the field.

**Alternatives considered**:

- Reuse `SharpPorticoProductVersion` — one number to maintain, but a false signal on every unrelated release.

## R10 — Where the code lives

**Decision**: `src/SharpPortico.Cli/Reporting/` (`MappingReport.cs`, `JsonReportWriter.cs`), with `Program.cs` orchestrating. No change to `src/SharpPortico.SourceGenerator/` or `src/SharpPortico.Runtime/`.

**Rationale**: The document is a CLI format. Putting it in the generator would make the compiler-hosted component know about a tool's output, and putting it in the runtime package would add consumer surface for something no consumer calls. Keeping `Mapping/` and `Emit/` untouched is also what keeps constitution I and FR-015 true by construction.

**Alternatives considered**:

- A shared `SharpPortico.Reporting` package — a new project and a new published artifact for one tool's output format.
- Emit the document from the generator as an MSBuild artifact — a build-time switch, not a request-time one; also mixes concerns.

## Post-Design Constitution Re-check

Re-evaluated after `data-model.md` and `contracts/cli.md`:

- **I** — no file under `Mapping/` or `Emit/` is touched; the descriptor still comes from `ProtoEmitter.Emit`. PASS.
- **II** — the writer has no reflection, no `Activator`, and adds no package; the report mentions no new AOT claim. PASS.
- **III** — the document shape is published in `contracts/cli.md`; every FR has a named test in `tasks.md`; determinism and the golden text report are explicit tasks. PASS.
- **IV** — diagnostics are transported with their own severity, and a refusal is still reported. PASS.
- **V** — no package or version change is required by the feature; `CHANGELOG.md` and both CLI documents are in scope. PASS.

No new `SP` diagnostic is introduced, so the ID ranges in `Diagnostics.cs` are untouched.