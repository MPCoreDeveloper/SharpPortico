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
    /// <summary>
    /// A document the reader cannot parse is refused with the reader's own words.
    /// </summary>
    /// <remarks>
    /// The shape below is the one that cost a real session: a property whose value is a plain scalar where a mapping
    /// belongs (a literal backslash-n from a scripted edit, in that case). The reader reports it and hands back a
    /// document without an info section - and answering "missing info/title section" sent the caller to inspect the one
    /// part of the file that was fine, for as long as it took to read the whole contract by hand.
    /// </remarks>
    [Fact]
    public void A_Document_That_Fails_To_Parse_Is_Refused_With_The_Readers_Own_Complaint()
    {
        // A tab where YAML allows only spaces: the reader reports it by name, so a refusal that answers
        // "missing info/title section" instead is hiding the one message the caller can act on.
        var spec = Spec(
            "openapi: 3.0.3",
            "info: { title: Broken, version: 1.0.0 }",
            "components:",
            "\tschemas:",
            "    Thing:",
            "      type: object",
            "      properties:",
            "        text: { type: string }");

        var result = GeneratorTestDriver.TryRun(spec, serviceName: "BrokenService");

        var refusal = result.Diagnostics.FirstOrDefault(d => d.StartsWith("SP1000", StringComparison.Ordinal));

        Assert.NotNull(refusal);
        Assert.DoesNotContain("missing info/title section", refusal, StringComparison.Ordinal);
    }

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
