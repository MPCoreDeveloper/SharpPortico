using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using SharpPortico.Samples.Generated;

namespace SharpPortico.Samples.GrpcServerExample;

/// <summary>
/// In-memory implementation of the generated PetServiceBase.
/// </summary>
internal sealed class PetServiceImpl : PetServiceBase
{
    private readonly Dictionary<long, Pet> _pets = new();

    public PetServiceImpl()
    {
        _pets[1] = new Pet { Id = 1, Name = "Rex", Status = StatusEnum.Available };
        _pets[2] = new Pet { Id = 2, Name = "Milo", Status = StatusEnum.Pending };
    }

    public override Task<ListPetsResponse> ListPetsAsync(ListPetsRequest request, ServerCallContext context)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var limit = request.Limit <= 0 ? 20 : request.Limit;
        var response = new ListPetsResponse();
        response.Items.Add(_pets.Values.OrderBy(p => p.Id).Skip((page - 1) * limit).Take(limit));
        return Task.FromResult(response);
    }

    public override Task<CreatePetResponse> CreatePetAsync(CreatePetRequest request, ServerCallContext context)
    {
        if (request.Body is null) throw new RpcException(new Status(StatusCode.InvalidArgument, "body required"));
        _pets[request.Body.Id] = request.Body;
        return Task.FromResult(new CreatePetResponse { Data = request.Body });
    }

    public override Task<GetPetResponse> GetPetAsync(GetPetRequest request, ServerCallContext context)
    {
        if (!_pets.TryGetValue(request.PetId, out var pet))
            throw new RpcException(new Status(StatusCode.NotFound, $"pet {request.PetId} not found"));
        return Task.FromResult(new GetPetResponse { Data = pet });
    }
}

internal static class Program
{
    private const int Port = 50051;

    public static async Task<int> Main()
    {
        // ---- Server: bind the generated service definition ----
        var server = new Server
        {
            Services = { PetService.BindService(new PetServiceImpl()) },
            Ports = { new ServerPort("localhost", Port, ServerCredentials.Insecure) }
        };
        server.Start();
        Console.WriteLine($"gRPC server listening on {Port}");

        // ---- Client: generated C# 14 client over a GrpcChannel ----
        using var channel = GrpcChannel.ForAddress($"http://localhost:{Port}");
        var client = PetServiceClient.Create(channel);

        // Unary with scalar overload: GetPetAsync(long petId, ct) -> GetPetResponse
        var petResponse = await client.GetPetAsync(1);
        Console.WriteLine($"GetPet(1) -> {petResponse.Data?.Name} (status {petResponse.Data?.Status})");

        // Unary with request object + collection expression
        var created = await client.CreatePetAsync(new CreatePetRequest
        {
            Body = new Pet { Id = 3, Name = "Luna", Status = StatusEnum.Sold }
        });
        Console.WriteLine($"CreatePet -> id={created.Data?.Id} {created.Data?.Name}");

        // Unary with pagination request object
        var pets = await client.ListPetsAsync(new ListPetsRequest { Limit = 1, Page = 1 });
        Console.WriteLine($"ListPets(limit=1, page=1) -> {pets.Items.Count} pet(s)");

        // Serialization round-trip through the generated protobuf messages
        var pet = petResponse.Data!;
        var bytes = pet.ToByteArray();
        var roundTrip = Pet.Parser.ParseFrom(bytes);
        Console.WriteLine($"Round-trip OK: name={roundTrip.Name}, status={roundTrip.Status}");

        await server.ShutdownAsync();
        return 0;
    }
}