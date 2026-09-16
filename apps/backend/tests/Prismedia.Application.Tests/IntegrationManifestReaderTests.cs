using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Tests;

public sealed class IntegrationManifestReaderTests {
    private static readonly IntegrationConnectionContext Connection = new(Guid.NewGuid(), "https://executor.test/", "installation", new Dictionary<string, string>(), new Dictionary<string, string>());
    private const string PluginId = "executor";
    private const string JobId = "job";
    private const string Revision = "revision";
    private static IntegrationArtifact Artifact(string id) => new(id, "selected-item", id + ".epub", "application/epub+zip", 100, new string('a', 64), IntegrationArtifactRole.Content);

    [Fact]
    public async Task ReadsAllPagesOfOneFrozenRevision() {
        var gateway = new Gateway(input => input.Cursor is null
            ? new(JobId, Revision, true, 2, [Artifact("one")], "next")
            : new(JobId, Revision, true, 2, [Artifact("two")]));
        var manifest = await new IntegrationManifestReader(gateway).ReadAsync(PluginId, Connection, JobId, Revision, default);
        Assert.Equal(2, manifest.Artifacts.Count);
        Assert.Equal(2, gateway.Calls);
    }

    [Fact]
    public async Task PageCannotSwitchManifestRevisionsMidRead() {
        var gateway = new Gateway(input => input.Cursor is null
            ? new(JobId, Revision, true, 2, [Artifact("one")], "next")
            : new(JobId, "another-revision", true, 2, [Artifact("two")]));
        await Assert.ThrowsAsync<IntegrationInvocationException>(() => new IntegrationManifestReader(gateway).ReadAsync(PluginId, Connection, JobId, Revision, default));
    }

    [Fact]
    public async Task MissingPageIsNotAcceptedAsACompleteOutputSet() {
        var gateway = new Gateway(_ => new(JobId, Revision, true, 2, [Artifact("one")]));
        await Assert.ThrowsAsync<IntegrationInvocationException>(() => new IntegrationManifestReader(gateway).ReadAsync(PluginId, Connection, JobId, Revision, default));
    }

    [Fact]
    public async Task RepeatingCursorCannotCauseAnUnboundedLoop() {
        var gateway = new Gateway(_ => new(JobId, Revision, true, 100, [Artifact("one")], "same"));
        await Assert.ThrowsAsync<IntegrationInvocationException>(() => new IntegrationManifestReader(gateway).ReadAsync(PluginId, Connection, JobId, Revision, default));
        Assert.Equal(2, gateway.Calls);
    }

    private sealed class Gateway(Func<ReadTransferManifestInput, TransferManifestPage> read) : IIntegrationTransferGateway {
        internal int Calls { get; private set; }
        public Task<TransferManifestPage> ReadManifestAsync(string pluginId, IntegrationConnectionContext connection, ReadTransferManifestInput input, CancellationToken cancellationToken) {
            Calls++; return Task.FromResult(read(input));
        }
        public Task<TransferInspection> InspectAsync(string pluginId, IntegrationConnectionContext connection, InspectTransferInput input, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<RemoteTransferSnapshot> SubmitAsync(string pluginId, IntegrationConnectionContext connection, SubmitTransferInput input, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<FindTransferResult> FindSubmissionAsync(string pluginId, IntegrationConnectionContext connection, FindTransferInput input, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<RemoteTransferSnapshot> GetJobAsync(string pluginId, IntegrationConnectionContext connection, RemoteTransferJobInput input, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<RemoteTransferSnapshot> CancelAsync(string pluginId, IntegrationConnectionContext connection, RemoteTransferJobInput input, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<HttpArtifactDelivery> AuthorizeArtifactAsync(string pluginId, IntegrationConnectionContext connection, AuthorizeTransferArtifactInput input, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<TransferRetentionResult> RenewRetentionAsync(string pluginId, IntegrationConnectionContext connection, RenewTransferRetentionInput input, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<TransferAcknowledgement> AcknowledgeAsync(string pluginId, IntegrationConnectionContext connection, AcknowledgeTransferInput input, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
