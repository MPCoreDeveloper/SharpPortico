using System.Text;
using SharpPortico.Generator.Model;

namespace SharpPortico.Generator.Emit;

internal static class ProtoEmitter
{
    private const string StreamKeyword = "stream ";

    /// <summary>
    /// The import that names each well-known type. A field of one of these types without its import produces a
    /// .proto that protoc refuses to compile, so the imports are derived from the model rather than assumed.
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<string, string> WellKnownTypeImports = new(System.StringComparer.Ordinal)
    {
        ["google.protobuf.Any"] = "google/protobuf/any.proto",
        ["google.protobuf.BoolValue"] = "google/protobuf/wrappers.proto",
        ["google.protobuf.BytesValue"] = "google/protobuf/wrappers.proto",
        ["google.protobuf.DoubleValue"] = "google/protobuf/wrappers.proto",
        ["google.protobuf.Duration"] = "google/protobuf/duration.proto",
        ["google.protobuf.Empty"] = "google/protobuf/empty.proto",
        ["google.protobuf.FieldMask"] = "google/protobuf/field_mask.proto",
        ["google.protobuf.FloatValue"] = "google/protobuf/wrappers.proto",
        ["google.protobuf.Int32Value"] = "google/protobuf/wrappers.proto",
        ["google.protobuf.Int64Value"] = "google/protobuf/wrappers.proto",
        ["google.protobuf.ListValue"] = "google/protobuf/struct.proto",
        ["google.protobuf.NullValue"] = "google/protobuf/struct.proto",
        ["google.protobuf.StringValue"] = "google/protobuf/wrappers.proto",
        ["google.protobuf.Struct"] = "google/protobuf/struct.proto",
        ["google.protobuf.Timestamp"] = "google/protobuf/timestamp.proto",
        ["google.protobuf.UInt32Value"] = "google/protobuf/wrappers.proto",
        ["google.protobuf.UInt64Value"] = "google/protobuf/wrappers.proto",
        ["google.protobuf.Value"] = "google/protobuf/struct.proto",
    };

    public static string Emit(GrpcModel model, OpenApiWorkItem item)
    {
        var w = new CodeWriter();
        w.Line("syntax = \"proto3\";");
        w.Line();
        w.Line("package " + model.ProtoPackage + ";");
        w.Line();

        var imports = RequiredImports(model);
        foreach (var import in imports)
        {
            w.Line("import \"" + import + "\";");
        }
        if (imports.Length > 0)
        {
            w.Line();
        }

        w.Line("option csharp_namespace = \"" + model.Namespace + "\";");
        w.Line();

        EmitEnums(w, model);
        EmitMessages(w, model);
        EmitServices(w, model);

        return w.ToString();
    }

    /// <summary>
    /// The imports the model's fields require: alphabetically ordered, without duplicates, and only the ones a
    /// well-known type actually used brings in.
    /// </summary>
    private static string[] RequiredImports(GrpcModel model)
    {
        var imports = new System.Collections.Generic.SortedSet<string>(System.StringComparer.Ordinal);

        foreach (var message in model.Messages)
        {
            foreach (var field in message.Fields)
            {
                // A message field with no resolved type name is emitted as google.protobuf.Empty, so that
                // is the type whose import it needs.
                var referenced = field.Kind switch
                {
                    FieldKind.Timestamp => "google.protobuf.Timestamp",
                    FieldKind.Message => field.TypeName ?? "google.protobuf.Empty",
                    _ => null
                };

                if (referenced is not null && WellKnownTypeImports.TryGetValue(referenced, out var import))
                {
                    imports.Add(import);
                }
            }
        }

        var result = new string[imports.Count];
        imports.CopyTo(result);
        return result;
    }

    private static void EmitEnums(CodeWriter w, GrpcModel model)
    {
        foreach (var enumModel in model.Enums)
        {
            w.Block("enum " + enumModel.Name, () =>
            {
                foreach (var v in enumModel.Values)
                {
                    w.Line(ToProtoEnumName(v.Name) + " = " + v.Number + ";");
                }
            });
            w.Line();
        }
    }

    private static void EmitMessages(CodeWriter w, GrpcModel model)
    {
        foreach (var msg in model.Messages)
        {
            w.Block("message " + msg.Name, () =>
            {
                foreach (var f in msg.Fields)
                {
                    var keyword = f.IsRepeated ? "repeated " : "";
                    w.Line(keyword + ProtoFieldType(f) + " " + f.ProtoName + " = " + f.Number + ";");
                }
            });
            w.Line();
        }
    }

    private static void EmitServices(CodeWriter w, GrpcModel model)
    {
        foreach (var svc in model.Services)
        {
            w.Block("service " + svc.Name, () =>
            {
                foreach (var rpc in svc.RpcMethods)
                {
                    var tuple = StreamTupleFor(rpc);
                    w.Line("rpc " + rpc.Name + " (" + tuple.Item1 + rpc.RequestType + ") returns (" + tuple.Item2 + rpc.ResponseType + ");");
                }
            });
            w.Line();
        }
    }

    private static string ProtoFieldType(FieldModel f)
        => f.Kind switch
        {
            FieldKind.String => "string",
            FieldKind.Int32 => "int32",
            FieldKind.Int64 => "int64",
            FieldKind.UInt32 => "uint32",
            FieldKind.UInt64 => "uint64",
            FieldKind.Float => "float",
            FieldKind.Double => "double",
            FieldKind.Bool => "bool",
            FieldKind.Bytes => "bytes",
            FieldKind.Enum => f.TypeName ?? "int32",
            FieldKind.Message => f.TypeName ?? "google.protobuf.Empty",
            FieldKind.Timestamp => "google.protobuf.Timestamp",
            _ => "string"
        };

    private static (string, string) StreamTupleFor(RpcModel rpc)
        => rpc.Kind switch
        {
            RpcKind.ClientStreaming => (StreamKeyword, ""),
            RpcKind.ServerStreaming => ("", StreamKeyword),
            RpcKind.BidiStreaming => (StreamKeyword, StreamKeyword),
            _ => ("", "")
        };

    private static string ToProtoEnumName(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0) sb.Append('_');
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }
}