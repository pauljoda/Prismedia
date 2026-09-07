using System.Text.Json.Nodes;
using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;
using Prismedia.Infrastructure.Acquisition;

namespace Prismedia.Infrastructure.Tests;

public sealed class AtomicUpgradeCheckpointJsonTests {
    [Fact]
    public void ContainerChangeRequiresDurableVideoPermission() {
        var checkpoint = Prepared(EntityKind.Movie);
        checkpoint = checkpoint with { Files = checkpoint.Files with { OwnedPath = Path.ChangeExtension(checkpoint.Files.OwnedPath, ".avi") } };
        Assert.Throws<InvalidDataException>(() => AtomicUpgradeCheckpointJson.Serialize(checkpoint));
        checkpoint = checkpoint with { Files = checkpoint.Files with { AllowFormatChange = true } };
        var json = AtomicUpgradeCheckpointJson.Serialize(checkpoint);
        Assert.Equal(checkpoint, AtomicUpgradeCheckpointJson.Deserialize(json));
        Assert.DoesNotContain(nameof(AtomicUpgradeFilePlan.InstallPath), json);
        Assert.Throws<InvalidDataException>(() => AtomicUpgradeCheckpointJson.Serialize(checkpoint with { Kind = EntityKind.Book }));
    }

    [Fact]
    public void PreviousSameFormatCheckpointRemainsReadableWithoutTheNewPermission() {
        var checkpoint = Prepared(EntityKind.Movie);
        var json = JsonNode.Parse(AtomicUpgradeCheckpointJson.Serialize(checkpoint))!;
        json[nameof(AtomicUpgradeCheckpoint.Files)]!.AsObject().Remove(nameof(AtomicUpgradeFilePlan.AllowFormatChange));
        Assert.Equal(checkpoint, AtomicUpgradeCheckpointJson.Deserialize(json.ToJsonString()));
    }

    [Theory]
    [InlineData(EntityKind.Book)]
    [InlineData(EntityKind.Movie)]
    [InlineData(EntityKind.VideoEpisode)]
    public void PreparedReplacementRoundTripsExactOwnershipAndDistinctRecoveryArtifacts(EntityKind kind) {
        var checkpoint = Prepared(kind);
        var json = AtomicUpgradeCheckpointJson.Serialize(checkpoint);

        Assert.True(AtomicUpgradeCheckpointJson.IsAtomic(json));
        Assert.Equal(checkpoint, AtomicUpgradeCheckpointJson.Deserialize(json));
        Assert.NotEqual(checkpoint.Files.OwnedPath, checkpoint.BackupPath);
        Assert.NotEqual(checkpoint.Files.IncomingPath, checkpoint.EvidencePath);
        Assert.NotEqual(checkpoint.BackupPath, (checkpoint with { AttemptId = Guid.NewGuid() }).BackupPath);
        Assert.DoesNotContain(nameof(AtomicUpgradeCheckpoint.BackupPath), json);
        Assert.DoesNotContain(nameof(AtomicUpgradeCheckpoint.EvidencePath), json);
    }

    [Theory]
    [InlineData("missing protocol")]
    [InlineData("wrong protocol")]
    [InlineData("empty attempt")]
    [InlineData("empty source")]
    [InlineData("unknown member")]
    [InlineData("outside payload")]
    [InlineData("relative owned")]
    [InlineData("same file")]
    [InlineData("empty incoming")]
    [InlineData("missing snapshot")]
    [InlineData("missing selected release")]
    public void DamagedReplacementEvidenceRequiresReview(string damage) {
        var checkpoint = Prepared(EntityKind.Movie);
        var json = JsonNode.Parse(AtomicUpgradeCheckpointJson.Serialize(checkpoint))!;
        switch (damage) {
            case "missing protocol": json.AsObject().Remove(nameof(AtomicUpgradeCheckpoint.Protocol)); break;
            case "wrong protocol": json[nameof(AtomicUpgradeCheckpoint.Protocol)] = AcquisitionCheckpointProtocol.Placement.ToCode(); break;
            case "empty attempt": json[nameof(AtomicUpgradeCheckpoint.AttemptId)] = Guid.Empty.ToString(); break;
            case "empty source": json[nameof(AtomicUpgradeCheckpoint.SourceFileId)] = Guid.Empty.ToString(); break;
            case "unknown member": json["FutureRecoveryEvidence"] = "not understood"; break;
            case "outside payload": json[nameof(AtomicUpgradeCheckpoint.Files)]![nameof(AtomicUpgradeFilePlan.IncomingPath)] = Path.GetFullPath("/other/video.mkv"); break;
            case "relative owned": json[nameof(AtomicUpgradeCheckpoint.Files)]![nameof(AtomicUpgradeFilePlan.OwnedPath)] = "video.mkv"; break;
            case "same file": json[nameof(AtomicUpgradeCheckpoint.Files)]![nameof(AtomicUpgradeFilePlan.OwnedPath)] = checkpoint.Files.IncomingPath; break;
            case "empty incoming": json[nameof(AtomicUpgradeCheckpoint.Files)]![nameof(AtomicUpgradeFilePlan.Incoming)]![nameof(AtomicUpgradeFileSnapshot.Length)] = 0; break;
            case "missing snapshot": json[nameof(AtomicUpgradeCheckpoint.Files)]![nameof(AtomicUpgradeFilePlan.Owned)] = null; break;
            case "missing selected release": json[nameof(AtomicUpgradeCheckpoint.SelectedRelease)] = null; break;
        }

        Assert.Throws<InvalidDataException>(() => AtomicUpgradeCheckpointJson.Deserialize(json.ToJsonString()));
    }

    [Fact]
    public void LegacyFamilyCheckpointsAreNotMistakenForAtomicPreparation() {
        Assert.False(AtomicUpgradeCheckpointJson.IsAtomic(null));
        Assert.False(AtomicUpgradeCheckpointJson.IsAtomic("{\"Units\":[]}"));
        Assert.Throws<InvalidDataException>(() => AtomicUpgradeCheckpointJson.IsAtomic("{"));
    }

    private static AtomicUpgradeCheckpoint Prepared(EntityKind kind) {
        var folder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "atomic-checkpoint-validation"));
        var payload = Path.Combine(folder, "download");
        var extension = kind == EntityKind.Book ? ".epub" : ".mkv";
        return new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), kind,
            new(Path.Combine(folder, "owned" + extension), Path.Combine(payload, "candidate" + extension),
                new(100, DateTime.UtcNow.AddMinutes(-1)), new(200, DateTime.UtcNow),
                kind == EntityKind.Book ? BookFormatTier.Reflowable : BookFormatTier.Unknown),
            payload, "owned-transfer", new SelectedRelease("Verified release", "Indexer", "hash"));
    }
}
