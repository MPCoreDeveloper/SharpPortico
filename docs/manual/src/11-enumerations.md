# 11. Enumerations

<p class="lead">An OpenAPI `enum` becomes a protobuf enum, and the same members become a C# `enum`. The
mapping is direct enough that the interesting questions are naming, sharing and what happens when a document
declares two enumerations that look alike.</p>

## The mapping

```yaml
components:
  schemas:
    WidgetKind:
      type: string
      enum: [gear, sprocket]

    Widget:
      type: object
      properties:
        kind: { $ref: "#/components/schemas/WidgetKind" }
        state:
          type: string
          enum: [on, off]
```

```protobuf
enum WidgetKind
{
    GEAR = 0;
    SPROCKET = 1;
}

enum StateEnum
{
    ON = 0;
    OFF = 1;
}

message Widget
{
    WidgetKind kind = 14;
    StateEnum state = 26;
}
```

Three rules produced that output:

1. **A component enumeration keeps its name.** `WidgetKind` is a message-like type in the model and is
   referenced by `$ref`.
2. **An inline enumeration is named after its property plus `Enum`.** `state` becomes `StateEnum`.
3. **Members are PascalCase in C# and upper-case with underscores in proto.** `gear` → `Gear` / `GEAR`;
   `inProgress` → `InProgress` / `IN_PROGRESS`.

The C# side is a real `enum`, so a consumer writes `WidgetKind.Gear` and pays no allocation. The protobuf
side is an `int` on the wire. `MessageEmitter` casts at every boundary:

```csharp
output.WriteEnum(1, (int)Kind);
Kind = (WidgetKind)input.ReadEnum();
```

## Values and numbers

| Declared value | C# member | Proto member | Number |
| --- | --- | --- | --- |
| `available` | `Available` | `AVAILABLE` | 0 |
| `pending` | `Pending` | `PENDING` | 1 |
| `inProgress` (third member) | `InProgress` | `IN_PROGRESS` | 2 |
| `1` | `N1` | `N1` | 1 |
| `10` | `N10` | `N10` | 10 |

A string enumeration is numbered by declaration order starting at 0. A numeric enumeration keeps its numbers:
`enum: [0, 10, 20]` becomes `N0 = 0`, `N10 = 10`, `N20 = 20`. This preserves a contract that uses numbers as
stable codes, which is often the reason the enumeration is numeric in the first place.

proto3 requires the **first** member of an enum to have number 0. A numeric enumeration that starts at 1
(`[1, 2, 3]`) is emitted faithfully, as `edge.proto` shows:

```protobuf
enum Level
{
    N1 = 1;
    N2 = 2;
    N3 = 3;
}
```

The C# types compile and round-trip, but `protoc` rejects the descriptor, because the language requires a
zero value. This is one of the few places where the generated C# and the generated `.proto` disagree about
validity; the CLI and the build do not add a synthetic `UNSPECIFIED = 0`, because that would change the
contract's numbers. Add a zero member to the document if the descriptor has to compile.

## Sharing and collisions

Two inline enumerations with the same generated name and the same members are one type and are reused. This
is what a vocabulary shared by several schemas should do: `status` on `Order` and `status` on `Invoice` with
identical members produces one `StatusEnum`, and both fields reference it.

Two with the same name and different members are a conflict. The mapper first derives a distinct name from the
original plus the declared members (`State` + `Purged` → `StatePurged`); if that name is free, both
enumerations survive and no diagnostic is raised. If the derived name is already taken by a different member
set, the document is refused:

```text
error SP2007: SharpPortico cannot map 'openapi/things.yaml': the inline enumeration named 'StatusEnum' is
declared twice with different members ('available, pending' and 'sold, returned'). An inline enumeration is
named after the property declaring it, so two schemas with a property of that name collide - declare one of
them as a named schema under components
```

The remedy in the message is the right one: declare one of the two as a named component. That gives it a name
you chose, keeps both member sets, and removes the dependence on a naming heuristic.

## Nullability

A nullable enumeration is a singular scalar, so it takes proto3's `optional` label and gains presence members:

```protobuf
optional WidgetKind kind = 3;
```

```csharp
widget.HasKind;    // was it sent?
widget.Kind;       // the value, or the default (the first member)
widget.ClearKind();
```

A `$ref` to an enumeration behaves the same as an inline one, because the field kind is `Enum` after the
reference is resolved. A `$ref` to an object does not, because a message field already has presence. Chapter
12 gives the full rule.

## In the gateway

The proxy's generated JSON writer emits an enumeration as its **number**, not its name:

```csharp
sb.Append((int)msg.Kind);
```

and the reader calls `GetInt32()` before casting. A legacy service that expects `"gear"` in JSON will not
understand the gateway's `0`, and a response containing `"gear"` will fail to read. This is a real limitation
of proxy mode for enumerated fields, not an option; chapter 23 describes it with the rest of the JSON
mapping. If the legacy API's enumerations are strings, the gateway is not a drop-in bridge for those fields.

## What is not generated

- **No `UNRECOGNIZED` member.** protoc's C# output adds one; SharpPortico does not, because the contract did
  not declare it and every switch over the enum would need a case the document never mentioned.
- **No aliases.** Two members with the same number are legal proto3 only with `allow_alias`, which is not
  emitted. A numeric enumeration with duplicate numbers will not compile as a descriptor.
- **No description.** OpenAPI `description` on the enum or its members is not carried into the `.proto`.
- **No switch to plain C# enums.** `MapEnumsToProtobufEnums` exists on the attribute but is not read
  (chapter 5); enumerations are always protobuf enums.
