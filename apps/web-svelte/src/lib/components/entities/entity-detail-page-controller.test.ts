import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import EntityDetailPageControllerHarness, {
  type TestDetailEntity,
} from "./entity-detail-page-controller.test-harness.svelte";

function entity(title: string): TestDetailEntity {
  return {
    id: "entity-1",
    kind: "test",
    title,
    capabilities: [],
  };
}

describe("useEntityDetailPage", () => {
  it("loads an entity, publishes breadcrumbs, and refreshes when NSFW visibility changes", async () => {
    const load = vi
      .fn()
      .mockResolvedValueOnce(entity("Initial"))
      .mockResolvedValueOnce(entity("Refreshed"));

    render(EntityDetailPageControllerHarness, { props: { load } });

    await waitFor(() => expect(screen.getByTestId("load-state")).toHaveTextContent("ready"));
    expect(screen.getByTestId("entity-title")).toHaveTextContent("Initial");
    expect(screen.getByTestId("breadcrumbs")).toHaveTextContent("Entities / Initial");
    expect(load).toHaveBeenCalledTimes(1);
    expect(load.mock.calls[0][0].nsfwMode).toBe("off");

    await fireEvent.click(screen.getByRole("button", { name: "Show NSFW" }));

    await waitFor(() => expect(screen.getByTestId("entity-title")).toHaveTextContent("Refreshed"));
    expect(load).toHaveBeenCalledTimes(2);
    expect(load.mock.calls[1][0].nsfwMode).toBe("show");
  });

  it("surfaces load errors and retries through the same state machine", async () => {
    const load = vi
      .fn()
      .mockRejectedValueOnce(new Error("No connection"))
      .mockResolvedValueOnce(entity("Recovered"));

    render(EntityDetailPageControllerHarness, { props: { load } });

    await waitFor(() => expect(screen.getByTestId("load-state")).toHaveTextContent("error"));
    expect(screen.getByTestId("error-message")).toHaveTextContent("No connection");

    await fireEvent.click(screen.getByRole("button", { name: "Retry" }));

    await waitFor(() => expect(screen.getByTestId("entity-title")).toHaveTextContent("Recovered"));
    expect(screen.getByTestId("load-state")).toHaveTextContent("ready");
  });

  it("centralizes optimistic rating and root metadata persistence", async () => {
    const load = vi.fn().mockResolvedValueOnce(entity("Initial")).mockResolvedValueOnce(entity("Saved"));
    const rating = vi.fn().mockResolvedValue(entity("Initial"));
    const metadata = vi.fn().mockResolvedValue(entity("Saved"));

    render(EntityDetailPageControllerHarness, {
      props: { load, mutations: { rating, metadata } },
    });
    await waitFor(() => expect(screen.getByTestId("load-state")).toHaveTextContent("ready"));

    await fireEvent.click(screen.getByRole("button", { name: "Rate" }));
    await waitFor(() => expect(rating).toHaveBeenCalledWith("entity-1", 4));

    await fireEvent.click(screen.getByRole("button", { name: "Save metadata" }));
    await waitFor(() => expect(screen.getByTestId("entity-title")).toHaveTextContent("Saved"));
    expect(metadata).toHaveBeenCalledWith(
      "entity-1",
      expect.objectContaining({ fields: ["title"] }),
    );
    expect(load).toHaveBeenCalledTimes(2);
  });
});
