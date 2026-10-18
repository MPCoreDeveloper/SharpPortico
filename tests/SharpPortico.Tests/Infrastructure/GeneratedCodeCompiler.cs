using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
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
        => Compile(sources, languageVersion, "SharpPortico.Generated.Under.Test")
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.ToString())
            .ToImmutableArray();

    /// <summary>
    /// Compiles sources and loads the result, so a test can run the code the generator emitted instead of reading it.
    /// </summary>
    /// <remarks>
    /// Presence is a runtime fact: an absent field and a field set to its default hold the same value and produce
    /// different bytes, and only running the emitted message can show whether the mapping carried that difference
    /// through. The assembly gets a unique name per call, because two assemblies with one identity cannot share a
    /// load context and more than one test loads an emission.
    /// </remarks>
    /// <param name="sources">The generated sources.</param>
    /// <param name="languageVersion">The C# version to compile them as.</param>
    /// <returns>The loaded assembly, or <see langword="null"/> and the errors when it did not compile.</returns>
    public static (Assembly? Assembly, ImmutableArray<string> Errors) Load(
        IEnumerable<string> sources,
        LanguageVersion languageVersion = LanguageVersion.Latest)
    {
        var compilation = Compile(
            sources,
            languageVersion,
            "SharpPortico.Generated.Under.Test." + Guid.NewGuid().ToString("N"));

        using var stream = new MemoryStream();
        var errors = compilation
            .Emit(stream)
            .Diagnostics
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.ToString())
            .ToImmutableArray();

        return errors.IsEmpty
            ? (Assembly.Load(stream.ToArray()), errors)
            : (null, errors);
    }

    private static CSharpCompilation Compile(
        IEnumerable<string> sources,
        LanguageVersion languageVersion,
        string assemblyName)
        => CSharpCompilation.Create(
            assemblyName,
            sources.Select(source => CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(languageVersion))),
            References(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

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

    private static string[] TrustedAssemblies() =>
        AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted
            ? trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            : [];
}
