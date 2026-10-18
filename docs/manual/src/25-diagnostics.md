# 25. Diagnostics

<p class="lead">SharpPortico reports what it could not do with `SP` ids, through the compiler and through the
CLI, with the same text in both. The numbering is by stage: `SP1xxx` while reading the document, `SP2xxx` while
mapping it, `SP3xxx` while emitting code.</p>

## Two channels, one message

| Channel | Where it appears |
| --- | --- |
| source generator | a warning or error on the `AdditionalFiles` item, in the build log and the IDE error list |
| CLI | stderr in `--format text`, the `diagnostics` array in `--format json` |

Both carry the same id, severity and message because both call the same `DiagnosticDescriptor`. A suppression in
`.editorconfig` therefore works for a build, and a saved JSON report can be compared with a later one without
normalising the text.

## Severity means one thing

| Severity | Meaning | What was emitted |
| --- | --- | --- |
| `error` | the contract cannot become code that compiles | nothing for that specification |
| `warning` | the contract mapped; something did not survive | the code, with the loss named |

An error is a refusal, not a partial result. Emitting the valid half would give a consumer a service missing an
operation, or a message shadowing another, and the build would be green while the contract was wrong.

`SP1000` is a warning by severity even though it produces no code: the document could not be read, so there is
nothing to map. A build reports the warning and compiles no generated types. When the CLI can open the path but
the document does not map, it exits 1 because there is no model; a path that does not exist at all is a usage
error and exits 2 (chapter 24).

## The ids

| Id | Severity | Raised when |
| --- | --- | --- |
| `SP1000` | warning | the file is missing, empty, or not valid YAML/JSON; the reader's own complaint is appended |
| `SP1002` | warning | a 3.1 document was rewritten to 3.0 before parsing |
| `SP1003` | warning | one 3.1 construct was mapped with a loss |
| `SP2000` | warning | a nullable array or nullable element could not keep its nullability |
| `SP2005` | **error** | two messages would be emitted with one name |
| `SP2006` | **error** | a property's member name equals its enclosing type's name |
| `SP2007` | **error** | two inline enumerations would share a name with different members |
| `SP2008` | **error** | a nullable property's `Has`/`Clear` member collides with another member |

Six more ids are declared and never raised by 1.5.0: `SP1001`, `SP2001`, `SP2002`, `SP2003`, `SP2004` and
`SP3000`. They are described at the end of this chapter, because a published id keeps its meaning even when a
version has no path to it.

## Parse: SP1000

```text
warning SP1000: SharpPortico could not parse OpenAPI file 'openapi/pets.yaml': ...
```

The path is resolved against the project directory for a build, and used exactly as given by the CLI. The suffix
is the OpenAPI reader's own message, which is why it is not paraphrased: it names the line and the token that
failed. Common causes are a YAML tab where indentation is required, an unquoted `:` in a description, a
`$ref` that points at a missing file, or a JSON document with a trailing comma.

`SP1000` is also what a document that is valid YAML but not an OpenAPI 3.x document produces, because the
bundled reader refuses it before the generator sees a version (see `SP1001` below).

## Parse: SP1002 and SP1003

`SP1002` is informational. It appears once for every 3.1 document and says how many constructs were rewritten:

```text
warning SP1002: SharpPortico parsed 'openapi/fleet-3.1.yaml' as OpenAPI 3.0: the bundled parser
(Microsoft.OpenApi 1.6.x) does not read a 3.1 document, so it was rewritten to the 3.0 equivalent
first - the declared version, $defs references, webhooks, a type array, const, prefixItems,
contentEncoding, a numeric exclusive bound
```

`SP1003` appears once per construct that the rewrite mapped without carrying everything:

```text
warning SP1003: SharpPortico mapped the tuple at components/schemas/Vehicle/properties/bounds in
'openapi/fleet-3.1.yaml', but a JSON Schema tuple's fixed length and per-position types have no
protobuf equivalent, so it maps to a repeated field
```

The second argument is the location inside the document, not a line number: a 3.1 document is rewritten as a
tree, so the source line of a rewritten node is not reliably known and the generator does not guess. Chapter 6
lists which constructs produce which kind of loss; chapter 17 says what each loss costs.

## Mapping warnings: SP2000

`SP2000` is the only warning the mapper raises about a construct it still mapped:

```text
warning SP2000: SharpPortico could not map 'nullable' (tags): a repeated field has no per-element
presence, so an element that was null reads back as the element's default value
```

The three arguments are the construct (`nullable`), the location (`Message/field`, or `field[]` for the
element), and what was lost. Chapter 12 explains why a repeated field cannot carry presence, and chapter 10
gives the workaround: a wrapper message with a presence field per element.

