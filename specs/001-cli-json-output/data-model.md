# Phase 1 Data Model: Machine-Readable Report

The document is a projection of two values the CLI already holds: the `GrpcModel` returned by `OpenApiParser.ParseAndMap` and the `GeneratorDiagnostic` list that accompanies it. Nothing is derived twice; every field below has a single source in existing code.

## Entities

### MappingReport (root; exactly one per run)

| Field | Type | Presence | Source | Requirement |
|---|---|---|---|---|
| `formatVersion` | integer | always | constant `1` | FR-004 |
| `spec` | string | always | the `file` argument, exactly as typed | FR-005, FR-011 |
| `service` | ServiceIdentity | only when the mapping produced a model; never invented on refusal | `GrpcModel.ServiceName`, `GrpcModel.Namespace`, `GrpcModel.ProtoPackage` | FR-005 |
| `services` | array of ServiceEntry | always, may be empty | `GrpcModel.Services` | FR-006 |
| `counts` | Counts | always | `GrpcModel.Messages.Length`, `GrpcModel.Enums.Length` | FR-007 |
| `diagnostics` | array of DiagnosticEntry | always, may be empty | `ParseResult.Diagnostics` | FR-008, FR-009 |
| `descriptor` | Descriptor | only after a successful write | the path the text mode prints today | FR-012 |

### ServiceIdentity

`name`, `namespace`, `package` — three strings. `name` is the model's service name (the text mode's `service:` line), not the first entry of `services`; a spec may declare more than one service entry, and the identity is the model's.

### ServiceEntry

| Field | Type | Presence | Source |
|---|---|---|---|
| `name` | string | always | `service.Name` |
| `rpcs` | array of RpcEntry | always, may be empty | `service.RpcMethods` |

### RpcEntry

| Field | Type | Presence | Source |
|---|---|---|---|
| `name` | string | always | `rpc.Name` |
| `httpMethod` | string | always | `rpc.HttpMethod` |
| `path` | string | always | `rpc.OriginalPath` |
| `kind` | string | always | `rpc.Kind` (`Unary`, `ServerStreaming`, `ClientStreaming`, `BidiStreaming`) |

### Counts

`messages`, `enums` — non-negative integers, present even when zero.

### DiagnosticEntry

| Field | Type | Presence | Source |
|---|---|---|---|
| `id` | string | always | `descriptor.Id` (`SP1000`–`SP2007`) |
| `severity` | string enum | always | `descriptor.DefaultSeverity` mapped through the existing `Severity(...)` helper: `error`, `warning`, `info` |
| `message` | string | always | `descriptor.MessageFormat` formatted with `diagnostic.Arguments`, `CultureInfo.InvariantCulture` — the same string the text mode prints |
| `line` | integer, 1-based | only when `Location.SourceTree` and a line span exist | `Location.GetLineSpan().StartLinePosition.Line + 1` |

### Descriptor

`path` — the path the descriptor was written to: `--out` itself when it ends in `.proto`, otherwise the joined `{HintName}.proto`, exactly as the text mode prints it.

## Invariants

- **I1** — One run produces at most one document, and in JSON mode standard output contains nothing else (FR-002).
- **I2** — Every array and every count is present even when empty or zero; absence never encodes "none" (spec edge case: a spec with no operations).
- **I3** — `severity` is derived, never re-assessed: an `SP2005` stays `error`, an `SP1002` stays `warning`. The mode neither promotes nor hides (constitution IV).
- **I4** — `line` is 1-based and appears only when the generator supplied a source location; it is never guessed. The current pipeline reports its diagnostics without a source tree, so real documents carry no `line`; the projection is still tested with a located diagnostic so the field cannot rot.
- **I5** — `message` is the same text the compiler and the text mode report for the same spec (SC-003).
- **I6** — Ordering is the model's order: services as mapped, RPCs as declared, diagnostics in report order. No sorting, no dictionary enumeration (FR-011).
- **I7** — Each diagnostic appears exactly once; the diagnostics are not re-emitted to stderr in this mode (FR-003).
- **I8** — `formatVersion` tracks the document shape only; it is not the package version (R9).
- **I9** — `descriptor` is present only after the file was written successfully (R8).
- **I10** — The bytes are UTF-8 without a BOM, use `\n` line endings inside the document, and end with a single trailing LF (FR-014, SC-002).

## State Transitions

| State | Trigger | Output | Exit code |
|---|---|---|---|
| Usage error | unknown command, unknown option, missing value, unknown `--format` value, missing spec file | usage/problem on stderr, no document | 2 |
| Refused | `ParseResult.IsSuccess` is false | document with diagnostics, empty `services`, zero `counts`, no identity, no descriptor | 1 |
| Mapped, text | default format | text summary on stdout, diagnostics on stderr, optional descriptor | 0 |
| Mapped, JSON | `--format json` | one document on stdout, descriptor written first when requested | 0 |
| I/O failure | reading the spec or writing the descriptor throws | message on stderr, no document | 1 |

## Validation Rules (from the specification)

| Requirement | Rule in the model |
|---|---|
| FR-002 | Serializer writes one root object; nothing else is written to `stdout` in this path |
| FR-005 | `spec`, `service.name`, `service.namespace`, `service.package` are non-null by construction |
| FR-006 | Every `ServiceEntry` carries its `RpcEntry` list, including an empty one |
| FR-008 | `DiagnosticEntry` has four fields, one of them optional by rule I4 |
| FR-012 | `descriptor.path` equals the string the text mode prints |
| FR-015 | The model is read, never mutated; `GrpcModel` and the diagnostics are the same instances the text mode uses |

## Key Entities in the Specification

The spec's entities map one-to-one: *Mapping report* → `MappingReport`, *RPC entry* → `RpcEntry`,
*Diagnostic entry* → `DiagnosticEntry`, *Format version* → `formatVersion`. No entity in the spec is
left without a home, and no field is introduced that the spec does not ask for.