using Grpc.Core;
using SharpPortico.Samples.MinimalApi.Generated;

namespace SharpPortico.Samples.MinimalApiExample;

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