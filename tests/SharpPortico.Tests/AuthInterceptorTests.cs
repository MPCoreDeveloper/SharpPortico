using System;
using System.Collections.Generic;
using SharpPortico.Tests.Infrastructure;
using Xunit;

namespace SharpPortico.Tests;

/// <summary>
/// The auth interceptor a contract's security schemes produce: it has to exist, cover every shape of call, and
/// carry the credential under the key the scheme names.
/// </summary>
/// <remarks>
/// A metadata helper only works where a caller remembers to pass its result, so the option that promises an
/// interceptor has to deliver one that attaches the credential on its own.
/// </remarks>
public class AuthInterceptorTests
{
    /// <summary>A contract that declares no security scheme gets no interceptor.</summary>
    [Fact]
    public void A_Contract_Without_Security_Schemes_Emits_No_Interceptor()
    {
        var result = GeneratorTestDriver.Run(SecureSpec(), serviceName: "PlainService");

        Assert.DoesNotContain("AuthInterceptor", result.GeneratedSource, StringComparison.Ordinal);
    }

    /// <summary>A bearer scheme produces an interceptor that covers every shape of call.</summary>
    [Fact]
    public void A_Bearer_Scheme_Produces_An_Interceptor_For_Every_Call_Shape()
    {
        var source = GeneratorTestDriver
            .Run(SecureSpec("    bearerAuth:", "      type: http", "      scheme: bearer"), serviceName: "SecureService")
            .GeneratedSource;

        Assert.Contains(
            "public sealed class SecureServiceAuthInterceptor : global::Grpc.Core.Interceptors.Interceptor",
            source,
            StringComparison.Ordinal);

        // Every shape of call, or the one kind left out fails only for whoever happens to use it.
        Assert.Contains("AsyncUnaryCall<TRequest, TResponse>", source, StringComparison.Ordinal);
        Assert.Contains("AsyncServerStreamingCall<TRequest, TResponse>", source, StringComparison.Ordinal);
        Assert.Contains("AsyncClientStreamingCall<TRequest, TResponse>", source, StringComparison.Ordinal);
        Assert.Contains("AsyncDuplexStreamingCall<TRequest, TResponse>", source, StringComparison.Ordinal);
        Assert.Contains("BlockingUnaryCall<TRequest, TResponse>", source, StringComparison.Ordinal);

        Assert.Contains("headers.Add(\"authorization\", \"Bearer \" + credential);", source, StringComparison.Ordinal);
    }

    /// <summary>An apiKey scheme carries its credential under the scheme's own header name, lowercased.</summary>
    [Fact]
    public void An_ApiKey_Scheme_Carries_Its_Header_Lowercased()
    {
        var source = GeneratorTestDriver
            .Run(SecureSpec("    apiKeyAuth:", "      type: apiKey", "      in: header", "      name: X-API-Key"), serviceName: "SecureService")
            .GeneratedSource;

        Assert.Contains("headers.Add(\"x-api-key\", credential);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two schemes that travel in the same header produce one metadata entry, not two: a call that carried the
    /// credential twice would be rejected as a malformed header.
    /// </summary>
    [Fact]
    public void Schemes_Sharing_A_Header_Produce_One_Entry()
    {
        var source = GeneratorTestDriver
            .Run(
                SecureSpec(
                    "    bearerAuth:",
                    "      type: http",
                    "      scheme: bearer",
                    "    oauthAuth:",
                    "      type: oauth2",
                    "      flows:",
                    "        clientCredentials:",
                    "          tokenUrl: https://example.com/token",
                    "          scopes: {}"),
                serviceName: "SecureService")
            .GeneratedSource;

        Assert.Equal(1, CountOccurrences(source, "headers.Add(\"authorization\", \"Bearer \" + credential);"));
    }

    /// <summary>
    /// The interceptor is generated, not merely described: it has to compile against the real gRPC references,
    /// which is what makes the override signatures a fact rather than a claim.
    /// </summary>
    [Fact]
    public void The_Interceptor_Compiles_Against_The_Real_References()
    {
        var result = GeneratorTestDriver.Run(
            SecureSpec("    bearerAuth:", "      type: http", "      scheme: bearer"),
            serviceName: "SecureService");

        Assert.Empty(result.Diagnostics);
        Assert.Empty(GeneratedCodeCompiler.Errors(result.Sources.Values));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;

        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    /// <summary>
    /// A one-operation contract whose security schemes are the lines given, so a test names only the schemes it
    /// is about.
    /// </summary>
    private static string SecureSpec(params string[] securitySchemeLines)
    {
        var lines = new List<string>
        {
            "openapi: 3.0.3",
            "info: { title: Secure, version: 1.0.0 }",
            "paths:",
            "  /secure:",
            "    get:",
            "      operationId: getSecure",
            "      responses:",
            "        '200':",
            "          description: ok",
            "          content:",
            "            application/json:",
            "              schema: { type: string }",
        };

        if (securitySchemeLines.Length > 0)
        {
            lines.Add("components:");
            lines.Add("  securitySchemes:");
            lines.AddRange(securitySchemeLines);
        }

        return string.Join('\n', lines);
    }
}
