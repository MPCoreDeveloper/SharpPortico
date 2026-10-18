# Nullable fields and proto3 presence

A property the contract declares nullable keeps that meaning all the way out: SharpPortico emits a proto3
`optional` field for it, so **"absent" and "set to the default" stop being the same answer**.

This page describes the behaviour as of **1.5.0** and is the document of record for it. The mapping used to drop the
flag, because a singular proto3 scalar has no presence of its own: `nullable: true` on `retries` arrived as a `0`, and
on `nickname` as an empty string. A consumer could not tell a value nobody sent from one the server had explicitly
zeroed, which is the difference between "keep the current value" and "set it to nothing".

| | Before 1.5.0 | 1.5.0 |
| --- | --- | --- |
| `retries: null` | `Retries = 0`, indistinguishable from `0` | `HasRetries == false`, `Retries` reads `0` |
| `retries: 0` | `Retries = 0` | `HasRetries == true`, `Retries == 0` |
| field omitted | `Retries = 0` | `HasRetries == false` |
| on the wire | a `0` is never written | a set `0` is written, an absent field is not |
| gateway (proxy) | the parameter was dropped | the parameter is sent whenever it was set |

---

## 1. What the mapping produces

The rule is one line in the mapper: a schema with `nullable: true` whose field kind can carry the label becomes
`IsOptional`, and every emitter then asks the field's own presence instead of comparing its value with a default.

| OpenAPI | `.proto` | generated C# |
| --- | --- | --- |
| `type: integer, nullable: true` | `optional int32 retries = 3;` | `int Retries`, `bool HasRetries`, `void ClearRetries()` |
| `type: number, nullable: true` | `optional double ratio = 4;` | `double Ratio` + presence members |
| `type: boolean, nullable: true` | `optional bool enabled = 5;` | `bool Enabled` + presence members |
| `type: string, nullable: true` | `optional string nickname = 6;` | `string Nickname` + presence members |
| `type: string, format: byte, nullable: true` | `optional bytes blob = 7;` | `ByteString Blob` + presence members |
| inline `enum`, `nullable: true` | `optional StatusEnum status = 8;` | `StatusEnum Status` + presence members |
| `$ref` to an **object** schema, nullable | `Pet data = 1;` (unchanged) | message field — already absent until set |
| `type: array` (nullable, or nullable items) | `repeated ...` (unchanged) | repeated — the loss is reported as `SP2000` |

A `$ref` to a named **enumeration** is an enum field like any other, so it takes the label exactly as an inline one does;
a `$ref` to an object schema is a message field and does not.

Two kinds are deliberately left alone. A **message** field is already a presence type: it is absent until it is set,
and protobuf has no way to write "a message set to null". A **`google.protobuf.Timestamp`** field is a message too.
Everything else that is singular — including enums, strings and `bytes` — takes the label.

A repeated field cannot carry it: a repeated field is a sequence, a sequence has no "unset" state distinct from empty,
and it has no per-element presence. A contract that asks for one is still mapped, and what the mapping cannot say is
reported. See §6.

---

## 2. The generated members

A nullable scalar is emitted the way protoc emits an `optional` field: a backing value, a flag saying whether it was
set, and `Has`/`Clear` members to read or reset that flag. The names match protoc's C# output, so code written against
a `protoc`-generated message reads the same here.

```csharp
public sealed partial class Thing : global::Google.Protobuf.IMessage<Thing>
{
    private int _value_Retries;
    private bool _has_Retries;

    public bool HasRetries => _has_Retries;

    public void ClearRetries()
    {
        _has_Retries = false;
        _value_Retries = default;
    }

    public int Retries
    {
        get { return _value_Retries; }
        set { _value_Retries = value; _has_Retries = true; }
    }
```

A `string` or a `bytes` field keeps a **nullable** backing field instead of a defaulted one, so "never set" and "set to
empty" are different in memory as well as on the wire. The property still reads back the protobuf default, exactly as
the non-optional string fields do:

