using Prismedia.Archiver;
using System.Security.Cryptography;
using System.Text.Json;

namespace Prismedia.IntegrationSimulator;

/// <summary>A single-principal durable reference executor, intentionally independent of Prismedia persistence.</summary>
public sealed class SimulatorStore {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string directory;
    private readonly string statePath;
    private readonly State state;
    /// <summary>Opens an isolated simulator directory and retains identity through restarts.</summary>
    public SimulatorStore(string directory) {
        this.directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(this.directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(this.directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        statePath = Path.Combine(this.directory, "state.json");
        state = File.Exists(statePath) ? JsonSerializer.Deserialize<State>(File.ReadAllBytes(statePath), Json)! : new();
        Persist();
    }
    /// <summary>Reads the service's stable installation identity and negotiated executor profile.</summary>
    public SystemInfo Info => new(state.InstanceId, ArchiverWire.ApiVersion, "1.0.0-simulator",
        [ArchiverWire.Inspect, ArchiverWire.Submit, ArchiverWire.Cancel, ArchiverWire.CancelOperation, ArchiverWire.Artifacts, ArchiverWire.Retention, ArchiverWire.Receipts],
        [ArchiverWire.PublicationProfile, ArchiverWire.ImageProfile, ArchiverWire.GalleryProfile], 1, 2L * 1024 * 1024 * 1024, 7, 30);

    /// <summary>Inspects only reserved synthetic URLs. No arbitrary URL is fetched.</summary>
    public Task<Inspection> InspectAsync(InspectRequest request) => Locked(() => {
        if (request is null || request.MediaKind is not (ArchiverWire.Book or ArchiverWire.Comic or ArchiverWire.Image or ArchiverWire.Gallery) || request.MaxItems is < 1 or > 100
            || !Uri.TryCreate(request.Url, UriKind.Absolute, out var url) || url.Scheme != "https" || url.Host != "fixtures.example"
            || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0
            || !(request.MediaKind switch {
                ArchiverWire.Book => url.AbsolutePath == "/book",
                ArchiverWire.Image => url.AbsolutePath == "/image",
                ArchiverWire.Gallery => url.AbsolutePath is "/gallery" or "/gallery-single",
                _ => url.AbsolutePath == "/comic"
            }))
            throw new ApiFailure(400, ArchiverWire.Invalid, "Use https://fixtures.example/book or https://fixtures.example/comic or https://fixtures.example/image or https://fixtures.example/gallery with its matching media kind.");
        var item = new SourceItem(Hash(url.AbsoluteUri), request.MediaKind switch { ArchiverWire.Book => "Integration Field Notes", ArchiverWire.Image => "Integration Image", ArchiverWire.Gallery => "Integration Gallery Vol. 1", _ => "Integration Comic" }, request.MediaKind,
            [request.MediaKind switch { ArchiverWire.Book => ArchiverWire.Epub, ArchiverWire.Image => ArchiverWire.Png, ArchiverWire.Gallery => ArchiverWire.ImageSet, _ => ArchiverWire.Cbz }]);
        var inspection = new Inspection(Guid.NewGuid().ToString("N"), Hash(item.Id), DateTimeOffset.UtcNow.AddMinutes(30), url.AbsoluteUri,
            ArchiverWire.SourceId, [item], []);
        state.Selections[inspection.Id] = inspection;
        Persist();
        return inspection;
    });

    /// <summary>Resolves accepted operation keys before expiry checks and atomically commits a single job.</summary>
    public Task<(JobSnapshot Snapshot, bool LoseResponse)> SubmitAsync(SubmitJob request, string? idempotencyKey) => Locked(() => {
        if (request is null || request.ClientOperationId == Guid.Empty || !Guid.TryParse(idempotencyKey, out var header) || header != request.ClientOperationId)
            throw new ApiFailure(400, ArchiverWire.Invalid, "Matching operation IDs are required in the body and Idempotency-Key header.");
        var fingerprint = Hash(JsonSerializer.Serialize(request, Json));
        if (state.Jobs.Values.FirstOrDefault(job => job.Request.ClientOperationId == request.ClientOperationId) is { } previous) {
            if (previous.Fingerprint != fingerprint) throw new ApiFailure(409, ArchiverWire.Conflict, "This operation already accepted another request.");
            return (Snapshot(previous), false);
        }
        if (request.Selection?.ItemIds is not { Count: 1 } || request.Input is null || request.Output is null || request.Limits is null
            || request.Limits.MaxItems != 1 || request.Limits.MaxBytes is < 1 or > 2147483648
            || request.Output.Profile != (request.Output.Format switch { ArchiverWire.Png => ArchiverWire.ImageProfile, ArchiverWire.ImageSet => ArchiverWire.GalleryProfile, _ => ArchiverWire.PublicationProfile }))
            throw new ApiFailure(400, ArchiverWire.Invalid, "Choose one item and the matching finite output profile.");
        if (state.CancelledOperations.Contains(request.ClientOperationId))
            throw new ApiFailure(409, ArchiverWire.Cancelled, "This operation was cancelled before acceptance and cannot create a job.");
        if (!state.Selections.TryGetValue(request.Selection.Id, out var inspection) || inspection.Revision != request.Selection.Revision
            || inspection.ExpiresAt <= DateTimeOffset.UtcNow || inspection.CanonicalUrl != request.Input.Url
            || inspection.Items[0].Id != request.Selection.ItemIds[0] || !inspection.Items[0].Formats.Contains(request.Output.Format))
            throw new ApiFailure(409, ArchiverWire.Stale, "Inspect the source again before creating a new operation.");
        var now = DateTimeOffset.UtcNow;
        var job = new Job { Id = Guid.NewGuid().ToString("N"), Request = request, Fingerprint = fingerprint,
            CreatedAt = now, UpdatedAt = now, ReadyAt = now.AddSeconds(state.Controls.ExecutionDelaySeconds), RetainedUntil = now.AddDays(7) };
        state.Jobs[job.Id] = job;
        var lose = state.Controls.LoseNextSubmissionResponse;
        state.Controls = state.Controls with { LoseNextSubmissionResponse = false };
        Persist();
        return (Snapshot(job), lose);
    });

    /// <summary>Returns the exact durable job for an operation, even after terminal execution.</summary>
    public Task<JobSnapshot> FindOperationAsync(Guid operation) => Locked(() => Snapshot(state.Jobs.Values.FirstOrDefault(job => job.Request.ClientOperationId == operation)
        ?? throw new ApiFailure(404, ArchiverWire.NotFound, "No accepted operation exists.")));
    /// <summary>Returns an authoritative immutable projection.</summary>
    public Task<JobSnapshot> GetAsync(string id) => Locked(() => Snapshot(Get(id)));
    /// <summary>Lists bounded durable jobs, with stable IDs as continuation tokens.</summary>
    public Task<object> ListAsync(string? cursor, int limit) => Locked<object>(() => {
        if (limit is < 1 or > 100) throw new ApiFailure(400, ArchiverWire.Invalid, "The page limit must be between 1 and 100.");
        var page = state.Jobs.Values.OrderBy(job => job.Id, StringComparer.Ordinal).Where(job => cursor is null || string.CompareOrdinal(job.Id, cursor) > 0).Take(limit + 1).ToArray();
        return new { items = page.Take(limit).Select(Snapshot).ToArray(), nextCursor = page.Length > limit ? page[limit - 1].Id : null };
    });
    /// <summary>Stops queued/running fixture work before publication without deleting files or history.</summary>
    public Task<JobSnapshot> CancelAsync(string id) => Locked(() => {
        var job = Get(id);
        if (job.Status is ArchiverWire.Queued or ArchiverWire.Running or ArchiverWire.Waiting) { job.Status = ArchiverWire.Cancelled; Touch(job); Persist(); }
        return Snapshot(job);
    });
    /// <summary>Serializes with submission to prevent acceptance after a cancellation tombstone commits.</summary>
    public Task<OperationCancellation> CancelOperationAsync(Guid operation) => Locked(() => {
        if (operation == Guid.Empty) throw new ApiFailure(400, ArchiverWire.Invalid, "A stable operation ID is required.");
        state.CancelledOperations.Add(operation);
        var job = state.Jobs.Values.FirstOrDefault(item => item.Request.ClientOperationId == operation);
        if (job?.Status is ArchiverWire.Queued or ArchiverWire.Running or ArchiverWire.Waiting) {
            job.Status = ArchiverWire.Cancelled; Touch(job);
        }
        Persist();
        return new OperationCancellation(state.InstanceId, operation, true, job is null ? null : Snapshot(job));
    });
    /// <summary>Returns the exact sealed output set; wrong revisions are rejected.</summary>
    public Task<ManifestPage> ManifestAsync(string id, string revision, string? cursor) => Locked(() => {
        var job = Get(id);
        if (job.ManifestRevision is null || job.ManifestRevision != revision || cursor is not null)
            throw new ApiFailure(409, ArchiverWire.Invalid, "The sealed manifest revision is unavailable.");
        return new ManifestPage(id, revision, true, job.Artifacts.Count, job.Artifacts, null);
    });
    /// <summary>Returns an opaque artifact's immutable file only while retained.</summary>
    public Task<(string Path, Artifact Artifact)> ContentAsync(string artifactId) => Locked(() => {
        var job = state.Jobs.Values.FirstOrDefault(job => job.Artifacts.Any(file => file.Id == artifactId))
            ?? throw new ApiFailure(404, ArchiverWire.NotFound, "The artifact was not found.");
        if (job.RetainedUntil < DateTimeOffset.UtcNow) throw new ApiFailure(410, ArchiverWire.Expired, "Artifact retention has expired.");
        var artifact = job.Artifacts.Single(file => file.Id == artifactId);
        return (Path.Combine(directory, artifact.Id), artifact);
    });
    /// <summary>Extends a guaranteed retention lease without changing terminal execution or manifest identity.</summary>
    public Task<LeaseResult> LeaseAsync(string id, LeaseRequest request) => Locked(() => {
        var job = Get(id);
        if (request.RetainUntil <= DateTimeOffset.UtcNow || request.RetainUntil > DateTimeOffset.UtcNow.AddDays(7))
            throw new ApiFailure(400, ArchiverWire.Invalid, "Request a retention horizon within the next seven days.");
        if (job.RetainedUntil < DateTimeOffset.UtcNow) throw new ApiFailure(410, ArchiverWire.Expired, "Expired outputs cannot be silently recreated.");
        if (request.RetainUntil > job.RetainedUntil) { job.RetainedUntil = request.RetainUntil; Touch(job); Persist(); }
        return new LeaseResult(id, job.RetainedUntil);
    });
    /// <summary>Validates exact imported artifacts and persists the receipt before returning confirmation.</summary>
    public Task<(ReceiptResult Receipt, bool LoseResponse)> ReceiptAsync(string id, ReceiptRequest request) => Locked(() => {
        var job = Get(id);
        if (request.ReceiptId == Guid.Empty || request.ManifestRevision != job.ManifestRevision || request.Artifacts is not { Count: > 0 }
            || request.Artifacts.Count > job.Artifacts.Count || request.Artifacts.Select(file => file.ArtifactId).Distinct().Count() != request.Artifacts.Count
            || request.Artifacts.Any(file => !job.Artifacts.Any(artifact => artifact.Id == file.ArtifactId && artifact.Sha256 == file.Sha256)))
            throw new ApiFailure(400, ArchiverWire.Invalid, "The receipt must identify exact sealed artifact hashes.");
        var fingerprint = Hash(JsonSerializer.Serialize(request, Json));
        if (job.Receipts.TryGetValue(request.ReceiptId, out var previous) && previous != fingerprint)
            throw new ApiFailure(409, ArchiverWire.Conflict, "This receipt ID already acknowledged another import.");
        job.Receipts[request.ReceiptId] = fingerprint;
        var lose = state.Controls.LoseNextReceiptResponse;
        state.Controls = state.Controls with { LoseNextReceiptResponse = false };
        Persist();
        return (new ReceiptResult(id, request.ReceiptId, true), lose);
    });
    /// <summary>Configures explicit fault injection in this isolated simulator.</summary>
    public Task<SimulationControls> ConfigureAsync(SimulationControls controls) => Locked(() => {
        if (controls.ExecutionDelaySeconds is < 0 or > 3600) throw new ApiFailure(400, ArchiverWire.Invalid, "The simulation delay must be between zero and 3600 seconds.");
        state.Controls = controls; Persist(); return controls;
    });
    /// <summary>Resumes the same persisted jobs after restart; sealed bytes are never regenerated.</summary>
    public Task AdvanceAsync() => Locked(() => {
        var changed = false;
        foreach (var job in state.Jobs.Values.Where(job => job.Status is ArchiverWire.Queued or ArchiverWire.Running)) {
            changed = true;
            if (job.Status == ArchiverWire.Queued) { job.Status = ArchiverWire.Running; Touch(job); }
            if (job.ReadyAt > DateTimeOffset.UtcNow) continue;
            var gallery = job.Request.Output.Profile == ArchiverWire.GalleryProfile;
            var count = gallery && !job.Request.Input.Url.EndsWith("/gallery-single", StringComparison.Ordinal) ? 3 : 1;
            var format = gallery ? ArchiverWire.Png : job.Request.Output.Format;
            var bytes = PublicationFixtures.Create(format);
            if ((long)bytes.Length * count > job.Request.Limits.MaxBytes) {
                job.Status = ArchiverWire.Failed;
                job.Failures = [new(job.Request.Selection.ItemIds[0], "The publication exceeds the accepted byte limit.")];
            } else {
                job.Artifacts = [];
                var group = gallery ? Guid.NewGuid().ToString("N") : null;
                for (var ordinal = 1; ordinal <= count; ordinal++) {
                    var id = Guid.NewGuid().ToString("N");
                    AtomicWrite(Path.Combine(directory, id), bytes);
                    job.Artifacts.Add(new(id, job.Request.Selection.ItemIds[0], gallery ? $"Source page {ordinal}.png" : "publication." + format,
                        format switch { ArchiverWire.Epub => "application/epub+zip", ArchiverWire.Png => "image/png", _ => "application/vnd.comicbook+zip" },
                        bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), ArchiverWire.Content, $"/api/v1/artifacts/{id}/content", group, gallery ? ordinal : null));
                }
                job.ManifestRevision = Guid.NewGuid().ToString("N");
                job.Status = ArchiverWire.Succeeded;
            }
            Touch(job);
        }
        if (changed) Persist(); return true;
    });
    private Job Get(string id) => state.Jobs.GetValueOrDefault(id) ?? throw new ApiFailure(404, ArchiverWire.NotFound, "The job was not found.");
    private JobSnapshot Snapshot(Job job) => new(state.InstanceId, job.Id, job.Request.ClientOperationId, job.Revision, job.Status,
        job.Status == ArchiverWire.Succeeded ? 1 : null, job.CreatedAt, job.UpdatedAt, job.ManifestRevision, job.RetainedUntil,
        job.RetainedUntil < DateTimeOffset.UtcNow, job.Status is ArchiverWire.Queued or ArchiverWire.Running ? DateTimeOffset.UtcNow.AddSeconds(5) : null, job.Failures);
    private static void Touch(Job job) { job.Revision++; job.UpdatedAt = DateTimeOffset.UtcNow; }
    private void Persist() => AtomicWrite(statePath, JsonSerializer.SerializeToUtf8Bytes(state, Json));
    private static void AtomicWrite(string path, byte[] bytes) {
        var temporary = path + ".tmp";
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, path, true);
    }
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    private async Task<T> Locked<T>(Func<T> action) { await gate.WaitAsync(); try { return action(); } finally { gate.Release(); } }
    /// <summary>Simulator-only persisted document, not a production storage prescription.</summary>
    public sealed class State {
        public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");
        public Dictionary<string, Inspection> Selections { get; set; } = [];
        public Dictionary<string, Job> Jobs { get; set; } = [];
        public HashSet<Guid> CancelledOperations { get; set; } = [];
        public SimulationControls Controls { get; set; } = new();
    }
    /// <summary>Durable fixture job and immutable acceptance evidence.</summary>
    public sealed class Job {
        public string Id { get; set; } = "";
        public SubmitJob Request { get; set; } = null!;
        public string Fingerprint { get; set; } = "";
        public string Status { get; set; } = ArchiverWire.Queued;
        public long Revision { get; set; } = 1;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public DateTimeOffset ReadyAt { get; set; }
        public DateTimeOffset RetainedUntil { get; set; }
        public string? ManifestRevision { get; set; }
        public List<Artifact> Artifacts { get; set; } = [];
        public List<ItemFailure> Failures { get; set; } = [];
        public Dictionary<Guid, string> Receipts { get; set; } = [];
    }
}
