# SharpPortico.Cli

![SharpPortico](https://raw.githubusercontent.com/MPCoreDeveloper/SharpPortico/main/docs/assets/SharpPortico.jpg)

**Command-line companion for the SharpPortico OpenAPI → gRPC source generator.**

A preview/validation tool that runs the generator's own mapping pipeline over your OpenAPI 3.0/3.1 spec (YAML or JSON) and reports what SharpPortico would generate — service name, namespace, proto package, RPC surface, protobuf message and enum counts — and, with `--out`, writes the `.proto` descriptor the generator embeds as `{Service}Proto.Text`. The actual C# code generation happens at compile time via the source generator.

```bash
dotnet tool install -g SharpPortico.Cli --version 1.5.0

# What the mapping makes of the spec
sharpportico generate openapi/petstore.yaml

# And the contract a build of it would ship
sharpportico generate openapi/petstore.yaml --out contracts/

# The same answer as one document, for a CI job that must not parse text
sharpportico generate openapi/petstore.yaml --format json
```

| Option | Effect |
| --- | --- |
| `--out DIR\|FILE` | Write the descriptor — a directory gets `<spec>.proto` |
| `--service-name NAME` | Base name of the generated service (`Service` is appended when absent) |
| `--namespace NS` | Namespace of the generated C#, as the declaration's third argument |
| `--format text\|json` | `text` (default) prints the summary; `json` prints one machine-readable document on stdout |

`--format json` writes one object on stdout and nothing else: `formatVersion`, the spec path as given, the service identity, the RPC surface, message and enum counts, and every diagnostic with its id, severity and message. A refused contract still produces the document and exits 1, so a gate reads `diagnostics[].severity` (or the exit code) instead of parsing text. The bytes are deterministic — UTF-8, no byte-order mark, `\n` line endings, one trailing newline — and the same on every operating system. The full contract, member by member, is in the [developer guide](https://github.com/MPCoreDeveloper/SharpPortico/blob/main/docs/SharpPortico.md#5-cli).

The exit code is the first gate and the document is the second, so a pipeline names the defect instead of only reporting that a step failed:

```bash
sharpportico generate openapi/petstore.yaml --format json > report.json
jq -e '[.diagnostics[] | select(.severity == "error")] | length == 0' report.json
jq -e '.service.name == "PetstoreService"' report.json
```

The descriptor is not a second, hand-written rendering of the spec: it comes from the emitter the generator itself calls and is LF-normalized exactly as the `{Service}Proto.Text` const holds it, so what you review — or hand to `protoc`, to a client generator, or to a colleague — is the contract a build of the same spec produces. The diagnostics are the generator's own, with the ids and messages the compiler reports: `SP1002` says a 3.1 document was read through the 3.0 rewrite, and `SP1003` names each construct the mapping widened and what it became.

Every sample spec's descriptor is compiled by `protoc` in CI (`.github/scripts/check-protos.sh`), which is the check for what the C# compiler never reads — a name `protoc` rejects or an import left out of the descriptor.

📖 **Full documentation & samples:** [SharpPortico on GitHub](https://github.com/MPCoreDeveloper/SharpPortico#readme)

## License

MIT — see [LICENSE](https://github.com/MPCoreDeveloper/SharpPortico/blob/main/LICENSE). Copyright (c) 2026 MPCoreDeveloper.
