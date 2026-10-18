namespace SharpPortico.Generator;

/// <summary>The version the generated code names as the tool that produced it.</summary>
/// <remarks>
/// <para>
/// Every generated type carries <c>[GeneratedCode("SharpPortico", "...")]</c>, and that second argument used to be
/// the literal <c>1.0.0</c> in every release: it named a version that never produced the file, so a stack trace or
/// an audit of committed output could not say which generator wrote it. Reading the version from this assembly
/// makes the attribute a fact about the build that emitted the code.
/// </para>
/// <para>
/// It is <see cref="System.Reflection.AssemblyName.Version"/> formatted to three parts, not the informational
/// version: the SDK appends the commit hash to <c>AssemblyInformationalVersion</c> during a CI build, and generated
/// code has to stay byte-identical between builds of one version.
/// </para>
/// </remarks>
internal static class GeneratorVersion
{
    /// <summary>This generator's version, for example <c>1.5.0</c>.</summary>
    public static string Value { get; } =
        typeof(GeneratorVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>The <c>[GeneratedCode]</c> attribute line naming this generator and its version.</summary>
    public static string GeneratedCodeAttribute { get; } =
        "[global::System.CodeDom.Compiler.GeneratedCode(\"SharpPortico\", \"" + Value + "\")]";
}
