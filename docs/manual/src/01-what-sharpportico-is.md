# 1. What SharpPortico Is

<p class="lead">SharpPortico is a Roslyn incremental source generator that reads an OpenAPI 3.0 or 3.1
document while your project compiles and writes a complete gRPC surface for it: protobuf message classes,
a service definition, a server base class, a typed client, authentication helpers and — when enabled — a
gateway that serves the gRPC contract by calling the REST service the document describes.</p>

The input is a document you already maintain. The output is ordinary C# in your compilation: no `.proto`
file has to be generated and compiled by `protoc`, no plugin runs outside MSBuild, and nothing is reflected
over at run time. A build with the generator referenced behaves exactly like a build without it, except that
several thousand lines of contract code appear in it.

## The problem it removes

A REST contract is a description of HTTP: paths, verbs, query parameters, status codes, JSON bodies. A gRPC
consumer needs something else: messages with numbered fields, one method per operation, a channel, and a
client. Converting one to the other by hand has three predictable failure modes.

- **Drift.** The document changes and the hand-written `.proto` does not. The drift is invisible until a
  field arrives with the wrong number or an operation has no method.
- **Judgement calls made twice.** Is `format: int64` a `long` or a string? Is `nullable: true` still
  nullable on the wire? Every team answers these differently unless the answer is written down in code.
- **A toolchain to operate.** `protoc`, a gRPC plugin, a code generator per language, and a build step
  that has to run before the C# compiler. Each is another thing that can be missing from a clean checkout.

SharpPortico answers all three by making the mapping part of the compiler. The generator runs where the C#
compiler runs, with the same inputs and the same cancellation, and emits the same files every time for the
same document and version. The mapping rules are not configuration: they are code in
`src/SharpPortico.SourceGenerator/Mapping`, and Part III of this manual is their specification.

## The pipeline

```
openapi/petstore.yaml
        │  AdditionalFiles item, or [assembly: OpenApiToGrpc(...)]
        ▼
OpenApi31Downgrade ── rewrite 3.1 constructs a 3.0 reader cannot parse
        ▼
OpenApiParser ──────── paths → RPCs, schemas → messages and enums
        │                (SchemaMapper owns types, shapes and presence)
        ▼
GrpcModel ──────────── an immutable intermediate representation
        │
        ├── ProtoEmitter   → {Service}Proto.Text (a proto3 descriptor as a C# const)
        ├── MessageEmitter → protobuf messages implementing IMessage<T>
        ├── ServiceEmitter → service definition, server base, typed client, DI, auth
        └── ProxyEmitter   → {Service}Proxy : {Service}Base (optional)
        ▼
obj/.../generated/.../{spec}.g.cs and {spec}.proto.cs
```

The intermediate model matters to you for one reason: every emitter reads the same model, so the descriptor,
the C# messages and the gateway cannot disagree about a field's type or number. The CLI runs the same parser
and the same `ProtoEmitter`, which is why `sharpportico generate` can print the descriptor before anything is
built.

## What a build produces

For a specification named `openapi/petstore.yaml`, with the service name `PetService` and the namespace
`MyApp.Generated`:

| Generated type | Kind | Purpose |
| --- | --- | --- |
| `Pet`, `ListPetsRequest`, `ListPetsResponse`, … | `sealed partial class` | protobuf messages, hand-written and reflection-free |
| `StatusEnum`, … | `enum` | protobuf enumerations, emitted as C# enums |
| `PetService` | `static partial class` | method descriptors, `BindService`, marshallers |
| `PetService.PetServiceClient` | nested class | the `Grpc.Core.ClientBase` client |
| `PetServiceBase` | `abstract partial class` | overridable server methods with `Unimplemented` defaults |
| `PetServiceClient` | `sealed partial class` | C# 14 convenience client with typed overloads |
| `ServiceCollectionExtensions` | `static class` | `AddSharpPorticoPetService()` |
| `PetServiceAuth`, `PetServiceAuthInterceptor` | `static class`, `sealed class` | only when `securitySchemes` exist |
| `PetServiceProxy` | `sealed partial class` | only when `EnableProxyGeneration = true` |
| `PetServiceProto` | `static partial class` | `public const string Text`, the `.proto` descriptor |

The full member-by-member list is chapter 27. What matters here is the shape of the result: the messages and
the service are the contract, and everything else is convenience built on top of them.

## What it deliberately does not do

A manual is only useful if it says where the tool stops.

- **It does not serve HTTP.** The generated code is a gRPC server and client. There is no ASP.NET Core
  endpoint, no JSON transcoding and no `google.api.http` annotation. Hosting is the job of `Grpc.Core` or
  `Grpc.AspNetCore`, as chapter 19 shows.
- **It does not read a `.proto` file.** The descriptor is an output, not an input. If you already have
  protobuf contracts, this is the wrong direction.
- **It does not generate OpenAPI.** The document is the source of truth; the generator never writes one.
- **It does not call `protoc`.** The message classes are emitted directly. `protoc` is used in this
  repository's CI to prove that the descriptor is valid, not to produce code.
- **It does not preserve every JSON Schema keyword.** Validation keywords such as `minLength`, `pattern`,
  `minimum` and `maxItems` are documentation. They do not become protobuf fields, because protobuf has no
  place to put them. Chapter 17 lists every such loss.
- **It does not make a legacy REST API into a gRPC API by itself.** Proxy mode does that at run time by
  translating each call back into HTTP; the legacy service stays the implementation and keeps its
  semantics, including its latency.

## The three claims

The manual assumes three properties, and each one is checked by CI rather than asserted here.

| Claim | What proves it |
| --- | --- |
| **Reflection-free and NativeAOT-safe** | `samples/NativeAotExample` is published with `PublishAot`, the published image must contain no managed assembly, and the binary must print `SMOKE TEST PASSED` |
| **The descriptor is real protobuf** | every sample's `{Service}Proto.Text` is written out and compiled by `protoc` in `.github/scripts/check-protos.sh` |
| **The package works as installed** | CI packs the NuGet package, consumes it from a throwaway project, and runs the packed CLI on both target frameworks |

Reflection-free is not a slogan about style. The generated messages implement `IMessage<T>` themselves:
`CalculateSize`, `WriteTo`, `MergeFrom`, `Clone`, `Equals` and `GetHashCode` are written as code, and the
gateway's JSON reader and writer are generated per message. That is what allows a trimmed or
ahead-of-time-compiled service to run without a descriptor registry, and it is why the gateway does not use
`System.Text.Json`'s reflection-based serializer.

## Where it fits

SharpPortico is a good fit when:

- you have an OpenAPI document that is already the contract between teams, and
- you want gRPC consumers — internal services, mobile clients, an AOT service — without maintaining a second
  contract, and
- you can accept the mapping rules in Part III as the contract's protobuf meaning.

It is a poor fit when the document is generated from code you can change freely (change the code instead),
when the API's semantics depend on HTTP details protobuf cannot carry (caching headers, content negotiation,
`multipart/form-data`), or when you need bidirectional compatibility with an existing `.proto` that was
written by hand — in that case the existing `.proto` is the contract, and a mapping tool would only be a
second opinion.

The next chapter builds the smallest working service, so that the rest of the manual has something concrete
to refer to.

