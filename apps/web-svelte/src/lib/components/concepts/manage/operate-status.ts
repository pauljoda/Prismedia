import type { LedStatus } from "@prismedia/ui-svelte";
import { CONNECTION_STATUS, IDENTIFY_QUEUE_STATE, JOB_GRAPH_STATUS, JOB_RUN_STATUS } from "$lib/api/generated/codes";
import type {
  ConnectionResponse,
  DownloadQueueItemView,
  JobGraphSummary,
  JobRun,
  PluginProvider,
  RequestActivityPage,
} from "$lib/api/generated/model";
import type { IdentifyQueueItem } from "$lib/api/identify-types";
import { acquisitionStatusVisual } from "$lib/requests/acquisition-status-display";
import {
  REQUEST_ACTIVITY_GROUP,
  managedRequestActivityGroup,
  managedTrackingActivityGroup,
  transferActivityGroup,
  type RequestActivityGroup,
} from "$lib/requests/request-activity";

/** The reading a control-room tile shows: its LED, a few words of state, and whether it waits on you. */
export interface TileReading {
  led: LedStatus;
  state: string;
  urgent: boolean;
  pulse?: boolean;
}

const numberFormat = new Intl.NumberFormat();

/** Formats a count for a tile figure. */
export function figure(value: number): string {
  return numberFormat.format(value);
}

/** "1 thing" / "3 things". */
export function plural(count: number, singular: string, pluralForm = `${singular}s`): string {
  return `${numberFormat.format(count)} ${count === 1 ? singular : pluralForm}`;
}

// ── Worker & jobs ──────────────────────────────────────────────

export interface JobsSummary {
  running: number;
  queued: number;
  waiting: number;
  failedRuns: number;
}

/** Live graph lanes plus retained failed runs. */
export function summarizeJobs(graphs: readonly JobGraphSummary[], runs: readonly JobRun[]): JobsSummary {
  return {
    running: graphs.filter((graph) => graph.status === JOB_GRAPH_STATUS.running).length,
    queued: graphs.filter((graph) => graph.status === JOB_GRAPH_STATUS.queued).length,
    waiting: graphs.filter((graph) => graph.status === JOB_GRAPH_STATUS.waiting).length,
    failedRuns: runs.filter((run) => run.status === JOB_RUN_STATUS.failed).length,
  };
}

// ── Downloads ──────────────────────────────────────────────────

export interface DownloadsSummary {
  total: number;
  downloading: number;
  queued: number;
  searching: number;
  attention: number;
  failed: number;
}

/** Buckets the global download queue by the shared acquisition lifecycle tones. */
export function summarizeDownloads(rows: readonly DownloadQueueItemView[]): DownloadsSummary {
  const summary: DownloadsSummary = { total: rows.length, downloading: 0, queued: 0, searching: 0, attention: 0, failed: 0 };
  for (const row of rows) {
    const { tone } = acquisitionStatusVisual(row.status);
    if (tone === "downloading") summary.downloading += 1;
    else if (tone === "queued" || tone === "cleanup") summary.queued += 1;
    else if (tone === "searching") summary.searching += 1;
    else if (tone === "attention") summary.attention += 1;
    else if (tone === "failed") summary.failed += 1;
  }
  return summary;
}

export function downloadsReading(summary: DownloadsSummary): TileReading {
  if (summary.failed > 0) return { led: "error", state: `${summary.failed} failed`, urgent: true };
  if (summary.attention > 0) return { led: "warning", state: `${summary.attention} need you`, urgent: true };
  if (summary.downloading > 0) return { led: "phosphor", state: "Transferring", urgent: false, pulse: true };
  if (summary.total > 0) return { led: "phosphor", state: "Queued", urgent: false };
  return { led: "idle", state: "Idle", urgent: false };
}

// ── Requests ───────────────────────────────────────────────────

export type RequestActivityCounts = Record<RequestActivityGroup, number>;

/** Places each connected-source activity record in the section the Request page would show it. */
export function summarizeRequestActivity(page: RequestActivityPage | null): RequestActivityCounts {
  const counts: RequestActivityCounts = {
    [REQUEST_ACTIVITY_GROUP.attention]: 0,
    [REQUEST_ACTIVITY_GROUP.progress]: 0,
    [REQUEST_ACTIVITY_GROUP.following]: 0,
    [REQUEST_ACTIVITY_GROUP.recent]: 0,
  };
  for (const item of page?.items ?? []) {
    const group = item.transfer
      ? transferActivityGroup(item.transfer)
      : item.request
        ? managedRequestActivityGroup(item.request)
        : item.holding
          ? managedTrackingActivityGroup(item.holding)
          : null;
    if (group) counts[group] += 1;
  }
  return counts;
}

