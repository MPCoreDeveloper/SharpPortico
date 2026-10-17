# Tasks: Machine-Readable Report for `generate`

**Input**: Design documents from `/specs/001-cli-json-output/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Included. The specification's success criteria (SC-002 to SC-005) and constitution principle III ("Every Contract Decision Is Proven") make tests part of the feature, not an option.

**Organization**: Tasks are grouped by user story so each story can be implemented and verified independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Exact file paths are in every description

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Make the CLI testable from the existing test project and capture the text baseline before anything moves.

- [x] T001 Add a `ProjectReference` from `tests/SharpPortico.Tests/SharpPortico.Tests.csproj` to `src/SharpPortico.Cli/SharpPortico.Cli.csproj` (both already multi-target `$(SharpPorticoNetTargetFrameworks)`)
- [x] T002 [P] Add `<InternalsVisibleTo Include="SharpPortico.Tests" />` to `src/SharpPortico.Cli/SharpPortico.Cli.csproj` so tests can call the internal entry point
- [x] T003 [P] Capture the current text output of `tests/SharpPortico.Tests/openapi/petstore.yaml` into `tests/SharpPortico.Tests/openapi/petstore.report.txt` using the pre-change build (golden file for SC-004)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The report types, the writer, and the entry-point split every user story builds on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T004 [P] Write `tests/SharpPortico.Tests/CliTextOutputTests.cs` comparing `Program.RunAsync` output for `openapi/petstore.yaml` with `openapi/petstore.report.txt` after normalizing CRLF to LF (must pass before and after T005)
- [x] T005 Refactor `src/SharpPortico.Cli/Program.cs`: thin `Main` delegating to `internal static Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)`, with `GenerateAsync`, `WriteProtoAsync` and `Format` taking the writers; no output text changes (FR-013)
- [x] T006 Add `--format <text|json>` parsing and validation to `src/SharpPortico.Cli/Program.cs`, default `text`, case-insensitive, unknown value → usage line on stderr and exit 2; update the usage and `--help` text (FR-001, FR-010)
- [x] T007 [P] Create `src/SharpPortico.Cli/Reporting/MappingReport.cs`: immutable projection of `GrpcModel` into the identity, service, RPC and count entities in `data-model.md` (FR-004, FR-005, FR-006, FR-007)
- [x] T008 Create `src/SharpPortico.Cli/Reporting/JsonReportWriter.cs`: `Utf8JsonWriter` with `Indented = true`, two-space indent, `NewLine = "\n"`, UTF-8 without BOM, one trailing LF (FR-002, FR-014, SC-002)
- [x] T009 Wire JSON mode into `src/SharpPortico.Cli/Program.cs`: in `json`, write the descriptor first when requested, then exactly one document to `stdout`; diagnostics go into the document only; exit codes unchanged (FR-002, FR-003, FR-009, FR-012)

**Checkpoint**: `--format text` is byte-identical to before (T004 green) and `--format json` emits one document.

---

## Phase 3: User Story 1 - Gate a build on the mapping (Priority: P1) 🎯 MVP

**Goal**: A CI job decides pass/fail from the document alone, including when the contract is refused.

**Independent Test**: Run the clean, warning and refused specs from `quickstart.md` (scenarios 2, 4, 5) and assert document contents plus exit codes.

### Tests for User Story 1

- [x] T010 [P] [US1] Add `tests/SharpPortico.Tests/CliJsonReportTests.cs` asserting the mapped document: `formatVersion` 1, `spec` as typed, service identity, RPC surface, counts, empty diagnostics, no BOM, one trailing LF, exit 0 (FR-004, FR-005, FR-006, FR-007, FR-008, FR-014, SC-001)
- [x] T011 [P] [US1] Add the refusal case to `tests/SharpPortico.Tests/CliJsonReportTests.cs` using the `SP2005` fixture from `ContractRefusalTests`: one parseable document containing an `error` diagnostic, empty descriptor, exit 1 (FR-009, SC-003)
- [x] T012 [P] [US1] Add the warning case to `tests/SharpPortico.Tests/CliJsonReportTests.cs` using `samples/NativeAotExample/openapi/fleet-3.1.yaml`: `SP1002`/`SP1003` entries with `severity` `warning` and a 1-based `line`, exit 0 (FR-008, constitution IV)
- [x] T013 [US1] Add usage-error cases to `tests/SharpPortico.Tests/CliJsonReportTests.cs`: `--format xml`, an unknown option, and a missing spec file each exit 2 with empty stdout (FR-010)
- [x] T014 [US1] Add the `--out` cases to `tests/SharpPortico.Tests/CliJsonReportTests.cs`: `descriptor.path` names the written file, and an unwritable target exits 1 with empty stdout (FR-012, invariant I9)

### Implementation for User Story 1

- [x] T015 [US1] Extend `src/SharpPortico.Cli/Reporting/MappingReport.cs` with the `DiagnosticEntry` projection: id, severity through the existing `Severity(...)` helper, formatted message, and `line` only when a source location exists (FR-008, invariant I4, constitution IV)
- [x] T016 [US1] Write the document on the refusal branch of `RunAsync` in `src/SharpPortico.Cli/Program.cs` before returning 1, and omit `descriptor` whenever the write failed (FR-009, FR-012, invariant I9)

**Checkpoint**: US1 is testable on its own and is the MVP: a gate can run on the document without touching the text output.

---

## Phase 4: User Story 2 - Review what changed between two contract revisions (Priority: P2)

**Goal**: The same input produces the same bytes, so a diff between two reports means the contract changed.

**Independent Test**: Produce the report twice for one spec and compare hashes; produce it for two revisions and confirm only the changed operation differs (quickstart scenario 6).

### Tests for User Story 2

- [x] T017 [P] [US2] Add `tests/SharpPortico.Tests/CliJsonReportTests.cs` determinism case: two runs of the same spec and options produce identical bytes (FR-011, SC-002)
- [x] T018 [P] [US2] Add `tests/SharpPortico.Tests/CliJsonReportTests.cs` ordering case: a spec with several services and operations preserves declaration order in `services` and `rpcs`, and diagnostics keep report order (FR-011, invariant I6)

### Implementation for User Story 2

- [x] T019 [US2] Check in `tests/SharpPortico.Tests/openapi/petstore.report.json` produced once from the T009 build, add the comparison test, and add a `.github/workflows/ci.yml` step in `build-test` that regenerates the report on Linux and diffs it against the golden file (SC-002 across operating systems)

**Checkpoint**: A report that differs between runs or between operating systems is a failing build.

---

## Phase 5: User Story 3 - Leave the human experience untouched (Priority: P3)

**Goal**: The default invocation is unchanged, and the new mode never mixes human text into its document.

**Independent Test**: Run the warning spec in text mode and in JSON mode and check each stream separately (quickstart scenarios 1 and 4).

### Tests for User Story 3

- [x] T020 [US3] Add `tests/SharpPortico.Tests/CliTextOutputTests.cs` case: the warning spec writes the summary to `stdout`, the diagnostics to `stderr`, and exits 0 — destinations unchanged from today (FR-013, SC-004)
- [x] T021 [US3] Add `tests/SharpPortico.Tests/CliJsonReportTests.cs` case: with warnings and `--out` together, `stdout` parses as exactly one document and `stderr` is empty (FR-003, invariant I7)

**Checkpoint**: Both audiences are served by one command without either noticing the other.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: The documentation, changelog, performance evidence and final gates the constitution requires.

- [x] T022 [P] Document `--format json` in `docs/SharpPortico.md` (§5 CLI) and `src/SharpPortico.Cli/README.md`, and add the third command example to the CLI section of `README.md` (SC-006)
- [x] T023 [P] Add the `CHANGELOG.md` entry under the unreleased/current version ("Added: `--format json` for CI gates"); no version bump is made by this feature (constitution V)
- [x] T024 [P] Extract `BuildLargeSpec` from `tests/SharpPortico.Tests/LargeSpecPerformanceTests.cs` into `tests/SharpPortico.Tests/Infrastructure/LargeSpecBuilder.cs` and add the text-versus-JSON timing assertion of at most 10% overhead, measured as the median of five runs after one warm-up run (SC-005)
- [x] T025 Verify with `git diff --stat` that `src/SharpPortico.SourceGenerator/Mapping/`, `src/SharpPortico.SourceGenerator/Emit/`, `src/SharpPortico.Runtime/` and `src/SharpPortico.Abstractions/` are untouched, and that all four sample descriptors are byte-identical (FR-015, constitution I)
- [x] T026 Run `specs/001-cli-json-output/quickstart.md` scenarios 1-10 and record pass/fail next to each scenario

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup; blocks every user story.
- **US1 (Phase 3)**: Depends on Foundational; independent of US2 and US3.
- **US2 (Phase 4)**: Depends on US1 (there must be a document to compare).
- **US3 (Phase 5)**: Depends on US1; its text-mode case depends on T004/T005.
- **Polish (Phase 6)**: Depends on US1-US3.

### Within Each User Story

- Tests precede implementation; a test that passes before its implementation task asserts nothing.
- T015 extends the type T007 created; T016 changes the branch T009 wired.
- T019's golden file is produced by T009, so it cannot precede it.

### Parallel Opportunities

- T002 and T003 (setup) in parallel after T001.
- T004 and T007 in parallel: different files, and T004 pins the behaviour T005 must not change.
- T010, T011 and T012 in parallel: three test cases written together, run independently.
- T017 and T018 in parallel.
- T022, T023 and T024 in parallel: docs, changelog and test infrastructure touch different files.

---

## Parallel Example: User Story 1

```text
Task: "T010 [P] [US1] Mapped document shape in tests/SharpPortico.Tests/CliJsonReportTests.cs"
Task: "T011 [P] [US1] Refusal still reports in tests/SharpPortico.Tests/CliJsonReportTests.cs"
Task: "T012 [P] [US1] Warning transported with line in tests/SharpPortico.Tests/CliJsonReportTests.cs"
```

## Parallel Example: Polish

```text
Task: "T022 [P] Documentation in docs/SharpPortico.md and src/SharpPortico.Cli/README.md"
Task: "T023 [P] Changelog entry in CHANGELOG.md"
Task: "T024 [P] Perf comparison via tests/SharpPortico.Tests/Infrastructure/LargeSpecBuilder.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1: Setup.
2. Phase 2: Foundational — the text path is pinned before it moves, and the writer exists.
3. Phase 3: US1 — the document, the refusal document and the exit codes.
4. **STOP and VALIDATE**: quickstart scenarios 1, 2, 4, 5 and 8. A gate can now be built.
5. Only then continue to US2 and US3.

