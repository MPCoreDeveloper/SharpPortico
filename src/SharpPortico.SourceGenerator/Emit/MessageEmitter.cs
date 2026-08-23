using System.Text;
using SharpPortico.Generator.Model;

namespace SharpPortico.Generator.Emit;

/// <summary>
/// Emits hand-written Google.Protobuf.IMessage implementations for every
/// message in the model. No reflection; NativeAOT-safe.
/// </summary>
internal static class MessageEmitter
{
    public static void EmitAll(CodeWriter w, GrpcModel model)
    {
        foreach (var enumModel in model.Enums)
        {
            EmitEnum(w, enumModel);
            w.Line();
        }

        foreach (var msg in model.Messages)
        {
            EmitMessage(w, msg);
            w.Line();
        }
    }

    private static void EmitEnum(CodeWriter w, EnumModel enumModel)
    {
        w.Block($"public enum {enumModel.Name}", () =>
        {
            foreach (var v in enumModel.Values)
                w.Line($"{v.Name} = {v.Number},");
        });
    }

    private static void EmitMessage(CodeWriter w, MessageModel msg)
    {
        w.Line("[global::System.CodeDom.Compiler.GeneratedCode(\"SharpPortico\", \"1.0.0\")]");
        w.Line($"public sealed partial class {msg.Name} : global::Google.Protobuf.IMessage<{msg.Name}>");
        w.Line("{");
        w.Open();

        w.Line($"private static readonly global::Google.Protobuf.MessageParser<{msg.Name}> _parser = new(() => new {msg.Name}());");
        w.Line($"public static global::Google.Protobuf.MessageParser<{msg.Name}> Parser => _parser;");
        w.Line("public global::Google.Protobuf.Reflection.MessageDescriptor Descriptor => null!;");
        w.Line();

        foreach (var f in msg.Fields)
            w.Line($"private const uint {ConstTag(f)} = {Wire.Tag(f.Number, Wire.WireTypeFor(f.Kind))}u;");
        w.Line();

        foreach (var f in msg.Fields)
        {
            EmitProperty(w, f);
            w.Line();
        }

        w.Line("public bool IsInitialized => true;");
        w.Line();

        // Clone
        w.Block($"public {msg.Name} Clone()", () =>
        {
            w.Line($"var result = new {msg.Name}();");
            foreach (var f in msg.Fields)
            {
                if (f.IsRepeated)
                {
                    w.Line($"foreach (var item in {f.Name}) result.{f.Name}.Add(item);");
                }
                else if (f.Kind == FieldKind.Message)
                {
                    w.Line($"if ({f.Name} is not null) result.{f.Name} = {f.Name}.Clone();");
                }
                else
                {
                    w.Line($"result.{f.Name} = {f.Name};");
                }
            }
            w.Line("return result;");
        });
        w.Line();

        // Equals
        w.Block("public override bool Equals(object? other)", () => w.Line($"return Equals(other as {msg.Name});"));
        w.Line();

        w.Block($"public bool Equals({msg.Name}? other)", () =>
        {
            w.Line("if (ReferenceEquals(other, null)) return false;");
            w.Line("if (ReferenceEquals(other, this)) return true;");
            foreach (var f in msg.Fields)
            {
                if (f.IsRepeated)
                {
                    w.Line($"if (!{f.Name}.Equals(other.{f.Name})) return false;");
                }
                else
                {
                    w.Line($"if (!global::System.Collections.Generic.EqualityComparer<{FieldPropertyType(f)}>.Default.Equals({f.Name}, other.{f.Name})) return false;");
                }
            }
            w.Line("return true;");
        });
        w.Line();

        // GetHashCode
        w.Block("public override int GetHashCode()", () =>
        {
            w.Line("var hash = 1;");
            foreach (var f in msg.Fields)
            {
                if (f.IsRepeated)
                {
                    w.Line($"hash ^= {f.Name}.GetHashCode();");
                }
                else if (IsReferenceField(f))
                {
                    w.Line($"if ({f.Name} is not null) hash ^= {f.Name}.GetHashCode();");
                }
                else
                {
                    w.Line($"hash ^= {f.Name}.GetHashCode();");
                }
            }
            w.Line("return hash;");
        });
        w.Line();

        // ToString via String.Concat (avoids interpolated-string escaping hazards)
        w.Block("public override string ToString()", () =>
        {
            w.Line("return \"[" + msg.Name + "] { \"");
            foreach (var f in msg.Fields)
            {
                w.Line($"    + \"{f.Name}=\" + {f.Name} + \", \"");
            }
            w.Line("    + \"}\";");
        });
        w.Line();

        // CalculateSize
        w.Block("public int CalculateSize()", () =>
        {
            w.Line("var size = 0;");
            foreach (var f in msg.Fields)
            {
                var tagSize = $"global::Google.Protobuf.CodedOutputStream.ComputeRawVarint32Size({ConstTag(f)})";

                if (f.IsRepeated)
                {
                    if (f.Kind == FieldKind.Message)
                    {
                        w.Line($"foreach (var item in {f.Name}) size += {tagSize} + global::Google.Protobuf.CodedOutputStream.ComputeMessageSize(item);");
                    }
                    else if (f.Kind == FieldKind.Enum)
                    {
                        w.Line($"foreach (var item in {f.Name}) size += {tagSize} + global::Google.Protobuf.CodedOutputStream.ComputeInt32Size((int)item);");
                    }
                    else
                    {
                        w.Line($"foreach (var item in {f.Name}) size += {tagSize} + global::Google.Protobuf.CodedOutputStream.{Wire.ComputeSize(f.Kind)}(item);");
                    }
                }
                else if (f.Kind == FieldKind.Message)
                {
                    w.Line($"if ({f.Name} is not null) size += {tagSize} + global::Google.Protobuf.CodedOutputStream.ComputeMessageSize({f.Name});");
                }
                else if (f.Kind == FieldKind.Enum)
                {
                    w.Line($"if ({f.Name} != 0) size += {tagSize} + global::Google.Protobuf.CodedOutputStream.ComputeInt32Size((int){f.Name});");
                }
                else if (f.Kind == FieldKind.Bytes)
                {
                    w.Line($"if ({f.Name} is not null) size += {tagSize} + global::Google.Protobuf.CodedOutputStream.ComputeBytesSize({f.Name});");
                }
                else if (f.Kind == FieldKind.String)
                {
                    w.Line($"if ({f.Name}.Length != 0) size += {tagSize} + global::Google.Protobuf.CodedOutputStream.ComputeStringSize({f.Name});");
                }
                else
                {
                    var guard = f.Kind == FieldKind.Bool ? $"if ({f.Name})" : $"if ({f.Name} != 0)";
                    w.Line($"{guard} size += {tagSize} + global::Google.Protobuf.CodedOutputStream.{Wire.ComputeSize(f.Kind)}({f.Name});");
                }
            }
            w.Line("return size;");
        });
        w.Line();

        // MergeFrom(other)
        w.Block($"public void MergeFrom({msg.Name} other)", () =>
        {
            w.Line("if (other is null) return;");
            foreach (var f in msg.Fields)
            {
                if (f.IsRepeated)
                {
                    w.Line($"{f.Name}.Add(other.{f.Name});");
                }
                else if (f.Kind == FieldKind.Message)
                {
                    w.Line($"if (other.{f.Name} is not null) {{ if ({f.Name} is null) {f.Name} = new {f.TypeName}(); {f.Name}.MergeFrom(other.{f.Name}); }}");
                }
                else if (f.Kind == FieldKind.String)
                {
                    w.Line($"if (other.{f.Name}.Length != 0) {f.Name} = other.{f.Name};");
                }
                else if (f.Kind == FieldKind.Bytes)
                {
                    w.Line($"if (other.{f.Name} is not null) {f.Name} = other.{f.Name};");
                }
                else
                {
                    var guard = f.Kind == FieldKind.Bool ? $"if (other.{f.Name})" : $"if (other.{f.Name} != 0)";
                    w.Line($"{guard} {f.Name} = other.{f.Name};");
                }
            }
        });
        w.Line();

        // MergeFrom(CodedInputStream)
        w.Block("public void MergeFrom(global::Google.Protobuf.CodedInputStream input)", () =>
        {
            w.Line("uint tag;");
            w.Line("while ((tag = input.ReadTag()) != 0)");
            w.Line("{");
            w.Open();
            w.Line("switch (tag)");
            w.Line("{");
            w.Open();
            foreach (var f in msg.Fields)
            {
                w.Line($"case {ConstTag(f)}:");
                w.Open();
                if (f.IsRepeated)
                {
                    if (f.Kind == FieldKind.Message)
                    {
                        w.Line($"{f.Name}.AddEntriesFrom(input, _repeated_{f.Name}_codec);");
                    }
                    else if (f.Kind == FieldKind.Enum)
                    {
                        w.Line($"var v = input.ReadInt32(); {f.Name}.Add(({f.TypeName})v);");
                    }
                    else
                    {
                        w.Line($"{f.Name}.AddEntriesFrom(input, _repeated_{f.Name}_codec);");
                    }
                }
                else if (f.Kind == FieldKind.Message)
                {
                    w.Line($"if ({f.Name} is null) {f.Name} = new {f.TypeName}();");
                    w.Line($"input.ReadMessage({f.Name});");
                }
                else if (f.Kind == FieldKind.Enum)
                {
                    w.Line($"var v = input.ReadInt32(); {f.Name} = ({f.TypeName})v;");
                }
                else
                {
                    w.Line($"{f.Name} = input.{Wire.ReadCall(f.Kind)};");
                }
                w.Line("break;");
                w.Close();
            }
            w.Line("default:");
            w.Open();
            w.Line("input.SkipLastField();");
            w.Line("break;");
            w.Close();
            w.Close();
            w.Line("}");
            w.Close();
            w.Line("}");
        });
        w.Line();

        // WriteTo
        w.Block("public void WriteTo(global::Google.Protobuf.CodedOutputStream output)", () =>
        {
            foreach (var f in msg.Fields)
            {
                if (f.IsRepeated)
                {
                    if (f.Kind == FieldKind.Message)
                    {
                        w.Line($"foreach (var item in {f.Name}) {{ output.WriteRawTag({RawTagArgs(f)}); output.WriteMessage(item); }}");
                    }
                    else if (f.Kind == FieldKind.Enum)
                    {
                        w.Line($"foreach (var item in {f.Name}) {{ output.WriteRawTag({RawTagArgs(f)}); output.WriteInt32((int)item); }}");
                    }
                    else
                    {
                        w.Line($"foreach (var item in {f.Name}) {{ output.WriteRawTag({RawTagArgs(f)}); output.{Wire.WriteCall(f.Kind)}(item); }}");
                    }
                }
                else if (f.Kind == FieldKind.Message)
                {
                    w.Line($"if ({f.Name} is not null) {{ output.WriteRawTag({RawTagArgs(f)}); output.WriteMessage({f.Name}); }}");
                }
                else if (f.Kind == FieldKind.Enum)
                {
                    w.Line($"if ({f.Name} != 0) {{ output.WriteRawTag({RawTagArgs(f)}); output.WriteInt32((int){f.Name}); }}");
                }
                else if (f.Kind == FieldKind.Bytes)
                {
                    w.Line($"if ({f.Name} is not null) {{ output.WriteRawTag({RawTagArgs(f)}); output.WriteBytes({f.Name}); }}");
                }
                else if (f.Kind == FieldKind.String)
                {
                    w.Line($"if ({f.Name}.Length != 0) {{ output.WriteRawTag({RawTagArgs(f)}); output.WriteString({f.Name}); }}");
                }
                else
                {
                    var guard = f.Kind == FieldKind.Bool ? $"if ({f.Name})" : $"if ({f.Name} != 0)";
                    w.Line($"{guard} {{ output.WriteRawTag({RawTagArgs(f)}); output.{Wire.WriteCall(f.Kind)}({f.Name}); }}");
                }
            }
        });

        // Repeated codecs
        foreach (var f in msg.Fields)
        {
            if (!f.IsRepeated) continue;
            if (f.Kind == FieldKind.Message)
            {
                w.Line($"private static readonly global::Google.Protobuf.FieldCodec<{f.TypeName}> _repeated_{f.Name}_codec = global::Google.Protobuf.FieldCodec.ForMessage({ConstTag(f)}, {f.TypeName}.Parser);");
            }
            else if (f.Kind == FieldKind.Enum)
            {
                w.Line($"private static readonly global::Google.Protobuf.FieldCodec<{f.TypeName}> _repeated_{f.Name}_codec = global::Google.Protobuf.FieldCodec.ForEnum({ConstTag(f)}, x => (int)x, x => ({f.TypeName})x);");
            }
            else
            {
                w.Line($"private static readonly global::Google.Protobuf.FieldCodec<{f.CsType}> _repeated_{f.Name}_codec = global::Google.Protobuf.FieldCodec.{Wire.FieldCodecFactory(f.Kind)}({ConstTag(f)});");
            }
        }

        w.Close();
        w.Line("}");
    }

