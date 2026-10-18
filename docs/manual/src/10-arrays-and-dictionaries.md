# 10. Arrays and Dictionaries

<p class="lead">An OpenAPI array becomes a protobuf `repeated` field. The interesting part is the element: it
may be a reference, an enumeration, an inline object, an arbitrary JSON value, or another array. This chapter
gives the rule for each, and the two places where a repeated field cannot say what the document said.</p>

## The element decides

| Array's `items` | Generated field |
| --- | --- |
| `$ref` to an object schema | `repeated <Message> <name>` |
| `$ref` to an enumeration schema | `repeated <Enum> <name>` |
| inline object with properties | a repeated field named after the property; the nested message is not emitted in 1.5.0 — use `$ref` (see below) |
| inline object without properties | `repeated google.protobuf.Struct <name>` |
| inline `enum` | `repeated <PropertyName>Enum <name>` |
| `type: string` / `integer` / … | `repeated string` / `int32` / … |
| the JSON-value marker | `repeated google.protobuf.Value <name>` |
| absent (`items` missing) | `repeated string <name>` |
| an array (`items.type: array`) | `repeated string <name>` (see below) |

`sink.proto` shows four of these on one message:

```protobuf
message Widget
{
    // ...
    repeated int64 counts = 12;
    repeated string matrix = 13;
    WidgetKind kind = 14;
    repeated WidgetKind tags = 15;
}
```

`counts` is a plain repeated scalar. `tags` is a repeated enumeration. `matrix` is the surprising one: its
schema is an array of arrays, and a protobuf field cannot repeat a repeated field. The mapper falls through to
its scalar default and produces `repeated string`, which is a real loss — the inner arrays are gone. If a
contract needs a matrix, model it as a repeated message with one repeated field inside, for example
`repeated Row rows`, where `Row` has `repeated double cells`.

The same fallback applies to an array with no `items` at all. It is mapped rather than refused, because an
array with an unconstrained element is still an array; `string` is the mapper's documented default for an
unknown type (chapter 7).

## Arrays of inline objects

An array property whose items are an inline object would be named after the **property**, not after the
element position:

```yaml
Widget:
  type: object
  properties:
    children:
      type: array
      items:
        type: object
        properties:
          id: { type: string }
```

The mapper derives a message named `Children` for that element. In 1.5.0 that message is not added to the
emitted model, so the `repeated Children children` field points at a type the descriptor and the C# file do
not contain. The generated code does not compile.

Declare the element as a named component and reference it:

```yaml
Widget:
  type: object
  properties:
    children:
      type: array
      items: { $ref: "#/components/schemas/Child" }
components:
  schemas:
    Child:
      type: object
      properties:
        id: { type: string }
```

```protobuf
message Child
{
    string id = 1;
}

message Widget
{
    repeated Child children = 1;
}
```

Chapter 17 records the inline-object defect. A duplicate component schema name is still refused as `SP2005`;
the missing inline message is not reported at all.

## Arrays at the edges of an operation

The position changes the wrapper, not the element rule.

| Position | Shape |
| --- | --- |
| response body is an array | `{Method}Response { repeated <Element> items = 1; }` |
| response array's items are inline objects | element message named `{Method}Item` |
| request body is an array | `{Method}Body { repeated <Element> values = 1; }` |
| component schema is an array | `message <Name> { repeated <Element> values = 1; }` |

`ListPetsResponse` is the first row: the `200` response's schema is an array of `Pet`, so the wrapper has
`repeated Pet items = 1`. The field is named `items` because the response is a payload of elements; a body
message uses `values` because it is a wrapper around a single array value.

## The two nullability losses

A repeated field is a sequence. It has no "unset" state distinct from empty, and it has no per-element
presence. Two things therefore cannot be represented:

1. `nullable: true` on the array itself;
2. `nullable: true` on the items schema.

Both are reported as `SP2000` when `EmitDiagnosticsForUnmappableConstructs` is `true`, and both still map:

```text
warning SP2000: SharpPortico could not map 'nullable' (tags): a repeated field has no per-element
presence, so an element that was null reads back as the element's default value
```

The message names the proto field (`tags`, or `tags[]` for the element case) and says what was lost. The
field remains `repeated string`; a null element becomes `""`, and a null array becomes an empty one. If that
distinction matters, the contract needs a wrapper message, not a repeated field.

## What is not generated

- **No `map<string, T>`.** A schema with `additionalProperties` becomes `google.protobuf.Struct`, even when
  the value type is declared. Chapter 17 records this.
- **No fixed-length arrays.** JSON Schema's `minItems`/`maxItems` are not carried; a 3.1 tuple's length is
  explicitly reported as lost (`SP1003`).
- **No multidimensional arrays.** The inner dimension collapses to `string`, as shown above.
- **No `repeated` of `optional`.** proto3 forbids it, and the mapper does not attempt it; the per-element loss
  is what `SP2000` describes.

The rule of thumb is the same as everywhere in this part: a repeated field carries a sequence of one type.
When the document describes anything more specific — a fixed length, a position-dependent type, a nullable
element — the extra meaning is reported rather than encoded.
