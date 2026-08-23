using SharpPortico;

[assembly: OpenApiToGrpc(
    "openapi/petstore.yaml",
    "PetService",
    "SharpPortico.Samples.Generated")]