This warning is gated by `EmitDiagnosticsForUnmappableConstructs` (default `true`). Turning it off removes the
warning and changes nothing else; the mapping is the same. It is the one diagnostic a consumer may legitimately
silence, which is why it has a switch and the errors do not.

## Mapping errors: SP2005–SP2008

An error means the generated file would not compile, so the generator refuses the whole document. Each message
names the two things that collide and asks for a rename; none of them attempts a rename of its own, because a
generated name that differs from the contract is harder to trace than a build failure.

### SP2005 — two messages, one name

```text
error SP2005: SharpPortico cannot map 'openapi/pets.yaml': the message name 'GetPetRequest' is
produced twice - an operation derives its messages as '{OperationId}Request' and
'{OperationId}Response', so a component schema of that name collides. Rename one of the two
```

An operation `getPet` derives `GetPetRequest` and `GetPetResponse`. A component schema named `GetPetRequest`
produces the same message name. Rename the schema (or the operation id). The same collision can happen between
two component names that sanitise to the same PascalCase string, for example `pet-details` and `PetDetails`
(chapter 8).

### SP2006 — a member named like its type

```text
error SP2006: SharpPortico cannot map 'openapi/things.yaml': property 'thing' of schema 'Thing'
would generate a member with the same name as its enclosing type, which C# forbids (CS0542).
Rename the property or the schema
```

C# forbids a member with its enclosing type's name. JSON Schema allows it, and a contract written without C# in
mind often contains it. Renaming either side is the only fix; the generator will not suffix the property.

### SP2007 — two enumerations, one name

```text
error SP2007: SharpPortico cannot map 'openapi/things.yaml': the inline enumeration named
'StatusEnum' is declared twice with different members ('available' and 'pending'). An inline
enumeration is named after the property declaring it, so two schemas with a property of that name
collide - declare one of them as a named schema under components
```

An inline enum is named `<Property>Enum`, so two schemas with a `status` property and different members both want
`StatusEnum`. The mapper first derives a distinct name from the original plus the declared members
(`StatusEnum` + `SoldReturned`); if that name is free, both survive and no error is raised. `SP2007` appears only
when the derived name is **also** taken by a different member set, at which point no name is left that the
contract actually implies. Promote one to `components.schemas` to choose its name yourself. If the two member
sets are identical, the mapper reuses one enum and no error is raised.

### SP2008 — presence collides with a property

```text
error SP2008: SharpPortico cannot map 'openapi/things.yaml': property 'more' of schema 'Thing' is
nullable, so its message needs presence members named 'HasMore' and 'ClearMore', but 'HasMore' is
already a member of 'Thing'. Rename one of the two
```

A nullable `more` becomes `optional` and gains `HasMore`/`ClearMore`. A sibling property `hasMore` sanitises to
`HasMore`, and the two cannot coexist. Rename the nullable property or the property that spells its presence
member.

## Ids that 1.5.0 does not raise

Six declared ids have no code path:

| Id | Declared as | Why nothing reaches it |
| --- | --- | --- |
| `SP1001` | document is not OpenAPI 3.x | the reader refuses it first, and its complaint is reported as `SP1000` |
| `SP2001` | unsupported HTTP method | every verb on a path item becomes an RPC, so no operation is dropped for its method |
| `SP2002` | unsupported schema composition | accepted compositions are resolved; 3.1 rewrites report `SP1003` |
| `SP2003` | circular reference | a self-referencing message is legal protobuf, so there is no cycle to report |
| `SP2004` | unsupported auth scheme | an unmodelled scheme still gets a metadata helper keyed by its name |
| `SP3000` | emit failure | emission is not wrapped in a handler, so a failure surfaces as `AD0001` |

They stay in the registry because an id that has appeared in a build log, a saved report or a suppression must
keep meaning the same thing. A manual that reused the numbers for new conditions would make old logs lie.

## Gating a build

Errors already fail a build. A warning is a warning until the project says otherwise, and MSBuild promotes by
id:

```xml
<PropertyGroup>
  <!-- This repository's contracts are not allowed to lose nullability quietly. -->
  <WarningsAsErrors>$(WarningsAsErrors);SP2000</WarningsAsErrors>
</PropertyGroup>
```

For a compiler-free gate, the CLI is enough: a refusal exits 1, and the JSON report carries the severity of
every diagnostic for a `jq` filter (chapter 24). Warning ids can be promoted the same way in CI by filtering the
report; suppressing an id in `.editorconfig` works for the build channel only, because the CLI does not read
editor configuration.

