using System.Text.Json.Serialization.Metadata;
using Prismedia.Api;
using Prismedia.Api.Codegen;
using Prismedia.Api.Endpoints;
using Prismedia.Api.Security;
using Prismedia.Api.Serialization;
using Prismedia.Application;
using Prismedia.Application.Updates;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.System;
using Prismedia.Infrastructure;
using Prismedia.Infrastructure.Database;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Security;
using Prismedia.Infrastructure.Updates;
using Prismedia.Infrastructure.Serialization;
using Prismedia.Infrastructure.Videos;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);

var configuredStaticWebRoot = builder.Configuration["PRISMEDIA_STATIC_WEB_ROOT"] ??
    builder.Configuration["Prismedia:StaticWebRoot"];
var resolvedStaticWebRoot = ResolveStaticWebRoot(
    configuredStaticWebRoot,
    builder.Environment.ContentRootPath);
var staticFileProvider = resolvedStaticWebRoot is not null
    ? new PhysicalFileProvider(resolvedStaticWebRoot)
    : null;

var dataDir = ResolvePath(builder.Configuration["PRISMEDIA_DATA_DIR"] ??
    builder.Configuration["Prismedia:DataDir"] ??
    "/data", builder.Environment.ContentRootPath);
var cacheDir = ResolvePath(builder.Configuration["PRISMEDIA_CACHE_DIR"] ??
    builder.Configuration["Prismedia:CacheDir"] ??
    Path.Combine(dataDir, "cache"), builder.Environment.ContentRootPath);

builder.Services.ConfigureHttpJsonOptions(options => {
    options.SerializerOptions.Converters.Add(new CodecJsonConverterFactory());
    options.SerializerOptions.TypeInfoResolver = (options.SerializerOptions.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver())
        .WithAddedModifier(CapabilityPolymorphism.ConfigureEntityCapabilityPolymorphism);
});
builder.Services.AddOpenApi(options => {
    // Nested types like CapabilitySource.Item share the simple name "Item"
    // with siblings in other capabilities. Use the declaring chain so each
    // nested record gets a unique OpenAPI schema id.
    options.CreateSchemaReferenceId = type => {
        if (type.Type is { IsNested: true } nested) {
            var declaring = nested.DeclaringType;
            var prefix = string.Empty;
            while (declaring is not null) {
                prefix = declaring.Name + prefix;
                declaring = declaring.DeclaringType;
            }
            return prefix + nested.Name;
        }
        return Microsoft.AspNetCore.OpenApi.OpenApiOptions.CreateDefaultSchemaReferenceId(type);
    };
    // Codec enums serialize as their string code, so without this they appear as a bare
    // `string` and the generated client loses enum typing. Emit them as typed string enums.
    options.AddSchemaTransformer<CodecEnumSchemaTransformer>();
});
builder.Services.AddHealthChecks();
builder.Services.AddHttpClient(GhcrUpdateCheckService.HttpClientName, client => {
    client.Timeout = TimeSpan.FromSeconds(5);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Prismedia-UpdateCheck/1.0");
});
builder.Services.AddSingleton(new UpdateCheckOptions(builder.Environment.ContentRootPath));
builder.Services.AddSingleton<IUpdateCheckService, GhcrUpdateCheckService>();
builder.Services.AddCors(options => {
    options.AddPolicy("PrismediaDevCors", policy => {
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .SetIsOriginAllowed(origin =>
                Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase)));
    });
});
builder.Services.AddPrismediaApplication();
builder.Services.AddPrismediaInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);
// Reap abandoned transcode/remux ffmpeg jobs (closed tabs, crashed clients, runaway encodes) so they
// cannot accumulate and saturate the host. Runs in the API process, where playback encodes are spawned.
builder.Services.AddHostedService<TranscodeReaperService>();

var app = builder.Build();

if (app.Environment.IsDevelopment()) {
    app.MapOpenApi();
    app.MapPrismediaCodegen();
    app.UseCors("PrismediaDevCors");
    if (staticFileProvider is null) {
        app.UseSpaDevServer("http://localhost:5173");
    }
}

