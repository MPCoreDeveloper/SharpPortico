using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace SharpPortico.Proxy;

/// <summary>
/// <see cref="IRestClient"/> backed by <see cref="HttpClient"/>. Substitutes path parameters,
/// appends query parameters, sets headers and dispatches the request. Fully sync and AOT-safe.
/// </summary>
public sealed class HttpRestClient : IRestClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public HttpRestClient(HttpClient http, string baseUrl)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
    }

    public RestResponse Send(RestRequest request)
    {
        var uri = BuildUri(request);
        using var msg = new HttpRequestMessage(new HttpMethod(request.Method), uri);

        if (request.Headers is not null)
        {
            foreach (var (key, value) in request.Headers)
            {
                msg.Headers.TryAddWithoutValidation(key, value);
            }
        }

        if (request.Body is { Length: > 0 } body)
        {
            msg.Content = new ByteArrayContent(body);
            if (!string.IsNullOrEmpty(request.ContentType))
                msg.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(request.ContentType);
        }

        using var response = _http.Send(msg, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
        var payload = ReadBody(response);
        var status = (int)response.StatusCode;

        return new RestResponse(status, payload, response.Content?.Headers?.ContentType?.MediaType ?? "application/json");
    }

    private Uri BuildUri(RestRequest request)
    {
        var path = request.PathTemplate ?? string.Empty;
        if (request.PathParameters is not null)
        {
            foreach (var (key, value) in request.PathParameters)
            {
                var token = "{" + key + "}";
                var escaped = Uri.EscapeDataString(value ?? string.Empty);
                while (true)
                {
                    var idx = path.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0) break;
                    path = path.Substring(0, idx) + escaped + path.Substring(idx + token.Length);
                }
            }
        }

        var sb = new StringBuilder(_baseUrl).Append(path);
        if (request.QueryParameters is { Count: > 0 })
        {
            var first = true;
            foreach (var (key, value) in request.QueryParameters)
            {
                sb.Append(first ? '?' : '&');
                first = false;
                sb.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value ?? string.Empty));
            }
        }
        return new Uri(sb.ToString(), UriKind.Absolute);
    }

    private static byte[] ReadBody(HttpResponseMessage response)
    {
        if (response.Content is null) return Array.Empty<byte>();
        using var stream = response.Content.ReadAsStream();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}