// ── Identify ───────────────────────────────────────────────────

export interface IdentifySummary {
  total: number;
  review: number;
  working: number;
  failed: number;
}

/** Mirrors the Identify workspace: results waiting on you, searches in flight, and errors. */
export function summarizeIdentify(queue: readonly IdentifyQueueItem[]): IdentifySummary {
  let review = 0;
  let working = 0;
  let failed = 0;
  for (const item of queue) {
    if (
      (item.state === IDENTIFY_QUEUE_STATE.proposal && item.proposal) ||
      (item.state === IDENTIFY_QUEUE_STATE.search && item.candidates.length > 0)
    ) {
      review += 1;
    } else if (
      item.state === IDENTIFY_QUEUE_STATE.queued ||
      item.state === IDENTIFY_QUEUE_STATE.searching ||
      item.state === IDENTIFY_QUEUE_STATE.applying
    ) {
      working += 1;
    } else if (item.state === IDENTIFY_QUEUE_STATE.error) {
      failed += 1;
    }
  }
  return { total: queue.length, review, working, failed };
}

export function identifyReading(summary: IdentifySummary): TileReading {
  if (summary.review > 0) return { led: "warning", state: `${summary.review} to review`, urgent: true };
  if (summary.failed > 0) return { led: "error", state: `${summary.failed} failed`, urgent: true };
  if (summary.working > 0) return { led: "phosphor", state: "Searching", urgent: false, pulse: true };
  return { led: "idle", state: summary.total > 0 ? "Waiting" : "Clear", urgent: false };
}

// ── Connections & plugins ─────────────────────────────────────

export interface IntegrationsSummary {
  plugins: number;
  needsCredentials: PluginProvider[];
  updates: number;
  connections: number;
  ready: number;
  broken: number;
}

/** Installed, enabled plugins and the health of every connection they back. */
export function summarizeIntegrations(
  plugins: readonly PluginProvider[],
  connections: readonly ConnectionResponse[],
): IntegrationsSummary {
  const installed = plugins.filter((plugin) => plugin.installed && plugin.enabled);
  const enabledConnections = connections.filter((connection) => connection.enabled);
  return {
    plugins: installed.length,
    needsCredentials: installed.filter((plugin) => plugin.missingAuthKeys.length > 0),
    updates: installed.filter((plugin) => plugin.updateAvailable).length,
    connections: enabledConnections.length,
    ready: enabledConnections.filter((connection) => connection.status === CONNECTION_STATUS.ready).length,
    broken: enabledConnections.filter(
      (connection) =>
        connection.status === CONNECTION_STATUS.unavailable || connection.status === CONNECTION_STATUS.identityChanged,
    ).length,
  };
}

export function integrationsReading(summary: IntegrationsSummary): TileReading {
  if (summary.broken > 0) return { led: "error", state: `${summary.broken} offline`, urgent: true };
  if (summary.needsCredentials.length > 0) return { led: "warning", state: "Needs keys", urgent: true };
  if (summary.plugins === 0) return { led: "idle", state: "None", urgent: false };
  return { led: "phosphor", state: "Healthy", urgent: false };
}

/** Human "in 6h" / "in 2d" for a future timestamp; "due" once it has passed. */
export function formatUntil(value: string | null): string | null {
  if (!value) return null;
  const minutes = Math.round((Date.parse(value) - Date.now()) / 60_000);
  if (!Number.isFinite(minutes)) return null;
  if (minutes <= 0) return "due";
  if (minutes < 60) return `in ${minutes}m`;
  const hours = Math.round(minutes / 60);
  if (hours < 48) return `in ${hours}h`;
  return `in ${Math.round(hours / 24)}d`;
}

/** "every 60m" / "every 6h" / "daily" for a minute interval. */
export function formatEvery(minutes: number): string {
  if (minutes === 1440) return "daily";
  if (minutes > 0 && minutes % 1440 === 0) return `every ${minutes / 1440}d`;
  if (minutes >= 60 && minutes % 60 === 0) return `every ${minutes / 60}h`;
  return `every ${minutes}m`;
}
