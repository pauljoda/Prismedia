using Prismedia.Application.Acquisition;
using Prismedia.Domain.Entities;

namespace Prismedia.Infrastructure.Acquisition;

public sealed partial class SlskdIndexerClient {
    private static bool IsPublication(EntityKind kind) =>
        kind is EntityKind.Book or EntityKind.ComicSeries or EntityKind.ComicVolume or EntityKind.ComicInstallment;

    private static IReadOnlyList<IndexerRelease> PublicationReleases(IndexerQuery query, Guid searchId,
        IEnumerable<SlskdSearchResponse> peers) {
        var audiobook = query.Kind == EntityKind.Book && query.BookRendition == BookRendition.Audiobook;
        var extensions = query.Kind != EntityKind.Book ? ImportPlanBuilder.ComicArchiveExtensions
            : audiobook ? ImportPlanBuilder.AudiobookExtensions : ImportPlanBuilder.PrimaryBookExtensions;
        var releases = new List<IndexerRelease>();
        foreach (var peer in peers.Where(peer => peer is not null && !string.IsNullOrWhiteSpace(peer.Username) && peer.Files is not null)
            .OrderByDescending(peer => peer.HasFreeUploadSlot).ThenByDescending(peer => peer.UploadSpeed).ThenBy(peer => peer.QueueLength)) {
            // Conflicting sizes cannot identify one remote file. Exact duplicates must not inflate a batch.
            var files = peer.Files.Where(file => file is not null && !string.IsNullOrWhiteSpace(file.Filename) && file.Size > 0
                    && extensions.Contains(Path.GetExtension(file.Filename)))
                .GroupBy(file => file.Filename, StringComparer.Ordinal)
                .Where(group => group.Select(file => file.Size).Distinct().Count() == 1)
                .Select(group => group.First()).ToArray();
            if (!audiobook) {
                releases.AddRange(files.Select(file => Release(searchId, peer, file.Filename, [file])));
                continue;
            }
            // M4B files represent independently selectable books. MP3/M4A chapters stay within their
            // exact peer folder and format, so alternate encodings and adjacent books are not combined.
            releases.AddRange(files.Where(IsWholeAudiobook).Select(file => Release(searchId, peer, file.Filename, [file])));
            foreach (var directory in files.Where(file => !IsWholeAudiobook(file))
                .GroupBy(file => DirectoryOf(file.Filename), StringComparer.Ordinal)) {
                if (string.IsNullOrWhiteSpace(directory.Key)) {
                    releases.AddRange(directory.Select(file => Release(searchId, peer, file.Filename, [file])));
                    continue;
                }
                foreach (var format in directory.GroupBy(file => Path.GetExtension(file.Filename), StringComparer.OrdinalIgnoreCase))
                    releases.Add(Release(searchId, peer, directory.Key,
                        format.OrderBy(file => file.Filename, StringComparer.OrdinalIgnoreCase).ToArray()));
            }
        }
        return releases;
    }

    private static bool IsWholeAudiobook(SlskdSearchFile file) =>
        Path.GetExtension(file.Filename).Equals(".m4b", StringComparison.OrdinalIgnoreCase);
}
