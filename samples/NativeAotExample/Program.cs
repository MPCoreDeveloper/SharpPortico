using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using SharpPortico.Samples.NativeAot.Generated;

namespace SharpPortico.Samples.NativeAotExample;

/// <summary>
/// In-memory implementation of the generated <c>FleetServiceBase</c>.
/// </summary>
/// <remarks>
/// The contract served here is the one the 3.1 document describes, so the body reads back what the 3.1
/// constructs became: a nullable string is a string, a <c>const</c> is a one-member enumeration, a union of
/// types is an arbitrary JSON value, a tuple is a repeated one, base64 is bytes, a <c>$defs</c> reference is
/// the message it points at, and a free-form object is a <c>Struct</c>.
/// </remarks>
internal sealed class FleetServiceImpl : FleetServiceBase
{
    private readonly Dictionary<long, Vehicle> _vehicles = new();

    /// <summary>
    /// The payload of the most recent webhook delivery. The document declares an empty response body, which
    /// the generator maps to a message carrying only a sentinel <c>_HasValue</c> field - so the payload is
    /// what the call actually carries, and it is what the run checks.
    /// </summary>
    public Vehicle? LastRetired { get; private set; }

    public override Task<ListVehiclesResponse> ListVehiclesAsync(ListVehiclesRequest request, ServerCallContext context)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var limit = request.Limit <= 0 ? 20 : request.Limit;

        var response = new ListVehiclesResponse();
        response.Items.Add(_vehicles.OrderBy(v => v.Key).Skip((page - 1) * limit).Take(limit).Select(v => v.Value));
        return Task.FromResult(response);
    }

    public override Task<RegisterVehicleResponse> RegisterVehicleAsync(RegisterVehicleRequest request, ServerCallContext context)
    {
        if (request.Body is null) throw new RpcException(new Status(StatusCode.InvalidArgument, "body required"));
        _vehicles[request.Body.Id] = request.Body;
        return Task.FromResult(new RegisterVehicleResponse { Data = request.Body });
    }

    public override Task<GetVehicleResponse> GetVehicleAsync(GetVehicleRequest request, ServerCallContext context)
    {
        if (!_vehicles.TryGetValue(request.VehicleId, out var vehicle))
            throw new RpcException(new Status(StatusCode.NotFound, $"vehicle {request.VehicleId} not found"));
        return Task.FromResult(new GetVehicleResponse { Data = vehicle });
    }

    /// <summary>
    /// The webhook operation the document declares. A webhook is a call the API makes, so the client method
    /// for one describes a payload the service sends rather than a call it accepts - which is why the mapping
    /// reports it. Serving it is what makes the shape visible here.
    /// </summary>
    public override Task<OnVehicleRetiredResponse> OnVehicleRetiredAsync(OnVehicleRetiredRequest request, ServerCallContext context)
    {
        LastRetired = request.Body;
        return Task.FromResult(new OnVehicleRetiredResponse());
    }
}

internal static class Program
{
    private static int _failures;

