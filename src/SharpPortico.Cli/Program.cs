using System;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpPortico.Cli.Reporting;
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
        "usage: sharpportico generate <openapi.yaml|json> [--out DIR|FILE] [--service-name NAME] [--namespace NS] [--format text|json]";

    /// <summary>The console host. The command itself lives in <see cref="RunAsync"/> so it can be driven by tests.</summary>
    public static Task<int> Main(string[] args) => RunAsync(args, Console.Out, Console.Error);

    /// <summary>
    /// Runs the command with its streams supplied by the caller. Tests pass writers, so the real argument
    /// parsing, the real pipeline and the real exit codes are exercised without redirecting
    /// <see cref="Console"/> - two tests racing for the console would be flaky, and a redirected console is a
    /// different code path from the one a user runs.
    /// </summary>
    internal static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            await stderr.WriteLineAsync(Usage).ConfigureAwait(false);
            return 2;
        }

        if (args[0] is "--help" or "-h")
        {
            await stdout.WriteLineAsync(Usage).ConfigureAwait(false);
            await stdout.WriteLineAsync().ConfigureAwait(false);
            await stdout.WriteLineAsync("  --out DIR|FILE      write the .proto descriptor (a directory gets <spec>.proto)").ConfigureAwait(false);
            await stdout.WriteLineAsync("  --service-name NAME base name of the generated service ('Service' is appended when absent)").ConfigureAwait(false);
            await stdout.WriteLineAsync("  --namespace NS      namespace of the generated C# (defaults to the service name)").ConfigureAwait(false);
            await stdout.WriteLineAsync("  --format text|json  'text' (default) prints the summary; 'json' prints one document").ConfigureAwait(false);
            return 0;
        }

        var command = args[0];
        if (!string.Equals(command, "generate", StringComparison.OrdinalIgnoreCase))
        {
            await stderr.WriteLineAsync($"unknown command '{command}'.").ConfigureAwait(false);
            await stderr.WriteLineAsync(Usage).ConfigureAwait(false);
            return 2;
        }

        if (args.Length < 2)
        {
            await stderr.WriteLineAsync("missing spec file.").ConfigureAwait(false);
            await stderr.WriteLineAsync(Usage).ConfigureAwait(false);
            return 2;
        }

        var file = args[1];
        string? outDir = null;
        string? serviceName = null;
        string? ns = null;
        var format = OutputFormat.Text;

        for (var i = 2; i < args.Length; i++)
        {
            var (name, value) = TryReadOption(args, i);
            if (name is null || value is null)
            {
                await stderr.WriteLineAsync($"unknown argument '{args[i]}'.").ConfigureAwait(false);
                await stderr.WriteLineAsync(Usage).ConfigureAwait(false);
                return 2;
            }

            i++;
            switch (name)
            {
                case "--out": outDir = value; break;
                case "--service-name": serviceName = value; break;
                case "--namespace": ns = value; break;
                case "--format":
                    if (!TryParseFormat(value, out format))
                    {
                        await stderr.WriteLineAsync($"unknown value '{value}' for --format.").ConfigureAwait(false);
                        await stderr.WriteLineAsync(Usage).ConfigureAwait(false);
                        return 2;
                    }

                    break;
            }
        }

        if (!File.Exists(file))
        {
            await stderr.WriteLineAsync($"file not found: {file}").ConfigureAwait(false);
            return 2;
        }

        return await GenerateAsync(file, outDir, serviceName, ns, format, stdout, stderr).ConfigureAwait(false);
    }

    /// <summary>Reads <c>--name value</c> at <paramref name="index"/>, or a name-less pair when it is malformed.</summary>
    private static (string? Name, string? Value) TryReadOption(string[] args, int index)
    {
        var name = args[index];
        if (index + 1 >= args.Length) return (null, null);
        return name is "--out" or "--service-name" or "--namespace" or "--format"
            ? (name, args[index + 1])
            : (null, null);
    }

    /// <summary>
    /// Reads <c>--format</c>. Compared case-insensitively, and only these two words: a value that is not a
    /// format is a usage error rather than a silent fallback, so a typo in CI fails the job instead of quietly
    /// producing the wrong format.
    /// </summary>
    private static bool TryParseFormat(string value, out OutputFormat format)
    {
        if (string.Equals(value, "text", StringComparison.OrdinalIgnoreCase))
        {
            format = OutputFormat.Text;
            return true;
        }

        if (string.Equals(value, "json", StringComparison.OrdinalIgnoreCase))
        {
            format = OutputFormat.Json;
            return true;
        }

        format = OutputFormat.Text;
        return false;
    }

    private static async Task<int> GenerateAsync(
        string file,
        string? outDir,
        string? serviceName,
        string? ns,
        OutputFormat format,
        TextWriter stdout,
        TextWriter stderr)
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

            if (format == OutputFormat.Json)
            {
                return await WriteJsonReportAsync(file, result, item, outDir, stdout).ConfigureAwait(false);
            }

            foreach (var diagnostic in result.Diagnostics)
            {
                await stderr.WriteLineAsync(Format(diagnostic)).ConfigureAwait(false);
            }

            if (!result.IsSuccess || result.Model is null)
            {
                await stderr.WriteLineAsync($"'{file}' was not mapped; nothing was generated.").ConfigureAwait(false);
                return 1;
            }

            var model = result.Model;
            await stdout.WriteLineAsync($"spec:       {file}").ConfigureAwait(false);
            await stdout.WriteLineAsync($"service:    {model.ServiceName}").ConfigureAwait(false);
            await stdout.WriteLineAsync($"namespace:  {model.Namespace}").ConfigureAwait(false);
            await stdout.WriteLineAsync($"package:    {model.ProtoPackage}").ConfigureAwait(false);

            foreach (var service in model.Services)
            {
                await stdout.WriteLineAsync($"rpc:        {service.Name} ({service.RpcMethods.Length})").ConfigureAwait(false);
                foreach (var rpc in service.RpcMethods)
                {
                    await stdout.WriteLineAsync($"  {rpc.Name} - {rpc.HttpMethod} {rpc.OriginalPath} ({rpc.Kind})").ConfigureAwait(false);
                }
            }

            await stdout.WriteLineAsync($"messages:   {model.Messages.Length}").ConfigureAwait(false);
            await stdout.WriteLineAsync($"enums:      {model.Enums.Length}").ConfigureAwait(false);

            if (outDir is not null)
            {
                var target = await WriteProtoAsync(model, item, outDir).ConfigureAwait(false);
                await stdout.WriteLineAsync($"proto:      {target}").ConfigureAwait(false);
            }

            return 0;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync($"error: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }

    /// <summary>
    /// Writes the report for a run that produced a model or a refusal. The descriptor is written first: a
    /// document that names a descriptor path must never name a file that was not written, so a failure here
    /// leaves standard output empty and reports on standard error.
    /// </summary>
    private static async Task<int> WriteJsonReportAsync(
        string file,
        ParseResult result,
        OpenApiWorkItem item,
        string? outDir,
        TextWriter stdout)
    {
        var report = MappingReport.From(file, result.Model, result.Diagnostics);

        if (result.IsSuccess && result.Model is not null && outDir is not null)
        {
            var target = await WriteProtoAsync(result.Model, item, outDir).ConfigureAwait(false);
            report = report with { Descriptor = new DescriptorInfo(target) };
        }

        await stdout.WriteAsync(JsonReportWriter.Serialize(report)).ConfigureAwait(false);
        return result.IsSuccess && result.Model is not null ? 0 : 1;
    }

    /// <summary>
    /// Writes the descriptor the generator embeds: LF-normalized, exactly as the generated
    /// <c>{Service}Proto.Text</c> const holds it, so the file and the const are the same bytes. Returns the path
    /// it wrote, which is the path both modes report.
    /// </summary>
    private static async Task<string> WriteProtoAsync(GrpcModel model, OpenApiWorkItem item, string outDir)
    {
        var proto = ProtoEmitter.Emit(model, item).Replace("\r\n", "\n");
        var target = DescriptorTarget(outDir, item);

        var directory = Path.GetDirectoryName(Path.GetFullPath(target));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(target, proto, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            .ConfigureAwait(false);
        return target;
    }

    /// <summary>
    /// Where <c>--out</c> puts the descriptor: the file itself when it was named with <c>.proto</c>, otherwise
    /// the hint name inside the directory. One definition, so the document and the summary cannot disagree.
    /// </summary>
    private static string DescriptorTarget(string outDir, OpenApiWorkItem item)
        => outDir.EndsWith(".proto", StringComparison.OrdinalIgnoreCase)
            ? outDir
            : Path.Combine(outDir, item.HintName + ".proto");

    /// <summary>
    /// Renders a mapped diagnostic the way a compiler would, so the CLI reports the same refusal, with the same
    /// code and the same message, that building the same spec reports.
    /// </summary>
    private static string Format(GeneratorDiagnostic diagnostic)
        => $"{DiagnosticEntry.LocationOf(diagnostic.Location)}" +
           $"{DiagnosticEntry.SeverityOf(diagnostic.Descriptor.DefaultSeverity)} " +
           $"{diagnostic.Descriptor.Id}: {DiagnosticEntry.MessageOf(diagnostic)}";
}

/// <summary>What the command writes to standard output.</summary>
internal enum OutputFormat
{
    /// <summary>The summary line by line, with diagnostics on standard error; the default.</summary>
    Text,

    /// <summary>One machine-readable document on standard output and nothing else.</summary>
    Json,
}