### Incremental Delivery

1. Setup + Foundational → `--format text` provably unchanged.
2. US1 → a CI gate works (MVP).
3. US2 → the report is diffable and cross-OS stable.
4. US3 → the human path and the new path are both pinned.
5. Polish → docs, changelog, performance evidence, final constitution gates.

---

## Notes

- [P] marks tasks that touch different files and have no ordering dependency.
- `Program.cs` is touched by T005, T006, T009, T016 and T020; those are sequential by design, not parallel.
- `MappingReport.cs` is touched by T007 and T015, so T015 follows T007.
- Tests are written before their implementation task and must fail first.
- Every FR in `spec.md` has at least one task above; the coverage table in the analysis report is the check.
- Do not change `Mapping/`, `Emit/`, `Diagnostics/` or the runtime package: the feature transports decisions, it does not make them.

## What implementation changed in the design

Three facts were only visible once the code ran; the artifacts were corrected rather than the code being bent
to match them.

- **A refused contract has no model.** `ParseResult.Failure` returns `Model = null`, so there is no service
  identity to report and no honest way to build one from the command line. FR-005, the contract table and the
  refusal example now say `service` is present only when the mapping produced a model; T011 asserts its absence.
- **The pipeline reports diagnostics without a source location.** `GeneratorDiagnostic.Location` is always
  `null` today, so real documents carry no `line` - which is correct under invariant I4 ("never guessed"). T012
  asserts the absence; `A_Diagnostic_That_Knows_Its_Line_Reports_It_One_Based` proves the field with a located
  diagnostic, so the projection cannot rot.
- **The tests needed a seam and a shared fixture.** `Infrastructure/CliHarness.cs` runs `Program.RunAsync` with
  captured writers, and `Infrastructure/LargeSpecBuilder.cs` is the 400-operation spec both the generator budget
  and the report-cost comparison measure. Neither adds a project or a package.