```csharp
    private global::Google.Protobuf.ByteString? _field_Blob;
    private bool _has_Blob;

    public bool HasBlob => _has_Blob;

    public void ClearBlob()
    {
        _has_Blob = false;
        _field_Blob = null;
    }

    public global::Google.Protobuf.ByteString Blob
    {
        get { return _field_Blob ?? global::Google.Protobuf.ByteString.Empty; }
        set { _field_Blob = value; _has_Blob = true; }
    }
```

The flag is private and the property is the only way in, which is what keeps the two in step: **every write raises the
flag**, including a write of the default value. That is the whole difference from a proto3 scalar, where writing `0` is
indistinguishable from writing nothing.

---

## 3. Round-trip semantics

Presence is part of the message's behaviour, not a property bolted beside it, and every generated member answers on the
same question:

| Operation | An absent field | A field set to the default |
| --- | --- | --- |
| `CalculateSize()` | contributes nothing | contributes the tag **and** the value |
| `WriteTo(output)` | writes no tag | writes the tag and the value (a set `0` occupies bytes) |
| `MergeFrom(input)` | remains absent | reads back as set |
| `MergeFrom(other)` | copied only when `other.HasX` | copied |
| `Clone()` | stays absent | stays set, byte-identical to the original |
| `Equals` / `Equals(object)` | two messages differ when one is absent and the other set, whatever the values | values compared once both are set |
| `GetHashCode()` | contributes nothing | contributes the value |
| `ClearX()` | — | returns to absent: flag false, backing default |

The visible consequence is that these hold, and they are pinned by
`tests/SharpPortico.Tests/NullabilityPresenceTests.cs`:

```csharp
var absent = new Thing();
var zeroed = new Thing { Retries = 0 };

absent.HasRetries;              // false
zeroed.HasRetries;              // true
absent.CalculateSize();         // 0
zeroed.CalculateSize();         // tag + value, so not 0
absent.Equals(zeroed);          // false - same value, different field
zeroed.Clone().HasRetries;      // true  - Clone copies presence, not just the value
```

`MergeFrom(byte[])` raises the flag the same way, because the generated reader assigns the property rather than the
backing field. A field that was never set is therefore never "set by accident" when a message is decoded.

---

## 4. On the wire

`optional` in a `.proto` file is proto3 presence, encoded the way a proto2 `optional` field always was: the tag is
written whenever the field is set, including when it holds the default value. Nothing else about the encoding changes,
so:

- **Field numbers, types and names are unchanged.** `optional int32 retries = 3;` and `int32 retries = 3;` read each
  other's bytes without translation.
- **An older reader loses the distinction, not the data.** A message generated before 1.5.0 reads a set `0` as `0` and
  an absent field as `0`; only the new type can tell them apart. Nothing is corrupted in either direction.
- **A new reader keeps the distinction an older writer produced.** A writer that never writes the tag produces an
  absent field, which is what it meant.
- **`protoc` must be 3.15 or newer** to accept `optional` in proto3 without an experimental flag. The descriptor this
  generator emits is accepted by `protoc 36.0` as-is, and CI compiles every sample's descriptor with `protoc`.

The distinction also survives JSON, which is the gateway's business rather than the message's — see §7.

---

## 5. OpenAPI 3.1

3.1 writes "this may be null" as a member of the type array rather than as a flag, and the downgrade pass translates it
before the 3.0 reader ever sees the document:

```yaml
# 3.1
nickname:
  type: [string, "null"]
```

```yaml
# what the rewrite hands the parser (3.0)
nickname:
  type: string
  nullable: true
```

```text
// the descriptor
optional string nickname = 2;
```

The rewrite is still reported — `SP1002` says the document was read as 3.0, and `SP1003` names each construct it
changed — because a 3.1 document did not reach the mapper unchanged even when the result is exactly right.

