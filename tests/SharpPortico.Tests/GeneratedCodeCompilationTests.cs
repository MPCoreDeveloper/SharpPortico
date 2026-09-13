using System;
using System.IO;
using Microsoft.CodeAnalysis.CSharp;
using SharpPortico.Tests.Infrastructure;
using Xunit;

namespace SharpPortico.Tests;

/// <summary>
/// The generated contract is compiled, not just inspected.
/// </summary>
/// <remarks>
/// A specification can map cleanly and still emit code that does not compile - and that is the failure a
/// consumer cannot work around, because the error is inside a generated file. Every case here compiles the
/// emission for real.
/// </remarks>
public class GeneratedCodeCompilationTests
{
    private static string Spec(params string[] lines) => string.Join('\n', lines);

    private static readonly string PetstorePath = Path.Combine(
        Path.GetDirectoryName(typeof(GeneratedCodeCompilationTests).Assembly.Location)!,
        "openapi", "petstore.yaml");

    [Fact]
    public void The_Petstore_Contract_Compiles()
    {
        var result = GeneratorTestDriver.Run(
            File.ReadAllText(PetstorePath),
            serviceName: "PetService",
            namespaceName: "SharpPortico.Samples.Generated");

        Assert.Empty(result.Diagnostics);
        Assert.Empty(GeneratedCodeCompiler.Errors(result.Sources.Values));
    }

    [Fact]
    public void The_Contract_Also_Compiles_As_CSharp_14_Because_That_Is_What_A_Net10_Consumer_Has()
    {
        var result = GeneratorTestDriver.Run(
            File.ReadAllText(PetstorePath),
            serviceName: "PetService",
            namespaceName: "SharpPortico.Samples.Generated");

        Assert.Empty(result.Diagnostics);

        // The product ships on net10 and net11, so the emitted code has to fit in net10's language version
        // as well - a claim that is easy to make and cheap to check.
        Assert.Empty(GeneratedCodeCompiler.Errors(result.Sources.Values, LanguageVersion.CSharp14));
    }

    [Fact]
    public void A_Message_With_Two_Enum_Fields_Compiles()
    {
        // The deserialiser declares a local per enum field, so two enums in one message used to emit "var v"
        // twice into the same switch scope - CS0128, in generated code, for a contract that maps cleanly.
        // Each case body now has its own scope, which is also what protoc emits.
        var spec = Spec(
            "openapi: 3.0.3",
            "info: { title: Tickets, version: 1.0.0 }",
            "paths:",
            "  /tickets:",
            "    post:",
            "      operationId: CreateTicket",
            "      requestBody:",
            "        required: true",
            "        content:",
            "          application/json:",
            "            schema: { $ref: '#/components/schemas/Ticket' }",
            "      responses:",
            "        '200':",
            "          description: ok",
            "          content:",
            "            application/json:",
            "              schema: { $ref: '#/components/schemas/Ticket' }",
            "components:",
            "  schemas:",
            "    Ticket:",
            "      type: object",
            "      properties:",
            "        severity: { $ref: '#/components/schemas/Severity' }",
            "        state: { $ref: '#/components/schemas/State' }",
            "    Severity:",
            "      type: string",
            "      enum: [unspecified, low, high]",
            "    State:",
            "      type: string",
            "      enum: [unspecified, open, closed]");

        var result = GeneratorTestDriver.Run(spec, serviceName: "TicketService");

        Assert.Empty(result.Diagnostics);
        Assert.Empty(GeneratedCodeCompiler.Errors(result.Sources.Values));
    }
}
