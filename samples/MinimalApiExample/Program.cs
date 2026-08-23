using Grpc.Core;
using Grpc.Net.Client;
using SharpPortico.Samples.MinimalApi.Generated;

// Minimal API example: a .NET Minimal HTTP API (ASP.NET Core) exposing the
// SharpPortico-generated PetService over HTTP while the gRPC service runs
// in-process. Demonstrates how the generated client can be composed with
// higher-level HTTP APIs.

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddConsole();
var app = builder.Build();

// 1) Start the generated gRPC service in-process.
const int GrpcPort = 50052;
var server = new Server
{
    Services = { PetService.BindService(new PetServiceImpl()) },
    Ports = { new ServerPort("localhost", GrpcPort, ServerCredentials.Insecure) }
};
server.Start();

// 2) Generated C# 14 client over a GrpcChannel.
using var channel = GrpcChannel.ForAddress($"http://localhost:{GrpcPort}");
var client = PetServiceClient.Create(channel);

// 3) HTTP endpoints backed by the gRPC client.
app.MapGet("/pets/{petId:long}", async (long petId) =>
{
    var response = await client.GetPetAsync(petId);
    return response.Data is null ? Results.NotFound() : Results.Ok(response.Data);
});

app.MapPost("/pets", async (Pet pet) =>
{
    var response = await client.CreatePetAsync(new CreatePetRequest { Body = pet });
    return Results.Created($"/pets/{response.Data?.Id}", response.Data);
});

app.MapGet("/pets", (int limit = 20, int page = 1) =>
{
    var response = client.ListPetsAsync(new ListPetsRequest { Limit = limit, Page = page }).GetAwaiter().GetResult();
    return Results.Ok(response.Items);
});

try
{
    await app.RunAsync("http://localhost:5080");
}
finally
{
    await server.ShutdownAsync();
}

/// <summary>In-memory implementation of the generated PetServiceBase.</summary>
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