    public static async Task<int> Main()
    {
        Console.WriteLine("FleetService (NativeAOT)");

        // Both switches are cleared by the PublishAot build flag, so this reports how the build was
        // configured rather than how the process was started. What proves the binary is a native image is
        // the publish step; what proves the generated code survives one is that this run reaches its checks.
        Console.WriteLine($"dynamic code:      compiled={RuntimeFeature.IsDynamicCodeCompiled} supported={RuntimeFeature.IsDynamicCodeSupported}");

        // The smoke test does not care which port it uses. A fixed one is not always available - a machine
        // that has excluded its dynamic port range (Hyper-V, WSL) fails to bind it, and that failure has
        // nothing to do with the code under test.
        var port = ReserveFreePort();

        // ---- Server: bind the generated service definition ----
        var service = new FleetServiceImpl();
        var server = new Server
        {
            Services = { FleetService.BindService(service) },
            Ports = { new ServerPort("localhost", port, ServerCredentials.Insecure) }
        };
        server.Start();
        Console.WriteLine($"server:            listening on {port}");

        // ---- Client: generated C# 14 client over a GrpcChannel ----
        using var channel = GrpcChannel.ForAddress($"http://localhost:{port}");
        var client = FleetServiceClient.Create(channel);

        // Every 3.1 construct the document declares travels in one message.
        var vehicle = new Vehicle
        {
            Id = 1,
            Callsign = "Atlas",                                          // type: [string, 'null']
            Kind = KindEnum.Truck,                                       // const: truck
            ModelYear = 2026,                                            // exclusiveMinimum: 0
            Manifest = ByteString.CopyFromUtf8("fleet"),                 // contentEncoding: base64
            ServiceWindow = new ServiceWindow { Label = "day-shift" },   // $ref: '#/$defs/ServiceWindow'
            Labels = new Struct                                          // free-form object
            {
                Fields =
                {
                    ["region"] = Value.ForString("eu-west"),
                    ["seats"] = Value.ForNumber(3),
                }
            },
            Telemetry = Value.ForString("idle"),                         // type: [object, array]
        };
        vehicle.Bounds.Add(Value.ForString("north"));                    // prefixItems
        vehicle.Bounds.Add(Value.ForNumber(51.5));

        var registered = await client.RegisterVehicleAsync(vehicle);
        Console.WriteLine(
            $"RegisterVehicle    -> id={registered.Data?.Id} callsign={registered.Data?.Callsign} " +
            $"kind={registered.Data?.Kind} year={registered.Data?.ModelYear} " +
            $"window={registered.Data?.ServiceWindow?.Label}");

        // Scalar overload: the path parameter is the only argument.
        var fetched = await client.GetVehicleDataAsync(1);
        Console.WriteLine(
            $"GetVehicle(1)      -> callsign={fetched.Callsign} labels={fetched.Labels.Fields.Count} " +
            $"bounds={fetched.Bounds.Count} telemetry={fetched.Telemetry.KindCase}");

        var listed = await client.ListVehiclesItemsAsync(new ListVehiclesRequest { Limit = 10, Page = 1 });
        Console.WriteLine($"ListVehicles       -> {listed.Count} vehicle(s)");

        // The webhook operation: the payload is what the call carries, and an empty response body comes back
        // as a message whose only field is the sentinel the generator adds for one.
        var retired = await client.OnVehicleRetiredAsync(fetched);
        Console.WriteLine($"OnVehicleRetired   -> callsign={service.LastRetired?.Callsign} response={retired is not null}");

        // Serialization round-trip through the generated protobuf messages: no descriptor, no reflection.
        var roundTrip = Vehicle.Parser.ParseFrom(fetched.ToByteArray());
        Console.WriteLine(
            $"Round-trip OK      -> callsign={roundTrip.Callsign} kind={roundTrip.Kind} " +
            $"manifest={roundTrip.Manifest.ToStringUtf8()} region={roundTrip.Labels.Fields["region"].StringValue}");

        // ---- What the transcript claims is checked, so a run can fail ----
        Expect(registered.Data?.Callsign == "Atlas", "RegisterVehicle returns the callsign it was given");
        Expect(registered.Data?.Kind == KindEnum.Truck, "a const travels as its one-member enumeration");
        Expect(fetched.Labels.Fields.Count == 2, "a free-form object travels as a Struct");
        Expect(fetched.Bounds.Count == 2, "a tuple travels as a repeated value");
        Expect(fetched.Telemetry.KindCase == Value.KindOneofCase.StringValue, "a union travels as a Value");
        Expect(listed.Count == 1, "the registered vehicle is listed");
        Expect(retired is not null, "the webhook call returns its (empty) response body");
        Expect(service.LastRetired?.Callsign == "Atlas", "the webhook payload arrives as the request body");
        Expect(roundTrip.Callsign == "Atlas" && roundTrip.Kind == KindEnum.Truck, "the round-trip carries the message");
        Expect(roundTrip.Manifest.ToStringUtf8() == "fleet", "base64 travels as the bytes it encodes");

        await server.ShutdownAsync();

        Console.WriteLine(_failures == 0 ? "SMOKE TEST PASSED" : $"SMOKE TEST FAILED ({_failures})");
        return _failures == 0 ? 0 : 1;
    }

    /// <summary>Records what the run claims, so a transcript line and its check cannot drift apart.</summary>
    private static void Expect(bool condition, string what)
    {
        if (condition) return;
        _failures++;
        Console.Error.WriteLine($"expected: {what}");
    }

    /// <summary>
    /// Asks the OS for a port that is free right now. The window between releasing it and the gRPC server
    /// binding it is tiny and, in a process that is the only caller, wide enough.
    /// </summary>
    private static int ReserveFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
