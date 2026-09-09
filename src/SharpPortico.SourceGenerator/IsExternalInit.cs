// Licensed under the MIT License (https://opensource.org/licenses/MIT)
// Polyfill so C# records/init accessors compile on netstandard2.0.
namespace System.Runtime.CompilerServices
{
    // Polyfill required by the C# compiler for init-only setters (records) on
    // netstandard2.0. The BCL ships this type only on modern TFMs, so the
    // empty class is intentional.
    internal static class IsExternalInit // NOSONAR S2094
    {
    }
}