using System;
using SharpPortico.Tests.Infrastructure;
using Xunit;

namespace SharpPortico.Tests;

/// <summary>
/// A .proto that names a well-known type without importing the file that declares it does not compile, so the
/// descriptor carries the import of every well-known type its messages use.
/// </summary>
public class ProtoImportTests
{
    /// <summary>A free-form object property maps to google.protobuf.Struct, and the descriptor imports it.</summary>
    [Fact]
    public void A_Free_Form_Object_Brings_In_The_Struct_Import()
    {
        var proto = GeneratorTestDriver.Proto(Spec("payload: { type: object, additionalProperties: true }"), serviceName: "FreeFormService");

        Assert.Contains("google.protobuf.Struct", proto, StringComparison.Ordinal);
        Assert.Contains("import \"google/protobuf/struct.proto\";", proto, StringComparison.Ordinal);
    }

    /// <summary>
    /// A contract that names no well-known type carries no import at all: the imports are derived from the
    /// model, not emitted as a fixed preamble.
    /// </summary>
    [Fact]
    public void A_Contract_Without_Well_Known_Types_Carries_No_Imports()
    {
        var proto = GeneratorTestDriver.Proto(Spec("id: { type: integer }"), serviceName: "PlainService");

        Assert.DoesNotContain("import ", proto, StringComparison.Ordinal);
    }

    /// <summary>The import sits with the other file-level statements, ahead of the first definition.</summary>
    [Fact]
    public void The_Import_Precedes_Every_Definition()
    {
        var proto = GeneratorTestDriver.Proto(Spec("payload: { type: object, additionalProperties: true }"), serviceName: "FreeFormService");

        var package = proto.IndexOf("package ", StringComparison.Ordinal);
        var import = proto.IndexOf("import \"google/protobuf/struct.proto\";", StringComparison.Ordinal);
        var definition = proto.IndexOf("message ", StringComparison.Ordinal);

        Assert.True(
            package >= 0 && import > package && definition > import,
            "the import belongs after the package statement and before the first definition");
    }

    /// <summary>
    /// The descriptor is not the only artifact a consumer keeps: the generated C# that carries it, and the
    /// Struct field in it, have to compile against the real references too.
    /// </summary>
    [Fact]
    public void A_Free_Form_Object_Still_Produces_Code_That_Compiles()
    {
        var result = GeneratorTestDriver.Run(Spec("payload: { type: object, additionalProperties: true }"), serviceName: "FreeFormService");

        Assert.Empty(result.Diagnostics);
        Assert.Empty(GeneratedCodeCompiler.Errors(result.Sources.Values));
    }

    /// <summary>
    /// Every shape a well-known type appears in compiles: a single field, an array of them, and an array of a
    /// message declared next to them. Each one has to name a C# type, not the proto type it travels as.
    /// </summary>
    [Fact]
    public void Every_Shape_A_Well_Known_Type_Takes_Compiles()
    {
        var result = GeneratorTestDriver.Run(
            Spec(
                "payload: { type: object, additionalProperties: true }\n" +
                "        payloads:\n" +
                "          type: array\n" +
                "          items: { type: object, additionalProperties: true }\n" +
                "        peers:\n" +
                "          type: array\n" +
                "          items: { $ref: '#/components/schemas/Thing' }"),
            serviceName: "FreeFormService");

        Assert.Empty(result.Diagnostics);
        Assert.Empty(GeneratedCodeCompiler.Errors(result.Sources.Values));
    }

    private static string Spec(string properties) => string.Join(
        '\n',
        "openapi: 3.0.3",
        "info: { title: FreeForm, version: 1.0.0 }",
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
        "        " + properties);
}
