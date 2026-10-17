using System;
using System.IO;
using System.Threading.Tasks;
using SharpPortico.Cli;

namespace SharpPortico.Tests.Infrastructure;

/// <summary>
/// Drives the CLI in-process with its streams captured. The console is not redirected and the working directory
/// is not changed, so these runs behave exactly like a user's invocation without disturbing any other test.
/// </summary>
internal static class CliHarness
{
    /// <summary>The repository root, found by walking up from the test output to the solution file.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>A specification that ships beside the tests (copied into the output directory).</summary>
    public static string Spec(string fileName) => Path.Combine(AppContext.BaseDirectory, "openapi", fileName);

    /// <summary>A path inside the repository; the samples are not copied into the test output.</summary>
    public static string RepositoryFile(params string[] parts) => Path.Combine(RepositoryRoot, Path.Combine(parts));

    /// <summary>An expected-output file that ships beside the tests, with platform line endings normalized.</summary>
    public static string ReadGolden(string fileName)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "openapi", fileName)).Replace("\r\n", "\n");

    /// <summary>Runs <c>sharpportico generate</c> with the arguments as given.</summary>
    public static Task<(string Stdout, string Stderr, int Exit)> GenerateAsync(params string[] args)
        => RunAsync(["generate", .. args]);

    /// <summary>Runs the command line verbatim and captures both streams and the exit code.</summary>
    public static async Task<(string Stdout, string Stderr, int Exit)> RunAsync(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await Program.RunAsync(args, stdout, stderr).ConfigureAwait(false);
        return (stdout.ToString(), stderr.ToString(), exit);
    }

    /// <summary>A test-owned directory that is removed afterwards.</summary>
    public sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "sharpportico-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string PathOf(string fileName) => System.IO.Path.Combine(Path, fileName);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // A locked file leaves a directory behind in the temp folder; that is not a test failure.
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SharpPortico.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
