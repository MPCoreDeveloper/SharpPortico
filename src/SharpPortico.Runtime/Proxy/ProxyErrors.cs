using System;

namespace SharpPortico.Proxy;

/// <summary>
/// Exception thrown by the generated proxy when the legacy service returns an error status.
/// The gRPC layer maps this to an <c>RpcException</c> with the matching status code.
/// </summary>
public sealed class ProxyRestException : Exception
{
    public ProxyRestException(int statusCode, string? responseBody, Exception? inner = null)
        : base($"Legacy REST service returned HTTP {statusCode}", inner)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public int StatusCode { get; }

    public string? ResponseBody { get; }
}

/// <summary>Maps HTTP status codes to gRPC status codes used by the proxy.</summary>
public static class GrpcStatusMapper
{
    public static int ToGrpcStatusCode(int httpStatus) => httpStatus switch
    {
        400 or 422 => 3,          // InvalidArgument
        401 or 403 => 16,         // Unauthenticated
        404 => 5,                 // NotFound
        408 or 504 => 4,          // DeadlineExceeded
        409 => 6,                 // AlreadyExists
        429 => 8,                 // ResourceExhausted
        500 or 502 => 13,         // Internal
        503 => 14,                // Unavailable
        501 or 505 => 12,         // Unimplemented
        _ => 2                    // Unknown
    };
}