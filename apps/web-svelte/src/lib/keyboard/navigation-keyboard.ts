export interface NavigationKeyboardActions {
  /** Omit when the shared modal owns Escape dismissal. */
  close?: () => void;
  /** Ignore keys routed from another overlay, including global search. */
  scope?: () => HTMLElement | null;
  prev: () => void;
  next: () => void;
  extraKeys?: Record<string, (event: KeyboardEvent) => void>;
}

function isTyping(target: EventTarget | null): boolean {
  if (!target || !(target instanceof HTMLElement)) return false;
  return (
    target.tagName === "INPUT" ||
    target.tagName === "TEXTAREA" ||
    target.isContentEditable ||
    !!target.closest('[role="slider"], [role="radio"], [role="combobox"], [role="listbox"], [role="menu"], [role="tab"]')
  );
}

export function createNavigationKeyHandler(actions: NavigationKeyboardActions): (event: KeyboardEvent) => void {
  return (event: KeyboardEvent) => {
    const scope = actions.scope?.();
    if (scope && event.target instanceof HTMLElement && !scope.contains(event.target)) return;
    if (event.defaultPrevented || event.metaKey || event.ctrlKey || event.altKey) return;
    if (isTyping(event.target) && event.key !== "Escape") return;

    switch (event.key) {
      case "Escape":
        if (!actions.close) return;
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
