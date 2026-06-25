using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;

namespace Prismedia.Application.Acquisition;

/// <summary>Application use case for listing, saving, and deleting book acquisition profiles.</summary>
public sealed class BookAcquisitionProfileCommandService(IBookAcquisitionProfileStore store) {
    public Task<IReadOnlyList<BookAcquisitionProfileView>> ListAsync(CancellationToken cancellationToken) =>
        store.ListAsync(cancellationToken);

    public Task<BookAcquisitionProfileView> SaveAsync(BookAcquisitionProfileSaveRequest request, CancellationToken cancellationToken) =>
        store.SaveAsync(ToCommand(request), cancellationToken);

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        store.DeleteAsync(id, cancellationToken);

    private static BookAcquisitionProfileSaveCommand ToCommand(BookAcquisitionProfileSaveRequest request) {
        if (string.IsNullOrWhiteSpace(request.DisplayName)) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionProfileInvalid, "A display name is required.");
        }

        if (request.TargetLibraryRootId == Guid.Empty) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionProfileInvalid, "A target library root is required.");
        }

        if (string.IsNullOrWhiteSpace(request.PathTemplate)) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionProfileInvalid, "A path template is required.");
        }

        if (request.MinSeeders < 0) {
            throw new AcquisitionConfigurationException(ApiProblemCodes.AcquisitionProfileInvalid, "Minimum seeders cannot be negative.");
        }

        return new BookAcquisitionProfileSaveCommand(
            request.Id,
            request.DisplayName.Trim(),
            request.IsDefault,
            request.TargetLibraryRootId,
            request.PathTemplate.Trim(),
            request.ImportMode,
            request.AllowedFormats.Distinct().ToArray(),
            string.IsNullOrWhiteSpace(request.Language) ? null : request.Language.Trim(),
            request.MinSeeders,
            request.MinSizeBytes,
            request.MaxSizeBytes,
            request.RequiredTerms.Where(term => !string.IsNullOrWhiteSpace(term)).Select(term => term.Trim()).ToArray(),
            request.IgnoredTerms.Where(term => !string.IsNullOrWhiteSpace(term)).Select(term => term.Trim()).ToArray(),
            request.AutoPick);
    }
}
