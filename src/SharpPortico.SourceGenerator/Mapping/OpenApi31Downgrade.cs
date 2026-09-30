using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using SharpPortico.Generator.Model;
using SharpYaml.Serialization;

namespace SharpPortico.Generator.Mapping;

/// <summary>
/// Rewrites the OpenAPI 3.1 constructs the bundled parser (Microsoft.OpenApi 1.6.x) cannot read into the
/// 3.0 form it can, so a 3.1 document maps instead of being refused or silently half-read.
/// </summary>
/// <remarks>
/// <para>
/// The parser has no 3.1 support at all - it rejects the version outright - so a 3.1 document is rewritten
/// before it is parsed. The rewrite runs on a document tree rather than on the text: a text rewrite of
/// <c>type</c> would have to recognise a YAML flow sequence, a block sequence and a JSON array as the same
/// thing, and it could not tell a schema's <c>type</c> from a property called <c>type</c>.
/// </para>
/// <para>
/// Every construct rewritten here has a mapping, not a fallback: a type array becomes the one type it names
/// (or <c>google.protobuf.Value</c> when it names several), a <c>const</c> becomes the single-member
/// enumeration it already is, a reference into <c>$defs</c> is hoisted to where the parser can resolve it,
/// and a webhook becomes an RPC like every other operation. What a construct loses by being mapped - a
/// tuple's fixed length, a webhook's direction - is reported rather than passed over.
/// </para>
/// <para>
/// A 3.0 document is returned untouched, byte for byte, so nothing here can change what an existing
/// consumer generates. When the tree rewrite cannot read a document it falls back to the version rewrite
/// this generator shipped before, which promises less than the tree rewrite but never less than before.
/// </para>
/// </remarks>
internal static class OpenApi31Downgrade
{
    /// <summary>
    /// The marker a schema holding an arbitrary JSON value - a JSON Schema type union, or a tuple whose
    /// positions hold different types - is rewritten to.
    /// </summary>
    /// <remarks>
    /// <c>google.protobuf.Value</c> is protobuf's type for exactly that, and a marker is what keeps the
    /// distinction a plain <c>type: object</c> would lose: an object is a map of keys, a union is any value
    /// at all. The mapper reads this keyword, so a consumer may also set it by hand to ask for
    /// <c>Value</c> on a schema whose JSON type the mapping cannot infer.
    /// </remarks>
    internal const string JsonValueKeyword = "x-sharpportico-json-value";

    /// <summary>The version a 3.1 document is declared as, so the 1.6.x reader accepts it.</summary>
    private const string DowngradedVersion = "3.0.3";

    /// <summary>A 3.1 document's declared version, in YAML (block) or JSON (flow) notation.</summary>
    private static readonly Regex Declared31Yaml = new(
        @"(?m)^(?<head>\s*openapi\s*:\s*[""']?)3\.1(?:\.\d+)?(?<tail>[""']?\s*)$",
        RegexOptions.Compiled);

    /// <summary>The same declaration written as JSON, where the key and the version are quoted.</summary>
    private static readonly Regex Declared31Json = new(
        @"(?m)(?<head>""openapi""\s*:\s*"")3\.1(?:\.\d+)?(?<tail>"")",
        RegexOptions.Compiled);

    /// <summary>
    /// Keys whose values are data rather than a schema, so a schema rewrite never reaches them: an
    /// <c>example</c> is a JSON document that may contain any key at all, including <c>const</c> or a
    /// <c>type</c> array, and rewriting one would corrupt the example.
    /// </summary>
    private static readonly HashSet<string> DataKeywords = new(StringComparer.Ordinal)
    {
        "example", "examples", "default", "enum", "const",
    };

    /// <summary>
    /// Keys whose value is a mapping of <em>names</em> to schemas. Those names are the consumer's, so the
    /// mapping holding them is not itself a schema - only its values are.
    /// </summary>
    private static readonly HashSet<string> SchemaNameContainers = new(StringComparer.Ordinal)
    {
        "properties", "patternProperties", "definitions", "$defs", "schemas",
    };

