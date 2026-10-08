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
/// <para>
/// Found twice: the same question is asked of a schema in every position it appears - a property, an array's element,
/// a request body and a response - and it was answered in one of them. A response the contract declares free-form, a
/// `GET` serving a document it does not describe, came back wrapped in a message whose only member was `has_value`.
/// The rule is therefore asserted in each position it binds, not only the one that was noticed first.
/// </para>
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

        // The declaration, not just the type name: a double-wrapped RepeatedField<RepeatedField<...>> also
        // contains the type name, and it is not what a consumer can use.
        Assert.Contains(
            "public global::Google.Protobuf.Collections.RepeatedField<global::Google.Protobuf.WellKnownTypes.Struct> Payloads { get; } = new();",
            result.GeneratedSource,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The descriptor names the well-known type and imports the file that declares it.
    /// </summary>
    /// <remarks>
    /// Naming `google.protobuf.Struct` without importing `google/protobuf/struct.proto` produces a .proto that
    /// protoc refuses, so the import is part of what this mapping owes the contract rather than a detail of the
    /// text. <see cref="ProtoImportTests"/> asserts the same thing on the unescaped descriptor.
    /// </remarks>
    [Fact]
    public void The_Descriptor_Names_And_Imports_The_Well_Known_Type()
    {
        var result = GeneratorTestDriver.Run(Spec("payload: { type: object, additionalProperties: true }"), serviceName: "FreeFormService");

        // The descriptor travels as one escaped C# constant; the type name and its import are what this asserts.
        var descriptor = result.Sources.First(pair => pair.Key.EndsWith(".proto.cs", StringComparison.Ordinal)).Value;

        Assert.Contains("google.protobuf.Struct", descriptor, StringComparison.Ordinal);
        Assert.Contains("google/protobuf/struct.proto", descriptor, StringComparison.Ordinal);
    }

    /// <summary>A response the contract declares as "any JSON object" is a Struct, not a placeholder.</summary>
    [Fact]
    public void A_Free_Form_Response_Maps_To_Struct()
    {
        var result = GeneratorTestDriver.Run(
            ResponseSpec("{ type: object, additionalProperties: true }"),
            serviceName: "FreeFormService");
        var answer = ClassBody(result.GeneratedSource, "GetThingsResponse");

        Assert.Contains(
            "public global::Google.Protobuf.WellKnownTypes.Struct Data { get; set; } = null!;",
            answer,
            StringComparison.Ordinal);
        Assert.DoesNotContain("_HasValue", answer, StringComparison.Ordinal);

        // And the client's convenience overload hands back the same type, which is a second place the answer is
        // named: it read the field's *proto* name, which is the same string as the C# name for every message the
        // generator declares itself and no type at all for a well-known one.
        Assert.Contains(
            "Task<global::Google.Protobuf.WellKnownTypes.Struct> GetThingsDataAsync(",
            result.GeneratedSource,
            StringComparison.Ordinal);
    }

    /// <summary>A response that declares no object at all is answered the same way: a document describing itself.</summary>
    [Fact]
    public void A_Response_That_Declares_No_Object_Maps_To_Struct()
    {
        var proto = GeneratorTestDriver.Proto(ResponseSpec("{}"), serviceName: "FreeFormService");

        Assert.Contains("google.protobuf.Struct data = 1;", proto, StringComparison.Ordinal);
        Assert.Contains("import \"google/protobuf/struct.proto\";", proto, StringComparison.Ordinal);

        // The message the response used to be wrapped in is gone, rather than left beside the field that replaced it:
        // a descriptor that declares both an empty message and the Struct is one more thing to consume and not one
        // more thing that was said.
        Assert.DoesNotContain("GetThingsData", proto, StringComparison.Ordinal);
    }

    /// <summary>An array of them is a repeated Struct on the response, not a repeated placeholder.</summary>
    [Fact]
    public void An_Array_Of_Free_Form_Responses_Maps_To_Repeated_Struct()
    {
        var result = GeneratorTestDriver.Run(
            ResponseSpec("{ type: array, items: { type: object, additionalProperties: true } }"),
            serviceName: "FreeFormService");

        Assert.Contains(
            "public global::Google.Protobuf.Collections.RepeatedField<global::Google.Protobuf.WellKnownTypes.Struct> Items { get; } = new();",
            result.GeneratedSource,
            StringComparison.Ordinal);
    }

    /// <summary>A body that says "any JSON object" is a Struct too - the same question in another position.</summary>
    [Fact]
    public void A_Free_Form_Request_Body_Maps_To_Struct()
    {
        var result = GeneratorTestDriver.Run(
            BodySpec("{ type: object, additionalProperties: true }"),
            serviceName: "FreeFormService");
        var sent = ClassBody(result.GeneratedSource, "SendThingsRequest");

        Assert.Contains(
            "public global::Google.Protobuf.WellKnownTypes.Struct Body { get; set; } = null!;",
            sent,
            StringComparison.Ordinal);
        Assert.DoesNotContain("_HasValue", sent, StringComparison.Ordinal);
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

    /// <summary>A one-operation specification: a GET that answers what the response schema says.</summary>
    /// <param name="responseSchema">The response body's schema, as YAML.</param>
    /// <returns>The specification.</returns>
    private static string ResponseSpec(string responseSchema) => string.Join(
        '\n',
        "openapi: 3.0.3",
        "info: { title: FreeForm, version: 1.0.0 }",
        "paths:",
        "  /things:",
        "    get:",
        "      operationId: GetThings",
        "      responses:",
        "        '200':",
        "          description: ok",
        "          content:",
        "            application/json:",
        "              schema: " + responseSchema);

    /// <summary>A one-operation specification: a POST that sends what the body schema says.</summary>
    /// <param name="bodySchema">The request body's schema, as YAML.</param>
    /// <returns>The specification.</returns>
    private static string BodySpec(string bodySchema) => string.Join(
        '\n',
        "openapi: 3.0.3",
        "info: { title: FreeForm, version: 1.0.0 }",
        "paths:",
        "  /things:",
        "    post:",
        "      operationId: SendThings",
        "      requestBody:",
        "        required: true",
        "        content:",
        "          application/json:",
        "            schema: " + bodySchema,
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
        "        name: { type: string }");
}
