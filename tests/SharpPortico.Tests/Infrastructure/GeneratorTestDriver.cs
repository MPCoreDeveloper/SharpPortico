using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using SharpPortico.Generator;
using SharpPortico.Generator.Emit;
using SharpPortico.Generator.Mapping;
using SharpPortico.Generator.Model;

namespace SharpPortico.Tests.Infrastructure;

/// <summary>
/// Drives the generator's pure mapping/emission pipeline directly (the same functions the
/// incremental generator executes at compile time) and returns the emitted source text.
/// The full end-to-end integration (MSBuild + analyzer + run) is covered by the
/// GrpcServerExample sample, which is built and executed in CI.
/// </summary>
internal static class GeneratorTestDriver
{
    /// <summary>Returns generated sources keyed by hint name, plus any parse diagnostics.</summary>
    public sealed record RunResult(
        ImmutableArray<string> Diagnostics,
        IReadOnlyDictionary<string, string> Sources)
    {
        /// <summary>The main generated C# file (hint ends with .g.cs).</summary>
        public string GeneratedSource => Sources
            .First(static pair => pair.Key.EndsWith(".g.cs", StringComparison.Ordinal)).Value;
    }

    public static RunResult Run(
        string specContent,
        string? serviceName = null,
        string? namespaceName = null,
        CancellationToken ct = default)
    {
        var item = new OpenApiWorkItem(
            FilePath: "openapi/petstore.yaml",
            HintName: "petstore",
            ServiceName: serviceName,
            NamespaceName: namespaceName,
            Content: specContent,
            EmitProtoFile: true,
            EmitClient: true,
            EmitServer: true,
            EmitDependencyInjection: true,
            RespectStreamingHints: true,
            EmitDiagnosticsForUnmappableConstructs: true,
            GenerateAuthMetadataHelpers: true,
            GenerateAuthInterceptors: true,
            DetectPagination: true,
            PaginationPageParameter: "page",
            PaginationLimitParameter: "limit",
            PaginationCursorParameter: "cursor",
            PaginationNextPageTokenParameter: "next_page_token",
            EmitGoogleRpcStatusWrapper: true,
            ServiceNameSuffix: "Service",
            LargePayloadStreamingThresholdBytes: 1_000_000);

        var result = OpenApiParser.ParseAndMap(item, ImmutableArray<AdditionalFileRequest>.Empty, ct);

        if (!result.IsSuccess || result.Model is null)
        {
            throw new InvalidOperationException(
                "Mapping failed: " + string.Join(" | ", result.Diagnostics.Select(d => d.ToString())));
        }

        var sources = CodeEmitter.Emit(result.Model!, item)
            .ToImmutableDictionary(static pair => pair.HintName, static pair => pair.Text, StringComparer.Ordinal);

        return new RunResult(ImmutableArray<string>.Empty, sources);
    }
}