    /// <summary>Keys whose value is a schema, or - for the composition keys - a list of schemas.</summary>
    private static readonly HashSet<string> SchemaValuedKeywords = new(StringComparer.Ordinal)
    {
        "schema", "items", "prefixItems", "additionalProperties", "additionalItems", "unevaluatedProperties",
        "unevaluatedItems", "propertyNames", "contains", "contentSchema", "not", "if", "then", "else",
        "allOf", "oneOf", "anyOf",
    };

    /// <summary>
    /// Prepares a document for the 3.0-only reader. Returns the content unchanged for a 3.0 document.
    /// </summary>
    /// <param name="content">The specification's text.</param>
    /// <param name="item">The work item, for the file name the diagnostics name.</param>
    /// <param name="diags">The diagnostics collected so far, which this appends to.</param>
    /// <returns>The text to parse.</returns>
    public static string Prepare(
        string content, OpenApiWorkItem item, ImmutableArray<GeneratorDiagnostic>.Builder diags)
    {
        if (!DeclaresOpenApi31(content))
        {
            return content;
        }

        var notes = new List<(string What, string Why)>();

        try
        {
            var stream = new YamlStream();
            using (var text = new StringReader(content))
            {
                stream.Load(text);
            }

            if (stream.Documents.Count > 0 && stream.Documents[0].RootNode is YamlMappingNode root)
            {
                var rewrites = Rewrite(root, notes);

                var output = new StringWriter();
                stream.Save(output, true, 2);

                diags.Add(new GeneratorDiagnostic(
                    Diagnostics.Diagnostics.OpenApi31ParsedAs30,
                    new object[] { item.FilePath, rewrites }));

                foreach (var (what, why) in notes)
                {
                    diags.Add(new GeneratorDiagnostic(
                        Diagnostics.Diagnostics.OpenApi31FidelityLoss,
                        new object[] { what, item.FilePath, why }));
                }

                return output.ToString();
            }
        }
        catch (Exception)
        {
            // A document the tree rewrite cannot read is still a 3.1 document and the reader still refuses
            // it, so the fallback below rewrites the version alone rather than letting it through unparsed.
        }

        return RewriteDeclaredVersion(content, item, diags);
    }

    /// <summary>True when the document declares OpenAPI 3.1, in either notation.</summary>
    /// <param name="content">The specification's text.</param>
    /// <returns>Whether it is a 3.1 document.</returns>
    private static bool DeclaresOpenApi31(string content)
        => Declared31Yaml.IsMatch(content) || Declared31Json.IsMatch(content);

    /// <summary>
    /// The version rewrite on its own: everything this generator promised a 3.1 document before the tree
    /// rewrite existed, kept as the fallback for a document the tree cannot hold.
    /// </summary>
    /// <param name="content">The specification's text.</param>
    /// <param name="item">The work item, for the file name the diagnostic names.</param>
    /// <param name="diags">The diagnostics collected so far, which this appends to.</param>
    /// <returns>The text to parse, with the declared version rewritten.</returns>
    private static string RewriteDeclaredVersion(
        string content, OpenApiWorkItem item, ImmutableArray<GeneratorDiagnostic>.Builder diags)
    {
        var match = Declared31Yaml.Match(content);
        if (!match.Success)
        {
            match = Declared31Json.Match(content);
        }

        if (!match.Success)
        {
            return content;
        }

        diags.Add(new GeneratorDiagnostic(
            Diagnostics.Diagnostics.OpenApi31ParsedAs30,
            new object[] { item.FilePath, "the declared version" }));

        return content.Substring(0, match.Index)
            + match.Groups["head"].Value
            + DowngradedVersion
            + match.Groups["tail"].Value
            + content.Substring(match.Index + match.Length);
    }

    /// <summary>
    /// Rewrites every 3.1 construct in the document and returns a description of what was rewritten.
    /// </summary>
    /// <remarks>
    /// The rewrites are ordered by dependency: references are hoisted before the schemas are walked, so a
    /// schema that moved is normalized in its new place, and the webhooks are merged into <c>paths</c> before
    /// the walk so their operations are reached like any other.
    /// </remarks>
    /// <param name="root">The document's root mapping.</param>
    /// <param name="notes">Collects the constructs that were mapped with a loss.</param>
    /// <returns>The kinds of construct rewritten, for the SP1002 message.</returns>
    private static string Rewrite(YamlMappingNode root, List<(string What, string Why)> notes)
    {
        var kinds = new List<string> { "the declared version" };

        Set(root, "openapi", new YamlScalarNode(DowngradedVersion));

        if (Find(root, "jsonSchemaDialect") is YamlScalarNode dialect)
        {
            Remove(root, "jsonSchemaDialect");
            notes.Add((
                "jsonSchemaDialect",
                $"the document declares the dialect '{dialect.Value}' and the mapping reads 3.0 keywords, so the dialect's own semantics are not honoured"));
        }

        if (HoistDefinitions(root, notes))
        {
            Add(kinds, "$defs references");
        }

        if (MergeWebhooks(root, notes))
        {
            Add(kinds, "webhooks");
        }

        Visit(root, string.Empty, canBeSchema: false, notes, kinds);

        return string.Join(", ", kinds);
    }

