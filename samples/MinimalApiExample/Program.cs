using Grpc.Core;
using Grpc.Net.Client;
using SharpPortico.Samples.MinimalApi.Generated;
using SharpPortico.Samples.MinimalApiExample;

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