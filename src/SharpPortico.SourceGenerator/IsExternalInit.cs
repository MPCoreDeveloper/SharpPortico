// Licensed under the MIT License (https://opensource.org/licenses/MIT)
// Polyfill so C# records/init accessors compile on netstandard2.0.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}