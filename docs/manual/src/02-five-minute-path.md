# 2. The Five-Minute Path

<p class="lead">This chapter takes an empty project and ends with a gRPC server answering a generated client,
using the petstore contract that ships in `samples/`. Every file shown is a file the sample actually has.</p>

## 1. The project

The generator is a NuGet package and a compiler extension, so a normal SDK project is enough. `net10.0` is
used here; the generator itself targets `netstandard2.0` and loads into the compiler on either supported
runtime.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="SharpPortico.SourceGenerator" Version="1.5.0" PrivateAssets="all" />
    <PackageReference Include="Google.Protobuf" />
    <PackageReference Include="Grpc.Core" />
    <PackageReference Include="Grpc.Core.Api" />
    <PackageReference Include="Grpc.Net.Client" />
  </ItemGroup>

  <ItemGroup>
    <AdditionalFiles Include="openapi/**/*.yaml" />
  </ItemGroup>
</Project>
```

The `<AdditionalFiles>` item is not optional. It is where the generator reads the specification from, and it
is the item type Roslyn provides for exactly this purpose. A file that is not an `AdditionalFiles` item is
invisible to the generator, even when it sits next to the project.

## 2. The contract

`samples/MinimalApiExample/openapi/petstore.yaml` is deliberately small: three operations, one schema, one
inline enumeration.

```yaml
openapi: "3.0.3"
info:
  title: Petstore
  version: "1.0.0"
paths:
  /pets:
    get:
      operationId: listPets
      parameters:
        - name: limit
          in: query
          schema: { type: integer }
        - name: page
          in: query
          schema: { type: integer }
      responses:
        "200":
          description: paged pets
          content:
            application/json:
              schema:
                type: array
                items: { $ref: "#/components/schemas/Pet" }
    post:
      operationId: createPet
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: "#/components/schemas/Pet" }
      responses:
        "201":
          description: created pet
          content:
            application/json:
              schema: { $ref: "#/components/schemas/Pet" }
  /pets/{petId}:
    get:
      operationId: getPet
      parameters:
        - name: petId
          in: path
          required: true
          schema: { type: integer, format: int64 }
      responses:
        "200":
          description: a pet
          content:
            application/json:
              schema: { $ref: "#/components/schemas/Pet" }
components:
  schemas:
    Pet:
      type: object
      properties:
        id: { type: integer, format: int64 }
        name: { type: string }
        status:
          type: string
          enum: [available, pending, sold]
```

## 3. The name

With no configuration the service is named after the file (`petstore` → `PetstoreService`) and its
namespace is that name. To name it explicitly, set two MSBuild properties; the package's `.props` file makes
them visible to the compiler.

```xml
<PropertyGroup>
  <SharpPorticoServiceName>PetService</SharpPorticoServiceName>
  <SharpPorticoNamespace>MyApp.Generated</SharpPorticoNamespace>
</PropertyGroup>
```

Alternatively, when the project references `SharpPortico.Abstractions` (as the repository's samples do), an
assembly attribute carries the same three values and every mapping option:

```csharp
using SharpPortico;

[assembly: OpenApiToGrpc(
    "openapi/petstore.yaml",
    "PetService",
    "SharpPortico.Samples.MinimalApi.Generated")]
```

The attribute is `Assembly`-scoped and `AllowMultiple = true`; several specifications can be declared in one
project. The file path is resolved against the project directory, and the content still comes from the
`AdditionalFiles` item — the attribute customizes generation, it does not embed the document.

## 4. Build

```bash
dotnet build
```

Two files appear in the compiler's generated-output directory, and they are the whole story:

```text
obj/Debug/net10.0/generated/SharpPortico.SourceGenerator/
  SharpPortico.Generator.SharpPorticoGenerator/
    petstore.g.cs        messages, service, client, server base, DI
    petstore.proto.cs    PetServiceProto.Text, the descriptor as a const
```

The directory name is Roslyn's, not SharpPortico's: `<generator assembly>/<generator type>`. Chapter 3
explains exactly what each file contains and how to get a `.proto` file out of the build.

## 5. The implementation

The server side is one class. Only the methods you care about are overridden; the rest inherit the
generated `Unimplemented` behaviour.

```csharp
using Grpc.Core;
using SharpPortico.Samples.MinimalApi.Generated;

