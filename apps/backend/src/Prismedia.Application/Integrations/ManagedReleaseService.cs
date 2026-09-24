using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Integrations;

/// <summary>Releases acquisition ownership only after freezing host effects and freshly observing remote inactivity.</summary>
public sealed class ManagedReleaseService(IManagedReleaseStore store, IManagedTrackingStore tracking,
    IManagedControlStore controls, IntegrationConnectionAccess access, IIntegrationManagerReleaseGateway gateway) {
    #region Actions - Handoff

    /// <summary>Reads the current exact scope, monitoring and remote activity without accepting a handoff.</summary>
    public async Task<ManagedReleasePreview> PreviewAsync(Guid connectionId, Guid holdingId, CancellationToken token) {
        var owned = await controls.RequireScopeAsync(connectionId, holdingId, token);
        await store.RequireSettledControlsAsync(holdingId, token);
        var holding = (await tracking.FindAsync(holdingId, token))!.Tracking;
        var observation = await InspectAsync(connectionId, owned.Scope,
            expectRemoteAbsence: holding.Status == ManagedTrackingStatus.Removed, token);
        var problem = Readiness(observation).Problem;
        return new(holding.Revision, owned.Fingerprint, observation, problem is null, problem);
    }

    /// <summary>Persists reviewed release intent before any ownership change; retries return the same handoff.</summary>
    public async Task<ManagedTrackingResponse> BeginAsync(Guid connectionId, Guid holdingId, ReleaseManagedHoldingRequest request,
        CancellationToken token) {
        if (request.OperationId == Guid.Empty || request.ExpectedRevision < 1 || request.ScopeFingerprint is not { Length: 64 }
            || !request.ScopeFingerprint.All(Uri.IsHexDigit)
            || request.RemoteItemAbsent && request.ExpectedPath is not null
            || !request.RemoteItemAbsent && (string.IsNullOrWhiteSpace(request.ExpectedPath) || request.ExpectedPath.Length > 8192)) {
            throw new ArgumentException("Review the exact holding and its activity before releasing ownership.");
        }

        if (await store.FindAsync(holdingId, token) is not null) {
            return await store.BeginAsync(connectionId, holdingId, request, token);
        }

        var preview = await PreviewAsync(connectionId, holdingId, token);
        if (preview.Revision < request.ExpectedRevision || preview.ScopeFingerprint != request.ScopeFingerprint
            || preview.Observation.RemoteItemAbsent != request.RemoteItemAbsent
            || !request.RemoteItemAbsent && preview.Observation.State?.Path != request.ExpectedPath) {
            throw new ManagedControlConflictException("The reviewed holding changed. Review the handoff again.");
        }

        if (!preview.CanRelease) {
            throw new ManagedControlConflictException(preview.Problem!);
        }

        return await store.BeginAsync(connectionId, holdingId, request, token);
    }

    #endregion

    #region Actions - Processing

    /// <summary>Handles release intents before ordinary reconciliation; failures retain the frozen owner for recovery.</summary>
    public async Task<bool> ProcessAsync(Guid holdingId, CancellationToken token) {
        var work = await store.FindAsync(holdingId, token);
        if (work is null) {
            return false;
        }

        if (work.Holding.Status == ManagedTrackingStatus.Released) {
            return true;
        }

        try {
            var observation = await InspectAsync(work.Holding.ConnectionId,
                ManagedControlIdentity.From(work.Holding).Scope, work.Request.RemoteItemAbsent, token);
            ValidateEvidence(work, observation);
            await store.CompleteAsync(work, observation, token);
        } catch (Exception error) when (error is IntegrationInvocationException or ConnectionNotFoundException
            or ConnectionSecretUnavailableException or ConnectionCapabilityUnavailableException) {
            await store.RecordProblemAsync(work,
                "The connected application could not be verified. Ownership remains reserved; refresh after restoring the connection.",
                token);
        } catch (Exception error) when (error is ArgumentException or ManagedControlConflictException) {
            await store.RecordProblemAsync(work, error.Message, token);
        }

        return true;
    }

    /// <summary>Checks pinned identity, path, complete finite coverage, monitoring and activity at the commit boundary.</summary>
    public static void ValidateEvidence(ManagedReleaseWork work, ManagedReleaseObservation observation) {
        var owned = ManagedControlIdentity.From(work.Holding);
        if (owned.Fingerprint != work.Request.ScopeFingerprint || observation is null
            || observation.RemoteItemAbsent != work.Request.RemoteItemAbsent) {
            throw new ArgumentException("The accepted release scope no longer has complete evidence.");
        }

        if (observation.RemoteItemAbsent) {
            if (observation.State is not null || work.Request.ExpectedPath is not null) {
                throw new ArgumentException("The confirmed remote absence no longer matches the reviewed handoff.");
            }
        } else {
            if (observation.State is null) {
                throw new ArgumentException("The manager returned no holding state for the reviewed handoff.");
            }

            ManagedControlValidation.Validate(owned.Scope, observation.State);
            if (observation.State.Path != work.Request.ExpectedPath) {
                throw new ArgumentException("The holding moved since handoff review. Ownership remains reserved.");
            }
        }

        if (Readiness(observation).Problem is { } problem) {
            throw new ArgumentException(problem);
        }
    }

    #endregion

    #region Actions - Observation

    private async Task<ManagedReleaseObservation> InspectAsync(Guid connectionId, ManagedControlScope scope,
        bool expectRemoteAbsence, CancellationToken token) {
        var connection = await access.RequireAsync(connectionId, PluginCapability.ExternalManager,
            IntegrationOperation.InspectManagedRelease, scope.Item.EntityKind, token);
        var observation = await gateway.InspectReleaseAsync(connection.Manifest.Id, connection.Context, new(scope), token);
        if (observation is null) {
            throw new IntegrationInvocationException("The manager returned no release evidence.");
        }

        if (observation.RemoteItemAbsent) {
            if (!expectRemoteAbsence) {
                throw new ManagedControlConflictException(
                    "The remote holding disappeared after review. Refresh its library state before releasing ownership.");
            }

            if (observation.State is not null) {
                throw new IntegrationInvocationException("The manager returned conflicting present and absent release evidence.");
            }
        } else {
            if (expectRemoteAbsence) {
                throw new ManagedControlConflictException(
                    "The removed remote identity exists again. Review it before releasing ownership.");
            }

            if (observation.State is null) {
                throw new IntegrationInvocationException("The manager returned incomplete release evidence.");
            }

            ManagedControlValidation.Validate(scope, observation.State);
        }

        return observation;
    }

    private static ManagedReleaseReadiness Readiness(ManagedReleaseObservation observation) =>
        new(observation.State?.Targets.Any(target => target.Monitored) == true, observation.QueueEmpty, observation.CommandsIdle);

    #endregion
}
