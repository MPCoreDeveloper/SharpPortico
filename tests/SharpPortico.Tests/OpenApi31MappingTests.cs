using System;
using System.Linq;
using SharpPortico.Tests.Infrastructure;
using Xunit;

namespace SharpPortico.Tests;

/// <summary>
/// The constructs only a 3.1 document has, and what the 3.0 document the generator reads is given in their
/// place: the protobuf type each one maps to, and the loss the caller is told about.
/// </summary>
/// <remarks>
/// The downgrade rewrites the document's nodes rather than its text, so a property named <c>type</c> is a name
/// and not a type, a union in flow style is the union in block style, and a <c>const</c> inside an
/// <c>example</c> is data that is left alone.
/// </remarks>
public class OpenApi31MappingTests
{
    private static string Spec(string properties) => Spec("3.1.0", properties);

    private static string Spec(string version, string properties) => Lines(
        "openapi: " + version,
        "info: { title: Pets, version: 1.0.0 }",
        "paths:",
        "  /pets:",
        "    post:",
        "      operationId: CreatePet",
        "      requestBody:",
        "        required: true",
        "        content:",
        "          application/json:",
        "            schema: { $ref: '#/components/schemas/Pet' }",
        "      responses:",
        "        '200':",
        "          description: ok",
        "          content:",
        "            application/json:",
        "              schema: { $ref: '#/components/schemas/Pet' }",
        "components:",
        "  schemas:",
        "    Pet:",
        "      type: object",
        "      properties:",
        "        " + properties);

    private static string Lines(params string[] lines) => string.Join('\n', lines);

    /// <summary>The descriptor one property declaration produces.</summary>
    /// <param name="properties">The property to map, at the indentation it would have in a document.</param>
    /// <returns>The .proto text.</returns>
    private static string Proto(string properties)
        => GeneratorTestDriver.Proto(Spec(properties), serviceName: "PetService");

    /// <summary>The pipeline's result for one property declaration.</summary>
    /// <param name="properties">The property to map, at the indentation it would have in a document.</param>
    /// <returns>The result.</returns>
    private static GeneratorTestDriver.RunResult Run(string properties)
        => GeneratorTestDriver.Run(Spec(properties), serviceName: "PetService");

    /// <summary>The diagnostics of one identifier, which each test here reads by what it says.</summary>
    /// <param name="id">The diagnostic identifier.</param>
    /// <param name="result">The run to read.</param>
    /// <returns>The rendered diagnostics.</returns>
    private static string[] Diagnostics(string id, GeneratorTestDriver.RunResult result)
        => result.Diagnostics.Where(d => d.StartsWith(id, StringComparison.Ordinal)).ToArray();

    /// <summary>The one SP1003 of a document, whose note says what the mapping does not carry over.</summary>
    /// <param name="result">The run to read.</param>
    /// <returns>The rendered diagnostic.</returns>
    private static string Loss(GeneratorTestDriver.RunResult result)
        => Diagnostics("SP1003", result).Single();

    /// <summary>A declaration as the descriptor writes one: the name, then its body below it.</summary>
    /// <param name="name">The message a test looks for.</param>
    /// <returns>The text that opens the declaration, which a name of its own cannot also spell.</returns>
    private static string Message(string name) => "message " + name + "\n{";

    /// <summary>
    /// A 3.1 type array that names one real type names a single type, and 3.1 writes it where 3.0 wrote
    /// <c>nullable: true</c>.
    /// </summary>
    [Fact]
    public void A_Type_Array_With_One_Real_Type_Maps_As_That_Type()
    {
        const string properties = "name: { type: [string, 'null'] }";
        var result = Run(properties);

        Assert.Contains("string name = ", Proto(properties), StringComparison.Ordinal);
        Assert.Single(Diagnostics("SP1002", result));

        // A nullable string is a string: nothing about it is beyond what the mapping carries, so nothing is lost.
        Assert.Empty(Diagnostics("SP1003", result));
    }

    /// <summary>
    /// YAML writes the null type as a bare literal as often as JSON Schema writes it quoted, and both are the
    /// same type.
    /// </summary>
    [Fact]
    public void An_Unquoted_Null_Is_The_Same_Null()
    {
        const string properties = "name: { type: [string, ~] }";
        var result = Run(properties);

        Assert.Contains("string name = ", Proto(properties), StringComparison.Ordinal);
        Assert.Empty(Diagnostics("SP1003", result));
    }

