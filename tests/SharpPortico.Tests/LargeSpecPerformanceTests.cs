using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using SharpPortico.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SharpPortico.Tests;

/// <summary>
/// Verifies the generator stays fast on very large OpenAPI specifications (~10,000 lines / hundreds of operations),
/// and that the CLI's machine-readable path does not pay for itself.
/// </summary>
public class LargeSpecPerformanceTests
{
    private static readonly TimeSpan GeneratorBudget = TimeSpan.FromSeconds(10);

    private readonly ITestOutputHelper _output;

    public LargeSpecPerformanceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Generator_Handles_10000_Line_Spec_Under_10_Seconds()
    {
        var spec = LargeSpecBuilder.Build(operations: 400, propertiesPerSchema: 5);

        var sw = Stopwatch.StartNew();
        var result = GeneratorTestDriver.Run(spec, serviceName: "LargeService");
        sw.Stop();

        _output.WriteLine($"Ran in {sw.Elapsed.TotalSeconds:F2}s; {result.GeneratedSource.Split('\n').Length} generated lines");
        Assert.Empty(result.Diagnostics);
        // .g.cs (C#) plus .proto.cs (proto descriptor) are emitted.
        Assert.Equal(2, result.Sources.Count);
        Assert.True(sw.Elapsed < GeneratorBudget, $"generator took {sw.Elapsed.TotalSeconds:F2}s");
    }

    /// <summary>
    /// SC-005: the report costs no more than 10% over the summary it sits beside. Both paths are measured as the
    /// median of five runs after one warm-up, so a cold start or a noisy host cannot decide the result.
    /// </summary>
    [Fact]
    public async Task The_Json_Report_Costs_At_Most_Ten_Percent_Over_The_Text_Summary()
    {
        using var directory = new CliHarness.TempDirectory();
        var spec = directory.PathOf("large.yaml");
        await File.WriteAllTextAsync(spec, LargeSpecBuilder.Build(operations: 400, propertiesPerSchema: 5));

        // One warm-up per path before anything is timed.
        await MeasureAsync(spec, "--format", "text");
        await MeasureAsync(spec, "--format", "json");

        var text = await MedianAsync(spec, "--format", "text");
        var json = await MedianAsync(spec, "--format", "json");

        _output.WriteLine($"text median {text.TotalMilliseconds:F0} ms, json median {json.TotalMilliseconds:F0} ms");
        Assert.True(
            json <= text * 1.10,
            $"the JSON path took {json.TotalMilliseconds:F0} ms against {text.TotalMilliseconds:F0} ms of text, more than the 10% budget");
    }

    private static async Task<TimeSpan> MedianAsync(string spec, params string[] options)
    {
        var runs = new List<TimeSpan>(capacity: 5);
        for (var i = 0; i < 5; i++) runs.Add(await MeasureAsync(spec, options));
        runs.Sort();
        return runs[runs.Count / 2];
    }

    private static async Task<TimeSpan> MeasureAsync(string spec, params string[] options)
    {
        var sw = Stopwatch.StartNew();
        var (_, stderr, exit) = await CliHarness.GenerateAsync([spec, .. options]);
        sw.Stop();

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, stderr);
        return sw.Elapsed;
    }
}
