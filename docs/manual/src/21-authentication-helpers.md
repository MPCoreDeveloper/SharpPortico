# 21. Authentication Helpers

<p class="lead">The generator reads `components.securitySchemes` and emits two things for them: metadata
constructors a call site can use directly, and a client interceptor that attaches the credential to every call
without the call site doing anything. It does not acquire, refresh or validate a credential.</p>

## What is read

`MapAuthSchemes` runs when either `GenerateAuthMetadataHelpers` or `GenerateAuthInterceptors` is on (both default
to `true`) and every scheme in `components.securitySchemes` becomes an `AuthSchemeModel`:

| OpenAPI scheme | Kind | Metadata |
| --- | --- | --- |
| `type: http, scheme: bearer` | `Bearer` | `authorization: Bearer <token>` |
| `type: apiKey, in: header` | `ApiKey` (header) | `<header name>: <key>` |
| `type: apiKey, in: query` | `ApiKey` (query) | `<parameter name>: <key>` in the interceptor; `x-api-key` in the helper |
| `type: oauth2` | `OAuth2` | `authorization: Bearer <token>` |
| `type: openIdConnect` | `OAuth2` | `authorization: Bearer <token>` |
| `type: http, scheme: basic` or any other | `Unknown` | a scheme-named metadata entry |

Two things are **not** read: the operation-level `security` requirement, and the scopes in it. The generator
cannot tell which RPC needs which scheme, so it assumes the service uses all of them. A document with an
unauthenticated health operation and an authenticated list operation gets the same treatment for both.

## The metadata helpers

For each scheme the contract class's sibling `PetServiceAuth` gets one method:

```csharp
public static class PetServiceAuth
{
    public static Metadata CreateBearerTokenMetadata(string token)
        => new() { { "authorization", $"Bearer {token}" } };

    public static Metadata CreateApiKeyMetadata(string key)
        => new() { { "x-api-key", key } };
}
```

The metadata key is lower-cased, because gRPC requires metadata keys to be lowercase. The helper's name is
fixed per kind, except for `Unknown`, which uses the scheme name (`CreateOAuthMetadata` for a scheme named
`oauth`).

Use a helper when one call needs a credential and the rest do not:

```csharp
var headers = PetServiceAuth.CreateBearerTokenMetadata(token);
await client.ListPetsAsync(new ListPetsRequest(), headers);
```

The helper allocates a fresh `Metadata` each time; it does not cache. It also does not validate the token; an
empty string produces `authorization: Bearer `, which the server will reject.

## The interceptor

`PetServiceAuthInterceptor` is a `Grpc.Core.Interceptors.Interceptor` that wraps all five call shapes — unary,
server-streaming, client-streaming, duplex and blocking unary — and attaches the contract's metadata:

```csharp
var invoker = channel.Intercept(new PetServiceAuthInterceptor(() => tokenProvider.GetToken()));
var client = new PetService.PetServiceClient(invoker);
```

The constructor takes a `Func<string?>` rather than a string so a rotated token is picked up on the next call
without rebuilding the interceptor. Returning `null` or `""` sends the call unauthenticated, which is how an
anonymous operation is expressed.

The interceptor copies the caller's existing metadata first and then adds the scheme entries, so per-call
metadata survives. It does not overwrite an `authorization` header the caller already set; it appends a second
one, and servers typically reject duplicate authorization headers. If a call site supplies its own credential,
do not also install the interceptor for that call.

## The limits, stated plainly

- **One credential, every scheme.** The factory returns a single string, and the interceptor adds it under
  every scheme's key. A contract that combines a bearer token and an API key in one request cannot express two
  different values. The helpers can (one call each), the interceptor cannot.
- **Query API keys are misnamed in the helper.** For `in: query`, the interceptor uses the query parameter name
  as the metadata key, which is the only sensible choice; `CreateApiKeyMetadata` always emits `x-api-key`
  unless the scheme was declared as a header. Treat the helper as header-only.
- **Two schemes of one kind collide.** The helper name is fixed per kind, so two `apiKey` schemes (or two
  bearer schemes) both emit `CreateApiKeyMetadata` (or `CreateBearerTokenMetadata`) in the same class and the
  generated file does not compile.
- **Basic is not supported.** It becomes an `Unknown` scheme and a metadata entry keyed by the scheme name,
  which is not what an HTTP Basic server expects. Use a hand-written header.
- **Nothing is refreshed.** OAuth2 flows, token endpoints and scopes are documentation here. The application
  owns acquisition and renewal; the generator only formats what it is given.
- **Nothing is validated.** There is no server-side authentication filter or policy. The server reads
  `context.RequestHeaders` and decides (chapter 19).

For anything beyond a single bearer token or a single API key, the generated auth surface is a starting point,
not a solution. The honest pattern is to disable it (`GenerateAuthInterceptors=false`) and write one
interceptor that knows the contract's real credential rules.
