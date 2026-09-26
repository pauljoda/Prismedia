using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Npgsql;
using Prismedia.Api.Diagnostics;
using Prismedia.Contracts.System;
using Prismedia.Infrastructure.Persistence;

namespace Prismedia.Api.Tests;

public sealed class FulfillmentOwnershipMiddlewareTests {
    [Fact]
    public async Task NativeOwnershipConstraintReturnsStableConflictProblem() {
        var databaseError = new PostgresException(
            "A connected application owns this work and scope.",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.CheckViolation,
            constraintName: FulfillmentOwnershipViolation.ConstraintName);
        var middleware = new FulfillmentOwnershipMiddleware(_ => throw databaseError);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var problem = await JsonSerializer.DeserializeAsync<ApiProblem>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.Equal(ApiProblemCodes.FulfillmentOwnershipConflict, problem?.Code);
    }
}
