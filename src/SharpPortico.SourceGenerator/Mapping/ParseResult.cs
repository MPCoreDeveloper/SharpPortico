using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpPortico.Generator.Model;

namespace SharpPortico.Generator.Mapping;

/// <summary>
/// Outcome of parsing and mapping a single OpenAPI work item into the immutable IR.
/// Carries diagnostics produced along the way so the incremental pipeline can report them.
/// </summary>
internal sealed record ParseResult(
    OpenApiWorkItem Item,
    bool IsSuccess,
    GrpcModel? Model,
    ImmutableArray<GeneratorDiagnostic> Diagnostics)
{
    public static ParseResult Success(OpenApiWorkItem item, GrpcModel model, ImmutableArray<GeneratorDiagnostic> diagnostics)
        => new(item, true, model, diagnostics);

    public static ParseResult Failure(OpenApiWorkItem item, GeneratorDiagnostic diagnostic)
        => new(item, false, null, ImmutableArray.Create(diagnostic));

    public static ParseResult Failure(OpenApiWorkItem item, ImmutableArray<GeneratorDiagnostic> diagnostics)
        => new(item, false, null, diagnostics);
}

/// <summary>An immutable, pipeline-friendly diagnostic entry.</summary>
internal sealed record GeneratorDiagnostic(
    DiagnosticDescriptor Descriptor,
    object?[] Arguments,
    Location? Location = null);
