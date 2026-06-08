using System.Text.Json.Serialization.Metadata;
using Prismedia.Api;
using Prismedia.Api.Codegen;
using Prismedia.Api.Endpoints;
using Prismedia.Api.Security;
using Prismedia.Api.Serialization;
using Prismedia.Application;
using Prismedia.Contracts.Entities;
using Prismedia.Contracts.Media;
using Prismedia.Contracts.System;
using Prismedia.Infrastructure;
using Prismedia.Infrastructure.Persistence;
using Prismedia.Infrastructure.Videos;
using Microsoft.Extensions.FileProviders;

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

app.UsePrismediaUiApiKeyCookie();

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
    app.UseStaticFiles(new StaticFileOptions { FileProvider = staticFileProvider });
} else {
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

Directory.CreateDirectory(cacheDir);
app.UseStaticFiles(new StaticFileOptions {
    FileProvider = new PhysicalFileProvider(cacheDir),
    RequestPath = "/assets",
    ServeUnknownFileTypes = false,
});

app.UsePrismediaApiAuthentication();

app.MapPrismediaEndpoints();

var staticIndexPath = resolvedStaticWebRoot is not null
    ? Path.Combine(resolvedStaticWebRoot, "index.html")
    : Path.Combine(app.Environment.WebRootPath ?? string.Empty, "index.html");

if (File.Exists(staticIndexPath)) {
    app.MapFallback(async () =>
        Results.Content(await File.ReadAllTextAsync(staticIndexPath), MediaContentTypes.Html));
} else {
    app.MapFallback(() => Results.NotFound(new ApiProblem(
        ApiProblemCodes.NotFound,
        "The requested Prismedia route was not found.")));
}

await PrismediaMigrationRunner.ApplyPrismediaMigrationsAsync(app.Services, app.Configuration);

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

public partial class Program;
