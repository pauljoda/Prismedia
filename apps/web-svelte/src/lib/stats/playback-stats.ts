import type {
  PlaybackStatisticsBucket,
  PlaybackStatisticsKindSlice,
  PlaybackStatisticsRhythmCell,
} from "$lib/api/generated/model";
import {
  entityAccentForKind,
  entityEmittedAccentForKind,
  entitySpectrumIndex,
  type EntityAccent,
} from "$lib/entities/entity-accent";
import { labelForEntityKind } from "$lib/entities/entity-codes";

const MINUTE_SECONDS = 60;
const HOUR_SECONDS = 60 * MINUTE_SECONDS;
const DAY_MS = 24 * 60 * 60 * 1000;
const DAYS_IN_WEEK = 7;
const HOURS_IN_DAY = 24;

/** Longest span the day series will zero-fill, so an all-time window cannot explode the chart. */
const MAX_SERIES_DAYS = 730;

/**
 * Integer and floating-point contract fields are serialized as `number | string`, so every read
 * of a statistics numeric goes through this normalizer instead of trusting the wire shape.
 */
export function statNumber(value: number | string | null | undefined): number {
  const parsed = Number(value ?? 0);
  return Number.isFinite(parsed) ? parsed : 0;
}

/** The caller's current offset from UTC in minutes, positive east of Greenwich. */
export function localUtcOffsetMinutes(now: Date = new Date()): number {
  return -now.getTimezoneOffset();
}

/** Formats an instant as the `YYYY-MM-DD` calendar day it falls on at the given UTC offset. */
export function localDayKey(instant: Date | string, utcOffsetMinutes: number): string {
  const date = typeof instant === "string" ? new Date(instant) : instant;
  if (Number.isNaN(date.getTime())) return "";
  return new Date(date.getTime() + utcOffsetMinutes * MINUTE_SECONDS * 1000)
    .toISOString()
    .slice(0, 10);
}

/**
 * Day-key arithmetic runs through UTC midnight so adding days can never be shifted by a daylight
 * saving transition in the viewer's zone.
 */
function shiftDayKey(dayKey: string, days: number): string {
  return new Date(Date.parse(`${dayKey}T00:00:00Z`) + days * DAY_MS).toISOString().slice(0, 10);
}

function daysBetween(fromDayKey: string, toDayKey: string): number {
  return Math.round((Date.parse(`${toDayKey}T00:00:00Z`) - Date.parse(`${fromDayKey}T00:00:00Z`)) / DAY_MS);
}

/** One calendar day of playback activity. */
export interface PlaybackDaySample {
  /** `YYYY-MM-DD` in the viewer's local time. */
  date: string;
  completedCount: number;
  skippedCount: number;
  totalEvents: number;
  watchSeconds: number;
}

/**
 * Expands sparse daily buckets into a gap-free day series so the timeline shows quiet days as
 * genuine gaps rather than closing them up.
 *
 * The series starts at the later of the requested window start and the first day with activity,
 * which keeps an all-time window anchored to real history instead of the epoch.
 */
export function buildDailySeries(
  buckets: readonly PlaybackStatisticsBucket[],
  windowFrom: string,
  windowTo: string,
  utcOffsetMinutes: number,
): PlaybackDaySample[] {
  const byDate = new Map<string, PlaybackDaySample>();
  for (const bucket of buckets) {
    const completedCount = statNumber(bucket.completedCount);
    const skippedCount = statNumber(bucket.skippedCount);
    byDate.set(bucket.date, {
      date: bucket.date,
      completedCount,
      skippedCount,
      totalEvents: completedCount + skippedCount,
      watchSeconds: statNumber(bucket.watchSeconds),
    });
  }

  const activeDays = [...byDate.keys()].sort();
  const windowStart = localDayKey(windowFrom, utcOffsetMinutes);
  const windowEnd = localDayKey(windowTo, utcOffsetMinutes);
  if (!windowEnd) return activeDays.map((date) => byDate.get(date)!);

  const firstActive = activeDays[0];
  let start = firstActive && (!windowStart || firstActive > windowStart) ? firstActive : windowStart;
  if (!start || start > windowEnd) start = windowEnd;

  const span = daysBetween(start, windowEnd);
  if (span > MAX_SERIES_DAYS) start = shiftDayKey(windowEnd, -MAX_SERIES_DAYS);

  const series: PlaybackDaySample[] = [];
  for (let cursor = start; cursor <= windowEnd; cursor = shiftDayKey(cursor, 1)) {
    series.push(
      byDate.get(cursor) ?? {
        date: cursor,
        completedCount: 0,
        skippedCount: 0,
        totalEvents: 0,
        watchSeconds: 0,
      },
    );
  }
  return series;
}