    /// <summary>A union of several types is the JSON value protobuf has a type for.</summary>
    [Fact]
    public void A_Union_Of_Types_Becomes_An_Arbitrary_Json_Value()
    {
        const string properties = "payload: { type: [object, array] }";
        var result = Run(properties);
        var proto = GeneratorTestDriver.Proto(Spec(properties), serviceName: "PetService");

        Assert.Contains("google.protobuf.Value payload = ", proto, StringComparison.Ordinal);

        // ...which the descriptor has to import, and which the generated C# has to name as the consumer's type.
        Assert.Contains("import \"google/protobuf/struct.proto\";", proto, StringComparison.Ordinal);
        Assert.Contains(
            "global::Google.Protobuf.WellKnownTypes.Value Payload",
            result.GeneratedSource,
            StringComparison.Ordinal);

        var loss = Loss(result);
        Assert.Contains("the type array at components/schemas/Pet/properties/payload", loss, StringComparison.Ordinal);
        Assert.Contains("any one of {object, array}", loss, StringComparison.Ordinal);
    }

    /// <summary>The marker is a statement a consumer may make by hand, and an explicit <c>false</c> withdraws it.</summary>
    [Fact]
    public void The_Json_Value_Marker_Can_Be_Set_And_Withdrawn_By_Hand()
    {
        var asked = Run("payload: { type: object, x-sharpportico-json-value: true }");
        var declined = Run("payload: { type: object, x-sharpportico-json-value: false }");

        Assert.Contains(
            "global::Google.Protobuf.WellKnownTypes.Value Payload",
            asked.GeneratedSource,
            StringComparison.Ordinal);

        // With the marker withdrawn, the object is the free-form object it always was.
        Assert.Contains(
            "global::Google.Protobuf.WellKnownTypes.Struct Payload",
            declined.GeneratedSource,
            StringComparison.Ordinal);
    }

    /// <summary>A tuple whose positions differ maps to a repeated JSON value.</summary>
    [Fact]
    public void A_Tuple_Of_Mixed_Positions_Repeats_An_Arbitrary_Json_Value()
    {
        const string properties =
            "bounds:\n" +
            "          type: array\n" +
            "          prefixItems:\n" +
            "            - { type: string }\n" +
            "            - { type: integer }";

        var result = Run(properties);
        var proto = GeneratorTestDriver.Proto(Spec(properties), serviceName: "PetService");

        Assert.Contains("repeated google.protobuf.Value bounds = ", proto, StringComparison.Ordinal);
        Assert.Contains("import \"google/protobuf/struct.proto\";", proto, StringComparison.Ordinal);

        var loss = Loss(result);
        Assert.Contains("the tuple at components/schemas/Pet/properties/bounds", loss, StringComparison.Ordinal);
        Assert.Contains("fixed length", loss, StringComparison.Ordinal);
    }

    /// <summary>A tuple whose positions agree maps to a repeated field of the one type they name.</summary>
    [Fact]
    public void A_Tuple_Whose_Positions_Agree_Repeats_That_Type()
    {
        const string properties =
            "pixels:\n" +
            "          type: array\n" +
            "          prefixItems:\n" +
            "            - { type: integer }\n" +
            "            - { type: integer }";

        var proto = Proto(properties);

        Assert.Contains("repeated int32 pixels = ", proto, StringComparison.Ordinal);
        Assert.DoesNotContain("google.protobuf.Value", proto, StringComparison.Ordinal);

        // The length is gone either way, and the caller is told that as well.
        Assert.Contains("the tuple at components/schemas/Pet/properties/pixels", Loss(Run(properties)), StringComparison.Ordinal);
    }

    /// <summary>3.1 makes <c>type</c> optional on a schema whose <c>items</c> already say it is an array.</summary>
    [Fact]
    public void An_Array_Without_A_Type_Of_Its_Own_Is_Still_An_Array()
    {
        const string properties =
            "tags:\n" +
            "          items: { type: string }";

        Assert.Contains("repeated string tags = ", Proto(properties), StringComparison.Ordinal);
    }

