# 9. Object Shapes and Composition

<p class="lead">An object schema can be written in several ways: properties, `allOf`, `oneOf`, `anyOf`, a
reference, or nothing at all. Protobuf has one construct for all of them — a message — so the mapper's job is
to decide which fields that message has.</p>

## The ordinary case

An object with `properties` becomes a message with one field per property, in declaration order, numbered
from 1. Nested objects are meant to become nested messages named after their property; in 1.5.0 an inline
object under a property is not emitted, so declare that object as a named component and reference it with
`$ref` (chapter 17). An inline response or request-body object is emitted as the wrapper's `data` or `body`
message, so it does not need a component. Arrays become repeated fields (chapter 10); references become
message- or enum-typed fields (chapter 11).

`sink.proto` shows the result for a schema that uses nesting and recursion:

```protobuf
message Dimensions
{
    double width = 1;
    double height = 2;
}

message Widget
{
    // ...
    Widget child = 23;
    repeated Widget children = 24;
    Dimensions dims = 25;
    StateEnum state = 26;
}
```

`child` is a `Widget` because the property references the schema being defined. A self-reference is legal
protobuf — a message may hold a field of its own type — so no cycle diagnostic is raised and no special case
is needed. `dims` is a `Dimensions` message because the property is a `$ref` to the `Dimensions` component;
the component is emitted because it has a name the model can carry.

## allOf: merge

`allOf` is read as "all of these together", so the mapper merges the members:

1. each `allOf` entry is merged in order (its properties, then its own nested `allOf`);
2. the schema's own `properties` are merged last.

Field numbers follow the merged order. A property declared in two entries appears twice, because the mapper
does not deduplicate: protobuf has no notion of overriding a field, and silently keeping one of two
declarations would hide a contradiction in the contract. If your document uses `allOf` for inheritance,
ensure the base and the extension do not declare the same property.

One case is special. A component whose entire body is a single `$ref`:

```yaml
components:
  schemas:
    PetAlias:
      allOf:
        - $ref: "#/components/schemas/Pet"
```

is emitted as an **empty message**, not as an alias:

```protobuf
message PetAlias
{
}
```

proto3 has no type alias. The mapper could copy `Pet`'s fields into `PetAlias`, but then the two types would
drift the moment `Pet` changed, and a consumer would have two incompatible messages that look identical.
It chooses a distinct, empty type instead. The practical consequence is that a `$ref` to `PetAlias` gives a
field with no members; reference `Pet` directly when the alias exists only to rename a type.

## oneOf and anyOf: flatten with a discriminator

A union cannot be a protobuf field: a field has one type. The mapper's approximation is:

- if the union has exactly **one** choice, merge that choice's fields (the union adds nothing);
- otherwise add a `string kind = 1` discriminator field, then merge the fields of the **first choice that
  declares properties**, or the first choice when none do.

The merged fields are flat: a `oneOf` of `Cat` and `Dog` produces one message with `kind` plus the union of
the two shapes' members, with the second shape contributing nothing if only the first was chosen. This is a
loss, and it is a deliberate one: the alternative — one message per alternative plus a wrapper — would
change the contract's type graph and force every consumer to write a switch that the document did not ask
for. Chapter 17 records it among the constructs that do not survive intact.

`anyOf` is treated exactly like `oneOf`. JSON Schema draws a distinction (at least one versus exactly one);
protobuf has no place to enforce either.

## Free-form objects: Struct

An object that declares no members — `type: object` with no `properties` — says "any JSON object".
Protobuf has a type for exactly that: `google.protobuf.Struct`, a map of string keys to
`google.protobuf.Value`. A bare `{}` schema is different: it has no type at all, so it falls through to the
scalar fallback and becomes a `string` (chapter 7).

```protobuf
message Widget
{
    // ...
    google.protobuf.Struct labels = 16;
    google.protobuf.Struct meta = 17;
}
```

`sink.yaml` declared `labels` as `type: object, additionalProperties: true` and `meta` as an object with no
properties; both became `Struct`. The `google/protobuf/struct.proto` import is added automatically when the
model contains one.

A schema whose `additionalProperties` names a **type** is still a free-form object in this mapping. There is
no `map<string, T>` generation: the mapper checks for declared properties, not for a typed value schema, so
`additionalProperties: { type: integer }` becomes `Struct` as well. That is a loss worth knowing before you
write a contract that depends on a typed dictionary (chapter 17).

## Any JSON value: Value

`google.protobuf.Value` is protobuf's "any JSON value": null, number, string, bool, list or object. The mapper
uses it when:

- the schema carries the internal marker `x-sharpportico-json-value: true` (written by the 3.1 rewrite for a
  multi-type union or a heterogeneous tuple), or
- the 3.1 construct is a type union with more than one non-null type.

```protobuf
message Vehicle
{
    google.protobuf.Value telemetry = 4;
    repeated google.protobuf.Value bounds = 5;
}
```

`telemetry: { type: [object, array] }` has no protobuf type that is both an object and an array, so `Value`
is the honest answer. `bounds` is a tuple whose positions differ in type; a repeated field holds one type, so
each element is a `Value`.

`Value` and `Struct` are different answers to different questions. `Struct` is "an object whose members I do
not describe"; `Value` is "a value whose type I do not describe". A contract that means one and gets the other
loses shape or flexibility, so the mapper asks the question at the position (property, array element, body,
response) rather than guessing from the component's name.

## A component that is not an object

A component schema can be an array, a scalar, or empty. Each has a defined shape when it becomes a message.

| Component schema | Generated message |
| --- | --- |
| `type: object` with properties | one field per property |
| `type: array` | `repeated <element> values = 1` |
| a scalar | `<scalar> value = 1` |
| `{}` or `type: object` with no properties, used as a component | `bool has_value = 1` |
| `allOf: [ $ref ]` | empty message (see above) |

The `Values` / `Value` wrappers exist because a component has to be a message to be referenced by a field.
When the same schema is used as a **property**, the mapper does not wrap it: a property whose schema is an
array is a repeated field directly, and a property whose schema is a free-form object is `Struct`. The
difference between "a component used as a type" and "a schema used as a field" is the source of several
surprises, and it is worth checking both positions when a contract changes.

A message with no fields at all is legal protobuf but awkward in C#: `new Empty()` carries nothing, and a
consumer cannot tell it apart from any other empty message. The `has_value` member is the mapper's way of
making "this response has no declared body" explicit rather than silent. It is not a field the document
declared, and it is not written on the wire unless set to `true`.

## What determines the shape

The decision order in `MapProperty` is the specification of this chapter, and it is worth quoting as a list:

1. an arbitrary JSON value marker → `google.protobuf.Value`;
2. a `$ref` → message or enum, by the referenced schema;
3. `type: array` → a repeated field (chapter 10);
4. an inline `enum` → `<Property>Enum` (chapter 11);
5. an object with properties → a nested message when it is a named component; an inline object is named by
   the mapper but not emitted in 1.5.0 (chapter 17);
6. an object without properties → `google.protobuf.Struct`;
7. anything else → a scalar (chapter 7).

Nothing about a property's `description`, `example`, `default`, `readOnly` or `deprecated` changes this
order. Those keywords are not carried into the descriptor, and a consumer that needs them has the OpenAPI
document.

