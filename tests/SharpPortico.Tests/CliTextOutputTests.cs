using System;
using System.Threading.Tasks;
using SharpPortico.Tests.Infrastructure;
using Xunit;

namespace SharpPortico.Tests;

/// <summary>
/// The default output is a compatibility surface: the script, the reviewer's terminal and the CI log that read it
/// today must not notice this feature. The golden file is the output of the build before the feature, with the
/// one thing that cannot be portable - the path the test happened to pass - normalized to the path a developer
/// in the repository root would type.
/// </summary>
public class CliTextOutputTests
{
    [Fact]
    public async Task The_Default_Format_Still_Prints_The_Summary_It_Always_Did()
    {
        var spec = CliHarness.Spec("petstore.yaml");

        var (stdout, stderr, exit) = await CliHarness.GenerateAsync(spec);

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, stderr);
        Assert.Equal(
            CliHarness.ReadGolden("petstore.report.txt"),
            stdout.Replace(spec, "tests/SharpPortico.Tests/openapi/petstore.yaml").Replace("\r\n", "\n"));
    }

    /// <summary>
    /// The two audiences are separated by stream, not by filtering: the summary is on standard output and every
    /// diagnostic is on standard error, exactly as before the machine-readable format existed.
    /// </summary>
    [Fact]
    public async Task A_Warning_Still_Reaches_Standard_Error_And_Never_The_Summary()
    {
        var spec = CliHarness.RepositoryFile("samples", "NativeAotExample", "openapi", "fleet-3.1.yaml");

        var (stdout, stderr, exit) = await CliHarness.GenerateAsync(spec);

        Assert.Equal(0, exit);
        Assert.Contains($"spec:       {spec}", stdout, StringComparison.Ordinal);
        Assert.Contains("messages:", stdout, StringComparison.Ordinal);
        Assert.Contains("warning SP1002:", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("SP1002", stdout, StringComparison.Ordinal);
    }
}
