using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using SharpPortico.Cli.Reporting;
using SharpPortico.Generator.Diagnostics;
using SharpPortico.Generator.Mapping;
using SharpPortico.Tests.Infrastructure;
using Xunit;

namespace SharpPortico.Tests;

/// <summary>
/// The machine-readable document is the interface a CI gate programs against, so every field, every stream and
/// every exit code in <c>specs/001-cli-json-output/contracts/cli.md</c> is asserted here rather than described.
/// </summary>
public class CliJsonReportTests
{
    [Fact]
    public async Task A_Mapped_Spec_Becomes_One_Document()
    {
        var spec = CliHarness.Spec("petstore.yaml");

        // 'JSON' rather than 'json': the option is case-insensitive, and a gate should not break on a typo in case.
        var (stdout, stderr, exit) = await CliHarness.GenerateAsync(spec, "--format", "JSON");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, stderr);

        // Parsing the whole stream is the assertion that it holds one document and nothing else.
        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal(spec, root.GetProperty("spec").GetString());

        var identity = root.GetProperty("service");
        Assert.Equal("PetstoreService", identity.GetProperty("name").GetString());
        Assert.Equal("PetstoreService", identity.GetProperty("namespace").GetString());
        Assert.Equal("petstoreservice", identity.GetProperty("package").GetString());

        var service = Assert.Single(root.GetProperty("services").EnumerateArray());
        Assert.Equal("PetstoreService", service.GetProperty("name").GetString());

        var rpcs = service.GetProperty("rpcs").EnumerateArray().ToArray();
        Assert.Equal(3, rpcs.Length);
        Assert.Equal("ListPets", rpcs[0].GetProperty("name").GetString());
        Assert.Equal("GET", rpcs[0].GetProperty("httpMethod").GetString());
        Assert.Equal("/pets", rpcs[0].GetProperty("path").GetString());
        Assert.Equal("Unary", rpcs[0].GetProperty("kind").GetString());
        Assert.Equal("GetPet", rpcs[2].GetProperty("name").GetString());
        Assert.Equal("/pets/{petId}", rpcs[2].GetProperty("path").GetString());

        Assert.Equal(7, root.GetProperty("counts").GetProperty("messages").GetInt32());
        Assert.Equal(1, root.GetProperty("counts").GetProperty("enums").GetInt32());
        Assert.Empty(root.GetProperty("diagnostics").EnumerateArray());

