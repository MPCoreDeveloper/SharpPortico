# 3. What the Build Writes

<p class="lead">A generator that writes code into your compilation owes you an inventory. This chapter lists
every file a SharpPortico build produces, where it lands, what is inside it, and which switch removes it.</p>

## Generated source is in memory by default

A Roslyn source generator adds syntax trees to the compilation; it does not have to write files. By default
the trees exist only for the duration of the build, which is why a clean project can use the generated types
in IntelliSense without a `Generated/` folder on disk.

To look at them, ask the compiler to persist them:

```bash
dotnet build -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=obj/generated
```

The path is then:

```text
obj/generated/SharpPortico.SourceGenerator/
  SharpPortico.Generator.SharpPorticoGenerator/
    petstore.g.cs
    petstore.proto.cs
```

Both segments are Roslyn's convention — the generator assembly name, then the generator type's full name —
not names SharpPortico chooses. Set the property in a `.props` file or on the command line; committing the
folder is not necessary and not recommended, because the files are rebuilt from the document on every build.

## The two files

For `openapi/petstore.yaml`, every generated file's hint name comes from the specification's file name without
its extension: `petstore`. The extension says what the file is.

| File | Contains | Controlled by |
| --- | --- | --- |
| `petstore.g.cs` | messages, enums, service definition, server base, client, DI, auth, proxy | the four `Emit*` switches |
| `petstore.proto.cs` | `PetServiceProto.Text`, a `const string` holding the proto3 descriptor | `EmitProtoFile` (default `true`) |

There is no third file, and there is no `.proto` file on disk. The descriptor is emitted as a C# constant so
that it is part of the compilation — trimmable, inspectable, and impossible to get out of step with the
messages. `ProtoEmitter` produces the text; `CodeEmitter` escapes it into a string literal and names the
class `{Service}Proto`.

```csharp
namespace MyApp.Generated
{
    public static partial class PetServiceProto
    {
        /// <summary>The .proto descriptor for the generated service.</summary>
        public const string Text = "syntax = \"proto3\";\n\npackage myapp.generated;\n...";
    }
}
```

## Getting a real `.proto` file

Two routes produce the same bytes.

**At run time**, write the constant out:

```csharp
await File.WriteAllTextAsync("petstore.proto", PetServiceProto.Text);
```

**Before a build**, use the CLI. It runs the same parser and the same emitter, so the file is what the const
will hold:

```bash
sharpportico generate openapi/petstore.yaml \
  --service-name PetService \
  --namespace MyApp.Generated \
  --out contracts/
```

The result is `contracts/petstore.proto` (the hint name, as `--out` names a directory) or exactly the path
given when `--out` ends in `.proto`. Chapter 24 covers the command in full. CI in this repository does the
second: it writes each sample's descriptor and compiles it with `protoc`, which is how the claim "the
descriptor is real protobuf" is tested rather than asserted.

## A generated file, in order

`petstore.g.cs` is one namespace block containing types in a fixed order. The order is stable, which makes
diffs between two builds meaningful.

1. **Enums.** One `public enum` per OpenAPI enumeration, members in declaration order.
2. **Messages.** Component schemas first, then request and response wrappers in operation order, then inline
   nested messages as they are encountered.
3. **Service.** `static partial class PetService` with descriptors and `BindService`.
4. **Server base.** `abstract partial class PetServiceBase`.
5. **Convenience client.** `sealed partial class PetServiceClient`.
6. **DI.** `static class ServiceCollectionExtensions`.
7. **Auth.** `PetServiceAuth` and `PetServiceAuthInterceptor`, when the document declares security schemes.
8. **Proxy.** `PetServiceProxy`, when proxy generation is enabled.

Every generated class — messages, service, server base, client, DI, authentication and proxy — carries
`[GeneratedCode("SharpPortico", "1.5.0")]`; enums do not. The version is read from the generator assembly
at build time, so a stack trace or a committed file says which generator produced it. Do not edit generated
code: the next build replaces it, and the replacement is not a merge.

## What is *not* written

| You might expect | Reality |
| --- | --- |
| a `.csproj` change per schema | none; the project is untouched |
| a `.proto` file compiled by `protoc` | no; the descriptor is a string, and the message classes are emitted directly |
| a `Generated/` folder by default | no; generated trees live in memory unless `EmitCompilerGeneratedFiles` is set |
| reflection metadata or a descriptor registry | no; `IMessage<T>` is implemented by hand, and the gateway's JSON code is generated per message |
| one file per message | no; one `.g.cs` per specification |

## Which switch removes what

| Option | Default | Effect on the output |
| --- | --- | --- |
| `EmitProtoFile` | `true` | `false` removes `{spec}.proto.cs`; the C# types are unaffected |
| `EmitClient` | `true` | `false` removes the convenience `{Service}Client`; the nested `{Service}.{Service}Client` from `Grpc.Core` remains. The DI extension still constructs the convenience client, so `EmitDependencyInjection` must be `false` too |
| `EmitServer` | `true` | `false` removes `{Service}Base`; the `BindService` overloads remain and reference it, so the generated file does not compile |
| `EmitDependencyInjection` | `true` | `false` removes `ServiceCollectionExtensions` |

The four switches are read from the attribute. The `AdditionalFiles` route always emits all four, because
that route has no attribute to carry the values; use the attribute when you need to turn one off.

## Why the build is reproducible

The generator is incremental. Its inputs are the specification's text, the work item's options and the
generator version; its output is a function of those alone. Nothing reads the clock, the machine name, the
current directory or a random seed. Field numbers follow declaration order, message names follow the rules in
chapter 8, and the descriptor is emitted with `\n` line endings regardless of the host.

That is also why the version is part of the output: two builds of the same document with different generator
versions can legitimately differ, and the `[GeneratedCode]` attribute makes that difference visible instead
of leaving a reviewer to guess.

The next part turns to the input side: how a contract is declared and which options actually change it.

