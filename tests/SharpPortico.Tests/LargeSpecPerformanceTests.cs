using System.Diagnostics;
using System.Text;
using SharpPortico.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SharpPortico.Tests;

/// <summary>
/// Verifies the generator stays fast on very large OpenAPI specifications
/// (~10,000 lines / hundreds of operations).
/// </summary>
public class LargeSpecPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public LargeSpecPerformanceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Generator_Handles_10000_Line_Spec_Under_10_Seconds()
    {
        var spec = BuildLargeSpec(operations: 400, propertiesPerSchema: 5);

        var sw = Stopwatch.StartNew();
        var result = GeneratorTestDriver.Run(spec, serviceName: "LargeService");
        sw.Stop();

        _output.WriteLine($"Ran in {sw.Elapsed.TotalSeconds:F2}s; {result.GeneratedSource.Split('\n').Length} generated lines");
        Assert.Empty(result.Diagnostics);
        // .g.cs (C#) plus .proto.cs (proto descriptor) are emitted.
        Assert.Equal(2, result.Sources.Count);
        Assert.True(sw.Elapsed.TotalSeconds < 10, $"generator took {sw.Elapsed.TotalSeconds:F2}s");
    }

    private static string BuildLargeSpec(int operations, int propertiesPerSchema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("openapi: 3.0.3");
        sb.AppendLine("info: { title: Large, version: 1.0.0 }");
        sb.AppendLine("paths:");
        for (var i = 0; i < operations; i++)
        {
            sb.AppendLine($"  /resource{i}:");
            sb.AppendLine($"    get:");
            sb.AppendLine($"      operationId: getResource{i}");
            sb.AppendLine($"      parameters:");
            sb.AppendLine($"        - {{ name: id, in: path, required: true, schema: {{ type: integer, format: int64 }} }}");
            sb.AppendLine($"      responses:");
            sb.AppendLine($"        '200':");
            sb.AppendLine($"          description: ok");
            sb.AppendLine($"          content:");
            sb.AppendLine($"            application/json:");
            sb.AppendLine($"              schema: {{ $ref: '#/components/schemas/Resource{i}' }}");
        }
        sb.AppendLine("components:");
        sb.AppendLine("  schemas:");
        for (var i = 0; i < operations; i++)
        {
            sb.AppendLine($"    Resource{i}:");
            sb.AppendLine("      type: object");
            sb.AppendLine("      properties:");
            for (var p = 0; p < propertiesPerSchema; p++)
            {
                var type = (p % 4) switch
                {
                    0 => "type: string",
                    1 => "type: integer, format: int64",
                    2 => "type: boolean",
                    _ => "type: number, format: double"
                };
                sb.AppendLine($"        prop{p}: {{ {type} }}");
            }
        }
        return sb.ToString();
    }
}