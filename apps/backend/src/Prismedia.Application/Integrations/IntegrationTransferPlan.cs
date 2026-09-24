using Prismedia.Contracts.Integrations;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Integrations;

/// <summary>Accepted finite transfer intent. Sensitive source locators and executor URLs require encrypted persistence.</summary>
public sealed record IntegrationTransferPlan(string Title, EntityKind EntityKind, Guid LibraryRootId, string LibraryPath,
    string OwnershipKey, string RequestFingerprint, SourceTransferPlan? Source = null, SubmitTransferInput? Executor = null);
