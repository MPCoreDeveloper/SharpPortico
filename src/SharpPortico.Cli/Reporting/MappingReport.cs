using System;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using SharpPortico.Generator.Mapping;
using SharpPortico.Generator.Model;

namespace SharpPortico.Cli.Reporting;

/// <summary>
/// The outcome of one run, projected from the values the mapping already produced: the model the generator
/// would emit from and the diagnostics it reported. Nothing here decides anything - a severity is transported,
/// a count is copied - so a report and a build of the same spec cannot disagree.
/// </summary>
/// <param name="FormatVersion">The document shape; bumped only when a consumer would misread the document.</param>
/// <param name="Spec">The path exactly as it was given on the command line.</param>
/// <param name="Service">The mapped identity, or <see langword="null"/> when the contract was refused.</param>
/// <param name="Services">The mapped services and their operations; empty when nothing was mapped.</param>
/// <param name="Counts">Message and enum counts; present even when zero.</param>
/// <param name="Diagnostics">The generator's diagnostics, in report order.</param>
/// <param name="Descriptor">Present only after the descriptor was written successfully.</param>
internal sealed record MappingReport(
    int FormatVersion,
    string Spec,
    ServiceIdentity? Service,
    ImmutableArray<ServiceEntry> Services,
    ReportCounts Counts,
    ImmutableArray<DiagnosticEntry> Diagnostics,
    DescriptorInfo? Descriptor = null)
{
    /// <summary>
    /// The shape this writer emits. It tracks the document only: the package version moves on its own
    /// schedule, and a release that does not touch the document does not move this.
    /// </summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// Projects one run. A refused contract has no model, so it has no identity and no services - but it
    /// still has its diagnostics, which is what a gate reads (FR-009).
    /// </summary>
    public static MappingReport From(
        string spec,
        GrpcModel? model,
        ImmutableArray<GeneratorDiagnostic> diagnostics,
        DescriptorInfo? descriptor = null)
    {
        var services = model is null
            ? ImmutableArray<ServiceEntry>.Empty
            : model.Services
                .Select(static service => new ServiceEntry(
                    service.Name,
                    service.RpcMethods
                        .Select(static rpc => new RpcEntry(rpc.Name, rpc.HttpMethod, rpc.OriginalPath, rpc.Kind.ToString()))
                        .ToImmutableArray()))
                .ToImmutableArray();

        return new MappingReport(
            CurrentFormatVersion,
            spec,
            model is null ? null : new ServiceIdentity(model.ServiceName, model.Namespace, model.ProtoPackage),
            services,
            model is null ? new ReportCounts(0, 0) : new ReportCounts(model.Messages.Length, model.Enums.Length),
            diagnostics.Select(DiagnosticEntry.From).ToImmutableArray(),
            descriptor);
    }
}

/// <summary>The three strings that name what a build would generate.</summary>
internal sealed record ServiceIdentity(string Name, string Namespace, string Package);

/// <summary>One mapped service and the operations it carries.</summary>
internal sealed record ServiceEntry(string Name, ImmutableArray<RpcEntry> Rpcs);

/// <summary>One operation, as the specification declared it.</summary>
internal sealed record RpcEntry(string Name, string HttpMethod, string Path, string Kind);

/// <summary>What a reviewer compares between two contract revisions.</summary>
internal sealed record ReportCounts(int Messages, int Enums);

/// <summary>The file that was written, exactly as the text mode names it.</summary>
internal sealed record DescriptorInfo(string Path);

/// <summary>
/// One diagnostic, transported rather than re-assessed: the id, the severity the generator assigned, the same
/// message the compiler reports, and the source line when one is known (never guessed).
/// </summary>
internal sealed record DiagnosticEntry(string Id, string Severity, string Message, int? Line)
{
    public static DiagnosticEntry From(GeneratorDiagnostic diagnostic) => new(
        diagnostic.Descriptor.Id,
        SeverityOf(diagnostic.Descriptor.DefaultSeverity),
        MessageOf(diagnostic),
        LineOf(diagnostic.Location));

    /// <summary>The severity as a consumer reads it, from the descriptor the generator used.</summary>
    public static string SeverityOf(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "error",
        DiagnosticSeverity.Warning => "warning",
        _ => "info",
    };

    /// <summary>The message with the descriptor's arguments applied, in the invariant culture.</summary>
    public static string MessageOf(GeneratorDiagnostic diagnostic) => string.Format(
        CultureInfo.InvariantCulture,
        diagnostic.Descriptor.MessageFormat.ToString(CultureInfo.InvariantCulture),
        diagnostic.Arguments);

    /// <summary>
    /// The <c>path(line): </c> prefix the text mode prints, or an empty string when the diagnostic carries no
    /// source tree. Shared so the two modes cannot phrase the same diagnostic differently.
    /// </summary>
    public static string LocationOf(Location? location)
        => location is { SourceTree: { } tree }
            ? $"{tree.FilePath}({location.GetLineSpan().StartLinePosition.Line + 1}): "
            : string.Empty;

    /// <summary>
    /// The 1-based line, or <see langword="null"/> when the generator supplied no source tree. A location
    /// without a line span is not turned into line 0: an absent line is not a line.
    /// </summary>
    public static int? LineOf(Location? location)
    {
        if (location is not { SourceTree: not null }) return null;

        var line = location.GetLineSpan().StartLinePosition.Line + 1;
        return line > 0 ? line : null;
    }
}
