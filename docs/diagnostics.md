# Diagnostics reference

Every diagnostic SharpPortico reports has an `SP` id, a category of `SharpPortico`, and one of two severities. The
numbering is by stage rather than by age: **1xxx** is reading the document, **2xxx** is mapping it, **3xxx** is
emitting code.

They arrive through two channels, and both carry the same ids and the same text:

- **The compiler**, from the source generator, on the source that declared the specification — a warning or error in the
  build log and in the error list.
- **The CLI**, from `sharpportico generate` — printed after the summary in `--format text`, and as `id`, `severity` and
  `message` in `--format json`. The CLI exits `1` when any diagnostic is an error, so a pipeline can branch on the exit
  code and then read the reason from the document.

## Severity means one thing

| Severity | Meaning | What was generated |
| --- | --- | --- |
| `error` | the contract cannot be mapped into code that compiles | **nothing** for that specification — no messages, no service, no descriptor |
| `warning` | the contract mapped, and something did not survive the mapping intact | the code, with the loss named |

An error is a refusal, not a partial result: emitting the part that happens to be valid would hand a consumer a service
that is missing an operation or a message that shadows another.

## One switch

`EmitDiagnosticsForUnmappableConstructs` (default **`true`**, settable on `[OpenApiToGrpc]`) controls the **loss**
diagnostics — the ones where the mapping succeeded and something was left behind. Today that is `SP2000`. Turning it off
silences those warnings and changes nothing else; refusals (`SP2005`–`SP2008`) are never silenced, because a refusal that
is not reported is a build that fails with no explanation.

## The ids

