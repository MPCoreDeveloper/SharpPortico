# 8. Names

<p class="lead">Names are where a mapping becomes visible in every line of consumer code. SharpPortico
derives a C# identifier and a protobuf identifier from each OpenAPI name, and the two are not the same
string. This chapter states both rules and the collisions they can produce.</p>

## Two names for one field

Every field carries a C# name and a proto name.

| OpenAPI property | C# member | `.proto` field | Why |
| --- | --- | --- | --- |
| `petId` | `PetId` | `pet_id` | C# convention; proto snake_case |
| `x-rate-limit` | `XRateLimit` | `x_rate_limit` | separators are removed in C#, kept as `_` in proto |
| `URL` | `URL` | `u_r_l` | see the acronym rule below |
| `snake_case` | `SnakeCase` | `snake_case` | |
| `2fa` | `N2fa` | `_2fa` | identifiers may not begin with a digit |
| `status.code` | `StatusCode` | `status_code` | `.` is a separator |

The C# rule (`SanitizePascal`) keeps letters and digits, drops every other character, and upper-cases the
character that follows a separator. It does not lower-case the rest, so an acronym survives: `URL` stays
`URL`.

The proto rule (`ToProtoName`) lower-cases, turns `_`, `-`, `.`, space and `:` into `_`, and inserts an
underscore before an upper-case letter that follows a lower-case one. That last clause is why `petId` becomes
`pet_id`; it is also why an acronym is spelled out:

```text
URL     →  u _ r _ l
```

This is not an accident of the implementation; it is the predictable consequence of applying the same
camel-case splitter to every input. If a contract needs `url`, write `url`.

## Operations

An RPC is named from `operationId`, passed through `SanitizePascal`:

| `operationId` | RPC |
| --- | --- |
| `listPets` | `ListPets` |
| `get-pet-by-id` | `GetPetById` |
| `Pets_Create` | `PetsCreate` |
| absent | `Op0POST` (operation index and verb) |

The fallback counts operations in document order, so a document without `operationId` still gets stable,
unique names — but the names say nothing about intent. Always declare `operationId` in a contract you
intend to keep.

## Messages

The wrappers around an operation are named after the RPC:

| Message | Content |
| --- | --- |
| `{Method}Request` | parameters and body of the operation |
| `{Method}Response` | the successful response's fields |
| `{Method}Body` | an inline request body that is not a `$ref` |
| `{Method}Data` | an inline response object that is not a `$ref` |
| `{Method}Error` | the error wrapper when the operation declares no success response |
| `{Method}Item` | the element message of a top-level array response with inline items |

Component schemas keep their component names: `components/schemas/Pet` becomes `message Pet`, with the name
passed through `SanitizePascal` (`pet_summary` → `PetSummary`).

Inline objects are named after the property that declares them: a property `dimensions` with an inline object
would be `Dimensions`, and a repeated property `children` would be `Children`. In 1.5.0 those nested messages
are named but not emitted, so the field refers to a type the file does not contain; declare the object as a
named component and use `$ref` (chapter 17). A property that already uses `$ref` is unaffected.

## Enumerations

| Where the enum appears | Generated name |
| --- | --- |
| `components/schemas/WidgetKind` with `enum` | `WidgetKind` |
| inline `enum` on property `state` | `StateEnum` |
| inline `enum` on repeated property `tags` | `TagsEnum` |
| 3.1 `const: truck` on property `kind` | `KindEnum` |
| inline `enum` on top-level array response items | `{Method}ItemsEnum` |

Members are `SanitizePascal` of the declared value. String members become PascalCase (`available` →
`Available`); numeric members become `N` plus the number (`1` → `N1`, `10` → `N10`), and the number is kept
as the protobuf value. In the `.proto`, `ToProtoEnumName` inserts an underscore before every upper-case
letter except the first, so `InProgress` becomes `IN_PROGRESS` and `N1` stays `N1`.

```protobuf
enum Level
{
    N1 = 1;
    N2 = 2;
    N3 = 3;
}

enum Priority
{
    N0 = 0;
    N10 = 10;
    N20 = 20;
}
```

The C# enum members use the PascalCase names (`Level.N1`), while the proto uses the upper-case form. A
numeric enum whose first member is not zero produces a descriptor `protoc` rejects, because proto3 requires
the first value of an enum to be `0`. If your numeric enumeration starts at 1, add a zero member in the
document or accept that the C# types work while the descriptor does not compile.

## Collisions

A name is not a suggestion: two types with one name in one namespace cannot compile, and a member may not
have its type's name. The mapper checks four collisions and either derives a distinct name or refuses the
document.

| Id | Collision | Behaviour |
| --- | --- | --- |
| `SP2005` | a component schema is named like an operation wrapper (`CreatePetRequest`, `CreatePetResponse`, …) | **error**, no output |
| `SP2006` | a property would generate a member with the enclosing type's name (`Pet.Pet`) | **error**, no output |
| `SP2007` | two inline enumerations share a name and declare different members, and the derived name also collides | **error**, no output |
| `SP2008` | a nullable property needs `Has{Name}`/`Clear{Name}` and the schema already declares that member | **error**, no output |

Two inline enumerations with one name and **the same** members are one type and are shared, which is what a
vocabulary reused across schemas should do. Two with different members are given a derived name made of the
original name plus the declared members in order — `State` with `purged` becomes `StatePurged`. If that
derived name is free, both enumerations survive without a diagnostic and without a name you wrote. The
`SP2007` error is reserved for the case where even the derived name is taken by a different member set.

An operation wrapper collision is the most common in practice, because the wrapper names are derived from
`operationId` rather than declared. A schema called `GetPetResponse` and an operation `getPet` cannot coexist:

```text
error SP2005: SharpPortico cannot map 'openapi/pets.yaml': the message name 'GetPetResponse' is produced
twice - an operation derives its messages as '{OperationId}Request' and '{OperationId}Response', so a
component schema of that name collides. Rename one of the two
```

Renaming the schema is usually right. Renaming the operation changes the RPC, the client method and every
call site, which is a larger contract change than renaming a component nobody outside the document sees.

## Names the generator will not change

Two identifiers are fixed:

- **`has_value`**: an operation or schema with no fields gets a single `bool has_value = 1` so that the
  message is not empty. It is spelled in proto and `HasValue` in C#. A contract that declares its own
  `has_value` property in that position produces duplicate members; rename the property.
- **`body`, `data`, `items`**: the request body, response payload and array payload fields. A parameter
  called `body` and a request body both map to `Body`, and the second field is not renamed; field-number
  assignment makes the collision a compile error rather than a silent overwrite.

The general advice follows from the rules: keep component names nouns, keep `operationId` verbs, and avoid
property names that differ only in punctuation. The mapping will handle the rest deterministically.