if (staticFileProvider is not null) {
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = staticFileProvider });
    app.UseStaticFiles(new StaticFileOptions {
        FileProvider = staticFileProvider,
        OnPrepareResponse = PrepareWebStaticResponse
    });
} else {
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions {
        OnPrepareResponse = PrepareWebStaticResponse
    });
}

Directory.CreateDirectory(cacheDir);
app.UseStaticFiles(new StaticFileOptions {
    FileProvider = new PhysicalFileProvider(cacheDir),
    RequestPath = "/assets",
    ServeUnknownFileTypes = false,
    OnPrepareResponse = context => {
        // Downloaded artwork filenames embed a content hash (plugins) or upload timestamp
        // (custom), so a changed image always gets a new URL and the old one may cache forever.
        // Generated assets (grid/video thumbs, previews) are overwritten in place at stable
        // URLs, so they get a day of freshness and then revalidate via ETag/Last-Modified.
        var path = context.Context.Request.Path.Value ?? string.Empty;
        var immutable = path.Contains("/plugins/artwork/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/custom/artwork/", StringComparison.OrdinalIgnoreCase);
        context.Context.Response.Headers.CacheControl = immutable
            ? "public, max-age=31536000, immutable"
            : "public, max-age=86400";
    },
});

app.UsePrismediaApiAuthentication();

var staticIndexPath = resolvedStaticWebRoot is not null
    ? Path.Combine(resolvedStaticWebRoot, "index.html")
    : Path.Combine(app.Environment.WebRootPath ?? string.Empty, "index.html");

if (File.Exists(staticIndexPath)) {
    app.UseStaticSpaFallback(staticIndexPath);
}

app.MapPrismediaEndpoints();

if (File.Exists(staticIndexPath)) {
    app.MapFallback(async (HttpContext context) => {
        SetSpaShellCacheHeaders(context.Response);
        context.Response.ContentType = MediaContentTypes.Html;
        await context.Response.SendFileAsync(staticIndexPath, context.RequestAborted);
    });
} else {
    app.MapFallback(() => Results.NotFound(new ApiProblem(
        ApiProblemCodes.NotFound,
        "The requested Prismedia route was not found.")));
}

await DatabaseRestoreRunner.ApplyPendingRestoreAsync(app.Services, app.Configuration);
await PrismediaMigrationRunner.ApplyPrismediaMigrationsAsync(app.Services, app.Configuration);
await UserBootstrapRunner.RunUserBootstrapAsync(app.Services, app.Configuration);

app.Run();

static string? ResolveStaticWebRoot(string? configuredPath, string contentRootPath) {
    if (string.IsNullOrWhiteSpace(configuredPath)) {
        return null;
    }

    var candidates = Path.IsPathRooted(configuredPath)
        ? [configuredPath]
        : new[]
        {
            Path.GetFullPath(Path.Combine(contentRootPath, configuredPath)),
            Path.GetFullPath(configuredPath)
        };

    return candidates.FirstOrDefault(Directory.Exists);
}

static string ResolvePath(string path, string basePath) =>
    Path.GetFullPath(Path.IsPathRooted(path)
        ? path
        : Path.Combine(basePath, path));

static void PrepareWebStaticResponse(StaticFileResponseContext context) {
    var requestPath = context.Context.Request.Path.Value ?? string.Empty;
    if (string.Equals(context.File.Name, "index.html", StringComparison.OrdinalIgnoreCase) ||
        requestPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) {
        SetSpaShellCacheHeaders(context.Context.Response);
        return;
    }

    if (requestPath.StartsWith("/_app/immutable/", StringComparison.OrdinalIgnoreCase)) {
        context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    }
}

static void SetSpaShellCacheHeaders(HttpResponse response) {
    // The SPA shell points at content-hashed JS/CSS chunks. It must be revalidated on
    // every navigation so mobile Safari/PWA sessions don't keep an older app build
    // after an API/DTO deploy and render new entity payloads as placeholder-only cards.
    response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    response.Headers.Pragma = "no-cache";
    response.Headers.Expires = "0";
}

public partial class Program;