A union that names **several** types is a different matter: `type: [string, integer]` has no protobuf type of its own and
becomes `google.protobuf.Value`, which already has "no value" among the things it can hold. Presence is not added on top
of it.

---

## 6. Where presence is not available

Two constructs ask for something protobuf cannot express. Both are handled the same way the rest of the mapping handles
a loss: the contract still maps, and the loss is named.

**A nullable array, or a nullable element — `SP2000` (warning).**

```yaml
tags:
  type: array
  nullable: true
  items:
    type: string
    nullable: true
```

```text
warning SP2000: SharpPortico could not map 'nullable' (tags): a repeated field cannot be null, so an
array that was never set and an array that was set to nothing are the same field
warning SP2000: SharpPortico could not map 'nullable' (tags[]): a repeated field has no per-element
presence, so an element that was null reads back as the element's default value
```

The field still maps to `repeated string tags = 1;`. What is lost is the ability to say "the array itself was null" and
"the third element was null" — no protobuf encoding carries either. The warnings are only reported when
`EmitDiagnosticsForUnmappableConstructs` is `true`, which is the default.

**A presence member the schema already declares — `SP2008` (error).**

```yaml
more:
  type: integer
  nullable: true
hasMore:
  type: boolean
```

`more` needs the members `HasMore` and `ClearMore`, and `hasMore` already generates `HasMore`. One member name cannot
carry both, and the result would not compile, so generation is refused rather than emitted broken:

```text
error SP2008: SharpPortico cannot map 'openapi/things.yaml': property 'more' of schema 'Thing' is
nullable, so its message needs presence members named 'HasMore' and 'ClearMore', but 'HasMore' is
already a member of 'Thing'. Rename one of the two
```

The fix is on the contract's side, because the collision is in the contract: rename the nullable property, or the
property that spells its presence member.

---

## 7. Gateway (proxy mode)

Presence is the difference between "send nothing" and "send zero", so the gRPC↔REST gateway asks the field rather than
comparing it with a default. Before 1.5.0 a nullable request parameter was forwarded only when it was non-default — an
explicit `0` was silently dropped — and a `null` in the response body was read as `0`.

| Direction | Behaviour in 1.5.0 |
| --- | --- |
| request → REST query/path parameter | sent when `request.HasX` — an explicit `0` or empty string is sent |
| request → REST JSON body | written when `HasX`; a `null` in the body would have no gRPC value to come from |
| REST response → message | a JSON `null` leaves the field absent; it is not read as `0` |
| message → REST JSON body | an absent field is written as `null`, never as a default nobody sent |

Two smaller rules ride along, both fixed in 1.5.0:

- **`bytes` are base64 on both paths.** A `bytes` field is written with `Convert.ToBase64String` and read with
  `GetBytesFromBase64`, and the repeated path does the same for every element — it used to read a base64 string as a
  plain string, so a repeated `bytes` field arrived as the text of its own encoding.
- **A `null` element is skipped rather than forced into a value.** The reader tests `JsonValueKind.Null` before it
  assigns anything.

The bytes rule matters beyond the gateway: `contentEncoding: base64` in a 3.1 document maps to `bytes`, and the same
conversion is what makes that mapping round-trip.

---

## 8. Upgrading from 1.4.0

SharpPortico 1.5.0 is a minor release: nothing is removed, the mapping of a non-nullable property is unchanged, and the
wire format of every field is unchanged. What changes is what a nullable property generates.

**Source compatibility.** A generated message for a schema with nullable scalars gains `Has…` and `Clear…` members — new
public members, so code that used the message still compiles. The one break is a schema that already declared a property
whose generated name is one of those members; that contract now fails with `SP2008` instead of producing a file that
never compiled.

**Behaviour.** `Equals` and `GetHashCode` now include presence. A message where a nullable field was never set no longer
equals one where it was set to `0`, even though both read back `0` — that is the point of the change. Code that relied
on value-only identity in a dictionary or a set should be checked, because presence is part of the hash now.

