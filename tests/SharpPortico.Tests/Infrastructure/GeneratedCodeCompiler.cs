using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpPortico.Tests.Infrastructure;

/// <summary>
/// Compiles generated sources against the framework and the packages the output depends on.
/// </summary>
/// <remarks>
/// Text assertions cannot catch a contract that does not compile, and that is a generator's most damaging
/// kind of failure: the consumer discovers it as a compiler error inside a generated file, with no way to
/// fix it. So the emitted code is compiled here for real, with the assemblies this test project carries.
/// </remarks>
internal static class GeneratedCodeCompiler
{
    /// <summary>Compiles sources and returns the compiler errors, empty when they compile.</summary>
    /// <param name="sources">The generated sources.</param>
    /// <param name="languageVersion">The C# version to compile them as.</param>
    public static ImmutableArray<string> Errors(
        IEnumerable<string> sources,
        LanguageVersion languageVersion = LanguageVersion.Latest)
    {
        var compilation = CSharpCompilation.Create(
            "SharpPortico.Generated.Under.Test",
            sources.Select(source => CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(languageVersion))),
            References(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return
        [
            .. compilation
                .GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(static diagnostic => diagnostic.ToString()),
        ];
    }

    private static ImmutableArray<MetadataReference> References()
    {
        // Everything next to this test, plus the assemblies this process actually resolved: between them
        // that is the framework, Google.Protobuf and Grpc.Core.Api, which is everything the generated code
        // names. The test's directory comes first so a package's assembly wins over a same-named framework
        // one, and TRUSTED_PLATFORM_ASSEMBLIES is used for the framework because it lists managed assemblies
        // only - a native library in the runtime directory is not a reference a compilation can take.
        var paths = Directory
            .EnumerateFiles(AppContext.BaseDirectory, "*.dll")
            .OrderBy(static candidate => candidate, StringComparer.Ordinal)
            .Concat(TrustedAssemblies().OrderBy(static candidate => candidate, StringComparer.Ordinal));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = ImmutableArray.CreateBuilder<MetadataReference>();

        foreach (var path in paths)
        {
            if (!seen.Add(Path.GetFileName(path)))
            {
                continue;
            }

            try
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
            catch (BadImageFormatException)
            {
                // A native library next to the test is not something a C# compilation can reference.
            }
        }

        return references.ToImmutable();
    }

    private static IEnumerable<string> TrustedAssemblies() =>
        AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted
            ? trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            : [];
}