    /// <summary>Records a kind of rewrite once, in the order the kinds were first seen.</summary>
    /// <param name="kinds">The kinds seen so far.</param>
    /// <param name="kind">The kind to record.</param>
    private static void Add(List<string> kinds, string kind)
    {
        if (!kinds.Contains(kind))
        {
            kinds.Add(kind);
        }
    }

    /// <summary>
    /// Walks a node, normalizing every schema in it.
    /// </summary>
    /// <remarks>
    /// What makes a mapping a schema is where it sits, not what it holds: a mapping under a <c>type</c> key is
    /// a schema, a mapping under <c>properties</c> is a map of names to schemas (not a schema itself, so its
    /// own keys are names and nothing is rewritten on it), and a mapping under <c>example</c> is data. The
    /// keywords rewritten are schema-only ones, so a position this walk cannot classify is left alone rather
    /// than guessed at.
    /// </remarks>
    /// <param name="node">The node to walk.</param>
    /// <param name="path">The route to the node, which the diagnostics name.</param>
    /// <param name="canBeSchema">Whether this position is one a schema may occupy.</param>
    /// <param name="notes">Collects the constructs that were mapped with a loss.</param>
    /// <param name="kinds">The kinds of construct rewritten.</param>
    private static void Visit(
        YamlNode node, string path, bool canBeSchema, List<(string What, string Why)> notes, List<string> kinds)
    {
        if (node is YamlMappingNode mapping)
        {
            if (canBeSchema)
            {
                NormalizeSchema(mapping, path, notes, kinds);
            }

            foreach (var child in mapping.ToArray())
            {
                var name = Scalar(child.Key);
                if (name is null || IsExtension(name) || DataKeywords.Contains(name))
                {
                    continue;
                }

                var childPath = path.Length == 0 ? name : path + "/" + name;

                if (SchemaNameContainers.Contains(name))
                {
                    // The keys of this mapping are the consumer's names, so its values are the schemas.
                    if (child.Value is YamlMappingNode named)
                    {
                        foreach (var entry in named.ToArray())
                        {
                            var entryPath = childPath + "/" + (Scalar(entry.Key) ?? "?");
                            Visit(entry.Value, entryPath, canBeSchema: true, notes, kinds);
                        }
                    }

                    continue;
                }

                Visit(child.Value, childPath, canBeSchema || SchemaValuedKeywords.Contains(name), notes, kinds);
            }

            return;
        }

        if (node is YamlSequenceNode sequence)
        {
            for (var index = 0; index < sequence.Children.Count; index++)
            {
                Visit(sequence.Children[index], path + "/" + index, canBeSchema, notes, kinds);
            }
        }
    }

    /// <summary>True when a key is an extension, whose value is the consumer's and not a schema.</summary>
    /// <param name="name">The key.</param>
    /// <returns>Whether it is an extension.</returns>
    private static bool IsExtension(string name) => name.StartsWith("x-", StringComparison.Ordinal);

    /// <summary>Whether a JSON pointer runs through a container this rewrite hoists.</summary>
    /// <remarks>
    /// A definition kept under <c>$defs</c> (3.1) or <c>definitions</c> (2.0) sits where the reader does not
    /// look, so a pointer into it resolves to nothing. The segment is matched together with its slashes because
    /// the bare names also occur as ordinary schema names - <c>#/components/schemas/defs</c> names a schema.
    /// </remarks>
    /// <param name="pointer">The reference.</param>
    /// <returns>Whether the reference points into a container of definitions.</returns>
    private static bool NamesADefinition(string pointer) =>
        pointer.Contains("/$defs/") || pointer.Contains("/definitions/");

