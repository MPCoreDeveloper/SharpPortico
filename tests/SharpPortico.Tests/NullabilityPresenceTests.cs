using System;
using System.Linq;
using Google.Protobuf;
using SharpPortico.Tests.Infrastructure;
using Xunit;

namespace SharpPortico.Tests;

/// <summary>
/// A property the contract declares nullable keeps that meaning all the way out: it becomes a proto3
/// <c>optional</c> field, so "absent" and "set to the default" stop being the same answer.
/// </summary>
/// <remarks>
/// The mapping used to drop the flag, because a proto3 scalar has no presence of its own: a nullable
/// <c>retries</c> arrived as a 0 that a consumer could not tell from a value nobody had sent, and a nullable
/// string arrived as an empty one. The assertions here are on the descriptor, on the emitted members, and - for
/// the part that only exists at run time - on the bytes an emitted message writes.
/// </remarks>
public class NullabilityPresenceTests
{
    /// <summary>A contract with one schema, whose properties the caller spells.</summary>
    /// <param name="properties">The YAML properties of <c>Thing</c>, already indented.</param>
    /// <returns>The specification.</returns>
    private static string Spec(params string[] properties) => string.Join(
        '\n',
        new[]
        {
            "openapi: 3.0.3",
            "info: { title: Things, version: 1.0.0 }",
            "paths:",
            "  /things:",
            "    get:",
            "      operationId: GetThing",
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
        }.Concat(properties));

