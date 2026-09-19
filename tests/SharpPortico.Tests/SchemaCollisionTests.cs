using System;
using SharpPortico.Tests.Infrastructure;
using Xunit;

namespace SharpPortico.Tests;

/// <summary>
/// Two schemas that say different things may not end up sharing one generated type.
/// </summary>
/// <remarks>
/// Both cases below were found by consuming the package: a contract with two `state` properties (an artifact
/// lifecycle and a session lifecycle) and a contract whose schemas share property names. They fail silently - the
/// generated code compiles only when nothing references the missing members, and the consumer then debugs generated
/// source instead of the contract.
/// </remarks>
public class SchemaCollisionTests
{
    /// <summary>
    /// Two enumerations that happen to share a property name but declare different members are refused.
    /// </summary>
    /// <remarks>
    /// Refused rather than resolved: an inline enumeration is named after the property that declares it, so the two
    /// cannot both be `StateEnum` - and either resolution is worse than saying so. Keeping the first silently drops the
    /// second lifecycle's members; inventing a name for the second hands the contract a type nobody wrote.
    /// </remarks>
    [Fact]
    public void Two_Enumerations_With_One_Name_And_Different_Members_Are_Refused()
    {
        var spec = Spec(
            "openapi: 3.0.3",
            "info: { title: Collisions, version: 1.0.0 }",
            "paths:",
            "  /artifacts:",
            "    get:",
            "      operationId: ListArtifacts",
            "      responses:",
            "        '200':",
            "          description: ok",
            "          content:",
            "            application/json:",
            "              schema: { $ref: '#/components/schemas/Artifact' }",
            "  /sessions:",
            "    get:",
            "      operationId: ListSessions",
            "      responses:",
            "        '200':",
            "          description: ok",
            "          content:",
            "            application/json:",
            "              schema: { $ref: '#/components/schemas/Session' }",
            "components:",
            "  schemas:",
            "    Artifact:",
            "      type: object",
            "      properties:",
            "        state: { type: string, enum: [pending, committed, purged] }",
            "    Session:",
            "      type: object",
            "      properties:",
            "        state: { type: string, enum: [open, closed, expired] }");

        var result = GeneratorTestDriver.TryRun(spec, serviceName: "CollisionService");

        Assert.Empty(result.Sources);
        Assert.Contains(
            result.Diagnostics,
            d => d.StartsWith("SP2007", StringComparison.Ordinal)
                && d.Contains("Pending", StringComparison.Ordinal)
                && d.Contains("Open", StringComparison.Ordinal));
    }

    /// <summary>Two enumerations with identical members are one type, which is what sharing a vocabulary means.</summary>
    [Fact]
    public void Two_Enumerations_With_The_Same_Members_Are_One_Type()
    {
        var spec = Spec(
            "openapi: 3.0.3",
            "info: { title: Shared, version: 1.0.0 }",
            "paths:",
            "  /things:",
            "    get:",
            "      operationId: ListThings",
            "      responses:",
            "        '200':",
            "          description: ok",
            "          content:",
            "            application/json:",
            "              schema: { $ref: '#/components/schemas/Thing' }",
            "components:",
            "  schemas:",
            "    Thing:",
            "      type: object",
            "      properties:",
            "        status: { type: string, enum: [active, removed] }",
            "        previous: { type: string, enum: [active, removed] }");

        var result = GeneratorTestDriver.Run(spec, serviceName: "SharedService");

        Assert.Equal(1, Occurrences(result.GeneratedSource, "public enum StatusEnum"));
    }

    /// <summary>How many times a fragment appears in the generated source.</summary>
    /// <param name="emitted">The generated source.</param>
    /// <param name="fragment">The fragment.</param>
    /// <returns>The count.</returns>
    private static int Occurrences(string emitted, string fragment)
    {
        var count = 0;
        var at = emitted.IndexOf(fragment, StringComparison.Ordinal);

        while (at >= 0)
        {
            count++;
            at = emitted.IndexOf(fragment, at + fragment.Length, StringComparison.Ordinal);
        }

        return count;
    }

    /// <summary>A schema's message carries that schema's own fields - not another schema's, and not one field twice.</summary>
    [Fact]
    public void A_Schema_Message_Carries_Its_Own_Fields()
    {
        var spec = Spec(
            "openapi: 3.0.3",
            "info: { title: Neighbours, version: 1.0.0 }",
            "paths:",
            "  /runbooks:",
            "    get:",
            "      operationId: ListRunbooks",
            "      responses:",
            "        '200':",
            "          description: ok",
            "          content:",
            "            application/json:",
            "              schema: { $ref: '#/components/schemas/RunbookList' }",
            "  /runbooks/apply:",
            "    post:",
            "      operationId: ApplyRunbook",
            "      responses:",
            "        '200':",
            "          description: ok",
            "          content:",
            "            application/json:",
            "              schema: { $ref: '#/components/schemas/AppliedRunbook' }",
            "  /sessions:",
            "    post:",
            "      operationId: CreateSession",
            "      responses:",
            "        '200':",
            "          description: ok",
            "          content:",
            "            application/json:",
            "              schema: { $ref: '#/components/schemas/SessionCreated' }",
            "components:",
            "  schemas:",
            "    RunbookVersion:",
            "      type: object",
            "      properties:",
            "        runbook_ref: { type: string }",
            "        name: { type: string }",
            "        version: { type: integer, format: int32 }",
            "        status: { type: string, enum: [active, removed] }",
            "    RunbookList:",
            "      type: object",
            "      properties:",
            "        runbooks:",
            "          type: array",
            "          items: { $ref: '#/components/schemas/RunbookVersion' }",
            "    AppliedRunbook:",
            "      type: object",
            "      properties:",
            "        runbook_ref: { type: string }",
            "        name: { type: string }",
            "        version: { type: integer, format: int32 }",
            "        status: { type: string, enum: [active, removed] }",
            "    SessionCreated:",
            "      type: object",
            "      properties:",
            "        session_id: { type: string }",
            "        runbook_ref: { type: string }",
            "        permitted_collections:",
            "          type: array",
            "          items: { type: string }");

        var result = GeneratorTestDriver.Run(spec, serviceName: "NeighbourService");
        var runbook = ClassBody(result.GeneratedSource, "RunbookVersion");

        // One assertion carrying the body, so a failure shows what was actually emitted.
        Assert.True(
            runbook.Contains("Version", StringComparison.Ordinal)
                && runbook.Contains("Status", StringComparison.Ordinal)
                && !runbook.Contains("SessionId", StringComparison.Ordinal)
                && !runbook.Contains("PermittedCollections", StringComparison.Ordinal),
            runbook);
    }

    /// <summary>The emitted text of one generated class, up to the next class declaration.</summary>
    /// <param name="emitted">The generated source.</param>
    /// <param name="className">The class to read.</param>
    /// <returns>Its body.</returns>
    private static string ClassBody(string emitted, string className)
    {
        var start = emitted.IndexOf($"partial class {className} ", StringComparison.Ordinal);

        Assert.True(start >= 0, $"the generated source has no '{className}' class");

        var next = emitted.IndexOf("partial class ", start + 1, StringComparison.Ordinal);

        return next < 0 ? emitted[start..] : emitted[start..next];
    }

    private static string Spec(params string[] lines) => string.Join('\n', lines);
}