internal sealed class PetServiceImpl : PetServiceBase
{
    private readonly Dictionary<long, Pet> _pets = new()
    {
        [1] = new Pet { Id = 1, Name = "Rex", Status = StatusEnum.Available },
        [2] = new Pet { Id = 2, Name = "Milo", Status = StatusEnum.Pending }
    };

    public override Task<GetPetResponse> GetPetAsync(GetPetRequest request, ServerCallContext context)
    {
        if (!_pets.TryGetValue(request.PetId, out var pet))
            throw new RpcException(new Status(StatusCode.NotFound, $"pet {request.PetId} not found"));
        return Task.FromResult(new GetPetResponse { Data = pet });
    }

    public override Task<CreatePetResponse> CreatePetAsync(CreatePetRequest request, ServerCallContext context)
    {
        if (request.Body is null) throw new RpcException(new Status(StatusCode.InvalidArgument, "body required"));
        _pets[request.Body.Id] = request.Body;
        return Task.FromResult(new CreatePetResponse { Data = request.Body });
    }

    public override Task<ListPetsResponse> ListPetsAsync(ListPetsRequest request, ServerCallContext context)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var limit = request.Limit <= 0 ? 20 : request.Limit;
        var response = new ListPetsResponse();
        response.Items.Add(_pets.Values.OrderBy(p => p.Id).Skip((page - 1) * limit).Take(limit));
        return Task.FromResult(response);
    }
}
```

Two details are worth noticing now, because they are the mapping in miniature.

- `GetPetRequest.PetId` is a `long`, because the parameter declared `format: int64`. It is one member of
  the request message, and the path template it came from is recorded on the method descriptor.
- `ListPetsResponse.Items` is a `RepeatedField<Pet>`, and `CreatePetRequest.Body` is a `Pet`. Array responses
  become repeated fields on a wrapper; a `$ref` body becomes a message-typed field named `body`.

## 6. The host

`Grpc.Core.Server` is enough for a console host and keeps the example honest: the generated code has no
hosting dependency of its own.

```csharp
using Grpc.Core;
using Grpc.Net.Client;
using SharpPortico.Samples.MinimalApi.Generated;

const int Port = 50051;

var server = new Server
{
    Services = { PetService.BindService(new PetServiceImpl()) },
    Ports = { new ServerPort("localhost", Port, ServerCredentials.Insecure) }
};
server.Start();

using var channel = GrpcChannel.ForAddress($"http://localhost:{Port}");
var client = PetServiceClient.Create(channel);

var pet = await client.GetPetAsync(1);                 // scalar overload: long petId
Console.WriteLine($"{pet.Data?.Name} ({pet.Data?.Status})");

var created = await client.CreatePetAsync(new Pet { Id = 3, Name = "Luna", Status = StatusEnum.Sold });
Console.WriteLine($"created {created.Data?.Id}");

var page = await client.ListPetsAsync(new ListPetsRequest { Limit = 1, Page = 1 });
Console.WriteLine($"{page.Items.Count} pet(s)");

await server.ShutdownAsync();
```

`PetServiceClient.Create(channel)` is the convenience client. It is a `sealed partial class` deriving from
the nested `PetService.PetServiceClient`, and it adds `Task<T>`-returning methods plus overloads that take
the interesting parameters directly. The nested client from `Grpc.Core` remains available when you need
`AsyncUnaryCall<T>`, `CallOptions` or per-call headers.

## 7. If something is missing

| Symptom | Cause | Fix |
| --- | --- | --- |
| no generated file at all | the YAML is not an `AdditionalFiles` item | add `<AdditionalFiles Include="openapi/**/*.yaml" />` |
| `SP1000` warning and no output | wrong path, empty file, or invalid YAML | check the path relative to the project, then the syntax |
| service named `PetstoreService` when you wanted `PetService` | no name configured | set `SharpPorticoServiceName`, or use the attribute |
| generated types are in an unexpected namespace | no namespace configured | set `SharpPorticoNamespace` |
| `CS0246` for `OpenApiToGrpc` | the attribute's assembly is not referenced | use the MSBuild properties, or reference `SharpPortico.Abstractions` |
| types appear twice in IntelliSense | the generator was referenced twice | reference the package once; do not also add `<Analyzer Include="...SharpPortico.SourceGenerator.dll" />` |

Diagnostics are covered in chapters 25 and 26. The next chapter is the inventory of what the build wrote.

