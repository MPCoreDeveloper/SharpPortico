using System;
using SharpPortico.Tests.Infrastructure;
using Xunit;

namespace SharpPortico.Tests;

/// <summary>
/// Contracts that cannot compile are refused with a reason, rather than emitted as code the consumer
/// has to diagnose from a compiler error inside generated source.
/// </summary>
public class ContractRefusalTests
{
    private static string Spec(params string[] lines) => string.Join('\n', lines);

    [Fact]
    public void A_Message_Name_Produced_Twice_Is_Refused()
    {
        // An operation derives "{OperationId}Request", so a component schema of that name collides
        // with it - and both would be emitted into one file.
        var spec = Spec(
            "openapi: 3.0.3",
            "info: { title: Search, version: 1.0.0 }",
            "paths:",
            "  /search:",
            "    post:",
            "      operationId: Search",
            "      requestBody:",
            "        required: true",
            "        content:",
            "          application/json:",
            "            schema: { $ref: '#/components/schemas/SearchRequest' }",
            "      responses:",
            "        '200':",
            "          description: ok",
            "components:",
            "  schemas:",
            "    SearchRequest:",
            "      type: object",
            "      properties:",
            "        text: { type: string }");

        var result = GeneratorTestDriver.TryRun(spec, serviceName: "SearchService");

        Assert.Empty(result.Sources);
        Assert.Contains(result.Diagnostics, d => d.StartsWith("SP2005", StringComparison.Ordinal));
    }

    [Fact]
    public void A_Property_Named_Like_Its_Schema_Is_Refused()
    {
        // The generated property would share its name with the generated type, which C# forbids.
        var spec = Spec(
            "openapi: 3.0.3",
            "info: { title: Streams, version: 1.0.0 }",
            "paths:",
            "  /head:",
            "    get:",
            "      operationId: GetHead",
            "      responses:",
            "        '200':",
            "          description: ok",
            "          content:",
            "            application/json:",
            "              schema: { $ref: '#/components/schemas/Head' }",
            "components:",
            "  schemas:",
            "    Head:",
            "      type: object",
            "      properties:",
            "        head: { type: integer, format: int64 }");

        var result = GeneratorTestDriver.TryRun(spec, serviceName: "StreamService");

        Assert.Empty(result.Sources);
        Assert.Contains(result.Diagnostics, d => d.StartsWith("SP2006", StringComparison.Ordinal));
    }
}
