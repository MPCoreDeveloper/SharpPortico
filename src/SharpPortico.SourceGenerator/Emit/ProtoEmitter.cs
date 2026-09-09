using System.Text;
using SharpPortico.Generator.Model;

namespace SharpPortico.Generator.Emit;

internal static class ProtoEmitter
{
    private const string StreamKeyword = "stream ";

    public static string Emit(GrpcModel model, OpenApiWorkItem item)
    {
        var w = new CodeWriter();
        w.Line("syntax = \"proto3\";");
        w.Line();
        w.Line("package " + model.ProtoPackage + ";");
        w.Line();
        w.Line("option csharp_namespace = \"" + model.Namespace + "\";");
        w.Line();

        EmitEnums(w, model);
        EmitMessages(w, model);
        EmitServices(w, model);

        return w.ToString();
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