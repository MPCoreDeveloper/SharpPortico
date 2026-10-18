# 7. Scalars and Formats

<p class="lead">A scalar is the smallest decision the mapping makes, and it is made in one place:
`SchemaMapper.ResolveScalarKind`. This chapter states the table, then shows what the table does not do —
because OpenAPI has more scalar formats than protobuf has scalar types.</p>

## The table

| OpenAPI `type` | `format` | Protobuf | C# | Notes |
| --- | --- | --- | --- | --- |
| `string` | absent, or anything but `binary`/`byte` | `string` | `string` | includes `uuid`, `date`, `date-time`, `email`, `uri`, `password` |
| `string` | `binary` | `bytes` | `ByteString` | file contents |
| `string` | `byte` | `bytes` | `ByteString` | base64 text |
| `integer` | absent | `int32` | `int` | |
| `integer` | `int64` | `int64` | `long` | |
| `integer` | any other format (`int8`, `int16`, `uint32`, …) | `int32` | `int` | the format is not consulted |
| `number` | `float` | `float` | `float` | |
| `number` | absent, `double`, or anything else | `double` | `double` | |
| `boolean` | — | `bool` | `bool` | |
| `file` | — | `bytes` | `ByteString` | OpenAPI 2.0 leftover, still read |
| absent or unrecognised | — | `string` | `string` | the fallback |

The implementation is one `switch` over `type` and a `format` comparison per arm:

```csharp
case "string":
    if (format is "binary" or "byte") return FieldKind.Bytes;
    return FieldKind.String;
case "integer":
    return format == "int64" ? FieldKind.Int64 : FieldKind.Int32;
case "number":
    return format == "float" ? FieldKind.Float : FieldKind.Double;
case "boolean":
    return FieldKind.Bool;
```

The rule is intentionally small. A mapper that tried to honour every format would need a type for each, and
protobuf has no `uuid`, no `date`, no `uri` and no `decimal`. Choosing the nearest protobuf scalar and saying
so is more useful than inventing wrapper messages nobody asked for.

## What the edge document shows

`edge.yaml` declares an inline object whose properties use the formats that most often surprise people:

```yaml
GetThingByIdData:
  type: object
  properties:
    total: { type: integer }
    ratio: { type: number }
    when: { type: string, format: date-time }
    uid: { type: string, format: uuid }
```

The generated message is:

```protobuf
message GetThingByIdData
{
    int32 total = 1;
    double ratio = 2;
    string when = 3;
    string uid = 4;
}
```

`date-time` and `uuid` are strings because that is what protobuf can carry without a convention. If a
consumer needs a `Timestamp` or a `Guid`, the conversion belongs at the edge of that consumer, where the
contract's textual format is known. The generator does not guess: `when` could be RFC 3339, a local time with
an offset, or a date, and the schema does not say which.

## Large integers and unsigned values

`integer` without `int64` is `int32`, even when the format says `uint32` or `int16`. Two consequences are
worth stating plainly:

- A contract with `format: uint32` and values above `2_147_483_647` overflows the generated `int`. Declare
  `int64` in the document if the range matters, or accept the narrower field.
- Negative values are representable, which `uint32` would forbid. The mapping does not add validation; it
  picks the type that can hold the common case.

There is no path from an OpenAPI document to `uint32` or `uint64` in this version. The field kinds exist in
the model and the emitters handle them, but `ResolveScalarKind` never returns them. A `.g.cs` you inspect
will therefore not contain `uint` fields even when the document says `format: uint32`.

## Strings that are really bytes

`format: binary` and `format: byte` both become `bytes`. The difference is only how the value is written in
JSON:

| Format | JSON representation | Generated field |
| --- | --- | --- |
| `byte` | base64 string | `ByteString` |
| `binary` | raw octets, not JSON | `ByteString` |

For an `application/octet-stream` body the mapper does not look at a schema at all: the field is always
`bytes body`. Chapter 16 covers uploads and downloads.

## Defaults and presence

A scalar field with no `nullable: true` has no presence. Its C# property returns the protobuf default —
`0`, `false`, `""`, an empty `ByteString` — when nothing was sent, and `CalculateSize` writes nothing for a
default value. This is proto3's design, not a SharpPortico choice.

A scalar with `nullable: true` becomes `optional`, gains `Has…`/`Clear…`, and a set default is written on the
wire. Chapter 12 is the full treatment. The important interaction with this chapter is that **presence does
not change the scalar type**: `optional int32` is still an `int`, and `optional string` is still a `string`.

## Where the type comes from when a schema is not inline

A `$ref` is resolved to the referenced schema's type. If the reference names an object, the field is a
message; if it names an enumeration, the field is that enum. The scalar table applies to the schema behind the
reference, not to the reference's name. Chapter 9 covers references and composition; the rule here is simply
that a scalar is always decided by `type` and `format`, never by a property's name or its `example`.

## Choosing a format in the document

Because the mapping is small, the document has more control than it looks. Three habits prevent surprises.

1. **Use `int64` for identifiers and counts that can grow.** An `int32` ID is a limit the contract did not
   intend to declare.
2. **Use `format: byte` when JSON must carry binary data.** The gateway writes a `bytes` field as base64, so
   a `byte` field round-trips through JSON without a custom encoder.
3. **Do not use `format` to express semantics.** `uuid`, `date-time`, `email` and `uri` are conventions for
   consumers; they do not change the protobuf type, and the generated code does not validate them.

If a contract needs a real timestamp type, the protobuf contract is the place to say so: a component schema
can declare a `google.protobuf.Timestamp` only if the source document can express it, and this mapping does
not infer one from `format: date-time`. That is a deliberate refusal to guess, and it is listed among the
losses in chapter 17.

