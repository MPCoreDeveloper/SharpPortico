# Feature Specification: Machine-Readable Report for `generate`

**Feature Branch**: `001-cli-json-output`

**Created**: 2026-10-17

**Status**: Draft

**Input**: User description: "Add a machine-readable JSON output mode to the sharpportico generate command so CI can gate on mapping diagnostics without scraping human-readable text"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Gate a build on the mapping (Priority: P1)

A developer who owns a service pipeline runs the generate command as part of CI. The job must fail when the contract is refused or when the mapping reports a diagnostic the team has decided is unacceptable, and it must do so without parsing human-readable text.

**Why this priority**: This is the only reason the mode exists. Today the summary is aligned text lines on standard output and the diagnostics are text on standard error, so a gate has to scrape both and is brittle the first time a column moves.

**Independent Test**: Run the command once against a spec that maps cleanly and once against a spec that is refused, read standard output as a single document, and assert the reported identity, counts, diagnostics and exit codes.

**Acceptance Scenarios**:

1. **Given** a spec that maps cleanly, **When** the machine-readable mode is requested, **Then** standard output holds one well-formed document naming the spec, service, namespace, package, RPC surface, counts and diagnostics, with exit code 0.
2. **Given** a spec that is refused because two messages would carry one name, **When** the machine-readable mode is requested, **Then** the document carries that diagnostic with its identifier, severity `error` and message, and the exit code is 1.
3. **Given** a spec that maps but carries a warning, **When** the machine-readable mode is requested, **Then** the warning is in the document and the exit code is 0.

---

### User Story 2 - Review what changed between two contract revisions (Priority: P2)

A reviewer compares the report produced for two revisions of the same spec to see what the mapping changed: an RPC renamed, a message count that moved, a diagnostic that appeared.

**Why this priority**: A diff is only meaningful if the same input produces the same bytes. Unstable ordering would report changes that did not happen.

**Independent Test**: Produce the report twice from one spec and compare the bytes; then produce it from two revisions and confirm the difference names only what actually changed.

**Acceptance Scenarios**:

1. **Given** one spec and one set of options, **When** the command runs twice, **Then** the two outputs are byte-identical.
2. **Given** two revisions that differ in one operation, **When** both are reported, **Then** the difference is confined to that operation and the counts that follow from it.

### User Story 3 - Leave the human experience untouched (Priority: P3)

An author running the command by hand keeps the summary they have today, and no existing script that reads the text output changes behaviour.

**Why this priority**: The new mode must be opt-in. A tool that quietly changes what it prints breaks the consumers it already has.

**Independent Test**: Run the command without the new option against the repository's sample specs and compare the output with the pre-change build.

**Acceptance Scenarios**:

1. **Given** a default invocation, **When** it runs, **Then** the text summary and diagnostics are unchanged in content and destination.
2. **Given** the machine-readable mode, **When** it runs, **Then** no human-readable line is interleaved into standard output.

### Edge Cases

- The spec file does not exist: the problem is reported on standard error, no document is emitted, and the exit code is the usage-error code.
- The requested format is not recognised: message on standard error, no document, usage-error code.
- A descriptor is also requested for writing: the document reports where it was written and standard output still holds exactly one document.
- A spec declares no operations: the document reports zero RPCs and zero counts rather than omitting the fields.
- Writing the descriptor fails after the mapping succeeded: the failure is reported on standard error with the failure code, and standard output must not contain a partial document.
- Diagnostic messages contain quotes, newlines or non-ASCII text: the document remains well-formed and the message is preserved.
- A diagnostic has no source location: the entry is still present, with a location that is explicitly absent rather than guessed.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The generate command MUST accept an explicit output-format selection, with the current human-readable format as the default and a machine-readable format as the alternative.
- **FR-002**: In the machine-readable format, standard output MUST contain exactly one well-formed document and nothing else.
- **FR-003**: In the machine-readable format, no human-readable summary or diagnostic line may be written to standard output; standard error stays reserved for usage and I/O failures.
- **FR-004**: The document MUST carry a format version so a consumer can detect an incompatible change instead of silently misreading one.
- **FR-005**: The document MUST carry the spec path as given on the command line and, whenever the contract mapped, the service name, the namespace and the proto package. A refused contract has no model, so no identity is invented for it.
- **FR-006**: The document MUST carry the RPC surface: for each service, its operations with name, HTTP method, original path and RPC kind.
- **FR-007**: The document MUST carry the message count and the enum count.
- **FR-008**: Each diagnostic MUST be reported as an entry carrying its identifier, severity (`error`, `warning` or `info`), formatted message and, when the generator knows it, the source line.
- **FR-009**: A refused contract MUST still produce the document, with the refusal diagnostics in it, and MUST exit with the failure code.
- **FR-010**: Exit codes MUST keep their current meaning: success when the contract mapped, failure when it was refused or an I/O failure occurred, usage error when the command line was wrong.
- **FR-011**: The document MUST be deterministic: the same spec and options produce the same bytes, with no timestamps, machine names or paths that were not passed in.
- **FR-012**: Requesting a written descriptor MUST stay compatible; the document MUST report the path that was written.
- **FR-013**: The default format MUST produce output byte-identical to the current release for the same spec and options.
- **FR-014**: The document MUST be UTF-8 without a byte-order mark.
- **FR-015**: The feature MUST NOT change the mapping, the diagnostics or the descriptor: it transports what the generator already decided.

### Key Entities

- **Mapping report**: One run's outcome — format version, the spec as given, the service identity (name, namespace, package), the RPC surface, counts, diagnostics, and the written descriptor path when one was requested.
- **RPC entry**: One operation — name, HTTP method, original path, RPC kind.
- **Diagnostic entry**: One reported finding — identifier, severity, message, optional source line.
- **Format version**: The contract between this command and its consumers; it changes only when a consumer would have to change.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A CI job can decide pass or fail from the document alone: 100% of the values a gate needs (refusal, diagnostic identifiers, counts) are present without reading text.
- **SC-002**: Repeated runs on one spec produce byte-identical output in 100% of cases, including across operating systems.
- **SC-003**: Every diagnostic the compiler reports for the same spec appears in the document with the same identifier and message — zero mismatches across the repository's sample specs.
- **SC-004**: Default output is unchanged for all four sample specs — zero diff against the current release.
- **SC-005**: For the repository's large-spec fixture, the machine-readable mode costs no more than 10% over the human-readable mode.
- **SC-006**: A consumer who has never seen the tool can build a passing gate from the documented shape in under 15 minutes, without reading the source.

## Assumptions

- Consumers use standard document tooling rather than a bespoke parser.
- The machine-readable format is new, so only the human-readable format carries a compatibility obligation.
- The format version starts at 1 and is bumped only for changes a consumer would have to react to.
- The report is a transport for the generator's own decisions; it never re-derives a diagnostic or a mapping of its own.
- Diagnostics keep the severity the generator assigns; the mode neither promotes warnings nor hides errors.
- The descriptor, when written, remains byte-for-byte the artifact a build embeds.