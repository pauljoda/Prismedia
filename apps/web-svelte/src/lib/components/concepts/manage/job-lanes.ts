import { JOB_RUN_STATUS, JOB_TYPE, type JobTypeCode } from "$lib/api/generated/codes";
import type { JobQueueCountDto, JobRun } from "$lib/api/generated/model";
import { jobLabelForType } from "$lib/jobs/jobs-dashboard";
import { bucketJobActivity, jobRunMoment, type ActivityBucket } from "./job-activity";

/** View grouping for lanes. These are presentation sections, not server state. */
export const JOB_LANE_SECTION = {
  scans: "scans",
  media: "media",
  metadata: "metadata",
  acquisition: "acquisition",
  upkeep: "upkeep",
} as const;

export type JobLaneSection = (typeof JOB_LANE_SECTION)[keyof typeof JOB_LANE_SECTION];

export const JOB_LANE_SECTIONS: ReadonlyArray<{ id: JobLaneSection; title: string }> = [
  { id: JOB_LANE_SECTION.scans, title: "Scans" },
  { id: JOB_LANE_SECTION.media, title: "Media processing" },
  { id: JOB_LANE_SECTION.metadata, title: "Metadata & Identify" },
  { id: JOB_LANE_SECTION.acquisition, title: "Acquisition" },
  { id: JOB_LANE_SECTION.upkeep, title: "Upkeep" },
]

const SECTION_BY_TYPE = {
  [JOB_TYPE.scanLibrary]: JOB_LANE_SECTION.scans,
  [JOB_TYPE.scanGallery]: JOB_LANE_SECTION.scans,
  [JOB_TYPE.scanBook]: JOB_LANE_SECTION.scans,
  [JOB_TYPE.scanComic]: JOB_LANE_SECTION.scans,
  [JOB_TYPE.scanAudio]: JOB_LANE_SECTION.scans,
  [JOB_TYPE.probeVideo]: JOB_LANE_SECTION.media,
  [JOB_TYPE.probeAudio]: JOB_LANE_SECTION.media,
  [JOB_TYPE.fingerprintVideo]: JOB_LANE_SECTION.media,
  [JOB_TYPE.fingerprintImage]: JOB_LANE_SECTION.media,
  [JOB_TYPE.fingerprintAudio]: JOB_LANE_SECTION.media,
  [JOB_TYPE.generatePreview]: JOB_LANE_SECTION.media,
  [JOB_TYPE.generateTrickplay]: JOB_LANE_SECTION.media,
  [JOB_TYPE.generateImageThumbnail]: JOB_LANE_SECTION.media,
  [JOB_TYPE.generateGridThumbnail]: JOB_LANE_SECTION.media,
  [JOB_TYPE.gridThumbnailSweep]: JOB_LANE_SECTION.media,
  [JOB_TYPE.generateBookCoverThumbnail]: JOB_LANE_SECTION.media,
  [JOB_TYPE.mapBookChapters]: JOB_LANE_SECTION.media,
  [JOB_TYPE.generateAudioWaveform]: JOB_LANE_SECTION.media,
  [JOB_TYPE.extractSubtitles]: JOB_LANE_SECTION.media,
  [JOB_TYPE.acquireSubtitles]: JOB_LANE_SECTION.media,
  [JOB_TYPE.acquireSubtitle]: JOB_LANE_SECTION.media,
  [JOB_TYPE.applyVideoSidecarMetadata]: JOB_LANE_SECTION.metadata,
  [JOB_TYPE.reconcileEntity]: JOB_LANE_SECTION.metadata,
  [JOB_TYPE.importMetadata]: JOB_LANE_SECTION.metadata,
  [JOB_TYPE.refreshEntity]: JOB_LANE_SECTION.metadata,
  [JOB_TYPE.identifySearch]: JOB_LANE_SECTION.metadata,
  [JOB_TYPE.identifyProviderCall]: JOB_LANE_SECTION.metadata,
  [JOB_TYPE.identifyApply]: JOB_LANE_SECTION.metadata,
  [JOB_TYPE.bulkIdentify]: JOB_LANE_SECTION.metadata,
  [JOB_TYPE.autoIdentify]: JOB_LANE_SECTION.metadata,
  [JOB_TYPE.identifyCascade]: JOB_LANE_SECTION.metadata,
  [JOB_TYPE.integrationTransfer]: JOB_LANE_SECTION.acquisition,
  [JOB_TYPE.managedLibraryReconcile]: JOB_LANE_SECTION.acquisition,
  [JOB_TYPE.managedControl]: JOB_LANE_SECTION.acquisition,
  [JOB_TYPE.acquisitionSearch]: JOB_LANE_SECTION.acquisition,
  [JOB_TYPE.acquisitionMonitor]: JOB_LANE_SECTION.acquisition,
  [JOB_TYPE.acquisitionImport]: JOB_LANE_SECTION.acquisition,
  [JOB_TYPE.acquisitionFinalize]: JOB_LANE_SECTION.acquisition,
  [JOB_TYPE.acquisitionFailedHandle]: JOB_LANE_SECTION.acquisition,
  [JOB_TYPE.monitoredSearch]: JOB_LANE_SECTION.acquisition,
  [JOB_TYPE.acquisitionUpgradeReplace]: JOB_LANE_SECTION.acquisition,
  [JOB_TYPE.acquisitionEnrich]: JOB_LANE_SECTION.acquisition,
  [JOB_TYPE.requestAcquisitionFanout]: JOB_LANE_SECTION.acquisition,
  [JOB_TYPE.noop]: JOB_LANE_SECTION.upkeep,
  [JOB_TYPE.refreshCollection]: JOB_LANE_SECTION.upkeep,
  [JOB_TYPE.libraryMaintenance]: JOB_LANE_SECTION.upkeep,
  [JOB_TYPE.databaseBackup]: JOB_LANE_SECTION.upkeep,
  [JOB_TYPE.updatePlugins]: JOB_LANE_SECTION.upkeep,
  [JOB_TYPE.recycleBinCleanup]: JOB_LANE_SECTION.upkeep,
} as const satisfies Record<JobTypeCode, JobLaneSection>;

