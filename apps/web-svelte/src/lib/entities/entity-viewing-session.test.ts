import { describe, expect, it } from "vitest";
import { EntityViewingSession, type EntityViewingSink } from "./entity-viewing-session";

describe("EntityViewingSession", () => {
  it("records one access and bounded viewing heartbeats", async () => {
    let now = 0;
    const accesses: Array<{ id: string; sessionId: string }> = [];
    const activity: Array<{ id: string; seconds: number }> = [];
    const sink: EntityViewingSink = {
      recordAccess: async (id, sessionId) => accesses.push({ id, sessionId }),
      recordActivity: async (id, seconds) => activity.push({ id, seconds }),
    };
    const session = new EntityViewingSession(sink, () => now);

    session.open("image-1");
    session.open("image-1");
    now = 15_000;
    session.heartbeat();
    now = 25_000;
    session.pause();
    now = 40_000;
    session.resume();
    now = 45_000;
    session.close();
    await session.flush();

    expect(accesses).toHaveLength(1);
    expect(accesses[0]?.id).toBe("image-1");
    expect(accesses[0]?.sessionId).toBeTruthy();
    expect(activity).toEqual([
      { id: "image-1", seconds: 15 },
      { id: "image-1", seconds: 10 },
      { id: "image-1", seconds: 5 },
    ]);
  });

  it("flushes the previous image before opening the next one", async () => {
    let now = 0;
    const accesses: string[] = [];
    const activity: Array<{ id: string; seconds: number }> = [];
    const session = new EntityViewingSession(
      {
        recordAccess: async (id) => accesses.push(id),
        recordActivity: async (id, seconds) => activity.push({ id, seconds }),
      },
      () => now,
    );

    session.open("image-1");
    now = 8_000;
    session.open("image-2");
    now = 12_000;
    session.close();
    await session.flush();

    expect(accesses).toEqual(["image-1", "image-2"]);
    expect(activity).toEqual([
      { id: "image-1", seconds: 8 },
      { id: "image-2", seconds: 4 },
    ]);
  });
});
