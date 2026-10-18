using System.Globalization;
using System.Text;

namespace SharpPortico.Tests.Infrastructure;

/// <summary>
/// The repository's large-spec fixture: hundreds of operations and schemas, big enough that the mapping dominates
/// and small enough to run in a test. Shared so the generator's own time budget and the CLI's report cost are
/// measured on the same input.
/// </summary>
internal static class LargeSpecBuilder
{
    public static string Build(int operations, int propertiesPerSchema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("openapi: 3.0.3");
        sb.AppendLine("info: { title: Large, version: 1.0.0 }");
        sb.AppendLine("paths:");
        for (var i = 0; i < operations; i++)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  /resource{i}/{{id}}:");
            sb.AppendLine($"    get:");
            sb.AppendLine(CultureInfo.InvariantCulture, $"      operationId: getResource{i}");
            sb.AppendLine($"      parameters:");
            sb.AppendLine($"        - {{ name: id, in: path, required: true, schema: {{ type: integer, format: int64 }} }}");
            sb.AppendLine($"      responses:");
            sb.AppendLine($"        '200':");
            sb.AppendLine($"          description: ok");
            sb.AppendLine($"          content:");
            sb.AppendLine($"            application/json:");
            sb.AppendLine(CultureInfo.InvariantCulture, $"              schema: {{ $ref: '#/components/schemas/Resource{i}' }}");
        }

        sb.AppendLine("components:");
        sb.AppendLine("  schemas:");
        for (var i = 0; i < operations; i++)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"    Resource{i}:");
            sb.AppendLine("      type: object");
            sb.AppendLine("      properties:");
            for (var p = 0; p < propertiesPerSchema; p++)
            {
                var type = (p % 4) switch
                {
                    0 => "type: string",
                    1 => "type: integer, format: int64",
                    2 => "type: boolean",
                    _ => "type: number, format: double",
                };
                sb.AppendLine(CultureInfo.InvariantCulture, $"        prop{p}: {{ {type} }}");
            }
        }

        return sb.ToString();
    }
}