    /// <summary>
    /// Rewrites the 3.1-only keywords of one schema into the 3.0 form the mapper reads.
    /// </summary>
    /// <param name="schema">The schema.</param>
    /// <param name="path">The route to the schema, which the diagnostics name.</param>
    /// <param name="notes">Collects the constructs that were mapped with a loss.</param>
    /// <param name="kinds">The kinds of construct rewritten.</param>
    private static void NormalizeSchema(
        YamlMappingNode schema, string path, List<(string What, string Why)> notes, List<string> kinds)
    {
        if (Find(schema, "type") is YamlSequenceNode types)
        {
            NormalizeTypeArray(schema, types, path, notes, kinds);
        }

        NormalizeConst(schema, kinds);
        NormalizeTuple(schema, path, notes, kinds);
        NormalizeContentEncoding(schema, path, notes, kinds);
        NormalizeExclusiveBounds(schema, kinds);
    }

    /// <summary>
    /// Rewrites a 3.1 type array to the single type it names, or to <c>google.protobuf.Value</c> when it
    /// names several.
    /// </summary>
    /// <remarks>
    /// <c>type: [string, "null"]</c> is what 3.1 writes where 3.0 wrote <c>nullable: true</c>, and it names a
    /// single type, so it maps as that type: proto3 has no null for a scalar, and the default value its field
    /// already has is the closest thing to one. A union of several types has no protobuf type that holds it,
    /// but it does have <c>google.protobuf.Value</c>, whose whole purpose is "a JSON value" - which describes
    /// the union better than any one of its branches.
    /// </remarks>
    /// <param name="schema">The schema holding the type array.</param>
    /// <param name="types">The type array.</param>
    /// <param name="path">The route to the schema, which the diagnostics name.</param>
    /// <param name="notes">Collects the constructs that were mapped with a loss.</param>
    /// <param name="kinds">The kinds of construct rewritten.</param>
    private static void NormalizeTypeArray(
        YamlMappingNode schema, YamlSequenceNode types, string path,
        List<(string What, string Why)> notes, List<string> kinds)
    {
        var declared = new List<string>();

        foreach (var item in types.Children)
        {
            var name = Scalar(item);
            if (IsNullType(name) || declared.Contains(name!))
            {
                continue;
            }

            declared.Add(name!);
        }

        Add(kinds, "a type array");

        if (declared.Count == 1)
        {
            Set(schema, "type", new YamlScalarNode(declared[0]));
            return;
        }

        Remove(schema, "type");
        Set(schema, JsonValueKeyword, new YamlScalarNode("true"));

        notes.Add((
            $"the type array at {path}",
            declared.Count == 0
                ? "a value that is always null has no protobuf equivalent of its own, so it maps to google.protobuf.Value"
                : $"a value that is any one of {{{string.Join(", ", declared)}}} has no protobuf equivalent, so it maps to google.protobuf.Value"));
    }

    /// <summary>True when a type-array entry names the null type.</summary>
    /// <remarks>
    /// JSON Schema spells it <c>"null"</c>, while YAML's own null - a bare <c>null</c>, a <c>~</c>, or nothing at
    /// all - is the same type written as a literal, and a document may use either. Both are counted, so
    /// <c>type: [string, null]</c> is the nullable string it plainly is rather than a union.
    /// </remarks>
    /// <param name="name">The entry's text, or <c>null</c> when the entry was not a scalar.</param>
    /// <returns>Whether it names the null type.</returns>
    private static bool IsNullType(string? name)
        => string.IsNullOrEmpty(name)
            || string.Equals(name, "null", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "~", StringComparison.Ordinal);

    /// <summary>
    /// Rewrites a 3.1 <c>const</c> to the single-member enumeration it already is.
    /// </summary>
    /// <remarks>
    /// A schema whose only allowed value is <c>X</c> describes a field that carries one value - what an
    /// enumeration of one member says - and the mapper already reads enumerations.
    /// </remarks>
    /// <param name="schema">The schema.</param>
    /// <param name="kinds">The kinds of construct rewritten.</param>
    private static void NormalizeConst(YamlMappingNode schema, List<string> kinds)
    {
        if (Find(schema, "const") is not { } value)
        {
            return;
        }

        Remove(schema, "const");
        Set(schema, "enum", new YamlSequenceNode(value));
        Add(kinds, "const");
    }

