using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using SharpPortico.Generator.Model;

namespace SharpPortico.Generator.Mapping;

/// <summary>
/// Maps OpenAPI schemas to protobuf-style messages and enums, handling $ref,
/// allOf/oneOf/anyOf composition, arrays (repeated fields), enums, and file uploads.
/// All created messages/enums are registered internally so the caller can collect them
/// once mapping completes. This class is stateless per call and safe for the incremental pipeline.
/// </summary>
internal sealed class SchemaMapper
{
    private readonly OpenApiDocument _document;
    private readonly CancellationToken _ct;
    private readonly Dictionary<string, MessageModel> _messages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EnumModel> _enums = new(StringComparer.Ordinal);

    /// <summary>All messages registered during mapping (by name, first wins).</summary>
    public IReadOnlyList<MessageModel> AllMessages => _messages.Values.ToList();

    /// <summary>All enums registered during mapping (by name, first wins).</summary>
    public IReadOnlyList<EnumModel> AllEnums => _enums.Values.ToList();

    public SchemaMapper(OpenApiDocument document, CancellationToken ct)
    {
        _document = document;
        _ct = ct;
    }

    /// <summary>True when the schema declares enum values.</summary>
    public bool IsEnum(OpenApiSchema schema) => schema.Enum is { Count: > 0 };

    /// <summary>Maps an enum schema to an <see cref="EnumModel"/> and registers it.</summary>
    public EnumModel MapEnum(string name, OpenApiSchema schema)
    {
        _ct.ThrowIfCancellationRequested();
        if (_enums.TryGetValue(name, out var existing)) return existing;

        var builder = ImmutableArray.CreateBuilder<EnumValueModel>();
        var number = 0;
        if (schema.Enum is { Count: > 0 })
        {
            foreach (var anyVal in schema.Enum)
            {
                string rawName;
                long? rawNum = null;
                if (anyVal is OpenApiInteger oi) { rawNum = oi.Value; rawName = oi.Value.ToString(); }
                else if (anyVal is OpenApiLong ol) { rawNum = ol.Value; rawName = ol.Value.ToString(); }
                else if (anyVal is OpenApiString os) { rawName = os.Value; }
                else { rawName = "VALUE_" + number; }

                var clean = SanitizeEnumName(rawName, number);
                builder.Add(new EnumValueModel(clean, rawNum is long rn ? (int)rn : number));
                number++;
            }
        }
        var model = new EnumModel(SanitizePascal(name), builder.ToImmutable());
        _enums[name] = model;
        return model;
    }

