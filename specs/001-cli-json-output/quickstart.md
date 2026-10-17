# Quickstart Validation: Machine-Readable Report for `generate`

Runnable scenarios that prove the feature end to end. They reference the contract
([contracts/cli.md](./contracts/cli.md)) and the model ([data-model.md](./data-model.md)) rather than
restating them.

## Prerequisites

- .NET 10 and .NET 11 SDKs, as CI uses.
- Repository root as the working directory.
- No `specify` installation and no NuGet restore of a published package: the tool is built from source.

## Build the tool

```powershell
dotnet build src/SharpPortico.Cli/SharpPortico.Cli.csproj -c Release -f net10.0
$cli = "src/SharpPortico.Cli/bin/Release/net10.0/SharpPortico.Cli.dll"
```

Running the built assembly (rather than `dotnet run`) keeps the document free of build output.

## Scenario 1 — The text path has not moved (FR-013, SC-004)

Capture the output on the current commit, then on the branch, and compare:

```powershell
dotnet $cli generate tests/SharpPortico.Tests/openapi/petstore.yaml > before.txt   # on the base commit
dotnet $cli generate tests/SharpPortico.Tests/openapi/petstore.yaml > after.txt    # after the change
Compare-Object (Get-Content before.txt) (Get-Content after.txt)
```

Expected: no differences, and stderr is empty for this spec. The same comparison is a test in
`CliTextOutputTests` against `openapi/petstore.report.txt`.

## Scenario 2 — Exactly one well-formed document (FR-002, FR-014)

```powershell
dotnet $cli generate tests/SharpPortico.Tests/openapi/petstore.yaml --format json > report.json
$doc = Get-Content report.json -Raw | ConvertFrom-Json
$doc.formatVersion            # 1
```

Expected: `ConvertFrom-Json` succeeds on the whole file, `formatVersion` is `1`, and the first three
bytes are not `EF BB BF` (no BOM). The file ends with exactly one `0A`.

## Scenario 3 — The document carries the mapping (FR-005 to FR-008)

```powershell
$doc.spec                     # tests/SharpPortico.Tests/openapi/petstore.yaml, exactly as typed
$doc.service.name             # non-empty
$doc.service.namespace        # non-empty
$doc.service.package          # non-empty
$doc.services[0].rpcs.Count   # > 0
$doc.counts.messages          # > 0
$doc.counts.enums             # >= 0
$doc.diagnostics.Count        # >= 0, and every entry has id, severity and message
```

Expected: every field above exists even when its value is empty or zero (invariant I2).

## Scenario 4 — Warnings are transported, not hidden (FR-008, constitution IV)

```powershell
dotnet $cli generate samples/NativeAotExample/openapi/fleet-3.1.yaml --format json > fleet.json
$LASTEXITCODE                 # 0
(Get-Content fleet.json -Raw | ConvertFrom-Json).diagnostics |
    Where-Object { $_.id -like 'SP100*' } |
    Select-Object id, severity, line
```

Expected: at least one `SP1002` entry with severity `warning` and no `line` (the generator reports these
without a source tree, and an absent location is not line 1), and the exit code is 0 because the
contract still mapped.

## Scenario 5 — A refusal still reports (FR-009, contract IV)

Create `collide.yaml` with the specification the refusal tests use:

```yaml
openapi: 3.0.3
info: { title: Search, version: 1.0.0 }
paths:
  /search:
    post:
      operationId: Search
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/SearchRequest' }
      responses:
        '200':
          description: ok
components:
  schemas:
    SearchRequest:
      type: object
      properties:
        text: { type: string }
```

```powershell
dotnet $cli generate collide.yaml --format json > refused.json
$LASTEXITCODE                 # 1
(Get-Content refused.json -Raw | ConvertFrom-Json).diagnostics | Select-Object id, severity
```

Expected: exit code 1, the document parses, the diagnostics contain `SP2005` with severity `error`, and
there is no `service` block - a refused contract never produced a model, so no identity is invented.
stdout holds no other text.

