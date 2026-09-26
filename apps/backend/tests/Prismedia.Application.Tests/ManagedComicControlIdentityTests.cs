using Prismedia.Application.Integrations;
using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;
using Prismedia.Domain.Integrations;

namespace Prismedia.Application.Tests;

public sealed class ManagedComicControlIdentityTests {
    [Fact]
    public void Reviewed_scope_retains_issue_label_and_rejects_a_changed_label() {
        var item = new ManagedItemInput(EntityKind.ComicSeries, "7", new Dictionary<string, string>());
        var target = new ManagedTargetIdentity("19", EntityKind.ComicInstallment, null, null, null, "12.5");
        var holding = new ManagedTrackingResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), item, "Run",
            ManagedTrackingStatus.Tracking, 1, null, null, [], [new(target, Guid.NewGuid())]);
        var scope = ManagedControlIdentity.From(holding).Scope;
        Assert.Equal("12.5", Assert.Single(scope.Targets).IssueLabel);

        var remote = new ManagedLibraryItem("7", EntityKind.ComicSeries, "Run", 2024, item.ExpectedExternalIds, true, null, 1);
        var valid = new ManagedControlState(remote, "/comics/run",
            [new(scope.Targets[0], true)], new(false, false, false));
        ManagedControlValidation.Validate(scope, valid);
        var observed = valid with {
            Targets = [new(scope.Targets[0] with { IssueLabel = "12" }, true)]
        };
        Assert.Throws<IntegrationInvocationException>(() => ManagedControlValidation.Validate(scope, observed));
    }
}
