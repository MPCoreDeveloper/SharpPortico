# Contract: `sharpportico generate`

This is the interface a consumer programs against: the command line, the streams, the exit codes and
the document. It is the artifact `/speckit-analyze` and reviewers check the implementation against.

## Command surface

```text
usage: sharpportico generate <openapi.yaml|json> [--out DIR|FILE] [--service-name NAME]
                             [--namespace NS] [--format text|json]
```

| Option | Value | Default | Meaning |
|---|---|---|---|
| `--out` | directory or `.proto` file | none | write the descriptor the generator would embed |
| `--service-name` | name | spec file stem | base name of the generated service |
| `--namespace` | namespace | service name | namespace of the generated C# |
| `--format` | `text` \| `json` | `text` | `text` keeps today's output; `json` emits one document on stdout |

`--format` is compared case-insensitively. Any other value is a usage error. The existing options and
their meanings are unchanged, and option order is irrelevant.

## Streams

| Situation | stdout | stderr | Exit |
|---|---|---|---|
| `text`, mapped | summary lines, then `proto:` when `--out` was used | diagnostic lines (warnings too) | 0 |
| `text`, refused | nothing | diagnostics, then `'<file>' was not mapped; nothing was generated.` | 1 |
| `json`, mapped | exactly one document | nothing | 0 |
| `json`, mapped with warnings | exactly one document containing them | nothing | 0 |
| `json`, refused | exactly one document containing the refusal | nothing | 1 |
| usage error (any mode) | nothing | problem and usage line | 2 |
| I/O failure (any mode) | nothing (never a partial document) | `error: <message>` | 1 |

## Exit codes

| Code | Meaning |
|---|---|
| 0 | the contract mapped (warnings allowed) |
| 1 | the contract was refused, or an I/O failure occurred |
| 2 | the command line was wrong: unknown command, unknown option, missing value, unknown `--format` value, missing spec file |

## Document contract (`--format json`)

A single JSON object, UTF-8, no byte-order mark, two-space indentation, `\n` line endings, one
trailing LF. Consumers MUST ignore fields they do not know; that is what makes an additive release
compatible.

| Field | Type | Required | Notes |
|---|---|---|---|
| `formatVersion` | integer | yes | `1` today; bumped only for a consumer-breaking change |
| `spec` | string | yes | the spec path exactly as given on the command line |
| `service.name` | string | when mapped | the model's service name |
| `service.namespace` | string | when mapped | namespace of the generated C# |
| `service.package` | string | when mapped | proto package |
| `services` | array | yes | one entry per mapped service; may be empty |
| `services[].name` | string | yes | service name for this entry |
| `services[].rpcs` | array | yes | may be empty |
| `services[].rpcs[].name` | string | yes | RPC name |
| `services[].rpcs[].httpMethod` | string | yes | as the spec declares it |
| `services[].rpcs[].path` | string | yes | the original OpenAPI path |
| `services[].rpcs[].kind` | string | yes | `Unary`, `ServerStreaming`, `ClientStreaming` or `BidiStreaming` |
| `counts.messages` | integer | yes | `>= 0` |
| `counts.enums` | integer | yes | `>= 0` |
| `diagnostics` | array | yes | may be empty; in report order |
| `diagnostics[].id` | string | yes | `SP` identifier |
| `diagnostics[].severity` | string | yes | `error`, `warning` or `info` |
| `diagnostics[].message` | string | yes | the same message the compiler reports |
| `diagnostics[].line` | integer | no | 1-based; present only when the generator has a source location |
| `descriptor.path` | string | no | present only after the descriptor was written |

`service` is present only when the contract mapped: a refused contract never produced a model, so its document
carries `spec`, an empty `services`, zero `counts` and the refusal diagnostics, and no identity is invented from
the command line (that would be a second copy of the defaults a build applies). Today's diagnostics are reported
without a source tree, so `line` is absent from real documents; the field exists for the diagnostics that carry
one and is tested with one.

Ordering is the model's order — services as mapped, RPCs as declared, diagnostics in report order. No
timestamps, machine names or derived paths appear anywhere in the document.

### Example: mapped with a warning

```json
{
  "formatVersion": 1,
  "spec": "samples/NativeAotExample/openapi/fleet-3.1.yaml",
  "service": {
    "name": "FleetService",
    "namespace": "Fleet.Generated",
    "package": "fleet"
  },
  "services": [
    {
      "name": "FleetService",
      "rpcs": [
        { "name": "ListVehicles", "httpMethod": "GET", "path": "/vehicles", "kind": "Unary" }
      ]
    }
  ],
  "counts": { "messages": 14, "enums": 2 },
  "diagnostics": [
    {
      "id": "SP1002",
      "severity": "warning",
      "message": "SharpPortico parsed 'fleet-3.1.yaml' as OpenAPI 3.0: ..."
    }
  ]
}
```

### Example: refused

```json
{
  "formatVersion": 1,
  "spec": "openapi/colliding.yaml",
  "services": [],
  "counts": { "messages": 0, "enums": 0 },
  "diagnostics": [
    {
      "id": "SP2005",
      "severity": "error",
      "message": "SharpPortico cannot map 'X': the message name 'Y' is produced twice - ..."
    }
  ]
}
```

A consumer gates on `diagnostics[].severity == "error"` (or on the exit code) and can print
`diagnostics[].id` plus `diagnostics[].message` without ever seeing a human-formatted line.

The examples show the document's shape; the exact whitespace is the writer's two-space indentation, and
`tests/SharpPortico.Tests/openapi/petstore.report.json` pins the bytes.

## Compatibility rules

- The default format's output is a compatibility surface: block headings, labels, order and stream
  stay as they are. `tests/SharpPortico.Tests/openapi/petstore.report.txt` pins it.
- Adding an optional field to the document is compatible and does not bump `formatVersion`.
- Removing a field, renaming one, changing its type or changing its meaning bumps `formatVersion`.
- `formatVersion` is not the package version; a release that does not touch the document does not
  move it.

## Non-goals

- No colour, no progress reporting and no logging in the document.
- No schema file is published in this iteration; the table above and the examples are the contract.
- The descriptor's own content is out of scope: it is written by `ProtoEmitter` exactly as today.