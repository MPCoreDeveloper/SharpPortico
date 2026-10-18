# 23. JSON at the Gateway

<p class="lead">The proxy does not use `System.Text.Json`'s serializer or protobuf's `JsonFormatter`. It emits a
serializer and a parser per message, as C# code, because the generated messages have no descriptor and the
project may be AOT-published. This chapter is the exact grammar of what those helpers read and write.</p>

## Writing a request body

`SerializePet(msg)` is a `StringBuilder` walk over the message's fields. For each field it appends
`"proto_name":value`, in declaration order, with no whitespace:

```json
{"id":7,"name":"Rex","status":2}
```

| Field | Written as |
| --- | --- |
| string | the string between quotes, **without escaping** |
| bool | `true` / `false` |
| int32, int64, uint32, uint64, float, double | invariant-culture number |
| enum | its number |
| bytes | a base64 string |
| message | a nested object, or `null` |
| repeated | a JSON array; messages recurse, scalars use the same rules |
| proto3 `optional`, set | its value |
| proto3 `optional`, not set | `null` |

Four decisions in that table are worth spelling out.

**Keys are proto names, not OpenAPI names.** The key is `pet_id`, not `petId`. The descriptor-side name comes
from the message field (chapter 8), so a legacy API that expects camelCase sees different JSON from the document
it published. If the REST service is strict about property names, the gateway is not a drop-in replacement.

**Every non-optional field is written, including defaults.** An unset `int` is `0`, an unset `string` is `""`, an
unset list is `[]`, an unset message is `null`. This is not proto3 JSON, which omits defaults. A legacy API that
distinguishes "absent" from "zero" will see a zero the caller never sent — unless the contract marked the field
nullable, in which case the field is `optional` and unwritten means `null`.

**Enums are numbers and 64-bit integers are numbers.** Proto3 JSON spells both as strings. The proxy's grammar is
closer to a typical REST API than to protojson, and a consumer must not assume the two are interchangeable.

**Strings are not escaped.** `sb.Append('"').Append(value).Append('"')` is the whole implementation. A value
containing `"`, `\`, a newline or a control character produces invalid JSON, and the legacy service rejects the
request or misparses it. This is a real defect in 1.5.0, not a documented trade-off; a contract whose string
fields can contain quotes needs a hand-written adapter. The same applies to `NaN` and `Infinity`, which are
written as bare tokens that JSON does not allow.

## Reading a response

`ParsePet(byte[] body, Pet msg)` parses the body with `JsonDocument` and walks the same field list. For each
field it looks up the **exact** proto-name key:

```csharp
if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("status", out var _status))
{
    msg.Status = (StatusEnum)_status.GetInt32();
}
```

| JSON | Read as |
| --- | --- |
| missing key | the field's default, untouched |
| extra key | ignored |
| `null` on a non-optional field | strings become `""`, messages stay unset; a numeric, boolean or enum `null` throws `InvalidOperationException` from `Get…` |
| `null` on an `optional` field | the field stays absent (`HasX` false) |
| number | `GetInt32` / `GetInt64` / `GetUInt32` / … |
| string | `GetString`, or `GetBytesFromBase64` for `bytes` |
| number for an enum | cast from `GetInt32` |
| string for an enum | **throws** — there is no name lookup |
| array | read only when the field is repeated |

Keys are case-sensitive and are not tried in camelCase. A response that sends `petId` or `PetId` does not fill a
field whose proto name is `pet_id`. A response that sends an enum as `"available"` instead of `0` throws
`InvalidOperationException`, which escapes the proxy handler and becomes an `Internal` gRPC error. A malformed
document throws `JsonException` the same way.

## Wrapper unwrapping

A REST response is often just the payload, while the gRPC response is a wrapper message with one `data` or
`items` field. The parser handles that with a root fallback: if the wrapper has exactly one non-sentinel field,
and the JSON root does not contain its key, the root is parsed as that field's type.

For `GetPetResponse { Pet data = 1; }` and a body of `{"id":7,"name":"Rex"}`:

- `data` is absent, so the whole object is parsed as a `Pet`;
- a body of `{"data":{…}}` still works, because the key is found first.

For `ListPetsResponse { repeated Pet items = 1; }` and a body of `[{"id":7}]`, the wrapper's single field is a
repeated message, so the root array is read as `items`. This fallback exists only at the top level and only for
a wrapper with exactly one field; a nested message's parser does not try it.

## What the helper cannot represent

The serializer and parser are generated per message from the field kinds, and four field shapes have no
generated path:

| Shape | Result |
| --- | --- |
| `google.protobuf.Struct` / `Value` anywhere in a serialized message | `SerializeStruct`/`ParseStruct` are referenced but never emitted: **CS0103** |
| non-optional `bytes` at the top level of a request | `IsSet` compares a `ByteString` with `0`: **CS0019** |
| repeated scalar at the top level of a request | `IsSet` reads `.Length` or compares a collection with `0`: **CS0019/CS1061** |
| any message-typed request field other than `Body` | silently omitted from path, query and body |

A `Struct` in a response breaks the compiler in the same way; the failure is in the parse helper, not in the
descriptor. These are the reasons a contract that maps cleanly to protobuf can still produce a proxy that does
not build. The generated messages, server and client are unaffected — only the gateway file is.

The supported subset is therefore: unary RPCs, singular scalars and enums as parameters, a single message
`Body`, JSON bodies, and responses whose shapes are made of scalars, enums, nested messages and repeated
messages. That subset covers a large share of ordinary REST APIs, and it is worth checking against a contract
before enabling `EnableProxyGeneration`.

## The string-escaping defect

The one runtime defect that affects an otherwise supported contract is the writer's treatment of strings. It
concatenates the value between quotes without escaping, so a name containing a double quote produces a body the
legacy service cannot parse. The parser is not affected: it uses `JsonDocument`, which handles escapes
correctly. Until the writer is fixed, a proxy in front of an API whose string fields can contain quotes,
backslashes or newlines needs a hand-written serializer or a contract that encodes those values (base64, for
example).

Nothing here is proto3 JSON. The gateway's grammar is its own, generated from the message model, and a caller
who assumes protojson's rules — string-encoded 64-bit integers, enum names, omitted defaults — will be surprised
in both directions.