    /// <summary>
    /// A schema whose only allowed value is <c>X</c> is the enumeration of one member the mapper already reads.
    /// </summary>
    [Fact]
    public void A_Const_Becomes_A_Single_Member_Enumeration()
    {
        var result = Run("kind: { const: pet }");

        Assert.Contains("public enum KindEnum", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Contains("Pet = 0", result.GeneratedSource, StringComparison.Ordinal);

        // The enumeration says exactly what the constant said, so there is no loss to report.
        Assert.Empty(Diagnostics("SP1003", result));
    }

    /// <summary>A base64 string is the bytes it encodes, which protobuf has a type for.</summary>
    [Fact]
    public void A_Base64_Content_Encoding_Maps_To_Bytes()
    {
        const string properties = "blob: { type: string, contentEncoding: base64 }";
        var result = Run(properties);

        Assert.Contains("bytes blob = ", Proto(properties), StringComparison.Ordinal);
        Assert.Empty(Diagnostics("SP1003", result));
    }

    /// <summary>An encoding the mapping has no equivalent for leaves the field as the string it is, and says so.</summary>
    [Fact]
    public void An_Encoding_With_No_Protobuf_Equivalent_Is_Reported()
    {
        const string properties = "body: { type: string, contentEncoding: gzip }";
        var result = Run(properties);
        var proto = GeneratorTestDriver.Proto(Spec(properties), serviceName: "PetService");

        Assert.Contains("string body = ", proto, StringComparison.Ordinal);

        var loss = Loss(result);
        Assert.Contains("the contentEncoding 'gzip' at components/schemas/Pet/properties/body", loss, StringComparison.Ordinal);
        Assert.Contains("travels as the string it is", loss, StringComparison.Ordinal);
    }

    /// <summary>
    /// 3.1 dropped the flag that made <c>minimum</c> exclusive, and a number where the 3.0 reader expects a flag
    /// makes it refuse the whole document.
    /// </summary>
    [Fact]
    public void A_Numeric_Exclusive_Bound_Parses_As_The_Bound_It_Is()
    {
        const string properties = "age: { type: integer, exclusiveMinimum: 0 }";
        var result = Run(properties);

        Assert.Contains("int32 age = ", Proto(properties), StringComparison.Ordinal);
        Assert.Contains("a numeric exclusive bound", Diagnostics("SP1002", result).Single(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A definition below the root is not somewhere the mapper looks, so it is hoisted to the place it does and
    /// the reference that named it is repointed at it.
    /// </summary>
    [Fact]
    public void A_Definition_Is_Hoisted_And_Its_Reference_Resolves()
    {
        var spec = Lines(
            "openapi: 3.1.0",
            "info: { title: Pets, version: 1.0.0 }",
            "paths:",
            "  /pets:",
            "    get:",
            "      operationId: ListPets",
            "      responses:",
            "        '200':",
            "          description: ok",
            "          content:",
            "            application/json:",
            "              schema: { $ref: '#/components/schemas/Pet' }",
            "components:",
            "  schemas:",
            "    Pet:",
            "      type: object",
            "      properties:",
            "        tag: { $ref: '#/$defs/Tag' }",
            "$defs:",
            "  Tag:",
            "    type: object",
            "    properties:",
            "      label: { type: string }");

        var result = GeneratorTestDriver.Run(spec, serviceName: "PetService");
        var proto = GeneratorTestDriver.Proto(spec, serviceName: "PetService");

        // The definition is declared as the message its reference names...
        Assert.Contains(Message("Tag"), proto, StringComparison.Ordinal);
        Assert.Contains("Tag tag = ", proto, StringComparison.Ordinal);

        // ...and neither the definition nor the reference to it is left in the document.
        Assert.DoesNotContain("$defs", proto, StringComparison.Ordinal);
        Assert.DoesNotContain("$defs", result.GeneratedSource, StringComparison.Ordinal);
        Assert.Empty(Diagnostics("SP1003", result));
    }

    /// <summary>
    /// A webhook is an operation the document declares, and a gRPC method is the only shape this mapping has for
    /// one - so it becomes an RPC, and the note says what that RPC does not mean.
    /// </summary>
    [Fact]
    public void A_Webhook_Becomes_An_Rpc_And_Says_What_That_Does_Not_Mean()
    {
        var spec = Lines(
            "openapi: 3.1.0",
            "info: { title: Pets, version: 1.0.0 }",
            "paths: {}",
            "webhooks:",
            "  petDeleted:",
            "    post:",
            "      operationId: OnPetDeleted",
            "      requestBody:",
            "        required: true",
            "        content:",
            "          application/json:",
            "            schema: { $ref: '#/components/schemas/Pet' }",
            "      responses:",
            "        '200': { description: ok }",
            "components:",
            "  schemas:",
            "    Pet:",
            "      type: object",
            "      properties:",
            "        id: { type: integer, format: int64 }");

        var result = GeneratorTestDriver.Run(spec, serviceName: "PetService");

        Assert.Contains("OnPetDeletedAsync", result.GeneratedSource, StringComparison.Ordinal);

        // The note is about the mapping, not about one webhook: it is the direction every webhook loses.
        var loss = Loss(result);
        Assert.Contains("1 webhook operation(s) were mapped to RPCs", loss, StringComparison.Ordinal);
        Assert.Contains("a client method for one describes a payload and not a call the server accepts", loss, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rewrite reads the document's nodes, so the notation it was written in makes no difference: the same
    /// document as JSON is the same document.
    /// </summary>
    [Fact]
    public void A_Json_31_Document_Is_Rewritten_Too()
    {
        const string spec = "{\"openapi\":\"3.1.0\",\"info\":{\"title\":\"Pets\",\"version\":\"1.0.0\"}," +
            "\"paths\":{\"/pets\":{\"post\":{\"operationId\":\"CreatePet\",\"requestBody\":{\"required\":true," +
            "\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/Pet\"}}}}," +
            "\"responses\":{\"200\":{\"description\":\"ok\",\"content\":{\"application/json\":{\"schema\":" +
            "{\"$ref\":\"#/components/schemas/Pet\"}}}}}}}},\"components\":{\"schemas\":{\"Pet\":{\"type\":\"object\"," +
            "\"properties\":{\"payload\":{\"type\":[\"object\",\"array\"]}}}}}}";

        var result = GeneratorTestDriver.Run(spec, serviceName: "PetService");
        var proto = GeneratorTestDriver.Proto(spec, serviceName: "PetService");

        Assert.Single(Diagnostics("SP1002", result));
        Assert.Contains("google.protobuf.Value payload = ", proto, StringComparison.Ordinal);
        Assert.Contains("CreatePetAsync", result.GeneratedSource, StringComparison.Ordinal);
    }

    /// <summary>A 3.0 document is the input the reader already takes, so nothing is rewritten and nothing is said.</summary>
    [Fact]
    public void A_30_Document_Is_Mapped_Without_Any_Rewrite()
    {
        var result = GeneratorTestDriver.Run(
            Spec("3.0.3", "payload: { type: object, additionalProperties: true }"),
            serviceName: "PetService");

        Assert.Empty(result.Diagnostics);
    }

    /// <summary>
    /// A 3.1 document that uses no 3.1 construct still goes through the rewrite and comes out where the 3.0
    /// document does: the pass is the version declaration plus whatever the document actually contains.
    /// </summary>
    [Fact]
    public void A_Document_That_Uses_No_31_Construct_Is_Mapped_The_Same_Whichever_Version_It_Declares()
    {
        const string properties = "id: { type: integer, format: int64 }";

        var declared31 = GeneratorTestDriver.Proto(Spec(properties), serviceName: "PetService");
        var declared30 = GeneratorTestDriver.Proto(Spec("3.0.3", properties), serviceName: "PetService");

        Assert.Equal(declared30, declared31);
    }

    /// <summary>Every construct the downgrade knows about, in one document, reaches the model.</summary>
    [Fact]
    public void Every_31_Construct_Reaches_The_Model_As_Its_30_Equivalent()
    {
        var spec = EverythingSpec();
        var result = GeneratorTestDriver.Run(spec, serviceName: "PetService");
        var proto = GeneratorTestDriver.Proto(spec, serviceName: "PetService");
        var source = result.GeneratedSource;

        // The types the constructs map to.
        Assert.Contains("string name = ", proto, StringComparison.Ordinal);
        Assert.Contains("google.protobuf.Value payload = ", proto, StringComparison.Ordinal);
        Assert.Contains("repeated google.protobuf.Value bounds = ", proto, StringComparison.Ordinal);
        Assert.Contains("repeated int32 pixels = ", proto, StringComparison.Ordinal);
        Assert.Contains("bytes blob = ", proto, StringComparison.Ordinal);
        Assert.Contains("int32 age = ", proto, StringComparison.Ordinal);
        Assert.Contains(Message("Tag"), proto, StringComparison.Ordinal);
        Assert.Contains("Tag tag = ", proto, StringComparison.Ordinal);
        Assert.Contains("import \"google/protobuf/struct.proto\";", proto, StringComparison.Ordinal);

        // The RPC surface: the operations the paths declare, and the one the webhooks declare.
        Assert.Contains("CreatePetAsync", source, StringComparison.Ordinal);
        Assert.Contains("OnPetDeletedAsync", source, StringComparison.Ordinal);

        // The rewrite says what it did...
        Assert.Contains("webhooks", Diagnostics("SP1002", result).Single(), StringComparison.Ordinal);

        // ...and what each mapping left behind, once per construct - and nothing for the constructs that lose
        // nothing: a nullable string, a constant, base64 bytes and a bound all map exactly.
        var losses = Diagnostics("SP1003", result);
        Assert.Contains(losses, d => d.Contains("jsonSchemaDialect", StringComparison.Ordinal));
        Assert.Contains(losses, d => d.Contains("the type array at components/schemas/Pet/properties/payload", StringComparison.Ordinal));
        Assert.Contains(losses, d => d.Contains("the tuple at components/schemas/Pet/properties/bounds", StringComparison.Ordinal));
        Assert.Contains(losses, d => d.Contains("the tuple at components/schemas/Pet/properties/pixels", StringComparison.Ordinal));
        Assert.Contains(losses, d => d.Contains("1 webhook operation(s) were mapped to RPCs", StringComparison.Ordinal));

        // One note per construct that loses something, and none for the four that do not.
        Assert.Equal(5, losses.Length);
    }

    /// <summary>
    /// The code a 3.1 document produces is compiled, not just inspected: the well-known types a union and a tuple
    /// bring in have to name C# types that exist, in every shape they appear in.
    /// </summary>
    [Fact]
    public void The_Generated_Code_For_A_31_Contract_Compiles()
    {
        var result = GeneratorTestDriver.Run(EverythingSpec(), serviceName: "PetService");

        // A mapping that cannot be carried out is an SP2xxx error rather than something to find in the emission.
        Assert.Empty(result.Diagnostics.Where(d => d.StartsWith("SP2", StringComparison.Ordinal)));
        Assert.Empty(GeneratedCodeCompiler.Errors(result.Sources.Values));
    }

    /// <summary>A 3.1 document declaring every construct this mapping rewrites.</summary>
    /// <returns>The specification.</returns>
    private static string EverythingSpec() => Lines(
        "openapi: 3.1.0",
        "info: { title: Pets, version: 1.0.0 }",
        "jsonSchemaDialect: https://json-schema.org/draft/2020-12/schema",
        "paths:",
        "  /pets:",
        "    post:",
        "      operationId: CreatePet",
        "      requestBody:",
        "        required: true",
        "        content:",
        "          application/json:",
        "            schema: { $ref: '#/components/schemas/Pet' }",
        "      responses:",
        "        '200':",
        "          description: ok",
        "          content:",
        "            application/json:",
        "              schema: { $ref: '#/components/schemas/Pet' }",
        "webhooks:",
        "  petDeleted:",
        "    post:",
        "      operationId: OnPetDeleted",
        "      requestBody:",
        "        required: true",
        "        content:",
        "          application/json:",
        "            schema: { $ref: '#/components/schemas/Pet' }",
        "      responses:",
        "        '200': { description: ok }",
        "components:",
        "  schemas:",
        "    Pet:",
        "      type: object",
        "      properties:",
        "        name: { type: [string, 'null'] }",
        "        kind: { const: pet }",
        "        payload: { type: [object, array] }",
        "        bounds:",
        "          type: array",
        "          prefixItems:",
        "            - { type: string }",
        "            - { type: integer }",
        "        pixels:",
        "          type: array",
        "          prefixItems:",
        "            - { type: integer }",
        "            - { type: integer }",
        "        blob: { type: string, contentEncoding: base64 }",
        "        age: { type: integer, exclusiveMinimum: 0 }",
        "        extra: { type: object, x-sharpportico-json-value: true }",
        "        tag: { $ref: '#/$defs/Tag' }",
        "$defs:",
        "  Tag:",
        "    type: object",
        "    properties:",
        "      label: { type: string }");
}
