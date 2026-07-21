import { ACQUISITION_HISTORY_EVENT } from "$lib/api/generated/codes";
import type {
  AcquisitionBlocklistEntry,
  AcquisitionHistoryView,
} from "$lib/api/generated/model";

export interface AcquisitionBlocklistGroup {
  key: string;
  title: string;
  entityId: string | null;
  kind: string | null;
  newestAt: string;
  entries: AcquisitionBlocklistEntry[];
}

interface BlocklistAssociation {
  entityId: string | null;
  kind: string | null;
  title: string;
}

/**
 * Groups release blocklist entries around the library work they affected. Current API rows retain the
 * acquisition id; durable history supplies the denormalized entity title when that acquisition is later
 * removed. Entries with no surviving association stay together in one honest fallback group.
 */
export function groupAcquisitionBlocklist(
  entries: AcquisitionBlocklistEntry[],
  history: AcquisitionHistoryView[],
): AcquisitionBlocklistGroup[] {
  const groups = new Map<string, AcquisitionBlocklistGroup>();

  for (const entry of entries) {
    const association = findAssociation(entry, history);
    const key = association.entityId
      ? `entity:${association.entityId}`
      : association.title === "Unlinked releases"
        ? "unlinked"
        : `work:${normalize(association.kind)}:${normalize(association.title)}`;
    const group = groups.get(key) ?? {
      key,
      title: association.title,
      entityId: association.entityId,
      kind: association.kind,
      newestAt: entry.createdAt,
      entries: [],
    };
    group.entries.push(entry);
    if (Date.parse(entry.createdAt) > Date.parse(group.newestAt)) group.newestAt = entry.createdAt;
    groups.set(key, group);
  }

  return [...groups.values()].sort((left, right) =>
    Date.parse(right.newestAt) - Date.parse(left.newestAt),
  );
}

/** Returns the live blocklist row represented by one durable Blocklisted history event. */
export function findBlocklistEntryForHistory(
  event: AcquisitionHistoryView,
  entries: AcquisitionBlocklistEntry[],
): AcquisitionBlocklistEntry | null {
  if (event.event !== ACQUISITION_HISTORY_EVENT.blocklisted) return null;

  const exactRelease = entries.find((entry) => releaseMatchesHistory(entry, event));
  const sameAcquisition = event.acquisitionId && !event.releaseTitle
    ? entries.find((entry) => entry.acquisitionId === event.acquisitionId)
    : undefined;
  return exactRelease
    ?? sameAcquisition
    ?? null;
}

/** Case-insensitive search over both work context and release-level diagnostics. */
export function blocklistGroupMatches(group: AcquisitionBlocklistGroup, query: string): boolean {
  const needle = normalize(query);
  if (!needle) return true;
  return [
    group.title,
    group.kind,
    ...group.entries.flatMap((entry) => [
      entry.title,
      entry.indexerName,
      entry.infoHash,
      entry.message,
      entry.reason,
    ]),
  ].some((value) => normalize(value).includes(needle));
}

function findAssociation(
  entry: AcquisitionBlocklistEntry,
  history: AcquisitionHistoryView[],
): BlocklistAssociation {
  if (entry.entityTitle) {
    return {
      entityId: entry.entityId ?? null,
      kind: entry.entityKind ?? null,
      title: entry.entityTitle,
    };
  }

  const event = entry.acquisitionId
    ? history.find((candidate) =>
        candidate.event === ACQUISITION_HISTORY_EVENT.blocklisted
        && candidate.acquisitionId === entry.acquisitionId)
    : history.find((candidate) =>
        candidate.event === ACQUISITION_HISTORY_EVENT.blocklisted
        && releaseMatchesHistory(entry, candidate));

  return event
    ? { entityId: event.entityId ?? null, kind: event.kind, title: event.title }
    : { entityId: null, kind: null, title: "Unlinked releases" };
}

function releaseMatchesHistory(
  entry: AcquisitionBlocklistEntry,
  event: AcquisitionHistoryView,
): boolean {
  return normalize(entry.title) === normalize(event.releaseTitle)
    && normalize(entry.indexerName) === normalize(event.indexerName);
}

function normalize(value: string | null | undefined): string {
  return value?.trim().toLocaleLowerCase() ?? "";
}