    /// <summary>
    /// Rewrites a 3.1 tuple to the repeated field it can be, and an array that omits its type to the array it
    /// plainly is.
    /// </summary>
    /// <remarks>
    /// A tuple - <c>prefixItems</c>, optionally followed by <c>items</c> for the tail - fixes the type of each
    /// position, while a protobuf field repeats one type. When every position names the same scalar type the
    /// tuple maps to a repeated field of it, which is what the tuple allowed for every element it could hold;
    /// when the positions differ, the only protobuf type that holds all of them is
    /// <c>google.protobuf.Value</c>. Either way the length is gone, and the caller is told.
    /// </remarks>
    /// <param name="schema">The schema.</param>
    /// <param name="path">The route to the schema, which the diagnostics name.</param>
    /// <param name="notes">Collects the constructs that were mapped with a loss.</param>
    /// <param name="kinds">The kinds of construct rewritten.</param>
    private static void NormalizeTuple(
        YamlMappingNode schema, string path, List<(string What, string Why)> notes, List<string> kinds)
    {
        if (Find(schema, "prefixItems") is YamlSequenceNode prefix)
        {
            Remove(schema, "prefixItems");
            Add(kinds, "prefixItems");

            if (Find(schema, "items") is null && prefix.Children.Count > 0)
            {
                Set(schema, "items", SharedElementType(prefix) ?? JsonValueSchema());
            }

            notes.Add((
                $"the tuple at {path}",
                "a JSON Schema tuple's fixed length and per-position types have no protobuf equivalent, so it maps to a repeated field"));
        }

        // 3.1 makes "type" optional on an array schema - the element schema is what makes it one - while the
        // mapper keys on the type, so an array that omits it is written as the array it is.
        if (Find(schema, "type") is null && Find(schema, "items") is YamlMappingNode)
        {
            Set(schema, "type", new YamlScalarNode("array"));
            Add(kinds, "an array without a type");
        }
    }

    /// <summary>
    /// The one element schema a tuple's positions all agree on, or <c>null</c> when they do not.
    /// </summary>
    /// <param name="prefix">The tuple's positions.</param>
    /// <returns>The element schema, or <c>null</c>.</returns>
    private static YamlNode? SharedElementType(YamlSequenceNode prefix)
    {
        string? sharedType = null;
        string? sharedFormat = null;

        foreach (var item in prefix.Children)
        {
            if (item is not YamlMappingNode element
                || Find(element, "type") is not YamlScalarNode type
                || Find(element, "$ref") is not null
                || Find(element, "properties") is not null
                || Find(element, "items") is not null)
            {
                return null;
            }

            var format = Scalar(Find(element, "format"));

            if (sharedType is null)
            {
                sharedType = type.Value;
                sharedFormat = format;
                continue;
            }

            if (!string.Equals(sharedType, type.Value, StringComparison.Ordinal)
                || !string.Equals(sharedFormat, format, StringComparison.Ordinal))
            {
                return null;
            }
        }

        if (sharedType is null)
        {
            return null;
        }

        var shared = new YamlMappingNode();
        shared.Add(new YamlScalarNode("type"), new YamlScalarNode(sharedType));

        if (sharedFormat is not null)
        {
            shared.Add(new YamlScalarNode("format"), new YamlScalarNode(sharedFormat));
        }

        return shared;
    }

    /// <summary>A schema that says "any JSON value at all", which the mapper reads as <c>Value</c>.</summary>
    /// <returns>The schema.</returns>
    private static YamlMappingNode JsonValueSchema()
    {
        var schema = new YamlMappingNode();
        schema.Add(new YamlScalarNode(JsonValueKeyword), new YamlScalarNode("true"));
        return schema;
    }

