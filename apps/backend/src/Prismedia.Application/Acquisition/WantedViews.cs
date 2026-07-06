using Prismedia.Domain.Entities;

namespace Prismedia.Application.Acquisition;

/// <summary>
/// One row of a Wanted list (Missing or Cutoff Unmet): a monitor whose wanted item is not yet in hand
/// (missing) or is in hand but below its kind's cutoff (cutoff unmet). Carries just enough for a paged
/// list surface — the monitor/acquisition identity for the row's bulk actions, the entity link (when the
/// monitor is bound to a library entity), the two statuses, the search cadence fields, and the owned →
/// cutoff quality strings the cutoff-unmet view renders. Denormalized and read-only: built by the store's
/// list methods, never persisted.
/// </summary>
/// <param name="MonitorId">The monitor backing this row; the target of Search-now / Unmonitor bulk actions.</param>
/// <param name="AcquisitionId">The acquisition the monitor keeps alive, or null when it was hard-deleted.</param>
/// <param name="EntityId">The library entity this wanted item resolves to, or null; when set the row links to its detail page.</param>
/// <param name="Kind">The media kind, for the row's kind badge and the kind filter.</param>
/// <param name="Title">Denormalized title of the wanted item.</param>
/// <param name="MonitorStatus">The monitor's current status.</param>
/// <param name="AcquisitionStatus">The linked acquisition's status, or null when the acquisition is gone.</param>
/// <param name="LastSearchedAt">When the monitor was last re-searched; null means never.</param>
/// <param name="NextSearchAt">
/// When the monitor is next due for a re-search, computed from <see cref="LastSearchedAt"/> plus the same
/// exponential backoff the due sweep uses. Null when it has never been searched (due immediately).
/// </param>
/// <param name="OwnedQuality">
/// The owned quality string in the kind's vocabulary — a book's "source/format" tier codes joined, or a
/// media ladder code — for the cutoff-unmet view; null on the missing view (nothing is owned).
/// </param>
/// <param name="CutoffQuality">The kind's cutoff quality, in the same vocabulary as <see cref="OwnedQuality"/>; null on the missing view.</param>
/// <param name="BarrenSearches">Consecutive fruitless searches so far, surfaced so the user sees a stuck item.</param>
/// <param name="PosterUrl">The wanted item's cover art (the acquisition's captured poster), for the list's thumbnail; null when none was captured.</param>
public sealed record WantedListItem(
    Guid MonitorId,
    Guid? AcquisitionId,
    Guid? EntityId,
    EntityKind Kind,
    string Title,
    MonitorStatus MonitorStatus,
    AcquisitionStatus? AcquisitionStatus,
    DateTimeOffset? LastSearchedAt,
    DateTimeOffset? NextSearchAt,
    string? OwnedQuality,
    string? CutoffQuality,
    int BarrenSearches,
    string? PosterUrl = null,
    string? Author = null);

/// <summary>
/// One page of a Wanted list: the page's items plus the total count of matching rows, so the surface can
/// render pagination controls. See the store's list methods for how <see cref="Total"/> is computed for
/// each list (an exact SQL count for Missing; an upper-bound SQL count for Cutoff Unmet — the imported+active
/// set, before the in-memory cutoff refinement).
/// </summary>
public sealed record WantedPage(IReadOnlyList<WantedListItem> Items, int Total);
