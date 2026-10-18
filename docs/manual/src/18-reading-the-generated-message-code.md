# 18. Reading the Generated Message Code

<p class="lead">The `.proto` text is a description; the C# file is the implementation. This chapter reads one
generated message line by line, so that the rest of the generated surface — servers, clients, the gateway —
stops looking like magic.</p>

## One class, no base class

Every message in the model becomes a `sealed partial class` implementing `Google.Protobuf.IMessage<T>`:

```csharp
[global::System.CodeDom.Compiler.GeneratedCode("SharpPortico", "1.5.0")]
public sealed partial class Pet : global::Google.Protobuf.IMessage<Pet>
{
    private static readonly global::Google.Protobuf.MessageParser<Pet> _parser = new(() => new Pet());
    public static global::Google.Protobuf.MessageParser<Pet> Parser => _parser;
    public global::Google.Protobuf.Reflection.MessageDescriptor Descriptor => null!;

    private const uint Tag_1_Id = 8u;
    private const uint Tag_2_Name = 18u;
    private const uint Tag_3_Status = 24u;

    public long Id { get; set; }

    private string? _field_Name;
    public string Name
    {
        get { return _field_Name ?? string.Empty; }
        set { _field_Name = value; }
    }

    public StatusEnum Status { get; set; }
}
```

Three things are worth noticing before the details:

- `partial` is real: you may add members in another file in the same namespace, and the generator will never
  collide with them because it only emits the names it computed.
- There is no `MessageBase` and no descriptor plumbing: `IMessage<T>` is implemented directly. The wire
  methods are `MergeFrom`, `WriteTo` and `CalculateSize` — with the parser's overloads — plus the inherited
  `Descriptor`, and the class implements them itself.
- `Descriptor` returns `null!`. The generated code does not build a `FileDescriptor` or a
  `MessageDescriptor`; it does not need one to serialize. The last section of this chapter is about what that costs.

## Field shapes

| Model field | C# property |
| --- | --- |
| scalar, no presence | `public long Id { get; set; }` |
| `string`, no presence | backing field + property whose getter returns `string.Empty` |
| `bytes`, no presence | `public ByteString Body { get; set; } = null!;` |
| message, no presence | `public Pet Owner { get; set; } = null!;` |
| enum | `public StatusEnum Status { get; set; }` |
| repeated | `public RepeatedField<T> Items { get; } = new();` |
| proto3 `optional` | backing value, `HasX`, `ClearX`, and a property that raises the flag |

Reference-typed fields default to `null`, not to an empty message. A message property that was never set is
`null`, and a `bytes` property that was never set is `null`; the generated writer checks for `null` before
writing either. That is why a nullable message field needs no `optional` label: absence already has a
representation.

Strings are the exception: `PET` with no name and `Pet { Name = "" }` are the same protobuf value, so the
property returns `string.Empty` rather than `null`, and a consumer never has to null-check a string.

## Presence, in code

A field the contract marked nullable gets the full proto3 `optional` treatment:

```csharp
private long _value_Age;
private bool _has_Age;

public bool HasAge => _has_Age;

public void ClearAge()
{
    _has_Age = false;
    _value_Age = default;
}

public long Age
{
    get { return _value_Age; }
    set { _value_Age = value; _has_Age = true; }
}
```

Writing `Age = 0` sets `HasAge`; never touching it leaves `HasAge` false. The writer emits the tag and a zero
when `HasAge` is true, which is the whole point of the label. `ClearAge` is the only way back to absent.

For a nullable `string` or `bytes`, the backing field is nullable and the getter falls back to the protobuf
default (`string.Empty` or `ByteString.Empty`), exactly as the non-optional shape does. `HasAge` still tells the
two apart.

## The repeated field

`RepeatedField<T>` is Google.Protobuf's own collection. It is mutable, it is never `null`, and assigning to it
is not possible — callers add, clear and index it. The generated class also carries a `FieldCodec<T>` per
repeated field, used by `MergeFrom` to decode packed or unpacked input:

```csharp
private static readonly global::Google.Protobuf.FieldCodec<Pet> _repeated_Pets_codec =
    global::Google.Protobuf.FieldCodec.ForMessage(Tag_4_Pets, Pet.Parser);
```

For enums the codec is built with explicit casts, because the generated enum is a plain C# enum rather than
protobuf's `INeedEnum`-style wrapper:

```csharp
global::Google.Protobuf.FieldCodec.ForEnum(Tag_3_Statuses, x => (int)x, x => (StatusEnum)x)
```

A plain enum is easier to use from application code and works in `switch` expressions. The cost is that the
enum has no descriptor and no `OriginalName`, which matters only to reflection-based tooling.

## The wire methods

`WriteTo`, `CalculateSize` and `MergeFrom` are the three methods gRPC's marshaller calls, and they are the
reason the class does not need reflection.

`WriteTo` writes the tag constant and then the value, skipping anything that is at its default:

```csharp
if (Id != 0) { output.WriteRawTag(8); output.WriteInt64(Id); }
if (Name.Length != 0) { output.WriteRawTag(18); output.WriteString(Name); }
if (Status != 0) { output.WriteRawTag(24); output.WriteInt32((int)Status); }
```

`CalculateSize` mirrors it with `CodedOutputStream.Compute…Size`, and the two must agree exactly or the
length-delimited envelope will be wrong. They are generated from the same field list, so they do.

`MergeFrom(CodedInputStream)` is a `while (ReadTag() != 0)` loop with a `switch` per tag number. A tag it does
not recognise is passed to `input.SkipLastField()`, which is the point noted in chapter 17: unknown fields are
not kept.

The parser is a static `MessageParser<T>` constructed once per type. The service class builds its marshallers
with `Grpc.Core.Marshallers.Create<T>` when its `static readonly` method descriptors are initialized, so no
marshaller is constructed per call and no reflection is involved.

## Equality, cloning and text

- `Equals` compares every field; for a presence field it compares the flag **and** the value, so
  `new Pet { Age = 0 }` is not equal to `new Pet()`. `GetHashCode` follows the same rule.
- `Clone` copies scalars, calls `Clone()` on a message property, and **adds the same item references** for a
  repeated message field. Cloning a message with repeated messages is therefore shallow at the element level.
- `ToString` is a hand-written `[Pet] { Id=…, Name=…, }` string, not protobuf's text format. It is for logs.
- `IsInitialized` is always `true`: proto3 has no required fields, so there is nothing to be uninitialized.

Messages are mutable. There is no `Freeze`, no builder, and no copy-on-write. A message shared between threads
and written concurrently is a data race like any other mutable object. The usual discipline applies: build a
request, send it, do not mutate it afterwards.

## The descriptor is null

`Descriptor => null!` is the most consequential line in the file. It means:

- **gRPC works.** `Grpc.Core` and `grpc-dotnet` marshal through `MessageParser<T>` and the three wire methods;
  neither asks for a descriptor.
- **`JsonFormatter` and `JsonParser` do not.** They walk `Descriptor`, and will throw.
- **Server reflection does not.** The gRPC reflection service enumerates descriptors; a service built from
  these messages cannot be discovered by `grpcurl` unless the `.proto` is supplied on the command line.
- **Rich error details do not compose.** `Google.Rpc.Status` and `Any` need type URLs and descriptors. The
  generated error wrapper is a plain `code`/`message` message for that reason (chapter 13).

If a tool needs descriptors, compile the emitted `{Service}Proto.Text` with `protoc` and use the generated
types there. The descriptor string exists precisely so that path is available; it is not used by the runtime.

## Extending a message

Because the class is `partial`, application code can add members in the same namespace:

```csharp
namespace MyApp.Generated;

public sealed partial class Pet
{
    public string DisplayName => $"{Name} ({Id})";
}
```

This is the supported way to add behaviour. Do not add fields that look like contract fields — the generator
will not know about them, they will not be serialized, and a future document change may collide with the name.

