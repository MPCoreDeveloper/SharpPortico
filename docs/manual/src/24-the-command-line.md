# 24. The Command Line

<p class="lead">`sharpportico` has one command. It runs the same parser and the same `ProtoEmitter` the build
runs, prints what the mapping decided, and optionally writes the descriptor to a file. It does not generate C#.</p>

## Usage

```text
usage: sharpportico generate <openapi.yaml|json> [--out DIR|FILE] [--service-name NAME]
                             [--namespace NS] [--format text|json]
```

The first argument must be `generate`; `--help` and `-h` print the same text and exit 0. Options are separate
arguments (`--out build`), not `--out=build`; a value glued to the name is reported as an unknown argument and
exits 2.

| Option | Effect |
| --- | --- |
| `--out DIR` | writes `<spec>.proto` inside `DIR`, creating it if needed |
| `--out FILE.proto` | writes exactly that file; the `.proto` suffix is the switch |
| `--service-name NAME` | service base name; `Service` is appended unless the name already ends with it |
| `--namespace NS` | namespace of the generated C#; defaults to the service name |
| `--format text` | the default: a summary on stdout, diagnostics on stderr |
| `--format json` | one JSON document on stdout and nothing else |

Without `--service-name`, the file stem is used, so `sink.yaml` becomes `SinkService` after the suffix is
applied. Without `--namespace`, the namespace equals the service name. Both are the defaults a build applies
through `AdditionalFileRequest.ToWorkItem`, not a second set of rules maintained by the CLI.

## A text run

Running the command against the manual's `sink.yaml`:

```text
spec:       docs/manual/fixtures/sink.yaml
service:    SinkService
namespace:  SinkService
package:    sinkservice
rpc:        SinkService (4)
  ListWidgets - GET /widgets (Unary)
  CreateWidget - POST /widgets (Unary)
  DeleteWidget - DELETE /widgets/{id} (Unary)
  UploadPhoto - PUT /widgets/{id}/photo (Unary)
messages:   10
enums:      2
```

The `rpc:` line gives the count; each following line is one operation, with the RPC name, the original HTTP
method and path, and the kind the mapping chose. Those four columns are the entire contract-to-RPC decision in
one place, which makes the command useful in review even when no file is written.

The package is the lower-cased service name because no package option was supplied. `--service-name` does not
change the package independently; the package follows the same derivation a build uses (chapter 8).

## A JSON run

```text
sharpportico generate docs/manual/fixtures/sink.yaml --format json --out build
```

writes one document to stdout and the descriptor to `build/sink.proto`:

```json
{
  "formatVersion": 1,
  "spec": "docs/manual/fixtures/sink.yaml",
  "service": {
    "name": "SinkService",
    "namespace": "SinkService",
    "package": "sinkservice"
  },
  "services": [
    {
      "name": "SinkService",
      "rpcs": [
        {
          "name": "ListWidgets",
          "httpMethod": "GET",
          "path": "/widgets",
          "kind": "Unary"
        },
        {
          "name": "CreateWidget",
          "httpMethod": "POST",
          "path": "/widgets",
          "kind": "Unary"
        },
        {
          "name": "DeleteWidget",
          "httpMethod": "DELETE",
          "path": "/widgets/{id}",
          "kind": "Unary"
        },
        {
          "name": "UploadPhoto",
          "httpMethod": "PUT",
          "path": "/widgets/{id}/photo",
          "kind": "Unary"
        }
      ]
    }
  ],
  "counts": {
    "messages": 10,
    "enums": 2
  },
  "diagnostics": [],
  "descriptor": {
    "path": "build/sink.proto"
  }
}
```

The writer emits members explicitly rather than serializing a type, so the document is stable and
reflection-free: two runs over the same input produce the same bytes, with two-space indentation, `\n` line
endings and one trailing newline. `formatVersion` is the document's version (`1`), independent of the package
version.

`diagnostics` carries the same id, severity, message and line a build would report. A refused contract has no
`service`, `services` or `descriptor`, but it still has diagnostics — that is what a gate reads.

## Exit codes and streams

| Code | Meaning |
| --- | --- |
| 0 | the contract mapped; the summary or JSON was written |
| 1 | the contract was refused, or the run threw; diagnostics explain why |
| 2 | a usage error: no command, unknown command, missing spec, unknown argument, bad `--format`, file not found |

The split between streams is deliberate:

- in **text** mode, the summary goes to stdout and diagnostics to stderr. Redirecting stdout to a file keeps
  the diagnostics visible on the console, which is what a developer wants;
- in **json** mode, the report goes to stdout as one document. A refused contract still produces that document,
  so a CI step can parse stdout without stripping warning lines; only an unexpected exception reports on stderr.

A warning does not change the exit code. A contract with `SP1003` or `SP2000` warnings maps and exits 0; the
warnings are in the report for a gate that chooses to fail on them. Only an error diagnostic, or a refusal with
no model, exits 1.

## What the command cannot do

- **It does not write C#.** The descriptor and the report are the outputs; message classes, the service and
  the proxy come from the source generator during a build.
- **It has no `validate` or `mapping` subcommand.** `generate` is the only command. A validation-only run is
  `generate` without `--out`: it maps, reports and writes nothing.
- **It takes one file.** There is no glob or directory mode; run it once per document in a loop.
- **It exposes no mapping options.** Pagination names, streaming hints, proxy switches and the rest are fixed at
  their build defaults, because the CLI builds the work item through the same `AdditionalFileRequest` path. A
  contract that depends on a non-default option cannot be reproduced exactly from the command line.
- **It does not read stdin.** The first argument is a path that must exist.

The last two limitations are the ones to remember when the CLI disagrees with a build. If a build sets
`DetectPagination=false` or `RespectStreamingHints=false`, the CLI still applies the default, and the RPC kinds
in its report will not match the generated code. The descriptor text itself is produced by the same emitter, so
for the default configuration the file and the `{Service}Proto.Text` constant are byte-for-byte equal.

## Using it in CI

A contract gate is three lines:

```bash
sharpportico generate openapi/petstore.yaml --format json > mapping.json
jq -e '.diagnostics | map(select(.severity == "error")) | length == 0' mapping.json
```

The second line fails the job on an error without depending on the exit code; the first already exits 1 on a
refusal. For a descriptor diff between two revisions, write the `.proto` with `--out` and compare the files:
the output is LF-normalized regardless of the host, so a Windows and a Linux job produce identical bytes for
the same input.

The next part turns to the diagnostics those jobs read: every `SP` id, what produces it, and what to do about
it.

