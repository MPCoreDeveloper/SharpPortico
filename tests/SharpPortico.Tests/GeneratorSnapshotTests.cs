using System.IO;
using SharpPortico.Tests.Infrastructure;
using Xunit;

namespace SharpPortico.Tests;

public class GeneratorSnapshotTests
{
    private static readonly string SpecPath = Path.Combine(
        Path.GetDirectoryName(typeof(GeneratorSnapshotTests).Assembly.Location)!,
        "openapi", "petstore.yaml");

    [Fact]
    public void Petstore_Generates_Service_With_Clean_Diagnostics()
    {
        var spec = File.ReadAllText(SpecPath);
        var result = GeneratorTestDriver.Run(spec, serviceName: "PetService", namespaceName: "SharpPortico.Samples.Generated");

        // No mapping diagnostics from the generated code.
        Assert.Empty(result.Diagnostics);
        // Exactly two source files emitted: C# (.g.cs) + proto descriptor (.proto.cs).
        Assert.Equal(2, result.Sources.Count);
        // The generated code includes the gRPC service contract.
        Assert.Contains("public static partial class PetService", result.GeneratedSource);
        Assert.Contains("public sealed partial class PetServiceClient", result.GeneratedSource);
        Assert.Contains("public abstract partial class PetServiceBase", result.GeneratedSource);
        // Messages + enum.
        Assert.Contains("public sealed partial class Pet :", result.GeneratedSource);
        Assert.Contains("public enum StatusEnum", result.GeneratedSource);
    }

    [Fact]
    public void Petstore_Generated_Code_Compiles_With_Real_References()
    {
        var spec = File.ReadAllText(SpecPath);
        var result = GeneratorTestDriver.Run(spec, serviceName: "PetService", namespaceName: "SharpPortico.Samples.Generated");

        Assert.Empty(result.Diagnostics);
    }
}