        // A document, not a document plus a line: exactly one trailing newline and no byte-order mark.
        Assert.True(stdout.EndsWith("\n", StringComparison.Ordinal), "the document must end with a newline");
        Assert.False(stdout.EndsWith("\n\n", StringComparison.Ordinal), "the document must end with one newline");
        Assert.False(stdout.StartsWith('\uFEFF'), "the document must not start with a byte-order mark");
    }

    /// <summary>
    /// A refusal is still an answer: the document parses, carries the refusal diagnostic with its own severity,
    /// and the exit code fails the job. Nothing is written to standard error in this mode.
    /// </summary>
    [Fact]
    public async Task A_Refused_Contract_Still_Produces_A_Document()
    {
        using var directory = new CliHarness.TempDirectory();
        var spec = directory.PathOf("collide.yaml");
        await File.WriteAllTextAsync(spec, CollidingSpec);

        var (stdout, stderr, exit) = await CliHarness.GenerateAsync(spec, "--format", "json");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, stderr);

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;

        Assert.Empty(root.GetProperty("services").EnumerateArray());
        Assert.Equal(0, root.GetProperty("counts").GetProperty("messages").GetInt32());
        Assert.Equal(0, root.GetProperty("counts").GetProperty("enums").GetInt32());

        var diagnostic = Assert.Single(root.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("SP2005", diagnostic.GetProperty("id").GetString());
        Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
        Assert.False(string.IsNullOrEmpty(diagnostic.GetProperty("message").GetString()));
        Assert.False(diagnostic.TryGetProperty("line", out _));

        // A refused contract never produced a model, so no identity is invented from the command line, and no
        // descriptor is claimed.
        Assert.False(root.TryGetProperty("service", out _));
        Assert.False(root.TryGetProperty("descriptor", out _));
    }

    /// <summary>
    /// Warnings are transported with the severity the generator assigned. They are not promoted to errors (the
    /// job still passes) and not dropped (a consumer can decide for itself).
    /// </summary>
    [Fact]
    public async Task Warnings_Are_Transported_Not_Promoted_And_Not_Hidden()
    {
        var spec = CliHarness.RepositoryFile("samples", "NativeAotExample", "openapi", "fleet-3.1.yaml");

        var (stdout, stderr, exit) = await CliHarness.GenerateAsync(spec, "--format", "json");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, stderr);

        using var document = JsonDocument.Parse(stdout);
        var diagnostics = document.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray();

        Assert.Contains(diagnostics, d => d.GetProperty("id").GetString() == "SP1002");
        Assert.Contains(diagnostics, d => d.GetProperty("id").GetString() == "SP1003");
        Assert.All(diagnostics, d => Assert.Equal("warning", d.GetProperty("severity").GetString()));

        // This pipeline reports these diagnostics without a source tree, and an absent location is not line 1.
        Assert.All(diagnostics, d => Assert.False(d.TryGetProperty("line", out _)));
    }

    /// <summary>
    /// The one place a line can come from: a diagnostic that carries a source tree. It is reported 1-based, so a
    /// consumer can open the file at that line.
    /// </summary>
    [Fact]
    public void A_Diagnostic_That_Knows_Its_Line_Reports_It_One_Based()
    {
        var tree = CSharpSyntaxTree.ParseText("openapi: 3.0.3\ninfo: { title: T, version: 1 }\n", path: "spec.yaml");
        var location = tree.GetRoot().GetLocation();

        var entry = DiagnosticEntry.From(
            new GeneratorDiagnostic(
                Diagnostics.CannotParseOpenApiFile,
                new object?[] { "spec.yaml", "the reader's complaint" },
                location));

        Assert.Equal("SP1000", entry.Id);
        Assert.Equal("warning", entry.Severity);
        Assert.Equal(1, entry.Line);
    }

    [Theory]
    [InlineData("--format", "xml")]
    [InlineData("--format", "")]
    [InlineData("--unknown", "value")]
    public async Task A_Wrong_Command_Line_Is_A_Usage_Error(string option, string value)
    {
        var (stdout, stderr, exit) = await CliHarness.GenerateAsync(CliHarness.Spec("petstore.yaml"), option, value);

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("usage: sharpportico generate", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Missing_Spec_File_Is_A_Usage_Error()
    {
        var (stdout, stderr, exit) = await CliHarness.GenerateAsync("does-not-exist.yaml", "--format", "json");

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("file not found", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--out</c> reports the path it wrote, and it is the same string the text mode prints: two definitions of
    /// the target would eventually disagree, and the difference would show up as a missing file.
    /// </summary>
    [Fact]
    public async Task The_Written_Descriptor_Is_Reported_With_The_Path_The_Summary_Prints()
    {
        using var directory = new CliHarness.TempDirectory();
        var spec = CliHarness.Spec("petstore.yaml");

        var (textOut, _, textExit) = await CliHarness.GenerateAsync(spec, "--out", directory.Path);
        Assert.Equal(0, textExit);

        var printed = textOut
            .Split('\n')
            .Single(line => line.StartsWith("proto:", StringComparison.Ordinal))["proto:".Length..]
            .Trim();

        var (stdout, stderr, exit) = await CliHarness.GenerateAsync(
            spec, "--out", directory.Path, "--format", "json");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, stderr);

        using var document = JsonDocument.Parse(stdout);
        var path = document.RootElement.GetProperty("descriptor").GetProperty("path").GetString();

        Assert.Equal(printed, path);
        Assert.True(File.Exists(path));
    }

    /// <summary>
    /// A descriptor that cannot be written fails the run before any document is produced: a document that names a
    /// path which does not exist would be worse than no document at all.
    /// </summary>
    [Fact]
    public async Task A_Descriptor_That_Cannot_Be_Written_Leaves_Standard_Output_Empty()
    {
        using var directory = new CliHarness.TempDirectory();
        var blocked = directory.PathOf("blocked");
        await File.WriteAllTextAsync(blocked, "a file where a directory is needed");

        var (stdout, stderr, exit) = await CliHarness.GenerateAsync(
            CliHarness.Spec("petstore.yaml"),
            "--out",
            Path.Combine(blocked, "x.proto"),
            "--format",
            "json");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, stdout);
        Assert.StartsWith("error:", stderr);
    }

    /// <summary>
    /// Two runs of one spec produce one byte sequence. That is what makes a diff between two contract revisions
    /// mean "the contract changed" instead of "the tool felt different today" (SC-002).
    /// </summary>
    [Fact]
    public async Task The_Same_Input_Produces_The_Same_Bytes()
    {
        var spec = CliHarness.Spec("petstore.yaml");

        var (first, _, firstExit) = await CliHarness.GenerateAsync(spec, "--format", "json");
        var (second, _, secondExit) = await CliHarness.GenerateAsync(spec, "--format", "json");

        Assert.Equal(0, firstExit);
        Assert.Equal(0, secondExit);
        Assert.Equal(first, second);
    }

    /// <summary>
    /// The checked-in report is regenerated by the Linux CI job and diffed against this file, so this comparison
    /// and that step together make "the same bytes on every operating system" a fact (SC-002).
    /// </summary>
    [Fact]
    public async Task The_Document_Matches_The_Checked_In_Report()
    {
        var spec = CliHarness.Spec("petstore.yaml");

        var (stdout, _, exit) = await CliHarness.GenerateAsync(spec, "--format", "json");

        Assert.Equal(0, exit);

        // The spec path appears JSON-escaped, so the separator is escaped before it can be replaced with the path
        // a developer in the repository root would type.
        var escapedSpec = spec.Replace("\\", "\\\\", StringComparison.Ordinal);

        Assert.Equal(
            CliHarness.ReadGolden("petstore.report.json"),
            stdout.Replace(escapedSpec, "tests/SharpPortico.Tests/openapi/petstore.yaml"));
    }

    /// <summary>
    /// Services, operations and diagnostics keep the order the mapping produced. A sorted document would make a
    /// diff between two revisions unreadable, because an unrelated insertion would move every following line.
    /// </summary>
    [Fact]
    public async Task Declaration_Order_Is_Preserved_And_Diagnostics_Keep_The_Report_Order()
    {
        using var directory = new CliHarness.TempDirectory();
        var spec = directory.PathOf("ordered.yaml");
        await File.WriteAllTextAsync(spec, OrderedSpec);

        var (stdout, _, exit) = await CliHarness.GenerateAsync(spec, "--format", "json");

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(stdout);
        var names = document.RootElement
            .GetProperty("services")[0]
            .GetProperty("rpcs")
            .EnumerateArray()
            .Select(rpc => rpc.GetProperty("name").GetString())
            .ToArray();

        // Not alphabetical: Zulu, Alpha, Mike is the order the specification declares.
        Assert.Equal(new[] { "Zulu", "Alpha", "Mike" }, names);

        // The diagnostics arrive in the order the generator reported them, which is the order the text mode prints.
        var fleet = CliHarness.RepositoryFile("samples", "NativeAotExample", "openapi", "fleet-3.1.yaml");
        var (fleetJson, fleetJsonError, fleetJsonExit) = await CliHarness.GenerateAsync(fleet, "--format", "json");
        var (_, fleetTextError, _) = await CliHarness.GenerateAsync(fleet);

        Assert.Equal(0, fleetJsonExit);
        Assert.Equal(string.Empty, fleetJsonError);

        using var fleetDocument = JsonDocument.Parse(fleetJson);
        var jsonIds = fleetDocument.RootElement
            .GetProperty("diagnostics")
            .EnumerateArray()
            .Select(diagnostic => diagnostic.GetProperty("id").GetString())
            .ToArray();

        var textIds = Regex.Matches(fleetTextError, @"SP\d{4}").Select(match => match.Value).ToArray();

        Assert.NotEmpty(textIds);
        Assert.Equal(textIds, jsonIds);
    }

    /// <summary>
    /// Warnings and a written descriptor at once: the document is still the only thing on standard output, and
    /// the diagnostic lines never leak to standard error in this mode (FR-003, invariant I7).
    /// </summary>
    [Fact]
    public async Task Warnings_And_A_Descriptor_Together_Leave_Standard_Error_Empty()
    {
        using var directory = new CliHarness.TempDirectory();
        var spec = CliHarness.RepositoryFile("samples", "NativeAotExample", "openapi", "fleet-3.1.yaml");

        var (stdout, stderr, exit) = await CliHarness.GenerateAsync(
            spec, "--out", directory.Path, "--format", "json");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, stderr);

        using var document = JsonDocument.Parse(stdout);
        Assert.NotEmpty(document.RootElement.GetProperty("diagnostics").EnumerateArray());

        var path = document.RootElement.GetProperty("descriptor").GetProperty("path").GetString();
        Assert.True(File.Exists(path));
    }

    /// <summary>Three operations in a deliberately non-alphabetical declaration order.</summary>
    private const string OrderedSpec = """
        openapi: 3.0.3
        info: { title: Ordered, version: 1.0.0 }
        paths:
          /zulu:
            get:
              operationId: zulu
              responses:
                '200': { description: ok }
          /alpha:
            get:
              operationId: alpha
              responses:
                '200': { description: ok }
          /mike:
            get:
              operationId: mike
              responses:
                '200': { description: ok }
        """;

    /// <summary>The refusal fixture from <see cref="ContractRefusalTests"/>: an operation derives a message name
    /// a component schema already declares.</summary>
    private const string CollidingSpec = """
        openapi: 3.0.3
        info: { title: Search, version: 1.0.0 }
        paths:
          /search:
            post:
              operationId: Search
              requestBody:
                required: true
                content:
                  application/json:
                    schema: { $ref: '#/components/schemas/SearchRequest' }
              responses:
                '200':
                  description: ok
        components:
          schemas:
            SearchRequest:
              type: object
              properties:
                text: { type: string }
        """;
}
