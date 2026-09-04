import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import DownloadManagerTable from "./DownloadManagerTable.svelte";
import type { DownloadManagerEntry } from "./download-tree";

function entry(id: string, title: string, tone: DownloadManagerEntry["item"]["tone"]): DownloadManagerEntry {
  return {
    row: { acquisitionId: id, entityId: null, updatedAt: "2026-01-01T00:00:00Z" } as DownloadManagerEntry["row"],
    item: { id, title, tone, statusLabel: tone, progress: null, selectable: true } as DownloadManagerEntry["item"],
  };
}

describe("DownloadManagerTable", () => {
  it("uses a single-choice status filter and never clears the current choice", async () => {
    render(DownloadManagerTable, {
      entries: [entry("one", "First transfer", "downloading"), entry("two", "Second transfer", "attention")],
      thumbnails: new Map(), onSelect: vi.fn(), onRemove: vi.fn(),
    });
    const attention = screen.getByRole("radio", { name: "Attention 1" });
    await fireEvent.click(attention);
    expect(attention).toHaveAttribute("aria-checked", "true");
    expect(screen.queryByRole("button", { name: "Inspect First transfer" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Inspect Second transfer" })).toBeInTheDocument();
    await fireEvent.click(attention);
    expect(attention).toHaveAttribute("aria-checked", "true");
    await fireEvent.click(screen.getByRole("radio", { name: "All" }));
    expect(screen.getByRole("button", { name: "Inspect First transfer" })).toBeInTheDocument();
  });

  it("sorts through the phone picker and direction control using the desktop column model", async () => {
    const { container } = render(DownloadManagerTable, {
      entries: [entry("z", "Zebra", "downloading"), entry("a", "Alpha", "downloading")],
      thumbnails: new Map(), onSelect: vi.fn(), onRemove: vi.fn(),
    });
    const picker = screen.getByRole("button", { name: "Sort downloads by" });
    expect(picker).toHaveTextContent("Updated");
    await fireEvent.keyDown(picker, { key: "Enter" });
    await fireEvent.pointerUp(await screen.findByRole("option", { name: "Entity" }));
    expect([...container.querySelectorAll(".row-title")].map(node => node.textContent)).toEqual(["Alpha", "Zebra"]);
    await fireEvent.click(screen.getByRole("button", { name: "Ascending order; switch to descending" }));
    expect([...container.querySelectorAll(".row-title")].map(node => node.textContent)).toEqual(["Zebra", "Alpha"]);
  });

  it("keeps transfer metrics and selection available in the adaptive rows", async () => {
    const transfer = entry("one", "First transfer", "downloading");
    transfer.item.progress = 0.5;
    Object.assign(transfer.row, { totalSizeBytes: 1048576, downloadSpeedBytesPerSecond: 1024, etaSeconds: 60 });
    const onRemove = vi.fn();
    const onSelect = vi.fn();
    const { container } = render(DownloadManagerTable, { entries: [transfer], thumbnails: new Map(), onSelect, onRemove });
    const row = container.querySelector(".download-row")!;
    expect(row).not.toHaveClass("no-progress");
    expect(row.querySelector(".progress-value")).toHaveTextContent("50%");
    for (const metric of ["size", "speed", "eta"]) {
      expect(row.querySelector(`.${metric}-cell`)).not.toHaveClass("empty-metric");
      expect(row.querySelector(`.${metric}-cell .metric-label`)).toHaveTextContent(/\w/);
    }
    await fireEvent.click(screen.getByRole("checkbox", { name: "Select First transfer" }));
    expect(screen.getByText("1 selected")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Remove" })).toBeEnabled();
    expect(onRemove).not.toHaveBeenCalled();
    expect(onSelect).not.toHaveBeenCalled();
  });
});
