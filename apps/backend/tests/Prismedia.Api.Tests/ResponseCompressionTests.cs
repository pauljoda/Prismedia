using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Prismedia.Api.Tests;

public sealed class ResponseCompressionTests {
    [Fact]
    public async Task JsonResponsesHonorGzipAcceptEncoding() {
        using var factory = new WebApplicationFactory<Program>().WithTestAuth();
        using var client = factory.CreateAuthenticatedClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/not-a-real-route");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("gzip", response.Content.Headers.ContentEncoding);
        Assert.Contains("Accept-Encoding", response.Headers.Vary);

        await using var compressed = await response.Content.ReadAsStreamAsync();
        await using var decompressed = new GZipStream(compressed, CompressionMode.Decompress);
        using var reader = new StreamReader(decompressed);
        var body = await reader.ReadToEndAsync();
        Assert.Contains("not_found", body);
    }
}