/** Consistency measures derived from a gap-free day series. */
export interface PlaybackCadence {
  activeDays: number;
  totalDays: number;
  currentStreak: number;
  longestStreak: number;
  busiestDay: PlaybackDaySample | null;
  /** Mean watch seconds across days that had activity, not across the whole window. */
  watchSecondsPerActiveDay: number;
}

/**
 * Summarizes how consistently the library gets used. The current streak counts back from the most
 * recent day in the series and tolerates a silent final day, because a window that ends mid-day
 * should not read as a broken streak.
 */
export function summarizeCadence(series: readonly PlaybackDaySample[]): PlaybackCadence {
  let activeDays = 0;
  let longestStreak = 0;
  let running = 0;
  let busiestDay: PlaybackDaySample | null = null;
  let watchSecondsTotal = 0;

  for (const day of series) {
    if (day.totalEvents > 0) {
      activeDays += 1;
      running += 1;
      watchSecondsTotal += day.watchSeconds;
      longestStreak = Math.max(longestStreak, running);
      if (!busiestDay || day.totalEvents > busiestDay.totalEvents) busiestDay = day;
    } else {
      running = 0;
    }
  }

  let currentStreak = 0;
  for (let index = series.length - 1; index >= 0; index -= 1) {
    if (series[index].totalEvents > 0) {
      currentStreak += 1;
      continue;
    }
    // Allow only the trailing partial day to be empty before the streak is considered broken.
    if (index === series.length - 1) continue;
    break;
  }

  return {
    activeDays,
    totalDays: series.length,
    currentStreak,
    longestStreak,
    busiestDay,
    watchSecondsPerActiveDay: activeDays > 0 ? watchSecondsTotal / activeDays : 0,
  };
}

/** A rolling mean over a series of values, used to lay a trend line over spiky counts. */
export function rollingAverage(values: readonly number[], window: number): number[] {
  if (window <= 1) return [...values];
  const averages: number[] = [];
  let sum = 0;
  for (let index = 0; index < values.length; index += 1) {
    sum += values[index];
    if (index >= window) sum -= values[index - window];
    averages.push(sum / Math.min(index + 1, window));
  }
  return averages;
}

/** A run of consecutive days collapsed into one plotted column. */
export interface PlaybackSpanSample {
  /** First day in the span; also the span's stable identity. */
  startDate: string;
  endDate: string;
  dayCount: number;
  completedCount: number;
  skippedCount: number;
  totalEvents: number;
  watchSeconds: number;
}

/**
 * Groups a day series into fixed-size spans. A year of days cannot be drawn one column per day on
 * a phone without aliasing into noise, so the caller reduces granularity to fit its plot width;
 * a `groupSize` of 1 returns one span per day unchanged.
 */
export function aggregateDaySeries(
  series: readonly PlaybackDaySample[],
  groupSize: number,
): PlaybackSpanSample[] {
  const size = Math.max(1, Math.floor(groupSize));
  const spans: PlaybackSpanSample[] = [];

  for (let start = 0; start < series.length; start += size) {
    const group = series.slice(start, start + size);
    spans.push({
      startDate: group[0].date,
      endDate: group[group.length - 1].date,
      dayCount: group.length,
      completedCount: group.reduce((sum, day) => sum + day.completedCount, 0),
      skippedCount: group.reduce((sum, day) => sum + day.skippedCount, 0),
      totalEvents: group.reduce((sum, day) => sum + day.totalEvents, 0),
      watchSeconds: group.reduce((sum, day) => sum + day.watchSeconds, 0),
    });
  }

  return spans;
}

/** One weekday/hour cell of the rhythm grid. */
export interface PlaybackRhythmCell {
  dayOfWeek: number;
  hour: number;
  totalEvents: number;
  watchSeconds: number;
  /** `totalEvents` relative to the busiest cell, 0 through 1. */
  intensity: number;
}

