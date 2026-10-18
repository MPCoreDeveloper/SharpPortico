# 28. A Worked Example End to End

<p class="lead">This chapter follows one small contract from YAML to a running call, naming the rule behind
every line. The document is `fixtures/petstore.yaml`; the descriptor is what 1.5.0 produced from it.</p>

## The document

```yaml
openapi: "3.0.3"
info: { title: Petstore, version: "1.0.0" }
paths:
  /pets:
    get:
      operationId: listPets
      parameters:
        - { name: limit, in: query, schema: { type: integer } }
        - { name: page,  in: query, schema: { type: integer } }
      responses:
        "200":
          content:
            application/json:
              schema:
                type: array
                items: { $ref: "#/components/schemas/Pet" }
    post:
      operationId: createPet
      requestBody:
        content:
          application/json:
            schema: { $ref: "#/components/schemas/Pet" }
      responses:
        "201":
          content:
            application/json:
              schema: { $ref: "#/components/schemas/Pet" }
  /pets/{petId}:
    get:
      operationId: getPet
      parameters:
        - { name: petId, in: path, required: true, schema: { type: integer, format: int64 } }
      responses:
        "200":
          content:
            application/json:
              schema: { $ref: "#/components/schemas/Pet" }
components:
  schemas:
    Pet:
      type: object
      properties:
        id:     { type: integer, format: int64 }
        name:   { type: string }
        status: { type: string, enum: [available, pending, sold] }
```

Nothing here is unusual, which is the point: the rules have to work on the ordinary case before the exotic ones
matter.

## What each rule does

**Operations become RPCs.** Three operations, three unary RPCs. The names are `SanitizePascal(operationId)`:
`listPets` → `ListPets`, `createPet` → `CreatePet`, `getPet` → `GetPet` (chapter 13). The methods keep their HTTP
verbs only in the report, not in the descriptor.

**Parameters become fields in declaration order.** `limit` is field 1, `page` is field 2. Their order is the
wire contract; a later revision may append a parameter, not insert one (chapter 14).

**The body comes last.** `CreatePetRequest` has one field, `body = 1`, because there are no parameters
(chapter 14). Its type is `Pet`, because the media type's schema is a `$ref`.

**The array response becomes a wrapper with `items`.** A `200` whose schema is an array produces
`ListPetsResponse { repeated Pet items = 1; }`. The element is a `$ref`, so no inline element message is created
(chapter 10).

**The object response becomes `data`.** A response whose schema is a `$ref` produces
`{ Pet data = 1; }`, not a bare `Pet` return. Every RPC returns a message (chapter 13).

**The inline enum becomes `StatusEnum`.** The property `status` declares the members; the enum is named after the
property plus `Enum`, and members are sanitised to PascalCase. `available` is 0, so the descriptor is valid proto3
(chapter 11).

**`int64` stays `long`.** A `format: int64` is not turned into a string. The JSON gateway serializes it as a number
regardless of protojson's convention (chapters 7 and 23).

**`201` is the success response.** It is the only 2xx, so it is the one mapped. A `default` or a `4xx` would not
add a field (chapter 17).

## The descriptor

```protobuf
syntax = "proto3";

package petstoreservice;

option csharp_namespace = "PetstoreService";

enum StatusEnum
{
    AVAILABLE = 0;
    PENDING = 1;
    SOLD = 2;
}

message Pet
{
    int64 id = 1;
    string name = 2;
    StatusEnum status = 3;
}

message ListPetsRequest
{
    int32 limit = 1;
    int32 page = 2;
}

message ListPetsResponse
{
    repeated Pet items = 1;
}

message CreatePetRequest
{
    Pet body = 1;
}

message CreatePetResponse
{
    Pet data = 1;
}

message GetPetRequest
{
    int64 pet_id = 1;
}

message GetPetResponse
{
    Pet data = 1;
}

service PetstoreService
{
    rpc ListPets (ListPetsRequest) returns (ListPetsResponse);
    rpc CreatePet (CreatePetRequest) returns (CreatePetResponse);
    rpc GetPet (GetPetRequest) returns (GetPetResponse);
}
```

The package and namespace are lower/pascal versions of the service name because no explicit values were given.
`petId` became `pet_id`; `limit` and `page` kept their names. `ListPets` is the only RPC with two request
fields, so it is the only one without a scalar convenience overload.

The CLI reports the same document in one screen:

```text
spec:       docs/manual/fixtures/petstore.yaml
service:    PetstoreService
namespace:  PetstoreService
package:    petstoreservice
rpc:        PetstoreService (3)
  ListPets - GET /pets (Unary)
  CreatePet - POST /pets (Unary)
  GetPet - GET /pets/{petId} (Unary)
messages:   7
enums:      1
```

Seven messages: `Pet`, three request wrappers, three response wrappers. One enum. Had `listPets` declared
`page` and `limit` but no `next_page_token`, it would still be unary; pagination detection only changes the kind
when a cursor or next-page-token parameter is present (chapter 13).

## The implementation and the call

```csharp
internal sealed class PetServiceImpl : PetstoreServiceBase
{
    private readonly Dictionary<long, Pet> _pets = new();

    public override Task<GetPetResponse> GetPetAsync(GetPetRequest request, ServerCallContext context)
    {
        if (!_pets.TryGetValue(request.PetId, out var pet))
            throw new RpcException(new Status(StatusCode.NotFound, $"pet {request.PetId} not found"));
        return Task.FromResult(new GetPetResponse { Data = pet });
    }

    public override Task<CreatePetResponse> CreatePetAsync(CreatePetRequest request, ServerCallContext context)
    {
        _pets[request.Body.Id] = request.Body;
        return Task.FromResult(new CreatePetResponse { Data = request.Body });
    }

    public override Task<ListPetsResponse> ListPetsAsync(ListPetsRequest request, ServerCallContext context)
    {
        var response = new ListPetsResponse();
        response.Items.Add(_pets.Values.OrderBy(p => p.Id).Skip((request.Page - 1) * request.Limit)
            .Take(request.Limit));
        return Task.FromResult(response);
    }
}
```

`request.Body` is non-null by convention, not by validation: a client can send `CreatePetRequest` with no body,
and the handler must check it if that matters. `Data` is a nullable message field; a response that leaves it
null is legal protobuf and a consumer must handle it.

The client side is where the mapping becomes ergonomic:

```csharp
using var channel = GrpcChannel.ForAddress("http://localhost:50051");
var client = PetstoreServiceClient.Create(channel);

var created = await client.CreatePetAsync(new CreatePetRequest
{
    Body = new Pet { Id = 3, Name = "Luna", Status = StatusEnum.Sold }
});

var fetched = await client.GetPetAsync(3);            // long → GetPetResponse
var name = (await client.GetPetDataAsync(3)).Name;    // long → Pet
var pets = await client.ListPetsAsync(new ListPetsRequest { Limit = 20, Page = 1 });
```

`GetPetAsync(3)` exists because `GetPetRequest` has one field; `GetPetDataAsync(3)` exists because
`GetPetResponse` has one field too. `ListPetsAsync` has no scalar overload because its request has two fields,
and its response field `Items` is repeated, so a `ListPetsItemsAsync` overload would hand back the whole
`RepeatedField<Pet>` rather than an element.

## What this example does not exercise

It has no `oneOf`, no `Struct`, no binary body, no pagination token, no security scheme and no proxy. Each of
those changes one part of the surface and is covered where it belongs: chapters 9, 15, 16, 13, 21 and 22. The
value of starting here is that the ordinary path is fully specified, so a surprise later can be attributed to
the construct that caused it rather than to the mapping as a whole.

