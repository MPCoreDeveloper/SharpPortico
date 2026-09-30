using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using SharpPortico.Generator;
using SharpPortico.Generator.Emit;
using SharpPortico.Generator.Mapping;
using SharpPortico.Generator.Model;

namespace SharpPortico.Cli;

/// <summary>
/// <c>sharpportico generate SPEC [--out DIR]</c> — runs the source generator's own mapping pipeline over an
/// OpenAPI 3.0/3.1 spec (YAML or JSON) and writes the protobuf descriptor the generator would embed for it.
/// </summary>
/// <remarks>
/// The descriptor comes from <see cref="ProtoEmitter"/>, the emitter the generator itself calls, so the file
/// written here is the descriptor a consumer's <c>{Service}Proto.Text</c> holds after a build. That is the
/// point of the command: a contract can be handed to <c>protoc</c>, a client generator or a reviewer before
/// anything is built, and what they read is what the build produces.
/// </remarks>
internal static class Program
{
    private const string Usage =
        "usage: sharpportico generate <openapi.yaml|json> [--out DIR|FILE] [--service-name NAME] [--namespace NS]";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            await Console.Error.WriteLineAsync(Usage).ConfigureAwait(false);
            return 2;
        }

        if (args[0] is "--help" or "-h")
        {
            Console.WriteLine(Usage);
            Console.WriteLine();
            Console.WriteLine("  --out DIR|FILE      write the .proto descriptor (a directory gets <spec>.proto)");
            Console.WriteLine("  --service-name NAME base name of the generated service ('Service' is appended when absent)");
            Console.WriteLine("  --namespace NS      namespace of the generated C# (defaults to the service name)");
            return 0;
        }

        var command = args[0];
        if (!string.Equals(command, "generate", StringComparison.OrdinalIgnoreCase))
        {
            await Console.Error.WriteLineAsync($"unknown command '{command}'.").ConfigureAwait(false);
            await Console.Error.WriteLineAsync(Usage).ConfigureAwait(false);
            return 2;
        }

        if (args.Length < 2)
        {
            await Console.Error.WriteLineAsync("missing spec file.").ConfigureAwait(false);
            await Console.Error.WriteLineAsync(Usage).ConfigureAwait(false);
            return 2;
        }

        var file = args[1];
        string? outDir = null;
        string? serviceName = null;
        string? ns = null;

        for (var i = 2; i < args.Length; i++)
        {
            var (name, value) = TryReadOption(args, i);
            if (name is null || value is null)
            {
                await Console.Error.WriteLineAsync($"unknown argument '{args[i]}'.").ConfigureAwait(false);
                await Console.Error.WriteLineAsync(Usage).ConfigureAwait(false);
                return 2;
            }

            i++;
            switch (name)
            {
                case "--out": outDir = value; break;
                case "--service-name": serviceName = value; break;
                case "--namespace": ns = value; break;
            }
        }

        if (!File.Exists(file))
        {
            await Console.Error.WriteLineAsync($"file not found: {file}").ConfigureAwait(false);
            return 2;
        }

        return await GenerateAsync(file, outDir, serviceName, ns).ConfigureAwait(false);
    }

    /// <summary>Reads <c>--name value</c> at <paramref name="index"/>, or a name-less pair when it is malformed.</summary>
    private static (string? Name, string? Value) TryReadOption(string[] args, int index)
    {
        var name = args[index];
        if (index + 1 >= args.Length) return (null, null);
        return name is "--out" or "--service-name" or "--namespace" ? (name, args[index + 1]) : (null, null);
    }

    private static async Task<int> GenerateAsync(string file, string? outDir, string? serviceName, string? ns)
    {
        try
        {
            var text = await File.ReadAllTextAsync(file).ConfigureAwait(false);

            // The work item is built through the AdditionalFiles route, so every default - the service name
            // suffix, the pagination parameters, proxy mode off - is the one a build would use. A CLI that
            // arranged these itself would be a second set of defaults to keep in step with the first.
            var stem = Path.GetFileNameWithoutExtension(file);
            var request = new AdditionalFileRequest(
                Path: file,
                ServiceName: string.IsNullOrWhiteSpace(serviceName) ? stem : serviceName,
                NamespaceName: ns,
                Content: text);
            var item = request.ToWorkItem();

            var result = OpenApiParser.ParseAndMap(
                item, ImmutableArray<AdditionalFileRequest>.Empty, CancellationToken.None);

            foreach (var diagnostic in result.Diagnostics)
            {
                await Console.Error.WriteLineAsync(Format(diagnostic)).ConfigureAwait(false);
            }

            if (!result.IsSuccess || result.Model is null)
            {
                await Console.Error.WriteLineAsync($"'{file}' was not mapped; nothing was generated.").ConfigureAwait(false);
                return 1;
            }

            var model = result.Model;
            Console.WriteLine($"spec:       {file}");
            Console.WriteLine($"service:    {model.ServiceName}");
            Console.WriteLine($"namespace:  {model.Namespace}");
            Console.WriteLine($"package:    {model.ProtoPackage}");

            foreach (var service in model.Services)
            {
                Console.WriteLine($"rpc:        {service.Name} ({service.RpcMethods.Length})");
                foreach (var rpc in service.RpcMethods)
                {
                    Console.WriteLine($"  {rpc.Name} - {rpc.HttpMethod} {rpc.OriginalPath} ({rpc.Kind})");
                }
            }

            Console.WriteLine($"messages:   {model.Messages.Length}");
            Console.WriteLine($"enums:      {model.Enums.Length}");

            if (outDir is not null)
            {
                await WriteProtoAsync(model, item, outDir).ConfigureAwait(false);
            }

            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"error: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }

    /// <summary>
    /// Writes the descriptor the generator embeds: LF-normalized, exactly as the generated
    /// <c>{Service}Proto.Text</c> const holds it, so the file and the const are the same bytes.
    /// </summary>
    private static async Task WriteProtoAsync(GrpcModel model, OpenApiWorkItem item, string outDir)
    {
        var proto = ProtoEmitter.Emit(model, item).Replace("\r\n", "\n");
        var target = outDir.EndsWith(".proto", StringComparison.OrdinalIgnoreCase)
            ? outDir
            : Path.Combine(outDir, item.HintName + ".proto");

        var directory = Path.GetDirectoryName(Path.GetFullPath(target));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(target, proto, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            .ConfigureAwait(false);
        Console.WriteLine($"proto:      {target}");
    }

    /// <summary>
    /// Renders a mapped diagnostic the way a compiler would, so the CLI reports the same refusal, with the same
    /// code and the same message, that building the same spec reports.
    /// </summary>
    private static string Format(GeneratorDiagnostic diagnostic)
    {
        var descriptor = diagnostic.Descriptor;
        var message = string.Format(
            CultureInfo.InvariantCulture,
            descriptor.MessageFormat.ToString(CultureInfo.InvariantCulture),
            diagnostic.Arguments);

        var location = string.Empty;
        if (diagnostic.Location is { } l && l.SourceTree is { } tree)
        {
            location = $"{tree.FilePath}({l.GetLineSpan().StartLinePosition.Line + 1}): ";
        }

        return $"{location}{Severity(descriptor.DefaultSeverity)} {descriptor.Id}: {message}";
    }

    private static string Severity(DiagnosticSeverity severity)
        => severity switch
        {
            DiagnosticSeverity.Error => "error",
            DiagnosticSeverity.Warning => "warning",
            _ => "info",
        };
}
