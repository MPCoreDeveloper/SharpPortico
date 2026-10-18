# 6. OpenAPI 3.0 and 3.1

<p class="lead">The bundled reader understands OpenAPI 3.0, not 3.1. SharpPortico therefore rewrites a 3.1
document's tree to its 3.0 equivalent before parsing it, maps every construct it can, and reports each
construct whose meaning changed. Nothing about a 3.0 document changes.</p>

## Why a rewrite rather than a second parser

`Microsoft.OpenApi` 1.6.x refuses a document whose `openapi` field is `3.1.0`. Supporting 3.1 could mean a
second parser for every mapping rule, or a translation step in front of the existing one. SharpPortico does
the second: `OpenApi31Downgrade` loads the YAML tree, walks it, and emits the same document with 3.1-only
constructs expressed in 3.0 form. The mapper then sees one dialect.

Two consequences follow, and both are visible to you.

- The rewrite is **structural**, not textual. It can tell a schema's `type` keyword from a property named
  `type`, and it handles YAML flow sequences, block sequences and JSON arrays identically.
- It is **reported**. `SP1002` says the document was parsed as 3.0 and lists the kinds of construct that were
  rewritten. `SP1003` names each construct whose mapping lost something.

A 3.0 document is returned byte for byte. A tree the rewrite cannot read falls back to rewriting only the
declared version, so an unusual 3.1 file still gets a chance to parse rather than being rejected outright.

## The fleet example

`samples/NativeAotExample/openapi/fleet-3.1.yaml` exists to exercise the rewrite. Its schema contains one of
almost every construct that 3.0 cannot spell:

```yaml
openapi: 3.1.0
jsonSchemaDialect: https://json-schema.org/draft/2020-12/schema
components:
  schemas:
    Vehicle:
      type: object
      properties:
        id: { type: integer, format: int64 }
        callsign: { type: [string, "null"] }
        kind: { const: truck }
        telemetry: { type: [object, array] }
        bounds:
          type: array
          prefixItems:
            - { type: string }
            - { type: integer }
        manifest: { type: string, contentEncoding: base64 }
        modelYear: { type: integer, exclusiveMinimum: 0 }
        serviceWindow: { $ref: "#/$defs/ServiceWindow" }
        labels: { type: object, additionalProperties: true }
$defs:
  ServiceWindow:
    type: object
    properties:
      label: { type: string }
webhooks:
  vehicleRetired:
    post:
      operationId: onVehicleRetired
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: "#/components/schemas/Vehicle" }
```

The generated descriptor begins with the message that exercises the rewrite:

```protobuf
syntax = "proto3";

package fleet31service;

import "google/protobuf/struct.proto";

option csharp_namespace = "Fleet31Service";

enum KindEnum
{
    TRUCK = 0;
}

message Vehicle
{
    int64 id = 1;
    optional string callsign = 2;
    KindEnum kind = 3;
    google.protobuf.Value telemetry = 4;
    repeated google.protobuf.Value bounds = 5;
    bytes manifest = 6;
    int32 model_year = 7;
    ServiceWindow service_window = 8;
    google.protobuf.Struct labels = 9;
}
```

Every line of that message is explained by a rule in this chapter or the next part of the manual.

## Construct by construct

| 3.1 construct | Rewritten to | Protobuf result | Loss reported |
| --- | --- | --- | --- |
| `openapi: 3.1.0` | `3.0.3` | — | `SP1002` (the notice) |
| `jsonSchemaDialect` | removed | — | `SP1003`: the dialect's semantics are not honoured |
| `type: [T, "null"]` | `type: T`, `nullable: true` | `optional T` on a singular scalar | none |
| `type: [A, B, …]` (several non-null) | `x-sharpportico-json-value: true` | `google.protobuf.Value` | `SP1003`: no protobuf union |
| `type: ["null"]` only | the same marker | `google.protobuf.Value` | `SP1003`: a value that is always null |
| `const: X` | `enum: [X]` | a single-member protobuf enum | none |
| `prefixItems` with one shared scalar type | `items: {type, format}` | `repeated` that scalar | `SP1003`: fixed length and positions are gone |
| `prefixItems` with differing types | `items:` the JSON-value marker | `repeated google.protobuf.Value` | `SP1003`: same |
| array with `items` and no `type` | `type: array` | `repeated` | none |
| `contentEncoding: base64` | `format: byte` | `bytes` | none |
| any other `contentEncoding` | removed | the underlying `string` | `SP1003`: encoding not applied |
| numeric `exclusiveMinimum: 0` | `minimum: 0`, `exclusiveMinimum: true` | the field's numeric type | none (bounds are documentation) |
| `$ref: "#/$defs/X"` | hoisted to `components/schemas/X` and repointed | a message named `X` | none |
| `webhooks` | merged under `/webhooks/{name}` | RPCs like any other operation | `SP1003`: a webhook is a call the API makes to *you* |

## What "mapped with a loss" means

A loss is not a refusal. The contract still generates, and the diagnostic says what the protobuf contract
cannot express. For the fleet document, the build reports something like:

```text
warning SP1002: SharpPortico parsed 'openapi/fleet-3.1.yaml' as OpenAPI 3.0: ... it was rewritten to
the 3.0 equivalent first - the declared version, $defs references, webhooks, a type array, const,
prefixItems, contentEncoding, a numeric exclusive bound
warning SP1003: SharpPortico mapped the tuple at components/schemas/Vehicle/properties/bounds in
'openapi/fleet-3.1.yaml', but a JSON Schema tuple's fixed length and per-position types have no
protobuf equivalent, so it maps to a repeated field
warning SP1003: SharpPortico mapped webhooks in 'openapi/fleet-3.1.yaml', but 1 webhook operation(s)
were mapped to RPCs; a webhook is a call the API makes to you, so a client method for one describes
a payload and not a call the server accepts
```

The full text is in chapter 25. What matters here is the shape: one informational notice for the rewrite,
then one warning per construct that did not survive intact. A build that treats warnings as errors will fail
on a 3.1 document containing a tuple or a webhook — which is the intended way to make a team decide whether
that loss is acceptable.

## What is not rewritten

The rewrite handles constructs it knows. Keywords it does not recognise are left in the tree and then either
read by the 3.0 reader or ignored. In particular:

- `unevaluatedProperties` and `unevaluatedItems` are not mapped to anything; `additionalProperties: true`
  still produces `google.protobuf.Struct`, as it does in 3.0.
- JSON Schema assertions (`pattern`, `minLength`, `maximum`, `contains`, `dependentRequired`) do not change a
  type and are not emitted as protobuf validation.
- `examples` and `example` are documentation; they do not appear in the descriptor.
- A `webhooks` entry whose generated path already exists under `paths` is left out, with an `SP1003` naming
  the collision.

If a 3.1 construct matters to your consumers, the test is simple: read the generated `.proto`. If the type
and the field are what you meant, the loss was cosmetic; if not, the `SP1003` warning is the explanation and
the contract should be changed to say what protobuf can carry.

Part III now turns to the mapping itself, beginning with the simplest question: what does a scalar become?

