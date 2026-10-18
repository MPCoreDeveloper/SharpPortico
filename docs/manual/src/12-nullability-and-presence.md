# 12. Nullability and Presence

<p class="lead">A singular proto3 scalar has no presence of its own: an absent field and a field set to `0`
are the same on the wire. SharpPortico restores the distinction the contract asked for by emitting proto3's
`optional` label and generating `Has…`/`Clear…` members, the way `protoc` does.</p>

## The difference in one table

For `retries: { type: integer, nullable: true }`:

| Situation | Before 1.5.0 | 1.5.0 |
| --- | --- | --- |
| JSON/C# value is `null` | `Retries = 0`, indistinguishable from `0` | `HasRetries == false`, `Retries` reads `0` |
| value is `0` | `Retries = 0` | `HasRetries == true`, `Retries == 0` |
| field omitted by the sender | `Retries = 0` | `HasRetries == false` |
| on the wire | a `0` is never written | a set `0` is written; an absent field is not |
| gateway | a `null` parameter was dropped | the parameter is sent whenever it was set |

That is the whole point: "keep the current value" and "set it to zero" are different requests, and a service
that receives `0` for both cannot tell them apart.

## What becomes optional

| OpenAPI schema | `.proto` | Generated C# |
| --- | --- | --- |
| `type: integer, nullable: true` | `optional int32 retries = 3;` | `int Retries`, `bool HasRetries`, `void ClearRetries()` |
| `type: string, nullable: true` | `optional string nickname = 4;` | `string Nickname` + presence |
| `type: boolean, nullable: true` | `optional bool enabled = 5;` | `bool Enabled` + presence |
| `type: string, format: byte, nullable: true` | `optional bytes blob = 6;` | `ByteString Blob` + presence |
| inline or `$ref` enum, `nullable: true` | `optional StatusEnum status = 7;` | `StatusEnum Status` + presence |
| `$ref` to an object, nullable | `Pet data = 1;` unchanged | already a presence type |
| `type: array, nullable: true` | `repeated …` unchanged | **loss**, `SP2000` |
| `items` nullable | `repeated …` unchanged | **loss**, `SP2000` |

Two kinds never take the label: a **message** field is absent until it is set, and protobuf has no way to
write "a message set to null"; a **`Timestamp`** would be a message too, though this version's mapper never
produces one (chapter 7). Everything else singular — enums, strings, bytes, numbers and booleans — takes it.

## The generated members

```csharp
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

A string or `bytes` field uses a nullable backing field and returns the protobuf default from the getter:

```csharp
private string? _field_Nickname;
private bool _has_Nickname;

public string Nickname
{
    get { return _field_Nickname ?? ""; }
    set { _field_Nickname = value; _has_Nickname = true; }
}
```

The names match `protoc`'s C# output, so code written against a `protoc`-generated message reads the same
here. Writing the property raises the flag; `Clear…` resets both the value and the flag.

## Wire and size

`CalculateSize`, `WriteTo` and `MergeFrom` ask the flag, not the value:

```csharp
// a set 0 is written; an absent field is not
if (HasRetries) { output.WriteRawTag(24); output.WriteInt32(Retries); }
```

On the reading side, a field present in the payload sets the flag — even when its value is the default. A
field absent from the payload leaves it unset. This is exactly how `optional` behaves in `protoc`-generated
code, and it is why the descriptor and the classes agree.

`Equals`, `GetHashCode` and `Clone` also use the flag, so `new Thing()` and `new Thing { Retries = 0 }` are
not equal. That is the C# expression of the same distinction.

## On the server

The flag is the question a `PATCH`-style endpoint actually asks:

```csharp
public override Task<UpdateThingResponse> UpdateThingAsync(UpdateThingRequest request, ServerCallContext context)
{
    if (request.HasRetries)
    {
        // Sent, even when it is 0 — which a "!= 0" test used to swallow.
        _thing.Retries = request.Retries;
    }

    if (request.HasNickname)
    {
        _thing.Nickname = request.Nickname;   // may be an explicit ""
    }

    return Task.FromResult(new UpdateThingResponse { Data = _thing });
}
```

`request.Retries != 0` is the wrong test for an optional field: it treats an explicit zero as "not sent".
Use `HasRetries` whenever the contract says the field may be null.

## On the client

```csharp
var thing = new Thing { Nickname = "Atlas" };

thing.HasNickname;      // true
thing.HasRetries;       // false — never set
thing.Retries;          // 0, the protobuf default, no presence behind it
thing.Retries = 0;      // now it has presence
thing.HasRetries;       // true
thing.ClearRetries();   // back to absent
```

A non-nullable property in the same message has no `Has…` member. Presence is a fact about the contract, not
a default applied to every scalar.

## In the gateway

The proxy uses the same flag in both directions. On the way out, a set field is sent even when its value is
the default, and an unset one is omitted; on the way back, a JSON `null` leaves the field unset rather than
writing `0` or `""`. The generated code reads:

```csharp
if (request.HasRetries)
{
    queryParams["Retries"] = Convert.ToString(request.Retries, CultureInfo.InvariantCulture) ?? "";
}
```

and, for a response, only assigns when the JSON value is not `null`. Chapter 23 has the full JSON rules.

## OpenAPI 3.1

The 3.1 spelling of a nullable string is a type array:

```yaml
callsign: { type: [string, "null"] }
```

The downgrade rewrites it to `type: string` plus `nullable: true`, so the result is identical:

```protobuf
optional string callsign = 2;
```

A type array with **two non-null types** is not a nullable type; it is a union, and it maps to
`google.protobuf.Value` (chapter 6). The rewrite distinguishes the two by counting the non-null entries.

## When presence cannot be represented

**A nullable array or element.** `repeated` has no unset state and no per-element presence. The field still
maps, and `SP2000` states the loss:

```text
warning SP2000: SharpPortico could not map 'nullable' (tags[]): a repeated field has no per-element
presence, so an element that was null reads back as the element's default value
```

**A presence member the schema already declares.** A nullable `more` needs `HasMore` and `ClearMore`; a
property literally named `hasMore` would produce the same member twice. That is refused, because no generated
code could satisfy both:

```text
error SP2008: SharpPortico cannot map 'openapi/things.yaml': property 'more' of schema 'Thing' is
nullable, so its message needs presence members named 'HasMore' and 'ClearMore', but 'HasMore' is
already a member of 'Thing'. Rename one of the two
```

Both cases are about the same limit: presence is a property of a singular field, and a contract that asks
for it elsewhere is asking protobuf for something the format does not have.

## Why not wrappers?

The alternative to `optional` is the `google.protobuf.*Value` wrapper types (`Int32Value`, `StringValue`, …).
They also carry presence, and they were the standard answer before proto3 added `optional`. The mapper does
not use them because they change the JSON shape (`{"value": 0}` instead of `0`), add an allocation per set
field, and require `google/protobuf/wrappers.proto`. `optional` is the modern spelling of the same idea, and
it keeps the wire format identical to a plain scalar apart from the tag being written for a default value.