/** Dense weekday x hour view of when playback happens. */
export interface PlaybackRhythm {
  /** Row-major `[dayOfWeek][hour]` grid, always 7 x 24. */
  cells: PlaybackRhythmCell[][];
  maxCellEvents: number;
  totalEvents: number;
  byHour: number[];
  byDayOfWeek: number[];
  peak: PlaybackRhythmCell | null;
}

/** Expands sparse rhythm cells into the full 7 x 24 grid the heatmap renders. */
export function buildRhythm(cells: readonly PlaybackStatisticsRhythmCell[]): PlaybackRhythm {
  const grid: PlaybackRhythmCell[][] = Array.from({ length: DAYS_IN_WEEK }, (_, dayOfWeek) =>
    Array.from({ length: HOURS_IN_DAY }, (_, hour) => ({
      dayOfWeek,
      hour,
      totalEvents: 0,
      watchSeconds: 0,
      intensity: 0,
    })),
  );

  let totalEvents = 0;
  let maxCellEvents = 0;
  for (const cell of cells) {
    const dayOfWeek = statNumber(cell.dayOfWeek);
    const hour = statNumber(cell.hour);
    if (dayOfWeek < 0 || dayOfWeek >= DAYS_IN_WEEK || hour < 0 || hour >= HOURS_IN_DAY) continue;

    const target = grid[dayOfWeek][hour];
    target.totalEvents = statNumber(cell.completedCount) + statNumber(cell.skippedCount);
    target.watchSeconds = statNumber(cell.watchSeconds);
    totalEvents += target.totalEvents;
    maxCellEvents = Math.max(maxCellEvents, target.totalEvents);
  }

  const byHour = Array.from({ length: HOURS_IN_DAY }, () => 0);
  const byDayOfWeek = Array.from({ length: DAYS_IN_WEEK }, () => 0);
  let peak: PlaybackRhythmCell | null = null;
  for (const row of grid) {
    for (const cell of row) {
      cell.intensity = maxCellEvents > 0 ? cell.totalEvents / maxCellEvents : 0;
      byHour[cell.hour] += cell.totalEvents;
      byDayOfWeek[cell.dayOfWeek] += cell.totalEvents;
      if (cell.totalEvents > 0 && (!peak || cell.totalEvents > peak.totalEvents)) peak = cell;
    }
  }

  return { cells: grid, maxCellEvents, totalEvents, byHour, byDayOfWeek, peak };
}

/** One entity family's band in the prism dispersion. */
export interface PlaybackDispersionBand {
  kind: string;
  label: string;
  /** Muted material pair, for the legend rail and any other persistent chrome. */
  accent: EntityAccent;
  /** Full brand pair, for the dispersed light itself. */
  emitted: EntityAccent;
  totalEvents: number;
  completedCount: number;
  skippedCount: number;
  distinctEntityCount: number;
  watchSeconds: number;
  /** Share of the window's events, 0 through 1. */
  share: number;
}

/**
 * Turns the family breakdown into dispersion bands ordered along the prism spectrum, so the
 * chart reads as one beam of light separating rather than as a sorted bar chart.
 */
export function buildDispersion(
  slices: readonly PlaybackStatisticsKindSlice[],
): PlaybackDispersionBand[] {
  const bands = slices
    .map((slice) => ({
      kind: slice.kind as string,
      label: labelForEntityKind(slice.kind),
      accent: entityAccentForKind(slice.kind),
      emitted: entityEmittedAccentForKind(slice.kind),
      totalEvents: statNumber(slice.totalEvents),
      completedCount: statNumber(slice.completedCount),
      skippedCount: statNumber(slice.skippedCount),
      distinctEntityCount: statNumber(slice.distinctEntityCount),
      watchSeconds: statNumber(slice.watchSeconds),
      share: 0,
    }))
    .filter((band) => band.totalEvents > 0);

  const total = bands.reduce((sum, band) => sum + band.totalEvents, 0);
  for (const band of bands) band.share = total > 0 ? band.totalEvents / total : 0;

  return bands.sort(
    (left, right) =>
      entitySpectrumIndex(left.kind) - entitySpectrumIndex(right.kind) ||
      left.label.localeCompare(right.label),
  );
}

