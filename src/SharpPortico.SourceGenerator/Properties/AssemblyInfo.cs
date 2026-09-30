using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("SharpPortico.Tests")]

// The CLI runs the generator's own mapping pipeline and descriptor emitter rather than a second
// implementation of them, so the .proto it writes is the descriptor the generator embeds - and the
// diagnostics it prints are the ones a compiler would report on the same spec.
[assembly: InternalsVisibleTo("SharpPortico.Cli")]
