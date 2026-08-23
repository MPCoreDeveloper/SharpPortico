using System;
using System.Collections.Generic;

namespace SharpPortico.Proxy;

/// <summary>
/// Describes a REST request the generated proxy dispatches to the legacy OpenAPI service.
/// AOT-safe value type; no reflection.
/// </summary>
public readonly record struct RestRequest(
    string Method,
    string PathTemplate,
    IReadOnlyDictionary<string, string> PathParameters,
    IReadOnlyDictionary<string, string> QueryParameters,
    IReadOnlyDictionary<string, string> Headers,
    byte[]? Body,
    string ContentType = "application/json");

/// <summary>Response of a REST call; Body is the raw payload (typically JSON).</summary>
public readonly record struct RestResponse(
    int StatusCode,
    byte[] Body,
    string ContentType = "application/json");

/// <summary>
/// Minimal HTTP transport abstraction. Implementations wrap <see cref="System.Net.Http.HttpClient"/>
/// and return fully materialized responses so the pipeline stays sync-friendly and cacheable.
/// </summary>
public interface IRestClient
{
    RestResponse Send(RestRequest request);
}