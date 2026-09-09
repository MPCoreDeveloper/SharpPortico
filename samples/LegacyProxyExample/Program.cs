using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharpPortico.Proxy;
using SharpPortico.Samples.LegacyProxy.Generated;
using SharpPortico.Samples.LegacyProxyExample;

const string LegacyKey = "legacy-secret-key";
const int LegacyRestPort = 5099;
const int GrpcPort = 50053;

// ------------------------------------------------------------------ legacy REST service
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls($"http://localhost:{LegacyRestPort}");
var app = builder.Build();

var restCallCount = 0;
var pets = new Dictionary<long, Pet>
{
    [1] = new Pet { Id = 1, Name = "Rex", Status = StatusEnum.Available },
    [2] = new Pet { Id = 2, Name = "Milo", Status = StatusEnum.Pending }
};

app.MapGet("/pets", (HttpContext ctx) =>
{
    if (ctx.Request.Headers["X-Api-Key"].ToString() != LegacyKey)
        return Results.Unauthorized();
    Interlocked.Increment(ref restCallCount);
    return Results.Json(pets.Values.ToList());
});

app.MapGet("/pets/{petId:long}", (HttpContext ctx, long petId) =>
{
    if (ctx.Request.Headers["X-Api-Key"].ToString() != LegacyKey)
        return Results.Unauthorized();
    Interlocked.Increment(ref restCallCount);
    return pets.TryGetValue(petId, out var pet) ? Results.Json(pet) : Results.NotFound();
});

app.MapPost("/pets", async (HttpContext ctx) =>
{
    if (ctx.Request.Headers["X-Api-Key"].ToString() != LegacyKey)
        return Results.Unauthorized();
    var pet = await ctx.Request.ReadFromJsonAsync<Pet>();
    if (pet is null) return Results.BadRequest();
    pets[pet.Id] = pet;
    return Results.Json(pet, statusCode: StatusCodes.Status201Created);
});

await app.StartAsync();

// Wait until the legacy REST service accepts connections (avoids startup races).
using (var probeClient = new HttpClient { BaseAddress = new Uri($"http://localhost:{LegacyRestPort}") })
{
    probeClient.DefaultRequestHeaders.Add("X-Api-Key", LegacyKey);
    for (var attempt = 0; attempt < 50; attempt++)
    {
        try
        {
            using var probe = await probeClient.GetAsync("/pets");
            _ = probe.StatusCode; // any response means the server is up
            break;
        }
        catch (HttpRequestException)
        {
            await Task.Delay(100);
        }
    }
}

// ------------------------------------------------------------------ generated proxy (gRPC server)
var services = new ServiceCollection();
services.AddMemoryCache();
services.AddSingleton<IKeyProvider>(_ => new DelegateKeyProvider(_ => LegacyKey));
services.AddSingleton<IProxyCache, MemoryProxyCache>();
services.AddSingleton<IProxyAuditLogger>(_ => new NullAuditLogger());
using var provider = services.BuildServiceProvider();

var options = new ProxyOptions
{
    BaseUrl = $"http://localhost:{LegacyRestPort}",
    ApiKeyHeaderName = "X-Api-Key",
    CacheTtl = TimeSpan.FromSeconds(60),
    CacheReadsOnly = true,
    ClientKeyMode = ClientKeyMode.None
};

using var httpClient = new HttpClient { BaseAddress = new Uri(options.BaseUrl) };
var rest = new HttpRestClient(httpClient, options.BaseUrl);
var proxy = new PetServiceProxy(options, rest,
    cache: provider.GetRequiredService<IProxyCache>(),
    keys: provider.GetRequiredService<IKeyProvider>(),
    audit: provider.GetRequiredService<IProxyAuditLogger>());

var server = new Server
{
    Services = { PetService.BindService(proxy) },
    Ports = { new ServerPort("localhost", GrpcPort, ServerCredentials.Insecure) }
};
server.Start();
Console.WriteLine($"gRPC server listening on {GrpcPort}");

// ------------------------------------------------------------------ local gRPC clients
using var channel = GrpcChannel.ForAddress($"http://localhost:{GrpcPort}");
var client = PetServiceClient.Create(channel);

// 1) Path param -> REST /pets/{id} with X-Api-Key (cacheable GET)
var get1 = await client.GetPetAsync(1);
Console.WriteLine($"GetPet(1) -> {get1.Data?.Name} (REST calls so far: {restCallCount})");

// 2) Same request again -> cache hit; REST count unchanged
var get2 = await client.GetPetAsync(1);
Console.WriteLine($"GetPet(1) cached -> {get2.Data?.Name} (REST calls so far: {restCallCount})");

// 3) Bypass cache per call -> REST again
var headers = new Metadata { { "x-portico-bypass-cache", "true" } };
var get3 = await client.GetPetAsync(new GetPetRequest { PetId = 1 }, headers);
Console.WriteLine($"GetPet(1) bypass -> {get3.Data?.Name} (REST calls so far: {restCallCount})");

// 4) Query params -> REST /pets?limit=20&page=1
var list = await client.ListPetsAsync(new ListPetsRequest { Limit = 20, Page = 1 });
Console.WriteLine($"ListPets -> {list.Items.Count} pets (REST calls so far: {restCallCount})");

// 5) POST body -> REST /pets (not cached)
var created = await client.CreatePetAsync(new CreatePetRequest { Body = new Pet { Id = 3, Name = "Luna", Status = StatusEnum.Sold } });
Console.WriteLine($"CreatePet -> id={created.Data?.Id} {created.Data?.Name} (REST calls so far: {restCallCount})");

Console.WriteLine($"TOTAL REST calls: {restCallCount}  (expected 4: get + bypass + list + create)");
Console.WriteLine("Proxy demo complete.");
await server.ShutdownAsync();
await app.StopAsync();