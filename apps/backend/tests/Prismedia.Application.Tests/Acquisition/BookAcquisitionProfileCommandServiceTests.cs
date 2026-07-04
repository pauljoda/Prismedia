using Prismedia.Application.Acquisition;
using Prismedia.Contracts.Acquisition;
using Prismedia.Contracts.System;
using Prismedia.Domain.Entities;

namespace Prismedia.Application.Tests.Acquisition;

public sealed class BookAcquisitionProfileCommandServiceTests {
    private static BookAcquisitionProfileSaveRequest Request(EntityKind kind, string pathTemplate) => new(
        Id: null,
        DisplayName: "Test",
        IsDefault: true,
        Kind: kind,
        TargetLibraryRootId: Guid.NewGuid(),
        PathTemplate: pathTemplate,
        ImportMode: ImportMode.Move,
        AllowedFormats: [],
        PreferredLanguages: [],
        MinSeeders: 1,
        MinSizeBytes: null,
        MaxSizeBytes: null,
        RequiredTerms: [],
        IgnoredTerms: [],
        PreferredTerms: [],
        WeightedTerms: [],
        AutoPick: false,
        AutoRedownload: false,
        UpgradeUntilCutoff: false,
        CutoffSourceTier: BookSourceTier.Unknown,
        CutoffFormatTier: BookFormatTier.Unknown);

    [Fact]
    public async Task InvalidTvTemplateIsRejected() {
        var service = new BookAcquisitionProfileCommandService(new CapturingStore());

        var ex = await Assert.ThrowsAsync<AcquisitionConfigurationException>(
            () => service.SaveAsync(Request(EntityKind.VideoSeries, "{Series} S{Season:00}E{Episode:00}.{ext}"), CancellationToken.None));

        Assert.Equal(ApiProblemCodes.AcquisitionProfileInvalid, ex.Code);
    }

    [Fact]
    public async Task BlankMediaTemplateIsStoredAsTheKindDefault() {
        var store = new CapturingStore();
        var service = new BookAcquisitionProfileCommandService(store);

        await service.SaveAsync(Request(EntityKind.Movie, "   "), CancellationToken.None);

        Assert.Equal(MediaNamingTemplates.MovieDefault, store.LastCommand!.PathTemplate);
    }

    [Fact]
    public async Task ValidCustomMovieTemplateIsStoredTrimmed() {
        var store = new CapturingStore();
        var service = new BookAcquisitionProfileCommandService(store);

        await service.SaveAsync(Request(EntityKind.Movie, "  {Title} [{Quality}]/{Title}.{ext}  "), CancellationToken.None);

        Assert.Equal("{Title} [{Quality}]/{Title}.{ext}", store.LastCommand!.PathTemplate);
    }

    [Fact]
    public async Task BlankBookTemplateStillRequiresATemplate() {
        var service = new BookAcquisitionProfileCommandService(new CapturingStore());

        var ex = await Assert.ThrowsAsync<AcquisitionConfigurationException>(
            () => service.SaveAsync(Request(EntityKind.Book, ""), CancellationToken.None));

        Assert.Equal(ApiProblemCodes.AcquisitionProfileInvalid, ex.Code);
    }

    /// <summary>A profile store that only records the command it is handed, for asserting the resolved template.</summary>
    private sealed class CapturingStore : IBookAcquisitionProfileStore {
        public BookAcquisitionProfileSaveCommand? LastCommand { get; private set; }

        public Task<BookAcquisitionProfileView> SaveAsync(BookAcquisitionProfileSaveCommand command, CancellationToken cancellationToken) {
            LastCommand = command;
            return Task.FromResult(new BookAcquisitionProfileView(
                command.Id ?? Guid.NewGuid(), command.Kind, command.DisplayName, command.IsDefault, command.TargetLibraryRootId,
                command.PathTemplate, command.ImportMode, command.AllowedFormats, command.PreferredLanguages, command.MinSeeders,
                command.MinSizeBytes, command.MaxSizeBytes, command.RequiredTerms, command.IgnoredTerms, command.PreferredTerms,
                command.WeightedTerms, command.AutoPick, command.AutoRedownload, command.UpgradeUntilCutoff,
                command.CutoffSourceTier, command.CutoffFormatTier));
        }

        public Task<BookAcquisitionRules> GetRulesAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BookImportProfile?> GetImportProfileAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> GetAutoPickAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> GetAutoRedownloadAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> GetDownloadCategoryAsync(Guid? profileId, EntityKind kind, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<BookAcquisitionProfileView>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BookAcquisitionProfileView?> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
