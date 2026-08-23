using System.IO;
using System.Linq;
using SharpPortico.Tests.Infrastructure;
using Xunit;

namespace SharpPortico.Tests;

/// <summary>
/// End-to-end scenarios exercising the OpenAPI → gRPC mapping rules through the real
/// generator pipeline: enums (single emission), streaming hints, pagination, auth
/// metadata helpers, schema composition, arrays, octet-stream, and error wrappers.
/// </summary>
public class MappingScenarioTests
{
    private static string Spec(params string[] lines) => string.Join('\n', lines);

    [Fact]
    public void Component_Enum_Is_Emitted_Exactly_Once()
    {
        var spec = Spec(
            "openapi: 3.0.3",
            "info: { title: Pets, version: 1.0.0 }",
            "paths:",
            "  /pets:",
            "    get:",
            "      operationId: listPets",
            "      responses:",
            "        '200':",
            "          description: ok",
            "          content:",
            "            application/json:",
            "              schema:",
            "                type: array",
            "                items: { $ref: '#/components/schemas/Pet' }",
            "components:",
            "  schemas:",
            "    Pet:",
            "      type: object",
            "      properties:",
            "        status:",
            "          type: string",
            "          enum: [available, sold]");

        var result = GeneratorTestDriver.Run(spec, serviceName: "PetService");

        Assert.Empty(result.Diagnostics);
        // The enum must appear exactly once in the generated source.
        Assert.Equal(1, CountOccurrences(result.GeneratedSource, "public enum StatusEnum"));
        Assert.Contains("Available = 0", result.GeneratedSource);
        Assert.Contains("Sold = 1", result.GeneratedSource);
    }

    [Fact]
    public void Streaming_Hints_Produce_Streaming_Rpcs()
    {
        var spec = Spec(
            "openapi: 3.0.3",
            "info: { title: Events, version: 1.0.0 }",
            "paths:",
            "  /events:",
            "    get:",
            "      operationId: watchEvents",
            "      x-grpc-streaming: server",
            "      responses:",
            "        '200':",
            "          description: ok",
            "          content:",
            "            application/json:",
            "              schema:",
            "                type: array",
            "                items: { $ref: '#/components/schemas/Event' }",
            "  /events/upload:",
            "    post:",
            "      operationId: uploadEvents",
            "      x-grpc-streaming: client",
            "      requestBody:",
            "        content:",
            "          application/json:",
            "            schema: { $ref: '#/components/schemas/Event' }",
            "      responses:",
            "        '200':",
            "          description: ok",
            "          content:",
            "            application/json:",
            "              schema: { $ref: '#/components/schemas/Event' }",
            "components:",
            "  schemas:",
            "    Event:",
            "      type: object",
            "      properties:",
            "        id: { type: integer, format: int64 }");

        var result = GeneratorTestDriver.Run(spec, serviceName: "EventsService");

        Assert.Empty(result.Diagnostics);
        Assert.Contains("MethodType.ServerStreaming", result.GeneratedSource);
        Assert.Contains("MethodType.ClientStreaming", result.GeneratedSource);
        // Convenience client exposes streaming entry points.
        Assert.Contains("WatchEventsStream(", result.GeneratedSource);
    }

    [Fact]
    public void Cursor_Pagination_Maps_To_Server_Streaming()
    {
        var spec = Spec(
            "openapi: 3.0.3",
            "info: { title: Search, version: 1.0.0 }",
            "paths:",
            "  /search:",
            "    get:",
            "      operationId: search",
            "      parameters:",
            "        - { name: cursor, in: query, schema: { type: string } }",
            "        - { name: limit, in: query, schema: { type: integer } }",
            "      responses:",
            "        '200':",
            "          description: ok",
            "          content:",
            "            application/json:",
            "              schema:",
            "                type: array",
            "                items: { $ref: '#/components/schemas/Result' }",
            "components:",
            "  schemas:",
            "    Result:",
            "      type: object",
            "      properties:",
            "        id: { type: integer }");

        var result = GeneratorTestDriver.Run(spec, serviceName: "SearchService");

        Assert.Empty(result.Diagnostics);
        Assert.Contains("MethodType.ServerStreaming", result.GeneratedSource);
        Assert.Contains("Search", result.GeneratedSource);
    }

    [Fact]
    public void Auth_Schemes_Generate_Metadata_Helpers()
    {
        var spec = Spec(
            "openapi: 3.0.3",
            "info: { title: Secure, version: 1.0.0 }",
            "paths:",
            "  /secure:",
            "    get:",
            "      operationId: getSecure",
            "      security: [{ bearerAuth: [] }]",
            "      responses:",
            "        '200':",
            "          description: ok",
            "          content:",
            "            application/json:",
            "              schema: { type: string }",
            "components:",
            "  securitySchemes:",
            "    bearerAuth:",
            "      type: http",
            "      scheme: bearer",
            "    apiKeyAuth:",
            "      type: apiKey",
            "      in: header",
            "      name: X-API-Key",
            "    oauthAuth:",
            "      type: oauth2",
            "      flows:",
            "        clientCredentials:",
            "          tokenUrl: https://example.com/token",
            "          scopes: {}");

        var result = GeneratorTestDriver.Run(spec, serviceName: "SecureService");

        Assert.Empty(result.Diagnostics);
        Assert.Contains("CreateBearerTokenMetadata", result.GeneratedSource);
        Assert.Contains("CreateApiKeyMetadata", result.GeneratedSource);
        Assert.Contains("CreateOAuth2Metadata", result.GeneratedSource);
        Assert.Contains("X-API-Key", result.GeneratedSource);
    }