    [Fact]
    public void A_Nullable_Scalar_Becomes_An_Optional_Field_With_Presence_Members()
    {
        var spec = Spec(
            "        retries: { type: integer, nullable: true }",
            "        nickname: { type: string, nullable: true }",
            "        status: { type: string, enum: [available, sold], nullable: true }",
            "        blob: { type: string, format: byte, nullable: true }",
            "        name: { type: string }");

        var result = GeneratorTestDriver.Run(
            spec, serviceName: "ThingService", namespaceName: "SharpPortico.Samples.Generated");
        Assert.Empty(result.Diagnostics);

        var proto = GeneratorTestDriver.Proto(spec, serviceName: "ThingService");
        Assert.Contains("optional int32 retries = ", proto, StringComparison.Ordinal);
        Assert.Contains("optional string nickname = ", proto, StringComparison.Ordinal);
        Assert.Contains("optional StatusEnum status = ", proto, StringComparison.Ordinal);
        Assert.Contains("optional bytes blob = ", proto, StringComparison.Ordinal);

        // A property that did not ask to be nullable stays a plain proto3 field: the label is a fact about the
        // contract, not a new default for every scalar.
        Assert.Contains("string name = ", proto, StringComparison.Ordinal);
        Assert.DoesNotContain("optional string name", proto, StringComparison.Ordinal);

        var source = result.GeneratedSource;
        Assert.Contains("public bool HasRetries => _has_Retries;", source, StringComparison.Ordinal);
        Assert.Contains("public void ClearRetries()", source, StringComparison.Ordinal);
        Assert.Contains("public bool HasNickname", source, StringComparison.Ordinal);
        Assert.Contains("public bool HasStatus", source, StringComparison.Ordinal);
        Assert.Contains("public bool HasBlob", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HasName", source, StringComparison.Ordinal);

        // A contract that maps cleanly still has to compile, and this one exercises the value, reference and
        // enum shapes of the presence members.
        Assert.Empty(GeneratedCodeCompiler.Errors(result.Sources.Values));
    }

    [Fact]
    public void Absent_And_Set_To_The_Default_Are_Different_Answers()
    {
        var spec = Spec(
            "        retries: { type: integer, nullable: true }",
            "        name: { type: string }");

        var result = GeneratorTestDriver.Run(
            spec, serviceName: "ThingService", namespaceName: "SharpPortico.Samples.Generated");
        Assert.Empty(result.Diagnostics);

        var (assembly, errors) = GeneratedCodeCompiler.Load(result.Sources.Values);
        Assert.Empty(errors);
        var type = assembly!.GetType("SharpPortico.Samples.Generated.Thing", throwOnError: true)!;

        var absent = Activator.CreateInstance(type)!;
        var setToZero = Activator.CreateInstance(type)!;
        type.GetProperty("Retries")!.SetValue(setToZero, 0);

        // The two hold the same value and are not the same field.
        Assert.False(Presence(absent, "Retries"));
        Assert.True(Presence(setToZero, "Retries"));
        Assert.Empty(Serialize(absent));
        Assert.NotEmpty(Serialize(setToZero));
        Assert.False(absent.Equals(setToZero));

        // ...and the difference survives the wire: an explicit default is written and reads back as set.
        var readBack = ReadBack(type, Serialize(setToZero));
        Assert.True(Presence(readBack, "Retries"));
        Assert.Equal(0, (int)type.GetProperty("Retries")!.GetValue(readBack)!);

        // Cloning and merging copy the field, not just its value.
        var clone = type.GetMethod("Clone")!.Invoke(setToZero, null)!;
        Assert.True(Presence(clone, "Retries"));
        Assert.Equal(Serialize(setToZero), Serialize(clone));
        Assert.Equal(setToZero, clone);

        var merged = Activator.CreateInstance(type)!;
        type.GetMethod("MergeFrom", new[] { type })!.Invoke(merged, new[] { absent });
        Assert.False(Presence(merged, "Retries"));

        // Clearing returns the field to "never set", which is not the same as writing the default into it.
        type.GetMethod("ClearRetries")!.Invoke(setToZero, null);
        Assert.False(Presence(setToZero, "Retries"));
        Assert.Empty(Serialize(setToZero));
    }

    [Fact]
    public void The_Gateway_Asks_The_Field_Whether_It_Was_Set()
    {
        var spec = string.Join(
            '\n',
            "openapi: 3.0.3",
            "info: { title: Things, version: 1.0.0 }",
            "paths:",
            "  /things:",
            "    get:",
            "      operationId: GetThing",
            "      parameters:",
            "        - name: retries",
            "          in: query",
            "          schema: { type: integer, nullable: true }",
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
            "        retries: { type: integer, nullable: true }");

        var result = GeneratorTestDriver.Run(
            spec,
            serviceName: "ThingService",
            namespaceName: "SharpPortico.Samples.Generated",
            enableProxy: true);
        Assert.Empty(result.Diagnostics);

        var source = result.GeneratedSource;

        // A parameter travels when it was set, so an explicit 0 is sent rather than dropped.
        Assert.Contains("if (request.HasRetries)", source, StringComparison.Ordinal);

        // A JSON null is the absence the field was given presence for, not a value to read as 0.
        Assert.Contains(
            "if (_retries.ValueKind != global::System.Text.Json.JsonValueKind.Null)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("if (msg.HasRetries)", source, StringComparison.Ordinal);

        // A field nobody set is written as null, not as a default nobody sent.
        Assert.Contains("else sb.Append(\"null\");", source, StringComparison.Ordinal);

        Assert.Empty(GeneratedCodeCompiler.Errors(result.Sources.Values));
    }

    [Fact]
    public void A_Nullable_Array_Or_Element_Is_Reported_Because_A_Repeated_Field_Cannot_Carry_It()
    {
        var spec = Spec(
            "        tags: { type: array, nullable: true, items: { type: string, nullable: true } }");

        var result = GeneratorTestDriver.TryRun(spec, serviceName: "ThingService");
        var reported = result.Diagnostics
            .Where(static d => d.StartsWith("SP2000", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(2, reported.Length);
        Assert.Contains(reported, d => d.Contains(" tags ", StringComparison.Ordinal)
            && d.Contains("cannot be null", StringComparison.Ordinal));
        Assert.Contains(reported, d => d.Contains("tags[]", StringComparison.Ordinal)
            && d.Contains("per-element presence", StringComparison.Ordinal));

        // A warning, not a refusal: the contract still maps, and the field is still repeated.
        Assert.NotEmpty(result.Sources);
        var proto = GeneratorTestDriver.Proto(spec, serviceName: "ThingService");
        Assert.Contains("repeated string tags = ", proto, StringComparison.Ordinal);
        Assert.DoesNotContain("optional", proto, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Presence_Member_The_Schema_Also_Declares_Is_Refused()
    {
        var spec = Spec(
            "        more: { type: integer, nullable: true }",
            "        hasMore: { type: boolean }");

        var result = GeneratorTestDriver.TryRun(spec, serviceName: "ThingService");

        // 'hasMore' becomes the HasMore member the nullable 'more' needs, so both cannot exist.
        var refusal = Assert.Single(result.Diagnostics);
        Assert.StartsWith("SP2008", refusal, StringComparison.Ordinal);
        Assert.Contains("HasMore", refusal, StringComparison.Ordinal);
        Assert.Empty(result.Sources);
    }

    /// <summary>The presence flag of a field, read the way a consumer reads it.</summary>
    /// <param name="message">The message.</param>
    /// <param name="field">The field's name.</param>
    /// <returns>Whether the field was set.</returns>
    private static bool Presence(object message, string field)
        => (bool)message.GetType().GetProperty("Has" + field)!.GetValue(message)!;

    /// <summary>Writes a message the way the gRPC stack would.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The bytes it occupies.</returns>
    private static byte[] Serialize(object message)
    {
        var encoded = (IMessage)message;
        var buffer = new byte[encoded.CalculateSize()];
        var output = new CodedOutputStream(buffer);
        encoded.WriteTo(output);
        output.Flush();
        return buffer;
    }

    /// <summary>Reads bytes back into a new message the way the gRPC stack would.</summary>
    /// <param name="type">The message type.</param>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The message.</returns>
    private static object ReadBack(Type type, byte[] bytes)
    {
        var message = (IMessage)Activator.CreateInstance(type)!;
        message.MergeFrom(new CodedInputStream(bytes));
        return message;
    }
}
