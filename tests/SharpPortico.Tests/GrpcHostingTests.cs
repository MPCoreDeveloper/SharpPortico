using System;
using System.Linq;
using SharpPortico.Tests.Infrastructure;
using Xunit;

namespace SharpPortico.Tests;

/// <summary>
/// The shapes a host needs in order to serve the generated contract, and the specifications that are
/// refused rather than emitted as code that cannot compile.
/// </summary>
/// <remarks>
/// Two hosts exist and both have to work: the Grpc.Core server binds through
/// <c>BindService(base)</c>, and ASP.NET Core's <c>MapGrpcService&lt;T&gt;</c> finds the binder through
/// <c>[BindServiceMethod]</c>, asks for a <c>BindService(ServiceBinderBase, Base)</c> overload, and
/// resolves handlers by the RPC's own name.
/// </remarks>
public class GrpcHostingTests
{
    private static string Spec(params string[] lines) => string.Join('\n', lines);

    private static string PetSpec() => Spec(
        "openapi: 3.0.3",
        "info: { title: Pets, version: 1.0.0 }",
        "paths:",
        "  /pets:",
        "    get:",
        "      operationId: ListPets",
        "      responses:",
        "        '200':",
        "          description: ok",
        "          content:",
        "            application/json:",
        "              schema:",
        "                type: array",
        "                items: { $ref: '#/components/schemas/Pet' }",
        "components:",
        "  schemas:",
        "    Pet:",
        "      type: object",
        "      properties:",
        "        id: { type: integer, format: int64 }");

    [Fact]
    public void The_Base_Class_Points_At_The_Binder_Method()
    {
        var result = GeneratorTestDriver.Run(PetSpec(), serviceName: "PetService");

        Assert.Contains(
            "[global::Grpc.Core.BindServiceMethod(typeof(PetService), \"BindService\")]",
            result.GeneratedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_Contract_Carries_Both_Binder_Shapes()
    {
        var result = GeneratorTestDriver.Run(PetSpec(), serviceName: "PetService");

        // The ASP.NET Core shape, which is new...
        Assert.Contains(
            "public static void BindService(global::Grpc.Core.ServiceBinderBase serviceBinder, PetServiceBase serviceImpl)",
            result.GeneratedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "serviceBinder.AddMethod(Method_ListPets, serviceImpl == null",
            result.GeneratedSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "new global::Grpc.Core.UnaryServerMethod<ListPetsRequest, ListPetsResponse>(serviceImpl.ListPetsAsync)",
            result.GeneratedSource,
            StringComparison.Ordinal);

        // ...and the Grpc.Core shape, which has to stay as it was.
        Assert.Contains(
            "public static global::Grpc.Core.ServerServiceDefinition BindService(PetServiceBase serviceBase)",
            result.GeneratedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_Base_Finds_Unary_Handlers_By_The_Rpc_Name()
    {
        var result = GeneratorTestDriver.Run(PetSpec(), serviceName: "PetService");

        // grpc-dotnet resolves a handler by the RPC's own name and (request, context) signature...
        Assert.Contains(
            "public virtual global::System.Threading.Tasks.Task<ListPetsResponse> ListPets(ListPetsRequest request, global::Grpc.Core.ServerCallContext context)",
            result.GeneratedSource,
            StringComparison.Ordinal);

        // ...and it forwards to Async, which stays the method an implementation overrides.
        Assert.Contains(
            "public virtual global::System.Threading.Tasks.Task<ListPetsResponse> ListPetsAsync(ListPetsRequest request, global::Grpc.Core.ServerCallContext context)",
            result.GeneratedSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_OpenApi_31_Document_Is_Parsed_With_A_Warning()
    {
        var spec = PetSpec().Replace("openapi: 3.0.3", "openapi: 3.1.0", StringComparison.Ordinal);

        var result = GeneratorTestDriver.Run(spec, serviceName: "PetService");

        Assert.Contains(result.Diagnostics, d => d.StartsWith("SP1002", StringComparison.Ordinal));
        Assert.Contains("ListPetsAsync", result.GeneratedSource, StringComparison.Ordinal);
    }
}
