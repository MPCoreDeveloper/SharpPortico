// Licensed under the MIT License (https://opensource.org/licenses/MIT)
// Polyfill so C# records/init accessors compile on netstandard2.0.
namespace System.Runtime.CompilerServices
{
    // Polyfill required by the C# compiler for init-only setters (records) on
    // netstandard2.0. The BCL ships this type only on modern TFMs, so the
    // empty class is intentional.
#pragma warning disable S2094 // an empty marker type is the polyfill, not dead code
    internal static class IsExternalInit
    {
    }
#pragma warning restore S2094
}