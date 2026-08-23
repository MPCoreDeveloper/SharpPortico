using SharpPortico;

[assembly: OpenApiToGrpc(
    "openapi/petstore.yaml",
    "PetService",
    "SharpPortico.Samples.LegacyProxy.Generated",
    EnableProxyGeneration = true,
    ProxyBaseUrl = "http://localhost:5099",
    ProxyApiKeyHeaderName = "X-Api-Key",
    ProxyCacheTtlSeconds = 60,
    ProxyClientKeyMode = ClientKeyMode.None,
    ProxyAuditEnabled = true)]