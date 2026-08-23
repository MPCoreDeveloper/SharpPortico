# SharpPortico.Cli

![SharpPortico](https://raw.githubusercontent.com/MPCoreDeveloper/SharpPortico/main/docs/assets/SharpPortico.jpg)

**Command-line companion for the SharpPortico OpenAPI → gRPC source generator.**

A lightweight preview/validation tool: parses your OpenAPI 3.0/3.1 spec (YAML or JSON) and reports what SharpPortico would generate — service name, RPC surface, protobuf message count. The actual code generation happens at compile time via the source generator.

```bash
dotnet tool install -g SharpPortico.Cli
sharpportico generate openapi/petstore.yaml --out out/
```

📖 **Full documentation & samples:** [SharpPortico on GitHub](https://github.com/MPCoreDeveloper/SharpPortico#readme)

## License

MIT — see [LICENSE](https://github.com/MPCoreDeveloper/SharpPortico/blob/main/LICENSE). Copyright (c) 2026 MPCoreDeveloper.
