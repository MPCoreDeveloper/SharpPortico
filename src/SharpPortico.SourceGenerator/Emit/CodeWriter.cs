using System;
using System.Text;
using SharpPortico.Generator.Model;

namespace SharpPortico.Generator.Emit;

/// <summary>
/// Minimal indentation-aware text writer for generated code.
/// </summary>
internal sealed class CodeWriter
{
    private readonly StringBuilder _sb = new();
    private int _indent;

    public void Open() => _indent++;

    public void Close() => _indent--;

    public void Line(string text = "")
    {
        if (text.Length > 0)
        {
            for (var i = 0; i < _indent; i++) _sb.Append("    ");
            _sb.Append(text);
        }
        _sb.Append('\n');
    }

    /// <summary>Writes a formatted line. Single-string calls bind to <see cref="Line(string)"/>.</summary>
    public void Line(string format, params object?[] args)
    {
        if (args.Length == 0)
        {
            Line(format);
            return;
        }
        Line(string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
    }

    public void Block(string header, Action body)
    {
        Line(header);
        Line("{");
        Open();
        body();
        Close();
        Line("}");
    }

    public override string ToString() => _sb.ToString();
}

/// <summary>Shared protocol-buffer wire helpers for the emitters.</summary>
internal static class Wire
{
    public const int Varint = 0;
    public const int Fixed64 = 1;
    public const int LengthDelimited = 2;
    public const int Fixed32 = 5;

    /// <summary>Full wire tag (field number + wire type).</summary>
    public static int Tag(int fieldNumber, int wireType) => (fieldNumber << 3) | wireType;

    /// <summary>Wire type for a field kind.</summary>
    public static int WireTypeFor(FieldKind kind) => kind switch
    {
        FieldKind.String or FieldKind.Bytes or FieldKind.Message or FieldKind.Timestamp => LengthDelimited,
        FieldKind.Float => Fixed32,
        FieldKind.Double => Fixed64,
        _ => Varint
    };

    /// <summary>Marshaller read case for a field kind.</summary>
    public static string ReadCall(FieldKind kind) => kind switch
    {
        FieldKind.Int32 => "ReadInt32()",
        FieldKind.Int64 => "ReadInt64()",
        FieldKind.UInt32 => "ReadUInt32()",
        FieldKind.UInt64 => "ReadUInt64()",
        FieldKind.Float => "ReadFloat()",
        FieldKind.Double => "ReadDouble()",
        FieldKind.Bool => "ReadBool()",
        FieldKind.Bytes => "ReadBytes()",
        FieldKind.String => "ReadString()",
        _ => "ReadInt32()"
    };

    /// <summary>Compute size helper name for a scalar kind.</summary>
    public static string ComputeSize(FieldKind kind) => kind switch
    {
        FieldKind.Int32 => "ComputeInt32Size",
        FieldKind.Int64 => "ComputeInt64Size",
        FieldKind.UInt32 => "ComputeUInt32Size",
        FieldKind.UInt64 => "ComputeUInt64Size",
        FieldKind.Float => "ComputeFloatSize",
        FieldKind.Double => "ComputeDoubleSize",
        FieldKind.Bool => "ComputeBoolSize",
        FieldKind.Bytes => "ComputeBytesSize",
        FieldKind.String => "ComputeStringSize",
        _ => "ComputeInt32Size"
    };

    /// <summary>Write call name for a scalar kind.</summary>
    public static string WriteCall(FieldKind kind) => kind switch
    {
        FieldKind.Int32 => "WriteInt32",
        FieldKind.Int64 => "WriteInt64",
        FieldKind.UInt32 => "WriteUInt32",
        FieldKind.UInt64 => "WriteUInt64",
        FieldKind.Float => "WriteFloat",
        FieldKind.Double => "WriteDouble",
        FieldKind.Bool => "WriteBool",
        FieldKind.Bytes => "WriteBytes",
        FieldKind.String => "WriteString",
        _ => "WriteInt32"
    };

    /// <summary>FieldCodec.ForXXX helper for repeated scalar kinds.</summary>
    public static string FieldCodecFactory(FieldKind kind) => kind switch
    {
        FieldKind.String => "ForString",
        FieldKind.Int32 => "ForInt32",
        FieldKind.Int64 => "ForInt64",
        FieldKind.UInt32 => "ForUInt32",
        FieldKind.UInt64 => "ForUInt64",
        FieldKind.Float => "ForFloat",
        FieldKind.Double => "ForDouble",
        FieldKind.Bool => "ForBool",
        FieldKind.Bytes => "ForBytes",
        _ => "ForInt32"
    };
}
