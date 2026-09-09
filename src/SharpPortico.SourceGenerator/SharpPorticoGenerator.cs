using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SharpPortico.Generator.Diagnostics;
using SharpPortico.Generator.Emit;
using SharpPortico.Generator.Model;
using SharpPortico.Generator.Mapping;

namespace SharpPortico.Generator;

/// <summary>
/// Entry point: an <see cref="IIncrementalGenerator"/> that turns OpenAPI 3.0/3.1
/// specifications (YAML/JSON, via the project's AdditionalFiles items or
/// an <c>[OpenApiToGrpc]</c> assembly attribute) into gRPC services, protobuf
/// messages and C# 14 clients.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SharpPorticoGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName = "SharpPortico.OpenApiToGrpcAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        AnalyzerDependencyResolver.EnsureHooked();

        // 1) Attribute-bound requests: [assembly: OpenApiToGrpc("openapi/users.yaml", ...)]
        var attributeRequests = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeMetadataName,
            predicate: static (node, _) => node is CompilationUnitSyntax,
            transform: static (ctx, ct) => AttributeRequest.From(ctx, ct));

        // 2) AdditionalFiles-driven requests: <AdditionalFiles Include="openapi/**/*.yaml" />
        var additionalFiles = context.AdditionalTextsProvider.Where(IsSupportedSpecFile)
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, ct) =>
            {
                var (file, options) = pair;
                var hint = options.GetOptions(file).TryGetValue("build_metadata.AdditionalFiles.SharpporticoServiceName", out var svc)
                    ? svc : Path.GetFileNameWithoutExtension(file.Path);
                var ns = options.GetOptions(file).TryGetValue("build_metadata.AdditionalFiles.SharpporticoNamespace", out var n)
                    ? n : null;
                return new AdditionalFileRequest(file.Path, hint, ns, file.GetText(ct)?.ToString() ?? string.Empty);
            });

        // 3) Attribute requests + additional files -> one stream of work items.
        // The attribute stream is kept as the left side: when both an attribute and an
        // AdditionalFiles entry describe the same spec (as the sample does), the dedup
        // below keys on the file name and the attribute item wins.
        // Project both request types to OpenApiWorkItem explicitly.
        var attributeWorkItems = attributeRequests
            .Select(static (req, _) => req?.ToWorkItem())
            .Where(static w => w is not null)
            .Select(static (w, _) => w)
            .Collect()
            .WithTrackingName("SP_AttributeWorkItems");

        var fileWorkItems = additionalFiles
            .Select(static (f, _) => f.ToWorkItem())
            .Collect()
            .WithTrackingName("SP_FileWorkItems");

        var workItems = attributeWorkItems
            .Combine(fileWorkItems)
            .SelectMany(static (pair, _) => MergeWorkItems(pair))
            .WithTrackingName("SP_WorkItems");

        // 4) Parse + map into immutable IR; content for attribute items is resolved
        //    against the collected AdditionalFiles.
        var parsed = workItems
            .Combine(additionalFiles.Collect())
            .Select(static (pair, ct) =>
            {
                var (item, files) = pair;
                return OpenApiParser.ParseAndMap(item, files, ct);
            })
            .WithTrackingName("SP_Parse");

        // 5) Report diagnostics from parsing/mapping.
        context.RegisterSourceOutput(parsed, static (spc, result) => ReportParseDiagnostics(spc, result));

        // 6) Emit generated C# (messages + gRPC contract + client + DI).
        context.RegisterSourceOutput(parsed, static (spc, result) => EmitParseResultSources(spc, result));
    }

    private static ImmutableArray<OpenApiWorkItem> MergeWorkItems(
        (ImmutableArray<OpenApiWorkItem> Attributes, ImmutableArray<OpenApiWorkItem> Files) pair)
    {
        var (attrs, files) = pair;
        var all = ImmutableArray.CreateBuilder<OpenApiWorkItem>(attrs.Length);
        foreach (var a in attrs)
        {
            all.Add(a);
        }

        var explicitNames = new HashSet<string>(
            attrs.Select(static a => System.IO.Path.GetFileName(a.FilePath))
                 .Where(static n => !string.IsNullOrEmpty(n)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var f in files.Where(f => !explicitNames.Contains(System.IO.Path.GetFileName(f.FilePath))))
        {
            all.Add(f);
        }
        return all.ToImmutable();
    }

    private static void ReportParseDiagnostics(SourceProductionContext spc, ParseResult result)
    {
        foreach (var d in result.Diagnostics)
        {
            spc.ReportDiagnostic(Diagnostic.Create(d.Descriptor, d.Location, d.Arguments));
        }
    }

    private static void EmitParseResultSources(SourceProductionContext spc, ParseResult result)
    {
        if (!result.IsSuccess || result.Model is null) return;
        var sources = CodeEmitter.Emit(result.Model, result.Item);
        foreach (var (hint, text) in sources)
        {
            spc.AddSource(hint, SourceText.From(text, System.Text.Encoding.UTF8));
        }
    }

    private static bool IsSupportedSpecFile(AdditionalText file)
    {
        var p = file.Path;
        return p.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }
}