    /// <summary>
    /// Maps an object schema (or array of objects) to a <see cref="MessageModel"/> and registers it.
    /// Returns <c>null</c> when the schema cannot be mapped (always mapped to a fallback in practice).
    /// </summary>
    public MessageModel? MapSchemaToMessage(string name, OpenApiSchema schema, bool isRequest, bool isResponse)
    {
        _ct.ThrowIfCancellationRequested();
        var key = SanitizePascal(name);
        if (_messages.TryGetValue(key, out var existing)) return existing;

        var fields = new List<FieldModel>();
        var fieldNo = 0;

        // $ref alias: pure reference schema with no inline properties
        if (schema.Reference is not null && CountOwnDefinitions(schema) == 0)
        {
            var targetName = SanitizePascal(schema.Reference.Id!);
            var alias = new MessageModel(key, ImmutableArray<FieldModel>.Empty, isRequest, isResponse);
            _messages[key] = alias;
            // The aliased target will exist as a separate message named targetName.
            return alias;
        }

        if (schema.AllOf is { Count: > 0 })
        {
            // allOf with exactly one $ref and nothing else -> alias to that type
            if (schema.AllOf.Count == 1 && schema.Properties.Count == 0 && schema.Type is null && schema.AllOf[0].Reference is not null)
            {
                var targetName = SanitizePascal(schema.AllOf[0].Reference.Id!);
                _messages[key] = new MessageModel(key, ImmutableArray<FieldModel>.Empty, isRequest, isResponse);
                return _messages[key];
            }

            foreach (var sub in schema.AllOf)
                MergeSchemaFields(fields, sub, ref fieldNo);
            // merge own properties after allOf
            MergeSchemaProperties(fields, schema, ref fieldNo);
        }
        else if (schema.OneOf is { Count: > 0 } || schema.AnyOf is { Count: > 0 })
        {
            var choices = schema.OneOf is { Count: > 0 } ? schema.OneOf : schema.AnyOf;
            if (choices.Count == 1)
            {
                MergeSchemaFields(fields, choices[0], ref fieldNo);
            }
            else
            {
                // Add a string discriminator + flatten the first object-valued choice
                fields.Add(new FieldModel("Kind", "kind", ++fieldNo, FieldKind.String, "string"));
                var chosen = choices.FirstOrDefault(c => c.Properties is { Count: > 0 }) ?? choices[0];
                MergeSchemaFields(fields, chosen, ref fieldNo);
            }
        }
        else if (schema.Properties is { Count: > 0 } || schema.Type == "object")
        {
            MergeSchemaProperties(fields, schema, ref fieldNo);
        }
        else if (schema.Type == "array")
        {
            // Top-level array schema -> repeated single field named Values
            var field = MapProperty("Values", schema, ref fieldNo);
            if (field is not null) fields.Add(field);
        }
        else
        {
            // Scalar top-level -> wrap into Value field (rare for components/schemas)
            var scalar = MapProperty("Value", schema, ref fieldNo);
            if (scalar is not null) fields.Add(scalar);
        }

        if (fields.Count == 0)
        {
            fields.Add(new FieldModel("_HasValue", "has_value", 1, FieldKind.Bool, "bool"));
        }

        var model = new MessageModel(key, fields.ToImmutableArray(), isRequest, isResponse);
        _messages[key] = model;
        return model;
    }

    /// <summary>
    /// Resolves the generated type name when the schema is a $ref; otherwise <c>null</c>.
    /// </summary>
    public string? ResolveSchemaName(OpenApiSchema schema)
        => schema.Reference?.Id is { } id ? SanitizePascal(id) : null;

    /// <summary>
    /// Maps an OpenAPI path/query/header parameter to a request <see cref="FieldModel"/>.
    /// </summary>
    public (bool IsPagination, FieldModel? Field) MapParameter(
        OpenApiParameter parameter,
        OpenApiWorkItem item,
        ref int index)
    {
        if (parameter.Schema is null) return (false, null);
        var field = MapProperty(parameter.Name, parameter.Schema, ref index);
        var isPage = item.DetectPagination
            && (string.Equals(parameter.Name, item.PaginationPageParameter, StringComparison.OrdinalIgnoreCase)
                || string.Equals(parameter.Name, item.PaginationLimitParameter, StringComparison.OrdinalIgnoreCase)
                || string.Equals(parameter.Name, item.PaginationCursorParameter, StringComparison.OrdinalIgnoreCase)
                || string.Equals(parameter.Name, item.PaginationNextPageTokenParameter, StringComparison.OrdinalIgnoreCase));
        return (isPage, field);
    }


    /// <summary>True when the schema is a top-level array whose items are inline objects (not $ref).</summary>
    public bool IsArrayOfInlineObject(OpenApiSchema schema)
        => schema.Type == "array"
           && schema.Items is not null
           && schema.Items.Reference is null
           && (schema.Items.Properties is { Count: > 0 } || schema.Items.Type == "object");

    /// <summary>Derives a deterministic name for the element message of an inline array schema.</summary>
    public string InlineArrayElementName(OpenApiSchema arraySchema, string fallbackBase)
        => arraySchema.Items is { Reference.Id: { } id }
            ? SanitizePascal(id)
            : SanitizePascal(fallbackBase) + "Item";

    // ---- internals ----

    private int CountOwnDefinitions(OpenApiSchema schema)
        => (schema.Properties?.Count ?? 0)
            + (schema.AllOf?.Count ?? 0)
            + (schema.OneOf?.Count ?? 0)
            + (schema.AnyOf?.Count ?? 0)
            + (schema.Items is null ? 0 : 1);

