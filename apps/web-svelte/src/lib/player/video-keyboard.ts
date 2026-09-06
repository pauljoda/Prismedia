/** Playback actions available to the page-level video keyboard handler. */
interface VideoKeyboardActions {
  togglePlay: () => void;
  seek: (deltaSeconds: number) => void;
  toggleMute: () => void;
  toggleFullscreen: () => void | Promise<void>;
}

/**
 * Builds global video shortcuts while leaving focused UI controls and consumed
 * events to their owner. The caller owns listener registration and player state.
 */
export function createVideoKeyboardHandler(actions: VideoKeyboardActions): (event: KeyboardEvent) => void {
  return (event) => {
    if (event.defaultPrevented) return;
    if (event.target instanceof Element && event.target.closest(
      'input, textarea, select, button, a, [contenteditable]:not([contenteditable="false"]), [role="tab"], [role="menuitem"], [role="option"], [role="slider"], [role="combobox"], [role="switch"], [role="checkbox"], [role="radio"]',
    )) return;

    switch (event.key.toLowerCase()) {
      case " ":
      case "k":
        if (event.key.toLowerCase() === "k" && (event.metaKey || event.ctrlKey)) break;
        event.preventDefault();
        actions.togglePlay();
        break;
      case "arrowleft":
        actions.seek(-5);
        break;
      case "arrowright":
        actions.seek(5);
        break;
      case "j":
        actions.seek(-10);
        break;
      case "l":
        actions.seek(10);
        break;
      case "m":
        actions.toggleMute();
        break;
      case "f":
        void actions.toggleFullscreen();
        break;
    }
  };
}
