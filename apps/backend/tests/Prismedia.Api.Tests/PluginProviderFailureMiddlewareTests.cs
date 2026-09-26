using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Api.Diagnostics;
using Prismedia.Application.Plugins;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Tests;

public sealed class PluginProviderFailureMiddlewareTests {
    [Fact]
    public async Task BusyPluginHasAStableConflictProblem() {
        using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        var middleware = new PluginProviderFailureMiddleware(_ => throw new PluginInUseException("Finish the accepted transfer first."));
        await middleware.InvokeAsync(context);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var result = await JsonSerializer.DeserializeAsync<ApiProblem>(context.Response.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(ApiProblemCodes.PluginInUse, result!.Code);
        Assert.Equal("Finish the accepted transfer first.", result.Message);
    }

    [Fact]
    public async Task ProviderFailureHasAStableUpstreamProblemInsteadOfNotFound() {
        using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        var middleware = new PluginProviderFailureMiddleware(_ => throw new PluginProviderUnavailableException("Provider is rate limited."));
        await middleware.InvokeAsync(context);
        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var result = await JsonSerializer.DeserializeAsync<ApiProblem>(context.Response.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(ApiProblemCodes.PluginProviderUnavailable, result!.Code);
        Assert.Equal("Provider is rate limited.", result.Message);
    }

    [Fact]
    public async Task UnrelatedServerErrorsAreNotExposedAsProviderMessages() {
        var expected = new InvalidOperationException("Internal configuration detail");
        var middleware = new PluginProviderFailureMiddleware(_ => throw expected);
        Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(new DefaultHttpContext())));
    }
}
