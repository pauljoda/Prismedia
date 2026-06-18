const CHANNEL_NAME = "prismedia-audio-playback";
const LEASE_KEY = "prismedia:audio-active-tab";
const LEASE_TTL_MS = 6000;
const HEARTBEAT_MS = 2000;

type Lease = {
  tabId: string;
  updatedAt: number;
};

type Message = {
  type: "claim" | "release";
  tabId: string;
  updatedAt: number;
};

export interface AudioTabCoordinator {
  readonly tabId: string;
  claimPlayback: (options?: { steal?: boolean }) => boolean;
  releasePlayback: () => void;
  onDisplaced: (listener: () => void) => () => void;
  destroy: () => void;
}

function now(): number {
  return Date.now();
}

function parseLease(value: string | null): Lease | null {
  if (!value) return null;
  try {
    const parsed: unknown = JSON.parse(value);
    if (!parsed || typeof parsed !== "object") return null;
    const record = parsed as Record<string, unknown>;
    return typeof record.tabId === "string" && typeof record.updatedAt === "number"
      ? { tabId: record.tabId, updatedAt: record.updatedAt }
      : null;
  } catch {
    return null;
  }
}

function isFresh(lease: Lease | null, at = now()): lease is Lease {
  return Boolean(lease && at - lease.updatedAt < LEASE_TTL_MS);
}

function randomTabId(): string {
  return typeof crypto !== "undefined" && "randomUUID" in crypto
    ? crypto.randomUUID()
    : `${now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

export function createAudioTabCoordinator(): AudioTabCoordinator {
  const tabId = randomTabId();
  const listeners = new Set<() => void>();
  const channel = typeof BroadcastChannel === "undefined" ? null : new BroadcastChannel(CHANNEL_NAME);
  let heartbeat: ReturnType<typeof setInterval> | null = null;
  let destroyed = false;

  function readLease(): Lease | null {
    try {
      return parseLease(localStorage.getItem(LEASE_KEY));
    } catch {
      return null;
    }
  }

  function writeLease() {
    const lease: Lease = { tabId, updatedAt: now() };
    try {
      localStorage.setItem(LEASE_KEY, JSON.stringify(lease));
    } catch {
      // Storage can be unavailable in private contexts; BroadcastChannel still coordinates live tabs.
    }
    channel?.postMessage({ type: "claim", tabId, updatedAt: lease.updatedAt } satisfies Message);
  }

  function clearLease() {
    try {
      const lease = readLease();
      if (!lease || lease.tabId === tabId) {
        localStorage.removeItem(LEASE_KEY);
      }
    } catch {
      // Ignore storage access errors.
    }
    channel?.postMessage({ type: "release", tabId, updatedAt: now() } satisfies Message);
  }

  function startHeartbeat() {
    if (heartbeat) return;
    heartbeat = setInterval(writeLease, HEARTBEAT_MS);
  }

  function stopHeartbeat() {
    if (!heartbeat) return;
    clearInterval(heartbeat);
    heartbeat = null;
  }

  function displaceSelf() {
    stopHeartbeat();
    for (const listener of listeners) listener();
  }

  channel?.addEventListener("message", (event: MessageEvent<Message>) => {
    const message = event.data;
    if (!message || message.tabId === tabId) return;
    if (message.type === "claim") displaceSelf();
  });

  const handleStorage = (event: StorageEvent) => {
    if (event.key !== LEASE_KEY) return;
    const lease = parseLease(event.newValue);
    if (isFresh(lease) && lease.tabId !== tabId) displaceSelf();
  };
  window.addEventListener("storage", handleStorage);

  return {
    tabId,
    claimPlayback: (options?: { steal?: boolean }) => {
      if (destroyed) return false;
      const lease = readLease();
      if (!options?.steal && isFresh(lease) && lease.tabId !== tabId) {
        return false;
      }

      writeLease();
      startHeartbeat();
      return true;
    },
    releasePlayback: () => {
      stopHeartbeat();
      clearLease();
    },
    onDisplaced: (listener: () => void) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    destroy: () => {
      destroyed = true;
      stopHeartbeat();
      clearLease();
      window.removeEventListener("storage", handleStorage);
      channel?.close();
      listeners.clear();
    },
  };
}
