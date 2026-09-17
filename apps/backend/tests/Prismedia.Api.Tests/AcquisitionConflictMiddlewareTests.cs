using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Prismedia.Api.Diagnostics;
using Prismedia.Application.Acquisition;
using Prismedia.Contracts.System;

namespace Prismedia.Api.Tests;

public sealed class AcquisitionConflictMiddlewareTests {
    [Fact]
    public async Task RetainedImportConflictExplainsWhyCancellationCannotProceed() {
        using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        var middleware = new AcquisitionConflictMiddleware(_ => throw new AcquisitionConfigurationException(
            ApiProblemCodes.AcquisitionInvalid, "This import needs recovery before cancellation."));
        await middleware.InvokeAsync(context);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var problem = await JsonSerializer.DeserializeAsync<ApiProblem>(context.Response.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(ApiProblemCodes.AcquisitionInvalid, problem!.Code);
        Assert.Equal("This import needs recovery before cancellation.", problem.Message);
    }

    [Fact]
    public async Task UnrelatedFailuresKeepTheirExistingHandling() {
        var error = new InvalidOperationException("Private implementation detail");
        var middleware = new AcquisitionConflictMiddleware(_ => throw error);
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(new DefaultHttpContext())));
    }
}
