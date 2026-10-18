# 4. Declaring a Contract

<p class="lead">A specification reaches the generator through an `AdditionalFiles` item; its name, namespace
and options come from MSBuild properties, per-file metadata, or an assembly attribute. This chapter describes
all three routes, the precedence between them, and how the three names a build needs are derived when you
configure nothing.</p>

## The content always comes from AdditionalFiles

The generator reads documents from Roslyn's `AdditionalTextsProvider`. There is no second way to supply the
text: an attribute names a file, but the file must still be an `AdditionalFiles` item in the project. This is
deliberate. `AdditionalFiles` is how a project tells the compiler "this is an input to an analyzer", it is
incremental and cached by Roslyn, and it works identically in `dotnet build`, Visual Studio and CI.

```xml
<ItemGroup>
  <AdditionalFiles Include="openapi/**/*.yaml" />
  <AdditionalFiles Include="openapi/**/*.json" />
</ItemGroup>
```

The supported extensions are `.yaml`, `.yml` and `.json`, compared case-insensitively. Any other
`AdditionalFiles` item is ignored by SharpPortico, so the same item group can carry files for other
analyzers.

## Route 1: MSBuild properties

The package ships a `.props` file that makes two properties and two per-file metadata names visible to the
compiler:

```xml
<PropertyGroup>
  <SharpPorticoServiceName>PetService</SharpPorticoServiceName>
  <SharpPorticoNamespace>MyApp.Generated</SharpPorticoNamespace>
</PropertyGroup>
```

`SharpPorticoServiceName` is the base name; the suffix rule in the next section applies to it. The namespace
is used verbatim for the generated C#, and the protobuf package is that namespace lower-cased.

The same values can be attached to one file, which is how several specifications in one project get different
names:

```xml
<AdditionalFiles Include="openapi/pets.yaml"
                 SharpporticoServiceName="PetService"
                 SharpporticoNamespace="MyApp.Pets" />
<AdditionalFiles Include="openapi/orders.yaml"
                 SharpporticoServiceName="OrderService"
                 SharpporticoNamespace="MyApp.Orders" />
```

Note the spelling: the MSBuild properties are `SharpPorticoServiceName` (capital P), while the item metadata
names are `SharpporticoServiceName` (lower-case p after the first word). The generator accepts exactly those
names; a typo silently falls back to the file name.

## Route 2: the assembly attribute

`SharpPortico.Abstractions` defines `OpenApiToGrpcAttribute`, which carries the file path, the service name,
the namespace, the four `Emit*` switches and every mapping option:

```csharp
using SharpPortico;

[assembly: OpenApiToGrpc(
    "openapi/petstore.yaml",
    "PetService",
    "SharpPortico.Samples.MinimalApi.Generated",
    EnableProxyGeneration = true)]
```

The file path is resolved against the project directory. The attribute is `Assembly`-scoped and
`AllowMultiple = true`, so one file can declare several specifications.

The attribute route requires a reference to the assembly that declares the attribute. In this repository the
samples use a `ProjectReference` to `src/SharpPortico.Abstractions`; a consumer using the NuGet package can
use the property route instead, which needs no compile-time reference at all.

## Using both, and what wins

The two routes are merged. The attribute work items come first; an `AdditionalFiles` item whose **file name**
matches an attribute's file name is dropped. The match is on the file name, not the full path, and it is
case-insensitive.

That is why the samples can declare both and still generate once:

```csharp
[assembly: OpenApiToGrpc("openapi/petstore.yaml", "PetService", "SharpPortico.Samples.MinimalApi.Generated")]
```

```xml
<AdditionalFiles Include="openapi/**/*.yaml" />
```

The attribute wins, which is what allows it to configure options that the `AdditionalFiles` route cannot
express. Two `AdditionalFiles` items with the same file name in different directories are **not** deduped
against each other; they produce two work items and two generated files with the same hint name, which
Roslyn rejects. Give such files distinct names, or declare them through attributes with distinct paths.

## The three derived names

When nothing is configured, the generator derives a service name, a namespace and a protobuf package.

| Name | Derivation | Example for `openapi/petstore.yaml` |
| --- | --- | --- |
| service | file name without extension, made PascalCase, then `Service` appended unless already present | `PetstoreService` |
| namespace | the service name when `SharpPorticoNamespace` is absent | `PetstoreService` |
| package | the namespace, lower-cased | `petstoreservice` |

The attribute route has one extra fallback: when the service name is omitted **and** the hint name is the
literal `Service`, the first word of `info.title` is used as the stem. The `AdditionalFiles` route always has
a file-stem hint, so it never reaches that fallback.

The suffix is configurable through `ServiceNameSuffix` (default `"Service"`), and the check is a plain
`EndsWith`, so `PetService` stays `PetService` rather than becoming `PetServiceService`.

## Several specifications in one project

Each work item produces its own `{hint}.g.cs` and `{hint}.proto.cs`, in its own namespace. There is no shared
registry between them, so two contracts may declare a message with the same name as long as their namespaces
differ.

The hint name is the file stem. Two specs named `openapi/v1/pets.yaml` and `openapi/v2/pets.yaml` both have the
hint `pets`, and the second `AddSource` call fails the build. Rename one file, or give the specs distinct file
names; the generated type names are unaffected by the file name once a service name is configured.

## A declaration checklist

| Question | Where the answer goes |
| --- | --- |
| Which document? | `AdditionalFiles` item, path relative to the project |
| What is the service called? | `SharpPorticoServiceName`, metadata, or the attribute's second argument |
| What namespace? | `SharpPorticoNamespace`, metadata, or the attribute's third argument |
| Which options? | attribute named arguments (chapter 5) |
| Several documents? | one item each, with distinct file names |
| JSON instead of YAML? | an `AdditionalFiles` item ending in `.json`; nothing else changes |

The next chapter is the option reference: every property the attribute exposes, its default, and whether the
generator reads it.

