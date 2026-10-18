# 26. Symptom Index

<p class="lead">This chapter starts from what you see — a compiler error, an empty stream, a field that never
arrives — and works back to the rule that produced it. Every entry names the chapter that explains the cause in
full.</p>

## Build symptoms

| Symptom | Most likely cause | Chapter |
| --- | --- | --- |
| No generated types at all; the namespace does not exist | the spec is not an `AdditionalFiles` item, or the attribute names a path that does not exist | 4 |
| `warning SP1000` and no generated types | the file is missing, empty or invalid YAML/JSON; the warning carries the reader's message | 25 |
| `error SP2005` | a component schema collides with an operation-derived `{OperationId}Request`/`Response` | 25 |
| `error SP2006` / `CS0542` | a property has the same name as its enclosing message | 25 |
| `error SP2007` | two inline enums with different members land on the same derived name | 25 |
| `error SP2008` | a nullable property's `Has`/`Clear` member collides with a sibling property | 25 |
| `warning SP1002` | a 3.1 document was rewritten; this is expected | 6 |
| `warning SP1003` | a 3.1 construct was mapped with a loss; the message names it | 6 |
| `warning SP2000` | a nullable array or element cannot carry its nullability | 12 |
| `CS0111` in `{Service}Auth` | two security schemes of one kind emit the same helper name | 21 |
| `CS0103: SerializeStruct` / `ParseStruct` | the contract has a `Struct` or `Value` field and proxy generation is on | 23 |
| `CS0019` / `CS1061` in `{Service}Proxy` | a `bytes` or repeated scalar at the top level of a request | 23 |
| `CS0246: google.protobuf.Struct` | the generated message references a well-known type but `Google.Protobuf` is not referenced | 3 |
| `CS0246: {Service}Base` | `EmitServer=false`, but the contract still emits both `BindService` overloads | 3 |
| `AD0001` from the generator | an emit-stage failure; `SP3000` is declared but not raised in 1.5.0 | 25 |

The first row is worth checking before anything else. A source generator runs only over files the project gives
it: an `AdditionalFiles` item in the `.csproj`, or an `[assembly: OpenApiToGrpc("path")]` attribute. A file that
is merely present in the repository is not an input.

## Runtime symptoms

| Symptom | Most likely cause | Chapter |
| --- | --- | --- |
| `Unimplemented` from a unary call | the server implementation did not override `XAsync` | 19 |
| `Unimplemented` from a streaming call | a client- or bidi-streaming RPC was not overridden | 19 |
| A server-streaming call returns zero messages | the base default completes the stream; override `X` | 19 |
| `Unimplemented` from the proxy | the RPC is streaming; the proxy only overrides unary | 22 |
| An empty stream from the proxy | pagination made the RPC server-streaming, and the base default completes it | 22 |
| `Internal` with a JSON message | the proxy could not parse the response (enum as string, malformed JSON) | 23 |
| `Unknown` with an HTTP body | the legacy status did not map to a specific gRPC code | 22 |
| Stale data through the proxy | the GET response is cached for `CacheTtl`; send the bypass metadata | 22 |
| `Cancelled` instead of `OperationCanceledException` | gRPC reports cancellation as an `RpcException` | 20 |
| `ResourceExhausted` on a large message | the channel or server message-size limit, not the contract | 20 |
| A body field missing at the legacy service | the proxy only sends a message field named `Body`, as JSON | 22 |
| A query parameter has the wrong name | the proxy sends the C# field name (`PetId`), not the OpenAPI name (`petId`) | 22 |
| `CS0246` for an inline object type (for example `Dims`) | 1.5.0 names inline object messages but does not emit them | declare a named component and use `$ref` | 17 |
| A response property never fills | the JSON key is not the exact proto name (`pet_id`) | 23 |

## Two symptoms that look like generator bugs

**"The field is always zero."** Check the descriptor before the code. A field number that changed between the
client's generated code and the server's is a wire mismatch: both sides read a valid message, and each puts the
value in a different field. Regenerate both from the same document and the same version. This is why chapter 14
treats parameter order as a wire contract: appending is safe, reordering is not.

**"The service is there but grpcurl cannot see it."** The generated messages return `null` from `Descriptor`
(chapter 18), so gRPC server reflection has nothing to enumerate. Pass the `.proto` file to `grpcurl` explicitly;
the descriptor is available as `{Service}Proto.Text` and from `sharpportico generate --out`.

## A triage order

When the cause is not obvious, four steps find it faster than reading the generated file from the top.

1. **Read the first SharpPortico diagnostic, not the first C# error.** A refusal produces no code, and the C#
   errors that follow are consequences of a missing type. `SP2005`–`SP2008` explain the refusal; a C# error
   usually does not.
2. **Run the CLI on the same file.** If the CLI maps the contract and the build does not, the difference is an
   option: the build may set `RespectStreamingHints=false`, change the pagination names, or use a different
   service name. The CLI always uses the defaults (chapter 24).
3. **Look at the generated file.** Set `EmitCompilerGeneratedFiles` and read
   `obj/.../generated/SharpPortico.SourceGenerator/...`:

   ```xml
   <PropertyGroup>
     <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
     <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)generated</CompilerGeneratedFilesOutputPath>
   </PropertyGroup>
   ```

   The `.g.cs` is the code that compiles, and the `.proto.cs` is the descriptor. A type that is missing from the
   first is not a C# problem.
4. **Compile the descriptor.** Write it with `sharpportico generate --out` and run `protoc --descriptor_set_out`.
   A descriptor that `protoc` rejects has a mapping-level defect (an enum without a zero member, a duplicate
   field number) that the C# compiler cannot see.

## What to put in a report

If the cause is a generator defect rather than a contract problem, the smallest useful report contains:

- the document, or the smallest excerpt that still reproduces it;
- the version from `{Service}Proto.Text`'s generated-code attribute, or `dotnet list package`;
- the build options, because they change the mapping;
- the full text of the first `SP` diagnostic, if there is one;
- whether the failure is a refusal (no generated code) or a compile error in generated code.

The distinction in the last point decides which part of the generator is at fault: a refusal comes from the
parser or mapper, a compile error from an emitter. The two are fixed in different places, and a report that
conflates them sends the reader to the wrong file.

## When the answer is "that is the mapping"

Some symptoms are the documented behaviour and will not change:

- a `oneOf` loses its alternatives (chapter 9);
- a non-success response has no message (chapter 17);
- a `Struct` field breaks the proxy (chapter 23);
- the proxy sends query names in C# spelling (chapter 22);
- a repeated field cannot carry nullability (chapter 12).

For those, the fix is in the contract or in a hand-written adapter, not in a bug report. The rules are in
Part III precisely so that this answer can be given with a chapter number instead of a shrug.

