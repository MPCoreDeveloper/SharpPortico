using SharpPortico;

[assembly: OpenApiToGrpc(
    "openapi/fleet-3.1.yaml",
    "FleetService",
    "SharpPortico.Samples.NativeAot.Generated")]
