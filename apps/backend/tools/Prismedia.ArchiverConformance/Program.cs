using System.Net.Http.Headers;
using System.Text.Json;
using Prismedia.ArchiverConformance;

try {
    var options = ConformanceOptions.Parse(args);
    using var handler = new HttpClientHandler { AllowAutoRedirect = false };
    using var http = new HttpClient(handler) { BaseAddress = options.Endpoint, Timeout = TimeSpan.FromSeconds(30) };
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
        Environment.GetEnvironmentVariable("ARCHIVER_TOKEN") ?? throw new ArgumentException("Set ARCHIVER_TOKEN in the environment."));
    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    var report = await new ConformanceRunner(http).RunAsync(options, deadline.Token);
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    return 0;
} catch (ArgumentException error) {
    Console.Error.WriteLine(error.Message);
    Console.Error.WriteLine("Usage: --endpoint <base URL> [--execute --input <source URL> --kind <kind> --format <format> --output-directory <directory> | --resume <plan.json>]");
    return 2;
} catch (ConformanceFailure error) {
    Console.Error.WriteLine(error.Message);
    return 1;
} catch (OperationCanceledException) {
    Console.Error.WriteLine("Validation timed out. Keep the plan.json file to resume the same operation after checking the service.");
    return 1;
} catch (Exception error) when (error is HttpRequestException or IOException or JsonException) {
    // Remote bodies, URLs and exception messages can contain credentials or private source details.
    Console.Error.WriteLine($"Validation could not finish ({error.GetType().Name}). Keep plan.json to resume the same operation.");
    return 1;
}