    private void MergeSchemaFields(List<FieldModel> fields, OpenApiSchema schema, ref int fieldNo)
    {
        _ct.ThrowIfCancellationRequested();
        MergeSchemaProperties(fields, schema, ref fieldNo);

        if (schema.AllOf is { Count: > 0 })
            foreach (var sub in schema.AllOf)
                MergeSchemaFields(fields, sub, ref fieldNo);

        if (schema.OneOf is { Count: > 0 })
        {
            var chosen = schema.OneOf.FirstOrDefault(c => c.Properties is { Count: > 0 }) ?? schema.OneOf[0];
            MergeSchemaFields(fields, chosen, ref fieldNo);
        }
        else if (schema.AnyOf is { Count: > 0 })
        {
            var chosen = schema.AnyOf.FirstOrDefault(c => c.Properties is { Count: > 0 }) ?? schema.AnyOf[0];
            MergeSchemaFields(fields, chosen, ref fieldNo);
        }
    }

    private void MergeSchemaProperties(List<FieldModel> fields, OpenApiSchema schema, ref int fieldNo)
    {
        if (schema.Properties is not { Count: > 0 }) return;
        foreach (var kvpProp in schema.Properties)
        {
            var propName = kvpProp.Key;
            var propSchema = kvpProp.Value;
            if (propName is null || propSchema is null) continue;
            var field = MapProperty(propName, propSchema, ref fieldNo);
            if (field is not null) fields.Add(field);
        }
    }
    private FieldModel? MapProperty(string name, OpenApiSchema schema, ref int fieldNo)
    {
        _ct.ThrowIfCancellationRequested();

        // $ref -> message or enum reference
        if (schema.Reference is not null)
        {
            var refName = SanitizePascal(schema.Reference.Id!);
            bool isEnum = _document.Components?.Schemas.TryGetValue(schema.Reference.Id!, out var target) == true && IsEnum(target);
            var kind = isEnum ? FieldKind.Enum : FieldKind.Message;
            return new FieldModel(SanitizePascal(name), ToProtoName(name), ++fieldNo, kind, refName, refName);
        }

        // array -> repeated
        if (schema.Type == "array")
        {
            var items = schema.Items;
            var repeatedName = SanitizePascal(name);
            if (items is null)
            {
                return new FieldModel(repeatedName, ToProtoName(name), ++fieldNo, FieldKind.String, "string", "string", IsRepeated: true);
            }
            if (items.Reference is not null)
            {
                var refName = SanitizePascal(items.Reference.Id!);
                bool isEnum = _document.Components?.Schemas.TryGetValue(items.Reference.Id!, out var target) == true && IsEnum(target);
                return new FieldModel(repeatedName, ToProtoName(name), ++fieldNo, isEnum ? FieldKind.Enum : FieldKind.Message, refName, refName, IsRepeated: true);
            }
            if (items.Enum is { Count: > 0 })
            {
                var enumName = repeatedName + "Enum";
                MapEnum(enumName, items);
                return new FieldModel(repeatedName, ToProtoName(name), ++fieldNo, FieldKind.Enum, enumName, enumName, IsRepeated: true);
            }
            if (items.Properties is { Count: > 0 } || items.Type == "object")
            {
                var nestedName = repeatedName;
                if (!_messages.ContainsKey(nestedName))
                    MapSchemaToMessage(nestedName, items, isRequest: false, isResponse: false);
                return new FieldModel(repeatedName, ToProtoName(name), ++fieldNo, FieldKind.Message, nestedName, nestedName, IsRepeated: true);
            }
            var scalarRepeated = MapScalarField(name, items, ref fieldNo);
            if (scalarRepeated is not null)
                return scalarRepeated with { IsRepeated = true };
            return new FieldModel(repeatedName, ToProtoName(name), ++fieldNo, FieldKind.String, "string", "string", IsRepeated: true);
        }

        // inline enum
        if (schema.Enum is { Count: > 0 })
        {
            var enumName = SanitizePascal(name) + "Enum";
            MapEnum(enumName, schema);
            return new FieldModel(SanitizePascal(name), ToProtoName(name), ++fieldNo, FieldKind.Enum, enumName, enumName);
        }

        // inline object / nested message
        if (schema.Properties is { Count: > 0 } || schema.Type == "object")
        {
            var nestedName = SanitizePascal(name);
            if (!_messages.ContainsKey(nestedName))
                MapSchemaToMessage(nestedName, schema, isRequest: false, isResponse: false);
            return new FieldModel(nestedName, ToProtoName(name), ++fieldNo, FieldKind.Message, nestedName, nestedName);
        }

        // scalar
        return MapScalarField(name, schema, ref fieldNo);
    }

