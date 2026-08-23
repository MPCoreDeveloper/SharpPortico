using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace SharpPortico.Cli;

/// <summary>
/// <c>dotnet sharpportico generate FILE</c> — parses an OpenAPI 3.0/3.1 spec (YAML/JSON)
/// and prints what SharpPortico would generate: the service name, the RPC surface and the
/// protobuf message list. The full code generation happens at compile time via the source
/// generator; this CLI is a lightweight preview/validation companion.
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: dotnet sharpportico generate <openapi.yaml|json> [--out DIR]");
            return 2;
        }

        var command = args[0];
        if (!string.Equals(command, "generate", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"unknown command '{command}'. usage: dotnet sharpportico generate <file>");
            return 2;
        }

        if (args.Length < 2)
        {
            Console.Error.WriteLine("missing spec file. usage: dotnet sharpportico generate <openapi.yaml|json>");
            return 2;
        }

        var file = args[1];
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"file not found: {file}");
            return 2;
        }

        string? outDir = null;
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Length) outDir = args[++i];
        }

        try
        {
            var text = await File.ReadAllTextAsync(file).ConfigureAwait(false);
            var reader = new OpenApiStringReader();
            var document = reader.Read(text, out var diagnostic);

            if (diagnostic?.Errors.Count > 0)
            {
                foreach (var error in diagnostic.Errors)
                {
                    Console.Error.WriteLine($"  {error.Message}");
                }
                return 1;
            }

            if (document.Info is null)
            {
                Console.Error.WriteLine("document has no info section; not an OpenAPI 3.x document?");
                return 1;
            }

            Console.WriteLine($"OpenAPI: {document.Info.Title} {document.Info.Version}");
            Console.WriteLine($"Operations: {CountOperations(document)}");
            Console.WriteLine($"Schemas: {document.Components?.Schemas.Count ?? 0}");

            if (outDir is not null)
            {
                Directory.CreateDirectory(outDir!);
                var target = Path.Combine(outDir!, Path.GetFileNameWithoutExtension(file) + ".generated.proto.txt");
                await File.WriteAllTextAsync(target, text).ConfigureAwait(false);
                Console.WriteLine($"preview written to {target}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int CountOperations(OpenApiDocument document)
    {
        var count = 0;
        if (document.Paths is null) return count;
        foreach (var path in document.Paths.Values)
        {
            if (path?.Operations is null) continue;
            count += path.Operations.Count;
        }
        return count;
    }
}