    /// <summary>
    /// Rewrites a 3.1 <c>contentEncoding</c> to the format that carries the same bytes.
    /// </summary>
    /// <remarks>
    /// A base64 string is a JSON spelling of bytes, which is what <c>format: byte</c> means, so the encoding
    /// becomes the format and the field maps to <c>bytes</c>. An encoding this mapping has no equivalent for
    /// leaves the field as the string it is and says so, rather than claiming to have applied it.
    /// </remarks>
    /// <param name="schema">The schema.</param>
    /// <param name="path">The route to the schema, which the diagnostics name.</param>
    /// <param name="notes">Collects the constructs that were mapped with a loss.</param>
    /// <param name="kinds">The kinds of construct rewritten.</param>
    private static void NormalizeContentEncoding(
        YamlMappingNode schema, string path, List<(string What, string Why)> notes, List<string> kinds)
    {
        if (Scalar(Find(schema, "contentEncoding")) is not { } encoding)
        {
            return;
        }

        Remove(schema, "contentEncoding");
        Remove(schema, "contentMediaType");
        Add(kinds, "contentEncoding");

        if (string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase))
        {
            Set(schema, "type", new YamlScalarNode("string"));
            Set(schema, "format", new YamlScalarNode("byte"));
            return;
        }

        notes.Add((
            $"the contentEncoding '{encoding}' at {path}",
            "the mapping has no protobuf equivalent for it, so the field travels as the string it is"));
    }

    /// <summary>
    /// Splits a 3.1 numeric <c>exclusiveMinimum</c> into the two 3.0 fields that carry the same bound.
    /// </summary>
    /// <remarks>
    /// 3.1 lets the bound keyword be omitted - <c>exclusiveMinimum: 0</c> means what <c>minimum: 0</c> with
    /// <c>exclusiveMinimum: true</c> means - and a number where the 3.0 reader expects a flag makes it refuse
    /// the whole document. A bound is documentation in generated code either way; what matters is that the
    /// document parses.
    /// </remarks>
    /// <param name="schema">The schema.</param>
    /// <param name="kinds">The kinds of construct rewritten.</param>
    private static void NormalizeExclusiveBounds(YamlMappingNode schema, List<string> kinds)
    {
        foreach (var (exclusive, bound) in new[] { ("exclusiveMinimum", "minimum"), ("exclusiveMaximum", "maximum") })
        {
            if (Scalar(Find(schema, exclusive)) is not { } value || value is "true" or "false")
            {
                continue;
            }

            Remove(schema, exclusive);

            if (Find(schema, bound) is null)
            {
                Set(schema, bound, new YamlScalarNode(value));
            }

            Set(schema, exclusive, new YamlScalarNode("true"));
            Add(kinds, "a numeric exclusive bound");
        }
    }

    /// <summary>
    /// Points every reference into a <c>$defs</c> at a definition the parser can resolve.
    /// </summary>
    /// <remarks>
    /// A <c>$defs</c> entry is a definition kept somewhere other than where a 3.0 document keeps one, so a
    /// pointer into it resolves to nothing: the reader keeps the last segment as the type name, and the mapper
    /// then emits a field of a message that is never declared - which the consumer meets as a compile error
    /// inside generated code, the worst place to meet one. Hoisting the definition to components/schemas gives
    /// every such reference something to point at. A pointer is absolute, so a nested definition moves without
    /// its own references having to be rewritten.
    /// </remarks>
    /// <param name="root">The document's root mapping.</param>
    /// <param name="notes">Collects the constructs that were mapped with a loss.</param>
    /// <returns>Whether any reference was repointed.</returns>
    private static bool HoistDefinitions(YamlMappingNode root, List<(string What, string Why)> notes)
    {
        var references = new List<YamlScalarNode>();
        CollectDefinitionReferences(root, references);

        if (references.Count == 0)
        {
            return false;
        }

        var hoisted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var reference in references)
        {
            if (reference.Value is not { } pointer)
            {
                continue;
            }

            var name = Unescape(pointer.Substring(pointer.LastIndexOf('/') + 1));

            if (!hoisted.Contains(name))
            {
                if (Resolve(root, pointer) is not { } definition)
                {
                    notes.Add((
                        $"the reference '{pointer}'",
                        "it names nothing in the document, so the field it types maps to a message that is never declared"));
                    continue;
                }

                var schemas = Schemas(root);
                if (Find(schemas, name) is not null)
                {
                    notes.Add((
                        $"the $defs entry '{name}'",
                        "a schema of that name is already declared under components/schemas, so the reference was pointed at the one that is there"));
                }
                else
                {
                    schemas.Add(new YamlScalarNode(name), definition);
                }

                hoisted.Add(name);
            }

            reference.Value = "#/components/schemas/" + name;
        }

        RemoveDefinitions(root);
        return hoisted.Count > 0;
    }

    /// <summary>Collects the reference nodes that point into a <c>$defs</c>.</summary>
    /// <param name="node">The node to search.</param>
    /// <param name="references">Collects the reference scalars.</param>
    private static void CollectDefinitionReferences(YamlNode node, List<YamlScalarNode> references)
    {
        if (node is YamlMappingNode mapping)
        {
            foreach (var child in mapping.ToArray())
            {
                var name = Scalar(child.Key);

                if (name == "$ref")
                {
                    if (child.Value is YamlScalarNode target
                        && target.Value is { } value
                        && NamesADefinition(value))
                    {
                        references.Add(target);
                    }

                    continue;
                }

                if (name is null || IsExtension(name) || DataKeywords.Contains(name))
                {
                    continue;
                }

                CollectDefinitionReferences(child.Value, references);
            }

            return;
        }

        if (node is YamlSequenceNode sequence)
        {
            foreach (var item in sequence.Children.ToArray())
            {
                CollectDefinitionReferences(item, references);
            }
        }
    }

    /// <summary>Follows a JSON pointer from the document's root, or returns <c>null</c> when it leads nowhere.</summary>
    /// <param name="root">The document's root mapping.</param>
    /// <param name="pointer">The pointer, which starts with <c>#/</c>.</param>
    /// <returns>The node the pointer names, or <c>null</c>.</returns>
    private static YamlNode? Resolve(YamlMappingNode root, string pointer)
    {
        YamlNode? current = root;

        foreach (var raw in pointer.Split('/'))
        {
            if (raw.Length == 0 || raw == "#")
            {
                continue;
            }

            var segment = Unescape(raw);

            current = current switch
            {
                YamlMappingNode mapping => Find(mapping, segment),
                YamlSequenceNode sequence when int.TryParse(segment, out var index)
                    && index >= 0 && index < sequence.Children.Count => sequence.Children[index],
                _ => null,
            };

            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>Resolves the escapes a JSON pointer uses for its two special characters.</summary>
    /// <param name="segment">A pointer segment.</param>
    /// <returns>The name it holds.</returns>
    private static string Unescape(string segment)
        => segment.Replace("~1", "/").Replace("~0", "~");

    /// <summary>The document's <c>components/schemas</c> mapping, created when the document has none.</summary>
    /// <param name="root">The document's root mapping.</param>
    /// <returns>The schemas mapping.</returns>
    private static YamlMappingNode Schemas(YamlMappingNode root)
    {
        if (Find(root, "components") is not YamlMappingNode components)
        {
            components = new YamlMappingNode();
            Set(root, "components", components);
        }

        if (Find(components, "schemas") is not YamlMappingNode schemas)
        {
            schemas = new YamlMappingNode();
            Set(components, "schemas", schemas);
        }

        return schemas;
    }

    /// <summary>Drops the <c>$defs</c> mappings, whose definitions have been hoisted.</summary>
    /// <param name="node">The node to search.</param>
    private static void RemoveDefinitions(YamlNode node)
    {
        if (node is YamlMappingNode mapping)
        {
            foreach (var child in mapping.ToArray())
            {
                var name = Scalar(child.Key);

                if (name is "$defs" or "definitions" && child.Value is YamlMappingNode)
                {
                    Remove(mapping, name);
                    continue;
                }

                if (name is null || IsExtension(name) || DataKeywords.Contains(name))
                {
                    continue;
                }

                RemoveDefinitions(child.Value);
            }

            return;
        }

        if (node is YamlSequenceNode sequence)
        {
            foreach (var item in sequence.Children.ToArray())
            {
                RemoveDefinitions(item);
            }
        }
    }

    /// <summary>
    /// Maps a 3.1 document's <c>webhooks</c> into <c>paths</c>, the only operation container the mapping reads.
    /// </summary>
    /// <remarks>
    /// A webhook is a call the API makes to you, and a gRPC method has no way to say that direction: the RPC
    /// generated for one describes the payload and is a shape for the consumer to handle, not a call the server
    /// accepts. Mapping it anyway is the lesser loss - the alternative is dropping the operations and every
    /// schema they name - and the fidelity note says what the RPC does not mean.
    /// <para>
    /// A path item's key has to begin with a slash: the reader rejects the document otherwise, and it rejects
    /// this one by dropping the paths rather than by failing, which loses the very operations this method exists
    /// to keep. A webhook name is not a path, so the merged key is built from one that says where the operation
    /// came from rather than inventing a route the API serves.
    /// </para>
    /// </remarks>
    /// <param name="root">The document's root mapping.</param>
    /// <param name="notes">Collects the constructs that were mapped with a loss.</param>
    /// <returns>Whether any webhook was mapped.</returns>
    private static bool MergeWebhooks(YamlMappingNode root, List<(string What, string Why)> notes)
    {
        if (Find(root, "webhooks") is not YamlMappingNode webhooks)
        {
            return false;
        }

        YamlMappingNode paths;

        if (Find(root, "paths") is YamlMappingNode declared)
        {
            paths = declared;
        }
        else
        {
            paths = new YamlMappingNode();
            Set(root, "paths", paths);
        }

        var merged = 0;

        foreach (var webhook in webhooks.ToArray())
        {
            var name = Scalar(webhook.Key);
            if (name is null)
            {
                continue;
            }

            var path = WebhookPath(name);

            if (Find(paths, path) is not null)
            {
                notes.Add((
                    $"the webhook '{name}'",
                    $"the document already declares a path at '{path}', so the webhook was left out of the RPC surface"));
                continue;
            }

            paths.Add(new YamlScalarNode(path), webhook.Value);
            merged++;
        }

        Remove(root, "webhooks");

        if (merged > 0)
        {
            notes.Add((
                "webhooks",
                $"{merged} webhook operation(s) were mapped to RPCs; a webhook is a call the API makes to you, so a client method for one describes a payload and not a call the server accepts"));
        }

        return merged > 0;
    }

    /// <summary>The key a webhook's path item is merged under.</summary>
    /// <remarks>
    /// A 3.0 path item's key has to be a path, and the reader refuses one that is not by reporting an error and
    /// dropping what it could not read. The name is kept, prefixed so the key cannot be mistaken for a route the
    /// API serves.
    /// </remarks>
    /// <param name="name">The webhook's name.</param>
    /// <returns>The path the webhook is merged under.</returns>
    private static string WebhookPath(string name) => "/webhooks/" + name.TrimStart('/');

    /// <summary>The value of a mapping's key, looked up by name.</summary>
    /// <remarks>
    /// Keys are the document author's text, so they are compared as text rather than by node identity: the
    /// keys of the mapping the reader built are not the node instances this file writes.
    /// </remarks>
    /// <param name="mapping">The mapping.</param>
    /// <param name="name">The key to find.</param>
    /// <returns>The value, or <c>null</c> when the mapping has no such key.</returns>
    private static YamlNode? Find(YamlMappingNode mapping, string name)
    {
        foreach (var child in mapping)
        {
            if (Scalar(child.Key) == name)
            {
                return child.Value;
            }
        }

        return null;
    }

    /// <summary>Sets a key's value, replacing the value of an existing key and appending a new one.</summary>
    /// <param name="mapping">The mapping.</param>
    /// <param name="name">The key.</param>
    /// <param name="value">The value.</param>
    private static void Set(YamlMappingNode mapping, string name, YamlNode value)
    {
        foreach (var child in mapping)
        {
            if (Scalar(child.Key) == name)
            {
                mapping.Children[child.Key] = value;
                return;
            }
        }

        mapping.Add(new YamlScalarNode(name), value);
    }

    /// <summary>Removes a key, whether or not the mapping had one.</summary>
    /// <param name="mapping">The mapping.</param>
    /// <param name="name">The key.</param>
    private static void Remove(YamlMappingNode mapping, string name)
    {
        YamlNode? key = null;

        foreach (var child in mapping)
        {
            if (Scalar(child.Key) == name)
            {
                key = child.Key;
                break;
            }
        }

        if (key is not null)
        {
            mapping.Children.Remove(key);
        }
    }

    /// <summary>The text of a scalar node, or <c>null</c> when the node is not a scalar.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The text, or <c>null</c>.</returns>
    private static string? Scalar(YamlNode? node) => node is YamlScalarNode scalar ? scalar.Value : null;
}