## Scenario 6 — Determinism (FR-011, SC-002)

```powershell
dotnet $cli generate tests/SharpPortico.Tests/openapi/petstore.yaml --format json > a.json
dotnet $cli generate tests/SharpPortico.Tests/openapi/petstore.yaml --format json > b.json
(Get-FileHash a.json).Hash -eq (Get-FileHash b.json).Hash    # True
```

Expected: identical hashes on repeated runs, and identical bytes between the Windows and Linux CI
jobs for the same input.

## Scenario 7 — `--out` reports what it wrote (FR-012)

```powershell
dotnet $cli generate tests/SharpPortico.Tests/openapi/petstore.yaml --out out/ --format json > with-descriptor.json
$doc = Get-Content with-descriptor.json -Raw | ConvertFrom-Json
Test-Path $doc.descriptor.path                                # True
```

Failure case: make the target unrepresentable (create a file named `blocked`, then ask for
`--out blocked/x.proto`). Expected: exit code 1, a message on stderr, and an empty stdout — never a
document claiming a path that does not exist (invariant I9).

## Scenario 8 — Usage errors stay usage errors (FR-010)

```powershell
dotnet $cli generate tests/SharpPortico.Tests/openapi/petstore.yaml --format xml
$LASTEXITCODE                 # 2
dotnet $cli generate does-not-exist.yaml --format json
$LASTEXITCODE                 # 2
```

Expected: both exit 2 with a message and the usage line on stderr, and nothing on stdout.

## Scenario 9 — Cost of the new path (SC-005)

```powershell
dotnet test tests/SharpPortico.Tests/SharpPortico.Tests.csproj -c Release --filter "FullyQualifiedName~LargeSpec"
```

Expected: the existing large-spec test still passes, and the added comparison reports the JSON path
within 10% of the text path on the same 400-operation specification. Both paths are measured as the
median of five runs after one warm-up run, so a cold start or a noisy CI host cannot decide the
result.

## Scenario 10 — The whole suite, both runtimes

```powershell
dotnet test tests/SharpPortico.Tests/SharpPortico.Tests.csproj -c Release --filter "FullyQualifiedName~Cli"
dotnet build SharpPortico.slnx -c Release
```

Expected: the CLI tests pass on `net10.0` and `net11.0`, and the solution builds without new warnings
— including in `Mapping/` and `Emit/`, which this feature does not touch.

## Result (2026-10-17)

Run against the branch on Windows with `net10.0` (both runtimes for scenario 10). Scenarios 6 and 9 have a
second half that only CI can run; that division is the point of them, not a gap.

| Scenario | Result | Evidence |
|---|---|---|
| 1 | PASS | `petstore.report.txt` is byte-identical; also pinned by `CliTextOutputTests` |
| 2 | PASS | `formatVersion` 1, parseable as a whole, no BOM, one trailing `0A`, empty stderr |
| 3 | PASS | `PetstoreService` / `PetstoreService` / `petstoreservice`, 3 RPCs, 7 messages, 1 enum, 0 diagnostics |
| 4 | PASS | `SP1002` plus five `SP1003` entries, all `warning`, no `line`, exit 0 |
| 5 | PASS | `SP2005` / `error`, exit 1, no `service` block, stderr empty |
| 6 | PASS | two runs hash identically; the Linux job diffs the checked-in report |
| 7 | PASS | `descriptor.path` exists and equals the text mode's `proto:` line; blocked target → exit 1, empty stdout, `error:` on stderr |
| 8 | PASS | `--format XML` → 2 and a missing spec → 2, both with empty stdout and the usage line |
| 9 | PASS | text median 229 ms, json median 240 ms (1.05×) on the 400-operation fixture |
| 10 | PASS | 78/78 tests on `net10.0` and 78/78 on `net11.0`; the solution builds with 0 errors |