function sectionForType(type: string): JobLaneSection {
  return (SECTION_BY_TYPE as Record<string, JobLaneSection>)[type] ?? JOB_LANE_SECTION.upkeep;
}

/** Scan job types, used to find the most recent library scan. */
export const SCAN_JOB_TYPES: ReadonlySet<string> = new Set(
  Object.entries(SECTION_BY_TYPE)
    .filter(([, section]) => section === JOB_LANE_SECTION.scans)
    .map(([type]) => type),
);

/** Retained run totals for one job type. */
export interface LaneCounts {
  running: number;
  queued: number;
  failed: number;
  completed: number;
  cancelled: number;
}

/** Every run of one job type folded into a single lane. */
export interface JobLane {
  type: string;
  label: string;
  section: JobLaneSection;
  counts: LaneCounts;
  /** Retained runs of this type, newest first. */
  runs: JobRun[];
  lastRunAt: string | null;
  buckets: ActivityBucket[];
}

function emptyCounts(): LaneCounts {
  return { running: 0, queued: 0, failed: 0, completed: 0, cancelled: 0 };
}

function addCount(counts: LaneCounts, status: string, amount: number) {
  if (status === JOB_RUN_STATUS.running) counts.running += amount;
  else if (status === JOB_RUN_STATUS.queued) counts.queued += amount;
  else if (status === JOB_RUN_STATUS.failed) counts.failed += amount;
  else if (status === JOB_RUN_STATUS.completed) counts.completed += amount;
  else if (status === JOB_RUN_STATUS.cancelled) counts.cancelled += amount;
}

/**
 * Folds the retained run list and per-type totals into one lane per job type. Totals come from the
 * server's per-type counts (all retained runs); the run list and activity strip come from the
 * bounded recent window the jobs API returns.
 */
export function buildJobLanes(runs: readonly JobRun[], counts: readonly JobQueueCountDto[], now: number): JobLane[] {
  const runsByType = new Map<string, JobRun[]>();
  for (const run of runs) {
    const list = runsByType.get(run.type);
    if (list) list.push(run);
    else runsByType.set(run.type, [run]);
  }

  const countsByType = new Map<string, LaneCounts>();
  for (const entry of counts) {
    const value = Number(entry.count);
    if (!Number.isFinite(value)) continue;
    const laneCounts = countsByType.get(entry.type) ?? emptyCounts();
    addCount(laneCounts, entry.status, value);
    countsByType.set(entry.type, laneCounts);
  }

  const types = new Set([...runsByType.keys(), ...countsByType.keys()]);
  const lanes: JobLane[] = [];
  for (const type of types) {
    const typeRuns = [...(runsByType.get(type) ?? [])].sort(
      (left, right) => Date.parse(jobRunMoment(right)) - Date.parse(jobRunMoment(left)),
    );
    let laneCounts = countsByType.get(type);
    if (!laneCounts) {
      laneCounts = emptyCounts();
      for (const run of typeRuns) addCount(laneCounts, run.status, 1);
    }
    lanes.push({
      type,
      label: jobLabelForType(type),
      section: sectionForType(type),
      counts: laneCounts,
      runs: typeRuns,
      lastRunAt: typeRuns[0] ? jobRunMoment(typeRuns[0]) : null,
      buckets: bucketJobActivity(typeRuns, now),
    });
  }

  return lanes.sort(
    (left, right) =>
      right.counts.running - left.counts.running ||
      right.counts.queued - left.counts.queued ||
      Number(right.counts.failed > 0) - Number(left.counts.failed > 0) ||
      (Date.parse(right.lastRunAt ?? "") || 0) - (Date.parse(left.lastRunAt ?? "") || 0) ||
      left.label.localeCompare(right.label),
  );
}

/** A lane with nothing live and no runs in the recent window folds into the section's quiet list. */
export function isQuietLane(lane: JobLane): boolean {
  return lane.runs.length === 0 && lane.counts.running === 0 && lane.counts.queued === 0;
}

/** Sums every lane's retained totals. */
export function totalLaneCounts(lanes: readonly JobLane[]): LaneCounts {
  const total = emptyCounts();
  for (const lane of lanes) {
    total.running += lane.counts.running;
    total.queued += lane.counts.queued;
    total.failed += lane.counts.failed;
    total.completed += lane.counts.completed;
    total.cancelled += lane.counts.cancelled;
  }
  return total;
}
