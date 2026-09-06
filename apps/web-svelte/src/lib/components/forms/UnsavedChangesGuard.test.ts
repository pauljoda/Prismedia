import { cleanup, render } from "@testing-library/svelte";
import { beforeNavigate } from "$app/navigation";
import type { BeforeNavigate } from "@sveltejs/kit";
import { afterEach, expect, it, vi } from "vitest";
import UnsavedChangesGuard from "./UnsavedChangesGuard.svelte";

vi.mock("$app/navigation", () => ({ beforeNavigate: vi.fn() }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.clearAllMocks(); });

function attempt(overrides: Partial<BeforeNavigate> = {}) {
  const navigation = {
    type: "link",
    willUnload: false,
    cancel: vi.fn(),
    from: { url: new URL("https://example.test/books/book-1") },
    to: { url: new URL("https://example.test/movies") },
    ...overrides,
  } as BeforeNavigate;
  vi.mocked(beforeNavigate).mock.calls.at(-1)![0](navigation);
  return navigation;
}

it("does not interrupt a clean form and observes changes to dirty state", async () => {
  const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
  const view = render(UnsavedChangesGuard, { dirty: false });
  expect(attempt().cancel).not.toHaveBeenCalled();
  expect(confirm).not.toHaveBeenCalled();
  await view.rerender({ dirty: true });
  expect(attempt().cancel).toHaveBeenCalledOnce();
  expect(confirm).toHaveBeenCalledOnce();
  await view.rerender({ dirty: false });
  expect(attempt().cancel).not.toHaveBeenCalled();
});

it.each(["link", "goto", "popstate"] as const)("keeps a dirty draft when the user cancels %s navigation", (type) => {
  const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
  render(UnsavedChangesGuard, { dirty: true });
  expect(attempt({ type }).cancel).toHaveBeenCalledOnce();
  expect(confirm).toHaveBeenCalledWith("Discard unsaved changes and leave this page?");
});

it("lets the original navigation continue when discarding, without replaying history", () => {
  vi.spyOn(window, "confirm").mockReturnValue(true);
  render(UnsavedChangesGuard, { dirty: true });
  expect(attempt({ type: "popstate", delta: -1 }).cancel).not.toHaveBeenCalled();
});

it("uses the browser unload warning for refresh or closing the tab", () => {
  const confirm = vi.spyOn(window, "confirm");
  render(UnsavedChangesGuard, { dirty: true });
  expect(attempt({ type: "leave", willUnload: true, to: null }).cancel).toHaveBeenCalledOnce();
  expect(confirm).not.toHaveBeenCalled();
});

it("does not interrupt an in-page anchor that keeps the same draft", () => {
  const confirm = vi.spyOn(window, "confirm");
  render(UnsavedChangesGuard, { dirty: true });
  const to = { url: new URL("https://example.test/books/book-1#metadata") } as BeforeNavigate["to"];
  expect(attempt({ to }).cancel).not.toHaveBeenCalled();
  expect(confirm).not.toHaveBeenCalled();
});

it("still warns for a full-page link back to the same URL", () => {
  const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
  render(UnsavedChangesGuard, { dirty: true });
  const to = { url: new URL("https://example.test/books/book-1") } as BeforeNavigate["to"];
  expect(attempt({ to, willUnload: true }).cancel).toHaveBeenCalledOnce();
  expect(confirm).toHaveBeenCalledOnce();
});

it("warns for external links that replace the current document", () => {
  vi.spyOn(window, "confirm").mockReturnValue(false);
  render(UnsavedChangesGuard, { dirty: true });
  const to = { url: new URL("https://another.example.test/") } as BeforeNavigate["to"];
  expect(attempt({ to, willUnload: true }).cancel).toHaveBeenCalledOnce();
});