    private static void EmitProperty(CodeWriter w, FieldModel f)
    {
        if (f.IsRepeated)
        {
            w.Line($"public global::Google.Protobuf.Collections.RepeatedField<{FieldPropertyType(f)}> {f.Name} {{ get; }} = new();");
            return;
        }
        if (f.Kind == FieldKind.Message || f.Kind == FieldKind.Bytes)
        {
            w.Line($"public {FieldPropertyType(f)} {f.Name} {{ get; set; }} = null!;");
            return;
        }
        if (f.Kind == FieldKind.String)
        {
            w.Line($"private string? _field_{f.Name};");
            w.Line($"public {FieldPropertyType(f)} {f.Name}");
            w.Line("{");
            w.Open();
            w.Line($"get {{ return _field_{f.Name} ?? string.Empty; }}");
            w.Line($"set {{ _field_{f.Name} = value; }}");
            w.Close();
            w.Line("}");
            return;
        }
        w.Line($"public {FieldPropertyType(f)} {f.Name} {{ get; set; }}");
    }

    private static string FieldPropertyType(FieldModel f)
    {
        if (f.IsRepeated)
        {
            if (f.Kind == FieldKind.Message || f.Kind == FieldKind.Enum) return f.TypeName ?? "string";
            return f.CsType;
        }
        if (f.Kind == FieldKind.Message || f.Kind == FieldKind.Bytes) return f.TypeName ?? f.CsType;
        return f.CsType;
    }

    private static bool IsReferenceField(FieldModel f)
        => f.Kind is FieldKind.Message or FieldKind.Bytes || (f.Kind == FieldKind.String && !f.IsRepeated);

    private static string ConstTag(FieldModel f) => "Tag_" + f.Number + "_" + f.Name;

    /// <summary>
    /// Produces the decimal byte arguments for <c>output.WriteRawTag(...)</c> by encoding
    /// the full tag (field number + wire type) as a base-128 varint. This avoids relying on
    /// implicit uint-to-byte constant conversions (which the compiler rejects for uint).
    /// </summary>
    private static string RawTagArgs(FieldModel f)
    {
        var tag = (uint)Wire.Tag(f.Number, Wire.WireTypeFor(f.Kind));
        var bytes = new System.Collections.Generic.List<byte>(5);
        while (tag >= 0x80)
        {
            bytes.Add((byte)(tag | 0x80));
            tag >>= 7;
        }
        bytes.Add((byte)tag);
        return string.Join(", ", bytes.Select(b => b.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
}
