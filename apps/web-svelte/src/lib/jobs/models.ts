/** Legacy diagnostic grouping only; executable scheduling is represented by backend graphs. */
export type QueueName = string;

export type JobStatus =
  | "waiting"
  | "active"
  | "completed"
  | "failed"
  | "dismissed"
  | "delayed"
  | "paused";

export type JobTriggerKind =
  | "manual"
  | "schedule"
  | "library-scan"
  | "gallery-scan"
  | "book-scan"
  | "audio-scan"
  | "system";

export type JobKind = "standard" | "force-rebuild";

export interface QueueSummary {
  name: QueueName;
  label: string;
  description: string;
  status: "idle" | "active" | "warning";
  active: number;
  waiting: number;
  delayed: number;
  backlog: number;
  completed: number;
  failed: number;
}

export interface JobRun {
  id: string;
  jobType: string;
  jobLabel: string;
  jobDescription: string;
  queueName: QueueName;
  queueLabel: string;
  status: JobStatus;
  targetType: string | null;
  targetId: string | null;
  targetLabel: string | null;
  triggeredBy: JobTriggerKind | null;
  triggerLabel: string | null;
  jobKind: JobKind | null;
  progress: number;
  attempts: number;
  statusMessage: string | null;
  error: string | null;
  startedAt: string | null;
  finishedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface JobRunGroup {
  key: string;
  jobType: string;
  jobLabel: string;
  jobDescription: string;
  queueName: QueueName;
  queueLabel: string;
  jobs: JobRun[];
  activeCount: number;
  waitingCount: number;
  totalCount: number;
}

export interface FailedJobGroup {
  fingerprint: string;
  representative: JobRun;
  jobs: JobRun[];
  count: number;
  firstFailedAt: string | null;
  lastFailedAt: string | null;
}

export interface JobsDashboard {
  queues: QueueSummary[];
  activeJobs: JobRun[];
  failedJobs: JobRun[];
  completedJobs: JobRun[];
  recentJobs: JobRun[];
  lastScanAt: string | null;
  schedule: {
    enabled: boolean;
    intervalMinutes: number;
  };
}
