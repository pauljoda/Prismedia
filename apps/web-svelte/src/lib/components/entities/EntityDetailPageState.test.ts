import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import EntityDetailPageStateHarness from "./EntityDetailPageState.test-harness.svelte";

describe("EntityDetailPageState", () => {
  it("renders the shared entity skeleton while loading", () => {
    render(EntityDetailPageStateHarness, { props: { loadState: "loading" } });

    expect(screen.getByRole("status", { name: "Loading entity" })).toBeInTheDocument();
    expect(screen.queryByText("Ready detail")).not.toBeInTheDocument();
  });

  it("renders an accessible retry action for load failures", async () => {
    const onRetry = vi.fn();
    render(EntityDetailPageStateHarness, {
      props: { loadState: "error", errorMessage: "Network unavailable", onRetry },
    });

    expect(screen.getByRole("alert")).toHaveTextContent("Network unavailable");
    await fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(onRetry).toHaveBeenCalledOnce();
  });

  it("renders page content only when ready", () => {
    render(EntityDetailPageStateHarness, { props: { loadState: "ready" } });

    expect(screen.getByText("Ready detail")).toBeInTheDocument();
    expect(screen.queryByRole("status", { name: "Loading entity" })).not.toBeInTheDocument();
  });
});
