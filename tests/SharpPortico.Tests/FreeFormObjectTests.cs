using System;
using System.Linq;
using SharpPortico.Tests.Infrastructure;
using Xunit;

namespace SharpPortico.Tests;

/// <summary>
/// A property the contract declares as "any JSON object" maps to protobuf's own answer for one.
/// </summary>
/// <remarks>
/// Found by consuming the package: the artifact path of a real contract stores recorded payloads as JSON, declared as
/// objects with `additionalProperties`, and what came back was a placeholder message whose only member was `_HasValue`
/// - not what the contract said, and not something a consumer can use.
/// </remarks>
public class FreeFormObjectTests
{
    /// <summary>A free-form object property becomes a Struct, not a placeholder message.</summary>
    [Fact]
    public void A_Free_Form_Object_Maps_To_Struct()
    {
        var result = GeneratorTestDriver.Run(Spec("payload: { type: object, additionalProperties: true }"), serviceName: "FreeFormService");
        var thing = ClassBody(result.GeneratedSource, "Thing");

        Assert.Contains("global::Google.Protobuf.WellKnownTypes.Struct", thing, StringComparison.Ordinal);
        Assert.DoesNotContain("_HasValue", thing, StringComparison.Ordinal);
    }

    /// <summary>An array of them becomes a repeated Struct.</summary>
    [Fact]
    public void An_Array_Of_Free_Form_Objects_Maps_To_Repeated_Struct()
    {
        var result = GeneratorTestDriver.Run(
            Spec("payloads:\n          type: array\n          items: { type: object, additionalProperties: true }"),
            serviceName: "FreeFormService");

        Assert.Contains(
            "RepeatedField<global::Google.Protobuf.WellKnownTypes.Struct>",
            result.GeneratedSource,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The descriptor names the well-known type.
    /// </summary>
    /// <remarks>
    /// The import is <em>not</em> asserted here because it is not emitted: a .proto that names `google.protobuf.Struct`
    /// or `google.protobuf.Timestamp` without importing it does not compile, and that gap predates this mapping - it is
    /// recorded in the changelog rather than papered over by a test that would pass while the file is wrong.
    /// </remarks>
    [Fact]
    public void The_Descriptor_Names_The_Well_Known_Type()
    {
        var result = GeneratorTestDriver.Run(Spec("payload: { type: object, additionalProperties: true }"), serviceName: "FreeFormService");

        // The descriptor travels as one escaped C# constant; the type name is what this asserts.
        var descriptor = result.Sources.First(pair => pair.Key.EndsWith(".proto.cs", StringComparison.Ordinal)).Value;

        Assert.Contains("google.protobuf.Struct", descriptor, StringComparison.Ordinal);
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