| Id | Severity | Raised when | Remedy |
| --- | --- | --- | --- |
| `SP1000` | warning | the specification could not be read: missing, empty, or not valid YAML/JSON | fix the path, or the document's syntax |
| `SP1002` | warning | an OpenAPI **3.1** document was parsed through the 3.0 rewrite | nothing — it is the notice that the rewrite happened; `SP1003` names each construct |
| `SP1003` | warning | a 3.1-only construct was mapped to its protobuf equivalent and something was not carried over | accept the mapping, or change the contract if the loss matters |
| `SP2000` | warning | a construct could not be mapped faithfully: today, a nullable array or a nullable element | nothing, unless the nullability matters; see the message for what was lost |
| `SP2005` | **error** | two different messages would be emitted with one name — typically a component schema named like an operation's derived `{OperationId}Request`/`Response` | rename the schema or the operation id |
| `SP2006` | **error** | a property's generated member name would equal its enclosing type's name (C# `CS0542`) | rename the property or the schema |
| `SP2007` | **error** | two inline enumerations would be emitted with one name but different members | declare one of them as a named schema under `components` |
| `SP2008` | **error** | a nullable property needs a `Has…`/`Clear…` member that the schema already declares | rename one of the two properties |

`SP2001`–`SP2004` and `SP3000` are in the generator's registry and **are not raised by 1.5.0**; §5 says why and what a
reader should do with them.

---

## 1. Parse (`SP10xx`)

### `SP1000` — the document could not be read

```text
warning SP1000: SharpPortico could not parse OpenAPI file 'openapi/pets.yaml': ...
```

The path is resolved against the project directory, and the content is read from the `AdditionalFiles` item of that
name. A spec that exists but is declared nowhere produces no generation at all and no diagnostic — there is no work item
to fail — so a silent build usually means a missing `<AdditionalFiles Include="…" />`.

### `SP1002` — a 3.1 document was read as 3.0

```text
warning SP1002: SharpPortico parsed 'openapi/fleet-3.1.yaml' as OpenAPI 3.0: the bundled parser
(Microsoft.OpenApi 1.6.x) does not read a 3.1 document, so it was rewritten to the 3.0 equivalent
first - the declared version, $defs references, webhooks, a type array, const, prefixItems,
contentEncoding, a numeric exclusive bound
```

The bundled reader has no 3.1 support, so a 3.1 document is rewritten to its 3.0 equivalent before parsing. This
diagnostic is the notice that it happened, and its tail lists the kinds of construct the rewrite touched; it is not an
error, and it does not mean anything was lost.

### `SP1003` — a 3.1 construct was mapped with a loss

```text
warning SP1003: SharpPortico mapped the tuple at components/schemas/Vehicle/properties/bounds in
'openapi/fleet-3.1.yaml', but a JSON Schema tuple's fixed length and per-position types have no
protobuf equivalent, so it maps to a repeated field
```

One diagnostic per construct where something did not survive the mapping — a tuple's fixed positions, a multi-type
union becoming `google.protobuf.Value`, a webhook becoming an RPC, a `jsonSchemaDialect` whose keywords are not read. The
contract still keeps its types and operations; this names what the mapping does not carry over, so a reader can decide
whether the contract needs to change.

---

## 2. Mapping, mapped with a loss (`SP2000`)

```text
warning SP2000: SharpPortico could not map 'nullable' (tags): a repeated field cannot be null, so an
array that was never set and an array that was set to nothing are the same field
warning SP2000: SharpPortico could not map 'nullable' (tags[]): a repeated field has no per-element
presence, so an element that was null reads back as the element's default value
```

The construct that could not be mapped faithfully — today, nullability that a repeated field cannot carry (see
[presence §6](presence.md#6-where-presence-is-not-available)). The field is still emitted; the warning exists so the loss
is not discovered later by a consumer wondering why `null` and `[]` are the same answer.

Silence it with `EmitDiagnosticsForUnmappableConstructs = false` when the contract is known and the loss is acceptable.

---

## 3. Mapping, refused (`SP2005`–`SP2008`)

A refusal emits nothing for that specification and stops the build with an error, because each of these describes a file
that could not compile.

### `SP2005` — a message name produced twice

```text
error SP2005: SharpPortico cannot map 'openapi/search.yaml': the message name 'SearchRequest' is
produced twice - an operation derives its messages as '{OperationId}Request' and
'{OperationId}Response', so a component schema of that name collides. Rename one of the two
```

An operation `search` derives `SearchRequest`/`SearchResponse`, and a component schema may already be called
`SearchRequest`. Both would land in one file with one name. Rename the schema or the operation id.

### `SP2006` — a property name equals its schema name

```text
error SP2006: SharpPortico cannot map 'openapi/pets.yaml': property 'pet' of schema 'Pet' would
generate a member with the same name as its enclosing type, which C# forbids (CS0542). Rename the
property or the schema
```

C# does not allow a member to share its name with the type that contains it, so a `Pet` schema with a `pet` property has
no legal translation. Rename one of them.

### `SP2007` — an enumeration name produced twice

```text
error SP2007: SharpPortico cannot map 'openapi/orders.yaml': the inline enumeration named 'StatusEnum'
is declared twice with different members ('available' and 'pending'). An inline enumeration is named
after the property declaring it, so two schemas with a property of that name collide - declare one of
them as a named schema under components
```

An inline `enum` is named `<Property>Enum`, so two schemas with a `status` property and different member sets collide.
Declaring one of the two enumerations as a named schema under `components` gives it a name of its own — and keeps both
member sets.

### `SP2008` — a presence member collides with a property

```text
error SP2008: SharpPortico cannot map 'openapi/things.yaml': property 'more' of schema 'Thing' is
nullable, so its message needs presence members named 'HasMore' and 'ClearMore', but 'HasMore' is
already a member of 'Thing'. Rename one of the two
```

A nullable scalar generates `HasX` and `ClearX` (see [presence](presence.md)), and a contract that also declares a
property of that name would generate those members twice. Rename the nullable property or the property that spells its
presence member.

---

## 4. Emit (`SP30xx`)

`SP3000` exists for a failure while writing generated code. The generator does not wrap emission in a handler today, so a
failure surfaces as a Roslyn analyzer fault (`AD0001`) from the compiler rather than as this id — see §5.

---

## 5. Ids that 1.5.0 does not raise

Six ids are declared in the generator's registry and no code path produces them. They are kept because an id, once
published, has to keep meaning one thing: a build log from an older version, a report a consumer has saved, or a
suppression in a `.editorconfig` all refer to these numbers.

| Id | Declared as | Why nothing reaches it |
| --- | --- | --- |
| `SP1001` | "Document is not OpenAPI 3.x" | the bundled reader refuses such a document itself, and its own complaint is reported as `SP1000` |
| `SP2001` | "Unsupported HTTP method" | every verb a path item carries becomes an RPC, so no operation is dropped for its method |
| `SP2002` | "Unsupported schema composition" | the compositions the parser accepts are resolved (`allOf` merged, a union widened to `google.protobuf.Value`), and anything a 3.1 rewrite changed is reported as `SP1003` |
| `SP2003` | "Circular reference detected" | a self-referencing schema is legal protobuf — a message may hold a field of its own type — so there is no cycle to report |
| `SP2004` | "Unsupported authentication scheme" | a scheme the mapper does not model is still given a metadata helper and an interceptor entry keyed by its own name, so nothing is dropped and there is no loss to name |
| `SP3000` | "SharpPortico emit failure" | emission is not wrapped in a handler, so a failure surfaces as an analyzer fault from the compiler |

---

## 6. Gating a build on diagnostics

The errors (`SP2005`–`SP2008`) already fail a build. A warning is a warning until a project says otherwise, and MSBuild
takes diagnostic ids, so a project can promote the ones it refuses to ship:

```xml
<PropertyGroup>
  <!-- This repository's contracts are not allowed to lose nullability quietly. -->
  <WarningsAsErrors>$(WarningsAsErrors);SP2000</WarningsAsErrors>
</PropertyGroup>
```

The CLI is the other gate, and it is the one that works without a compiler: the exit code is `1` when any diagnostic is
an error, and the document carries the rest.

```bash
sharpportico generate openapi/petstore.yaml --format json > report.json

# Fail unless nothing at all was reported, warning or error.
jq -e '.diagnostics | length == 0' report.json
```


