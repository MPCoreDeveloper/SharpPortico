using System;
using System.IO;
using System.Reflection;

namespace SharpPortico.Generator;

internal static class AnalyzerDependencyResolver
{
    private static readonly object Sync = new();
    private static bool _hooked;

    public static void EnsureHooked()
    {
        if (_hooked) return;
        lock (Sync)
        {
            if (_hooked) return;
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            _hooked = true;
        }
    }

    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        if (name is null) return null;
        if (!name.StartsWith("Microsoft.OpenApi", StringComparison.Ordinal) && name != "SharpYaml") return null;
        var dir = Path.GetDirectoryName(typeof(AnalyzerDependencyResolver).Assembly.Location);
        if (string.IsNullOrEmpty(dir)) return null;
        var candidate = Path.Combine(dir, name + ".dll");
        return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
    }
}
