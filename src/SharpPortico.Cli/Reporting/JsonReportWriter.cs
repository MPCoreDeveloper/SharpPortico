using System.IO;
using System.Text;
using System.Text.Json;

namespace SharpPortico.Cli.Reporting;

/// <summary>
/// Writes the report as one deterministic document: UTF-8 without a byte-order mark, two-space indentation,
/// <c>\n</c> between fields and exactly one trailing newline.
/// </summary>
/// <remarks>
/// The writer enumerates members explicitly instead of serializing a type, which keeps the CLI reflection-free
/// (constitution II) and makes an accidental field impossible: a new field appears here or it does not appear.
/// </remarks>
internal static class JsonReportWriter
{
    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
        NewLine = "\n",
    };

    /// <summary>The whole document as text, ready to be written to standard output.</summary>
    public static string Serialize(MappingReport report)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, Options))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", report.FormatVersion);
            writer.WriteString("spec", report.Spec);

            // Omitted, not empty: a refused contract never produced an identity, and inventing one from the
            // command line would mean a second copy of the defaults a build applies.
            if (report.Service is { } identity)
            {
                writer.WriteStartObject("service");
                writer.WriteString("name", identity.Name);
                writer.WriteString("namespace", identity.Namespace);
                writer.WriteString("package", identity.Package);
                writer.WriteEndObject();
            }

            writer.WriteStartArray("services");
            foreach (var service in report.Services)
            {
                writer.WriteStartObject();
                writer.WriteString("name", service.Name);
                writer.WriteStartArray("rpcs");
                foreach (var rpc in service.Rpcs)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", rpc.Name);
                    writer.WriteString("httpMethod", rpc.HttpMethod);
                    writer.WriteString("path", rpc.Path);
                    writer.WriteString("kind", rpc.Kind);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartObject("counts");
            writer.WriteNumber("messages", report.Counts.Messages);
            writer.WriteNumber("enums", report.Counts.Enums);
            writer.WriteEndObject();

            writer.WriteStartArray("diagnostics");
            foreach (var diagnostic in report.Diagnostics)
            {
                writer.WriteStartObject();
                writer.WriteString("id", diagnostic.Id);
                writer.WriteString("severity", diagnostic.Severity);
                writer.WriteString("message", diagnostic.Message);
                if (diagnostic.Line is { } line) writer.WriteNumber("line", line);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            if (report.Descriptor is { } descriptor)
            {
                writer.WriteStartObject("descriptor");
                writer.WriteString("path", descriptor.Path);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        // One trailing LF and nothing else. A document that ends differently per platform would make the
        // golden-file comparison depend on the operating system rather than on the contract.
        var document = Encoding.UTF8.GetString(buffer.ToArray());
        return document.EndsWith('\n') ? document : document + "\n";
    }
}