    [Fact]
    public void AllOf_Single_Ref_Maps_To_Alias()
    {
        var spec = Spec(
            "openapi: 3.0.3",
            "info: { title: Alias, version: 1.0.0 }",
            "paths:",
            "  /things:",
            "    get:",
            "      operationId: getThing",
            "      responses:",
            "        '200':",
            "          description: ok",
            "          content:",
            "            application/json:",
            "              schema: { $ref: '#/components/schemas/ExtendedThing' }",
            "components:",
            "  schemas:",
            "    BaseThing:",
            "      type: object",
            "      properties:",
            "        id: { type: integer }",
            "    ExtendedThing:",
            "      allOf:",
            "        - { $ref: '#/components/schemas/BaseThing' }");

        var result = GeneratorTestDriver.Run(spec, serviceName: "ThingService");

        Assert.Empty(result.Diagnostics);
        Assert.Contains("public sealed partial class ExtendedThing", result.GeneratedSource);
        Assert.Contains("public sealed partial class BaseThing", result.GeneratedSource);
    }

    [Fact]
    public void Array_Response_Produces_Repeated_Items_Field()
    {
        var spec = Spec(
            "openapi: 3.0.3",
            "info: { title: Pets, version: 1.0.0 }",
            "paths:",
            "  /pets:",
            "    get:",
            "      operationId: listPets",
            "      responses:",
            "        '200':",
            "          description: ok",
            "          content:",
            "            application/json:",
            "              schema:",
            "                type: array",
            "                items: { $ref: '#/components/schemas/Pet' }",
            "components:",
            "  schemas:",
            "    Pet:",
            "      type: object",
            "      properties:",
            "        id: { type: integer, format: int64 }");

        var result = GeneratorTestDriver.Run(spec, serviceName: "PetService");

        Assert.Empty(result.Diagnostics);
        Assert.Contains("RepeatedField<Pet> Items { get; } = new();", result.GeneratedSource);
        Assert.Contains("ListPetsItemsAsync", result.GeneratedSource);
        Assert.Contains("RepeatedField<Pet>", result.GeneratedSource);
    }

    [Fact]
    public void OctetStream_Body_Maps_To_Bytes()
    {
        var spec = Spec(
            "openapi: 3.0.3",
            "info: { title: Files, version: 1.0.0 }",
            "paths:",
            "  /upload:",
            "    post:",
            "      operationId: uploadFile",
            "      requestBody:",
            "        content:",
            "          application/octet-stream:",
            "            schema: { type: string, format: binary }",
            "      responses:",
            "        '201':",
            "          description: ok",
            "          content:",
            "            application/json:",
            "              schema: { type: string }");

        var result = GeneratorTestDriver.Run(spec, serviceName: "FileService");

        Assert.Empty(result.Diagnostics);
        Assert.Contains("Google.Protobuf.ByteString", result.GeneratedSource);
        Assert.Contains("ByteString Body", result.GeneratedSource);
    }

    [Fact]
    public void Missing_Success_Response_Emits_Error_Wrapper()
    {
        var spec = Spec(
            "openapi: 3.0.3",
            "info: { title: Errors, version: 1.0.0 }",
            "paths:",
            "  /missing:",
            "    get:",
            "      operationId: getMissing",
            "      responses:",
            "        '404':",
            "          description: not found");

        var result = GeneratorTestDriver.Run(spec, serviceName: "ErrorService");

        Assert.Empty(result.Diagnostics);
        // google.rpc.Status-shaped error wrapper message.
        Assert.Contains("GetMissingError", result.GeneratedSource);
    }

    [Fact]
    public void Plain_Petstore_EndToEnd_Emits_Service_And_Compiles()
    {
        var specPath = Path.Combine(
            Path.GetDirectoryName(typeof(MappingScenarioTests).Assembly.Location)!,
            "openapi", "petstore.yaml");
        var spec = File.ReadAllText(specPath);
        var result = GeneratorTestDriver.Run(spec, serviceName: "PetService");

        Assert.Empty(result.Diagnostics);
        // .g.cs (C#) plus .proto.cs (proto descriptor) are emitted.
        Assert.Equal(2, result.Sources.Count);
        Assert.Contains("public static partial class PetService", result.GeneratedSource);
        Assert.Contains("public sealed partial class PetServiceClient", result.GeneratedSource);
        Assert.Contains("public abstract partial class PetServiceBase", result.GeneratedSource);
        Assert.Contains("public enum StatusEnum", result.GeneratedSource);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}