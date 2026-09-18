using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Prismedia.Archiver;

namespace Prismedia.ArchiverConformance;

/// <summary>A failed public contract assertion; messages deliberately exclude remote content and credentials.</summary>
public sealed class ConformanceFailure(string message) : Exception(message);
/// <summary>Credential-free durable submission and receipt identities, reusable after client or server restart.</summary>
public sealed record ConformancePlan(string Endpoint, string InstanceId, SubmitJob Request, Guid ReceiptId);
/// <summary>Checks completed against one remote installation; a probe alone does not certify execution behavior.</summary>
public sealed record ConformanceReport(string InstanceId, string ApiVersion, IReadOnlyList<string> Checks, string? PlanPath);

/// <summary>Black-box executor acceptance checks using only the independent public Archiver HTTP contract.</summary>
public sealed class ConformanceRunner(HttpClient http) {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const long MaximumBytes = 32 * 1024 * 1024;
    private const int MaximumArtifacts = 100;

    /// <summary>Probes advertised support, or executes/resumes one bounded fixture and retains verified bytes before acknowledging them.</summary>
    public async Task<ConformanceReport> RunAsync(ConformanceOptions options, CancellationToken token) {
        var checks = new List<string>();
        var info = await SendAsync<SystemInfo>(HttpMethod.Get, "api/v1/system", null, token);
        Require(!string.IsNullOrWhiteSpace(info.InstanceId) && info.ApiVersion == ArchiverWire.ApiVersion, "Stable identity and API version are required.");
        Require(info.MaximumBytes > 0 && info.MaximumItems > 0, "Advertised limits must be positive.");
        checks.Add("Stable instance identity and supported API version");
        if (options.Input is null && options.ResumePath is null) return new(info.InstanceId, info.ApiVersion, checks, null);
        foreach (var capability in new[] { ArchiverWire.Inspect, ArchiverWire.Submit, ArchiverWire.Artifacts,
                     ArchiverWire.Retention, ArchiverWire.Receipts, ArchiverWire.CancelOperation })
            Require(info.Capabilities.Contains(capability), $"Required capability is absent: {capability}.");

        var (plan, planPath) = await PreparePlanAsync(options, info, token);
        var request = plan.Request;
        var job = await SendAsync<JobSnapshot>(HttpMethod.Post, "api/v1/jobs", request, token, request.ClientOperationId);
        Match(job, plan);
        var replay = await SendAsync<JobSnapshot>(HttpMethod.Post, "api/v1/jobs", request, token, request.ClientOperationId);
        Match(replay, plan, job.JobId);
        var recovered = await SendAsync<JobSnapshot>(HttpMethod.Get, $"api/v1/operations/{request.ClientOperationId}", null, token);
        Match(recovered, plan, job.JobId);
        await ExpectProblemAsync($"api/v1/jobs", request with { Limits = request.Limits with { MaxBytes = request.Limits.MaxBytes - 1 } },
            request.ClientOperationId, HttpStatusCode.Conflict, ArchiverWire.Conflict, token);
        checks.Add("Identical submission replay, durable operation lookup, and conflicting replay rejection");

        var cancelledId = Guid.NewGuid();
        var cancelled = await SendAsync<OperationCancellation>(HttpMethod.Post, $"api/v1/operations/{cancelledId}/cancel", null, token);
        Require(cancelled.InstanceId == plan.InstanceId && cancelled.ClientOperationId == cancelledId
            && cancelled.PreventedAcceptance && cancelled.Job is null, "Operation cancellation must prevent later acceptance.");
        await ExpectProblemAsync("api/v1/jobs", request with { ClientOperationId = cancelledId }, cancelledId,
            HttpStatusCode.Conflict, ArchiverWire.Cancelled, token);
        checks.Add("Cancellation before submission prevents job creation");

        while (job.State is ArchiverWire.Queued or ArchiverWire.Running or ArchiverWire.Waiting) {
            var delay = job.NextPollAfter is { } next ? next - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(1);
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 10)), token);
            var previous = job;
            job = await SendAsync<JobSnapshot>(HttpMethod.Get, $"api/v1/jobs/{Uri.EscapeDataString(job.JobId)}", null, token);
            Match(job, plan, previous.JobId);
            Require(job.Revision >= previous.Revision, "Job revisions must not move backwards.");
        }
        Require(job.State == ArchiverWire.Succeeded && job.ItemFailures.Count == 0 && !job.ArtifactsExpired
            && !string.IsNullOrWhiteSpace(job.ManifestRevision), "The test fixture must complete with a retained sealed manifest.");
        checks.Add("Authoritative successful execution with a sealed manifest");
        var artifacts = await ReadManifestAsync(job, request, token);
        var directory = Path.GetDirectoryName(planPath)!;
        for (var i = 0; i < artifacts.Count; i++) await VerifyArtifactAsync(artifacts[i], Path.Combine(directory, $"artifact-{i:D3}.bin"), token);
        checks.Add("Complete manifest, bounded bytes, SHA-256, HEAD, and resumable Range retrieval");

        var retainedUntil = DateTimeOffset.UtcNow.AddMinutes(10);
        var lease = await SendAsync<LeaseResult>(HttpMethod.Post, $"api/v1/jobs/{Uri.EscapeDataString(job.JobId)}/lease", new LeaseRequest(retainedUntil), token);
        Require(lease.JobId == job.JobId && lease.RetainedUntil >= retainedUntil, "The retention lease must cover the requested horizon.");
        var receipt = new ReceiptRequest(plan.ReceiptId, job.ManifestRevision!, artifacts.Select(x => new ImportedArtifact(x.Id, x.Sha256, null)).ToArray());
        for (var attempt = 0; attempt < 2; attempt++) {
            var result = await SendAsync<ReceiptResult>(HttpMethod.Post, $"api/v1/jobs/{Uri.EscapeDataString(job.JobId)}/receipts", receipt, token);
            Require(result.Accepted && result.JobId == job.JobId && result.ReceiptId == plan.ReceiptId, "Receipt retries must acknowledge the same import.");
        }
        checks.Add("Retention lease and idempotent receipt after durable local verification");
        var report = new ConformanceReport(info.InstanceId, info.ApiVersion, checks, planPath);
        await WriteAsync(Path.Combine(directory, "report.json"), report, token);
        return report;
    }

    private async Task<(ConformancePlan Plan, string Path)> PreparePlanAsync(ConformanceOptions options, SystemInfo info, CancellationToken token) {
        if (options.ResumePath is { } resume) {
            var path = Path.GetFullPath(resume);
            var plan = JsonSerializer.Deserialize<ConformancePlan>(await File.ReadAllTextAsync(path, token), Json)
                ?? throw new ConformanceFailure("The saved plan is invalid.");
            Require(plan.InstanceId == info.InstanceId && plan.Endpoint == options.Endpoint.AbsoluteUri,
                "Resume requires the same endpoint and installation identity.");
            Require(plan.Request.Limits.MaxBytes is > 1 and <= MaximumBytes && plan.Request.Limits.MaxItems == 1,
                "The saved plan exceeds the checker limits.");
            return (plan, path);
        }
        var inspection = await SendAsync<Inspection>(HttpMethod.Post, "api/v1/inspect", new InspectRequest(options.Input!, options.Kind!, 1), token);
        Require(inspection.Items.Count == 1 && inspection.Items[0].MediaKind == options.Kind
            && inspection.Items[0].Formats.Contains(options.Format!) && inspection.ExpiresAt > DateTimeOffset.UtcNow,
            "Inspect must return one unexpired item with the requested kind and format.");
        var profile = options.Format switch { ArchiverWire.Png => ArchiverWire.ImageProfile, ArchiverWire.ImageSet => ArchiverWire.GalleryProfile, _ => ArchiverWire.PublicationProfile };
        Require(info.OutputProfiles.Contains(profile), "The selected output profile is not advertised.");
        var request = new SubmitJob(Guid.NewGuid(), new(inspection.CanonicalUrl), new(inspection.Id, inspection.Revision, [inspection.Items[0].Id]),
            new(profile, options.Format!), new(1, Math.Min(MaximumBytes, info.MaximumBytes)));
        Require(request.Limits.MaxBytes > 1, "The server's byte limit is too small for the acceptance fixture.");
        var created = new ConformancePlan(options.Endpoint.AbsoluteUri, info.InstanceId, request, Guid.NewGuid());
        var directory = Path.Combine(Path.GetFullPath(options.OutputDirectory!), request.ClientOperationId.ToString());
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var planPath = Path.Combine(directory, "plan.json");
        await WriteAsync(planPath, created, token); // Persist operation and receipt identities before any submission.
        return (created, planPath);
    }

    private async Task<List<Artifact>> ReadManifestAsync(JobSnapshot job, SubmitJob request, CancellationToken token) {
        var artifacts = new List<Artifact>(); var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null; int? count = null;
        do {
            var path = $"api/v1/jobs/{Uri.EscapeDataString(job.JobId)}/artifacts?revision={Uri.EscapeDataString(job.ManifestRevision!)}";
            if (cursor is not null) path += $"&cursor={Uri.EscapeDataString(cursor)}";
            var page = await SendAsync<ManifestPage>(HttpMethod.Get, path, null, token);
            Require(page.JobId == job.JobId && page.Revision == job.ManifestRevision && page.Sealed
                && page.ArtifactCount is > 0 and <= MaximumArtifacts && (count is null || count == page.ArtifactCount), "Manifest identity, count and seal must remain stable.");
            count = page.ArtifactCount; artifacts.AddRange(page.Artifacts);
            Require(artifacts.Count <= count && page.Artifacts.Count > 0, "Manifest pagination must make bounded progress.");
            cursor = page.NextCursor;
            Require(cursor is null || cursors.Add(cursor), "Manifest cursor repeated.");
        } while (cursor is not null);
        Require(artifacts.Count == count && artifacts.Select(x => x.Id).Distinct().Count() == count, "The manifest must contain every artifact exactly once.");
        Require(artifacts.All(x => x.SizeBytes is > 0 and <= MaximumBytes && request.Selection.ItemIds.Contains(x.ItemId)
            && x.Role == ArchiverWire.Content && x.Sha256.Length == 64 && x.Sha256.All(Uri.IsHexDigit))
            && artifacts.Sum(x => x.SizeBytes) <= request.Limits.MaxBytes, "Artifact identities, roles, hashes and sizes must match the bounded selection.");
        if (request.Output.Profile == ArchiverWire.GalleryProfile) {
            Require(artifacts.All(x => !string.IsNullOrWhiteSpace(x.GroupId) && x.Ordinal >= 0)
                && artifacts.Select(x => x.GroupId).Distinct().Count() == 1
                && artifacts.Select(x => x.Ordinal).Distinct().Count() == count, "Gallery outputs need one explicit group and unique ordering.");
        } else Require(artifacts.Count == 1, "The single-item output profile requires one artifact.");
        return artifacts;
    }

    private async Task VerifyArtifactAsync(Artifact artifact, string destination, CancellationToken token) {
        var uri = new Uri(http.BaseAddress!, artifact.ContentPath);
        Require(uri.Scheme == http.BaseAddress!.Scheme && uri.Authority == http.BaseAddress.Authority
            && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0
            && uri.AbsolutePath.StartsWith(http.BaseAddress.AbsolutePath, StringComparison.Ordinal), "Artifact retrieval must remain inside the authenticated API origin and base path.");
        using var head = await http.SendAsync(new(HttpMethod.Head, uri), HttpCompletionOption.ResponseHeadersRead, token);
        Require(head.StatusCode == HttpStatusCode.OK && head.Content.Headers.ContentLength == artifact.SizeBytes, "HEAD must report the exact artifact size.");
        using var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var response = await http.SendAsync(new(HttpMethod.Get, uri), HttpCompletionOption.ResponseHeadersRead, token);
        Require(response.StatusCode == HttpStatusCode.OK, "Artifact GET failed.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        var buffer = new byte[81920]; long total = 0; byte first = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) != 0) {
            if (total == 0) first = buffer[0]; total += read;
            Require(total <= artifact.SizeBytes, "Artifact response exceeded the sealed size.");
            hash.AppendData(buffer, 0, read); await file.WriteAsync(buffer.AsMemory(0, read), token);
        }
        Require(total == artifact.SizeBytes && Convert.ToHexStringLower(hash.GetHashAndReset()).Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase), "Downloaded bytes do not match the sealed size and SHA-256.");
        await file.FlushAsync(token); file.Flush(flushToDisk: true);
        using var range = new HttpRequestMessage(HttpMethod.Get, uri);
        range.Headers.Range = new RangeHeaderValue(0, 0);
        using var partial = await http.SendAsync(range, HttpCompletionOption.ResponseHeadersRead, token);
        Require(partial.StatusCode == HttpStatusCode.PartialContent && partial.Content.Headers.ContentRange is { From: 0, To: 0 } cr
            && cr.Length == artifact.SizeBytes && partial.Content.Headers.ContentLength == 1, "Range GET must return the requested byte and full size.");
        await using var partialStream = await partial.Content.ReadAsStreamAsync(token);
        var bytes = new byte[2];
        var partialLength = await partialStream.ReadAtLeastAsync(bytes, 2, throwOnEndOfStream: false, token);
        Require(partialLength == 1 && bytes[0] == first, "Range GET returned different bytes.");
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken token, Guid? operation = null) {
        using var request = Request(method, path, body, operation);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        Require(response.IsSuccessStatusCode, $"Contract request failed with HTTP {(int)response.StatusCode}.");
        await response.Content.LoadIntoBufferAsync(1024 * 1024, token);
        return await response.Content.ReadFromJsonAsync<T>(Json, token) ?? throw new ConformanceFailure("Expected a JSON response.");
    }

    private async Task ExpectProblemAsync(string path, object body, Guid operation, HttpStatusCode status, string code, CancellationToken token) {
        using var request = Request(HttpMethod.Post, path, body, operation);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        Require(response.StatusCode == status, "Conflicting operation returned an unexpected HTTP status.");
        await response.Content.LoadIntoBufferAsync(1024 * 1024, token);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(Json, token);
        Require(problem?.Code == code, "Conflicting operation returned the wrong problem code.");
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, object? body, Guid? operation) {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        if (operation is not null) request.Headers.Add("Idempotency-Key", operation.Value.ToString());
        return request;
    }
    private static void Match(JobSnapshot job, ConformancePlan plan, string? jobId = null) => Require(
        job.InstanceId == plan.InstanceId && job.ClientOperationId == plan.Request.ClientOperationId
        && !string.IsNullOrWhiteSpace(job.JobId) && (jobId is null || job.JobId == jobId), "Job and operation identity changed.");
    private static async Task WriteAsync<T>(string path, T value, CancellationToken token) {
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(value, Json), token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path + ".tmp", UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using (var stream = new FileStream(path + ".tmp", FileMode.Open, FileAccess.Write)) stream.Flush(flushToDisk: true);
        File.Move(path + ".tmp", path, true);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new ConformanceFailure(message); }
}