**On the wire,** a nullable field is written only when it was set, and a *set* default value is written. A peer running
1.4.0 still reads the values correctly; it just cannot see the difference.

**Regenerating** is enough on the consumer side: the generator runs on every build, and no hand-written code needs to
change.

---

## 9. A complete example

```yaml
openapi: 3.0.3
info: { title: Things, version: 1.0.0 }
paths:
  /things:
    get:
      operationId: GetThing
      parameters:
        - name: retries
          in: query
          schema: { type: integer, nullable: true }
      responses:
        '200':
          description: ok
          content:
            application/json:
              schema: { $ref: '#/components/schemas/Thing' }
components:
  schemas:
    Thing:
      type: object
      properties:
        retries: { type: integer, nullable: true }
        nickname: { type: string, nullable: true }
        name: { type: string }        # not nullable: a plain proto3 field
```

```bash
sharpportico generate things.yaml --out contracts/
```

```text
syntax = "proto3";

package thingsservice;

option csharp_namespace = "ThingsService";

message Thing
{
    optional int32 retries = 1;
    optional string nickname = 2;
    string name = 3;
}

message GetThingRequest
{
    optional int32 retries = 1;
}

message GetThingResponse
{
    Thing data = 1;
}

service ThingsService
{
    rpc GetThing (GetThingRequest) returns (GetThingResponse);
}
```

On the server side the flag is what decides whether a value was sent, which is the question a PATCH-style endpoint
actually asks:

```csharp
public override Task<GetThingResponse> GetThing(GetThingRequest request, ServerCallContext context)
{
    var filter = new Filter();

    if (request.HasRetries)
    {
        // Sent, even when it is 0 - which a "!= 0" test used to swallow.
        filter.Retries = request.Retries;
    }

    return Task.FromResult(new GetThingResponse { Data = Load(filter) });
}
```

And on the client side:

```csharp
var thing = new Thing { Nickname = "Atlas" };

thing.HasNickname;      // true
thing.HasRetries;       // false - never set
thing.Retries;          // 0, the protobuf default, with no presence behind it
thing.Retries = 0;      // now it has presence
thing.HasRetries;       // true
thing.ClearRetries();   // back to absent
```

`name` in the same message has no `HasName`, because the contract did not ask it to be nullable. Presence is a fact about
the contract, not a new default for every scalar.

---

## 10. What is verified, and where

`tests/SharpPortico.Tests/NullabilityPresenceTests.cs` holds the behaviour still, on both `net10.0` and `net11.0`:

| Test | What it pins |
| --- | --- |
| `A_Nullable_Scalar_Becomes_An_Optional_Field_With_Presence_Members` | the `optional` label for int, string, enum and bytes; `Has…`/`Clear…` for each; no `HasName` for a non-nullable property; the file compiles |
| `Absent_And_Set_To_The_Default_Are_Different_Answers` | presence, `CalculateSize`, `Equals`, a wire round trip, `Clone`, `MergeFrom(other)` and `Clear…`, against a compiled copy of the emitted message |
| `The_Gateway_Asks_The_Field_Whether_It_Was_Set` | the proxy's `if (request.HasRetries)`, its `null`-aware reader and its `null` writer |
| `A_Nullable_Array_Or_Element_Is_Reported_Because_A_Repeated_Field_Cannot_Carry_It` | `SP2000` twice (array and element), and that the field still maps |
| `A_Presence_Member_The_Schema_Also_Declares_Is_Refused` | `SP2008`, and that no source is emitted |

Beyond the tests: `protoc 36.0` accepts the descriptor a nullable contract produces, without an experimental flag, and
CI compiles every sample's descriptor with `protoc` so an invalid label cannot ship. The 3.1 sample's contract uses
`type: [T, "null"]` and is published as a native image and run in CI, which is where the 3.1 half of this mapping is
proved end to end.




