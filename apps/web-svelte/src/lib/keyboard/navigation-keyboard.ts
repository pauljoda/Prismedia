export interface NavigationKeyboardActions {
  close: () => void;
  prev: () => void;
  next: () => void;
  extraKeys?: Record<string, (event: KeyboardEvent) => void>;
}

function isTyping(target: EventTarget | null): boolean {
  if (!target || !(target instanceof HTMLElement)) return false;
  return (
    target.tagName === "INPUT" ||
    target.tagName === "TEXTAREA" ||
    target.isContentEditable
  );
}

export function createNavigationKeyHandler(actions: NavigationKeyboardActions): (event: KeyboardEvent) => void {
  return (event: KeyboardEvent) => {
    if (isTyping(event.target) && event.key !== "Escape") return;

    switch (event.key) {
      case "Escape":
        event.preventDefault();
        actions.close();
        return;
      case "ArrowLeft":
      case "h":
      case "H":
        event.preventDefault();
        actions.prev();
        return;
      case "ArrowRight":
      case "l":
      case "L":
        event.preventDefault();
        actions.next();
        return;
    }

    if (actions.extraKeys?.[event.key]) {
      event.preventDefault();
      actions.extraKeys[event.key](event);
    }
  };
}
