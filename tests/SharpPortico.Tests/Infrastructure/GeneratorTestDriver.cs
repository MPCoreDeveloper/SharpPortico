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
        var item = Item(specContent, serviceName, namespaceName);
        var result = OpenApiParser.ParseAndMap(item, ImmutableArray<AdditionalFileRequest>.Empty, ct);

        if (!result.IsSuccess || result.Model is null)
        {
            throw new InvalidOperationException(
                "Mapping failed: " + string.Join(" | ", result.Diagnostics.Select(d => d.ToString())));
        }

        return new RunResult(DiagnosticsOf(result), Emit(result.Model, item));
    }

    /// <summary>
    /// Runs the same pipeline without throwing, so a specification that has to be refused can be
    /// asserted on: the diagnostics say why, and no sources are produced.
    /// </summary>
    public static RunResult TryRun(
        string specContent,
        string? serviceName = null,
        string? namespaceName = null,
        CancellationToken ct = default)
    {
        var item = Item(specContent, serviceName, namespaceName);
        var result = OpenApiParser.ParseAndMap(item, ImmutableArray<AdditionalFileRequest>.Empty, ct);

        return new RunResult(
            DiagnosticsOf(result),
            result.IsSuccess && result.Model is not null
                ? Emit(result.Model, item)
                : ImmutableDictionary<string, string>.Empty);
    }

    private static OpenApiWorkItem Item(string specContent, string? serviceName, string? namespaceName)
        => new(
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

    private static IReadOnlyDictionary<string, string> Emit(GrpcModel model, OpenApiWorkItem item)
        => CodeEmitter.Emit(model, item)
            .ToImmutableDictionary(static pair => pair.HintName, static pair => pair.Text, StringComparer.Ordinal);

    /// <summary>The parse diagnostics as "ID args" text, so a test can assert on an identifier.</summary>
    private static ImmutableArray<string> DiagnosticsOf(ParseResult result)
        => result.Diagnostics
            .Select(static d => d.Descriptor.Id + " " + string.Join(" ", d.Arguments.Select(static a => a?.ToString())))
            .ToImmutableArray();
}