/**
 * Formats accumulated playback time for a headline figure. Hours stay the unit well past a day
 * because "301h" of viewing reads more naturally for a media library than "12d 13h".
 */
export function formatWatchDuration(seconds: number): string {
  const total = Math.max(0, Math.round(seconds));
  if (total < MINUTE_SECONDS) return `${total}s`;
  if (total < HOUR_SECONDS) return `${Math.round(total / MINUTE_SECONDS)}m`;

  const hours = Math.floor(total / HOUR_SECONDS);
  const minutes = Math.round((total % HOUR_SECONDS) / MINUTE_SECONDS);
  if (hours >= 100) return `${hours.toLocaleString()}h`;
  if (minutes === 0) return `${hours}h`;
  return `${hours}h ${minutes}m`;
}

/** Formats an hour of the day as a short clock label such as `9a` or `10p`. */
export function formatHourLabel(hour: number): string {
  const normalized = ((Math.round(hour) % HOURS_IN_DAY) + HOURS_IN_DAY) % HOURS_IN_DAY;
  const suffix = normalized < 12 ? "a" : "p";
  const clock = normalized % 12 === 0 ? 12 : normalized % 12;
  return `${clock}${suffix}`;
}

/** Share of events that finished rather than being abandoned, 0 through 1. */
export function completionRate(completedCount: number, skippedCount: number): number {
  const total = completedCount + skippedCount;
  return total > 0 ? completedCount / total : 0;
}

/**
 * Formats a `YYYY-MM-DD` day key using the viewer's locale. The parts are fed to a local
 * `Date` rather than parsed as an instant, so the label can never slip to the neighbouring day.
 */
export function formatDayKey(dayKey: string, options: Intl.DateTimeFormatOptions): string {
  const [year, month, day] = dayKey.split("-").map(Number);
  if (!year || !month || !day) return dayKey;
  return new Intl.DateTimeFormat(undefined, options).format(new Date(year, month - 1, day));
}

/** Short day label such as `Jul 12`. */
export function formatDayShort(dayKey: string): string {
  return formatDayKey(dayKey, { month: "short", day: "numeric" });
}

/** Full day label such as `Sunday, July 12, 2026`. */
export function formatDayLong(dayKey: string): string {
  return formatDayKey(dayKey, { weekday: "long", month: "long", day: "numeric", year: "numeric" });
}

/** Names a plotted column: the full day when it is one day, otherwise the range it covers. */
export function formatSpanLabel(span: PlaybackSpanSample): string {
  if (span.dayCount <= 1) return formatDayLong(span.startDate);
  return `${formatDayShort(span.startDate)} – ${formatDayShort(span.endDate)}`;
}

/** The locale's abbreviated weekday names, Sunday first, for rhythm row labels. */
export function weekdayLabels(): string[] {
  const formatter = new Intl.DateTimeFormat(undefined, { weekday: "short" });
  // 2026-02-01 is a Sunday, which anchors the list to the grid's Sunday-first row order.
  return Array.from({ length: DAYS_IN_WEEK }, (_, index) =>
    formatter.format(new Date(2026, 1, 1 + index)),
  );
}

/**
 * Steps a chart maximum lands on. Coarser sets (1, 2, 5, 10) waste up to half the plot height —
 * a peak of 105 would push the axis to 200 — so the ladder is deliberately fine-grained, and
 * every step is even so the midpoint gridline still labels as a whole number.
 */
const AXIS_STEPS = [1, 1.2, 1.6, 2, 2.4, 3, 4, 5, 6, 8, 10];

/** Smallest axis maximum, below which the zero, mid, and top labels would collide. */
const MIN_AXIS_MAX = 2;

/**
 * Rounds a chart maximum up to the next readable gridline value so axis labels land on round
 * numbers without stranding the data in the bottom of the plot.
 */
export function niceAxisMax(value: number): number {
  if (!Number.isFinite(value) || value <= 0) return MIN_AXIS_MAX;

  const magnitude = 10 ** Math.floor(Math.log10(value));
  const normalized = value / magnitude;
  const step = AXIS_STEPS.find((candidate) => candidate >= normalized) ?? 10;
  return Math.max(MIN_AXIS_MAX, step * magnitude);
}
