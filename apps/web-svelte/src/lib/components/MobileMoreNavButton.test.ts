import { fireEvent, render, screen } from "@testing-library/svelte";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import MobileMoreNavButton from "./MobileMoreNavButton.svelte";

const toggleShowOff = vi.hoisted(() => vi.fn());

vi.mock("$lib/nsfw/store.svelte", () => ({
  useNsfw: () => ({ toggleShowOff }),
}));

describe("MobileMoreNavButton", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    toggleShowOff.mockClear();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it("toggles SFW/full NSFW after a two second hold and suppresses the opening click", async () => {
    const onToggleSheet = vi.fn();
    const button = renderButton(onToggleSheet);
    stubPointerCapture(button);

    await fireEvent(button, pointerEvent("pointerdown", { clientX: 20, clientY: 20, pointerId: 7 }));
    await vi.advanceTimersByTimeAsync(1999);
    expect(toggleShowOff).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(1);
    expect(toggleShowOff).toHaveBeenCalledTimes(1);

    await fireEvent.click(button);
    expect(onToggleSheet).not.toHaveBeenCalled();
  });

  it("keeps normal click behavior when released before two seconds", async () => {
    const onToggleSheet = vi.fn();
    const button = renderButton(onToggleSheet);
    stubPointerCapture(button);

    await fireEvent(button, pointerEvent("pointerdown", { clientX: 20, clientY: 20, pointerId: 7 }));
    await vi.advanceTimersByTimeAsync(1999);
    await fireEvent(button, pointerEvent("pointerup", { clientX: 20, clientY: 20, pointerId: 7 }));
    await vi.advanceTimersByTimeAsync(1);
    await fireEvent.click(button);

    expect(toggleShowOff).not.toHaveBeenCalled();
    expect(onToggleSheet).toHaveBeenCalledTimes(1);
  });

  it("cancels the shortcut when the pointer moves past the existing threshold", async () => {
    const onToggleSheet = vi.fn();
    const button = renderButton(onToggleSheet);
    stubPointerCapture(button);

    await fireEvent(button, pointerEvent("pointerdown", { clientX: 20, clientY: 20, pointerId: 7 }));
    await fireEvent(button, pointerEvent("pointermove", { clientX: 35, clientY: 20, pointerId: 7 }));
    await vi.advanceTimersByTimeAsync(2000);

    expect(toggleShowOff).not.toHaveBeenCalled();
  });
});

function renderButton(onToggleSheet = vi.fn()): HTMLButtonElement {
  render(MobileMoreNavButton, {
    props: {
      isMoreActive: false,
      sheetOpen: false,
      onToggleSheet,
    },
  });
  return screen.getByRole("button", { name: "More navigation. Press and hold two seconds to toggle SFW and full NSFW." }) as HTMLButtonElement;
}

function stubPointerCapture(button: HTMLButtonElement) {
  const captured = new Set<number>();
  button.setPointerCapture = vi.fn((pointerId: number) => captured.add(pointerId));
  button.releasePointerCapture = vi.fn((pointerId: number) => captured.delete(pointerId));
  button.hasPointerCapture = vi.fn((pointerId: number) => captured.has(pointerId));
}

function pointerEvent(
  type: string,
  options: { button?: number; clientX?: number; clientY?: number; pointerId?: number } = {},
) {
  const event = new Event(type, { bubbles: true, cancelable: true });
  Object.defineProperty(event, "button", { value: options.button ?? 0 });
  Object.defineProperty(event, "clientX", { value: options.clientX ?? 0 });
  Object.defineProperty(event, "clientY", { value: options.clientY ?? 0 });
  Object.defineProperty(event, "pointerId", { value: options.pointerId ?? 1 });
  return event;
}
