# Changelog

All notable changes to SharpPortico are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-09-09

### Fixed
- **Code quality** — resolved all 89 open SonarCloud issues (including the 3 issues flagged on the PR review):
  - **S3776 ×9** — cognitive-complexity refactors across `MessageEmitter`, `ServiceEmitter`, `ProtoEmitter`, `ProxyEmitter`, `OpenApiParser`, `SchemaMapper` and `SharpPorticoGenerator`. Generated code output stays **byte-for-byte identical** (SHA-256 verified on all samples, incl. proxy mode).
  - **S2365 ×2** — `SchemaMapper.AllMessages` / `AllEnums` collection-copying properties converted to `GetAllMessages()` / `GetAllEnums()` methods.
  - **S1854 ×9** — removed dead `++paramIndex` / `++respIndex` assignments in the OpenAPI parser.
  - **S8970 ×20 / S8969 ×2 / S1125 ×2** — removed redundant null-forgiving operators and unnecessary boolean literals.
  - **S1172 ×6 / S107 / S1481 ×2 / S1905 ×3** — removed unused method parameters (incl. the 3 flagged on the PR), slimmer helper signatures (≤ 7 params), removed unused locals and redundant casts.
  - **S2325 ×8 / S3267 ×7 / S1192 ×7 / S6610 ×4 / S1066 ×2 / S2681 / S2589 / S127 / S125** — static members, LINQ loop simplifications, named constants, single-char comparisons, merged nested `if`s, inline `if` braces, duplicated condition, loop-counter hygiene and dead comments.

### Changed
- CLI `--out` argument parsing no longer mutates its loop counter.
- Named constants introduced for repeated literals (streaming keyword, `Google.Protobuf.ByteString`, `GeneratedCode` attribute, proxy `request.` prefix, `X-Api-Key` header, schema type names).

## [0.1.0] - 2026-08-23

### Added
- Initial release: OpenAPI 3.0/3.1 → gRPC + protobuf + C# 14 incremental source generator (net10, C# 14, NativeAOT-safe).
- Hand-written `IMessage<T>` protobuf messages (reflection-free), gRPC contract, server base, typed C# 14 client and DI extensions.
- gRPC → REST **proxy mode** — response cache with per-call bypass, ULID client-key validation, `IKeyProvider` (config / Key Vault / delegate / composite) and audit logging.
- `dotnet sharpportico` CLI preview/validation tool.
- Developer guide (`docs/SharpPortico.md`), samples (gRPC server, Minimal API, legacy proxy) and xunit test suite (snapshot, mapping, performance).
