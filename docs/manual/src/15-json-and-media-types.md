# 15. JSON and Media Types

<p class="lead">gRPC does not send JSON. A request body declared as JSON becomes a protobuf message, and the
JSON document is only involved when a client or the gateway converts. This chapter covers which media type
the mapper reads, what a JSON schema becomes, and what is lost when JSON's flexibility meets protobuf's
types.</p>

## Choosing a media type

`MapRequestBodyField` and `MapSuccessResponse` each select one schema from a `content` map:

1. if `application/octet-stream` is present, it wins, and the field is `bytes` (chapter 16);
2. otherwise, the first entry — in the document's order — whose schema is not null is used;
3. entries without a schema are skipped;
4. all other entries are ignored.

There is no preference for `application/json` over `application/xml` at this step. A document that lists XML
first produces the XML schema's messages if that schema is the first with a schema at all. The recommendation
is to put the media type you mean first, or to declare only one.

`application/problem+json`, `application/merge-patch+json` and vendor types such as
`application/vnd.api+json` are treated as ordinary media types: their schema is read, and the JSON dialect
implied by the name is not interpreted.

## What a JSON schema becomes

JSON and protobuf agree on more than they disagree:

| JSON Schema | Protobuf |
| --- | --- |
| object with `properties` | message; an inline object under a property is not emitted in 1.5.0 (chapter 17) |
| array | repeated field |
| string / number / boolean | scalar |
| nested object reached through `$ref` | message (inline objects are not emitted; chapter 17) |
| `$ref` | message or enum reference |
| `enum` | protobuf enum |
| object with no properties | `google.protobuf.Struct` in a field position; a named component becomes a placeholder message with `has_value` |
| union of several types | `google.protobuf.Value` |

The disagreements are where this manual spends its time: nullability (chapter 12), arrays of arrays (chapter
10), unions (chapter 9), and validation keywords (chapter 17).

## JSON-specific constructs that are ignored

| Construct | Why it does not map |
| --- | --- |
| `discriminator` | protobuf has no tagged union; `oneOf` is flattened with a `kind` string |
| `readOnly` / `writeOnly` | a protobuf message is used in both directions; omitting a field would make the same schema two types |
| `xml` | an XML serialization hint |
| `example` / `examples` | documentation |
| `default` | protobuf has no schema-level defaults; a consumer's default is the type's zero value |
| `pattern`, `minLength`, `maximum`, … | validation, not type |
| `propertyNames`, `dependentRequired` | object-level validation |
| `$schema`, `$id`, `$anchor` | JSON Schema bookkeeping |

A `discriminator` is the one that most often surprises a reviewer. OpenAPI's `oneOf` plus `discriminator` is
a tagged union; the mapper produces a flat message with a `string kind` plus the first object choice's
fields. The discriminator's property name and mapping are not emitted, so a consumer cannot use the generated
`Kind` to decide which fields are meaningful. If a contract depends on a real union, the honest protobuf
contract is one message per variant with a `oneof` — which this generator does not produce from OpenAPI.

## JSON in the gateway

Proxy mode does serialize and parse JSON at run time, with generated code rather than reflection. Its rules
are not the OpenAPI document's rules; they are the legacy service's JSON, and they are covered in chapter 23.
Three facts are worth having here because they affect the contract:

- Property names are the **proto** names (`pet_id`), not the OpenAPI names (`petId`) and not the C# names
  (`PetId`).
- Enumerations are written and read as **numbers**.
- A `bytes` field is base64 text.

If the legacy API uses camelCase JSON and string enumerations — which most do — the gateway's output will not
match it. Chapter 23 states the limits plainly.

There is a sharper limit for free-form data. The gateway's JSON helpers are generated per message in the
model, and `google.protobuf.Struct` and `google.protobuf.Value` are not model messages: a proxy generated for
a contract containing either references a serializer that does not exist, and the generated file does not
compile. A contract with free-form objects or unions can use the messages, the server and the client, but not
proxy mode in 1.5.0. Chapter 17 records this with the other losses.

## Binary over gRPC

Protobuf's own encoding is binary and compact; there is no JSON step between the generated message and the
wire. A `string` field is UTF-8, a `bytes` field is raw octets, and numbers are varints or fixed-width
depending on their type. That is why the mapping can be reflection-free and why a payload does not grow when
it crosses the gRPC boundary.

The practical consequence is that `application/json` in the document describes the **legacy** API's media
type, not the generated contract's. A gRPC consumer sends a message; it does not send a JSON document. Only
the gateway converts back.