    private FieldModel? MapScalarField(string name, OpenApiSchema schema, ref int fieldNo)
    {
        var kind = ResolveScalarKind(schema);
        var csType = kind switch
        {
            FieldKind.String => "string",
            FieldKind.Int32 => "int",
            FieldKind.Int64 => "long",
            FieldKind.UInt32 => "uint",
            FieldKind.UInt64 => "ulong",
            FieldKind.Float => "float",
            FieldKind.Double => "double",
            FieldKind.Bool => "bool",
            FieldKind.Bytes => "Google.Protobuf.ByteString",
            FieldKind.Timestamp => "Google.Protobuf.WellKnownTypes.Timestamp",
            _ => "string"
        };
        string? typeName = kind is FieldKind.Timestamp ? csType : null;
        return new FieldModel(SanitizePascal(name), ToProtoName(name), ++fieldNo, kind, csType, typeName);
    }

    private static FieldKind ResolveScalarKind(OpenApiSchema schema)
    {
        switch (schema.Type?.ToLowerInvariant())
        {
            case "string":
                if (string.Equals(schema.Format, "binary", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(schema.Format, "byte", StringComparison.OrdinalIgnoreCase))
                    return FieldKind.Bytes;
                return FieldKind.String;
            case "integer":
                return string.Equals(schema.Format, "int64", StringComparison.OrdinalIgnoreCase) ? FieldKind.Int64 : FieldKind.Int32;
            case "number":
                return string.Equals(schema.Format, "float", StringComparison.OrdinalIgnoreCase) ? FieldKind.Float : FieldKind.Double;
            case "boolean":
                return FieldKind.Bool;
            case "file":
                return FieldKind.Bytes;
            default:
                return FieldKind.String;
        }
    }

    internal static string SanitizePascal(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "Field";
        var chars = new System.Collections.Generic.List<char>(input.Length);
        var upper = true;
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                chars.Add(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            else
            {
                upper = true;
            }
        }
        if (chars.Count == 0) return "Field";
        if (char.IsDigit(chars[0])) chars.Insert(0, 'N');
        return new string(chars.ToArray());
    }

    private static string ToProtoName(string name)
    {
        var chars = new System.Collections.Generic.List<char>(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (chars.Count > 0 && char.IsUpper(c) && chars.Count > 0)
                {
                    char prev = chars[chars.Count - 1];
                    if (char.IsLower(prev))
                    {
                        chars.Add('_');
                    }
                }
                chars.Add(char.ToLowerInvariant(c));
            }
            else if (c is '_' or '-' or '.' or ' ' or ':')
            {
                chars.Add('_');
            }
        }
        var result = new string(chars.ToArray());
        if (result.Length == 0) return "field";
        if (char.IsDigit(result[0])) result = "_" + result;
        return result;
    }

    private static string SanitizeEnumName(string raw, int fallback)
    {
        var pascal = SanitizePascal(raw);
        if (pascal == "Field" && string.IsNullOrWhiteSpace(raw)) return "VALUE_" + fallback;
        if (pascal.Length == 0) return "VALUE_" + fallback;
        if (char.IsDigit(pascal[0])) pascal = "V_" + pascal;
        return pascal;
    }
}
