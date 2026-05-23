export type FileActionId = "open" | "new-folder" | "rename" | "move" | "rescan" | "delete";

export interface FileAction {
  id: FileActionId;
  label: string;
  destructive?: boolean;
}

const rootActions: FileAction[] = [
  { id: "open", label: "Open" },
  { id: "new-folder", label: "New folder" },
  { id: "rescan", label: "Rescan" },
];

const directoryActions: FileAction[] = [
  { id: "open", label: "Open" },
  { id: "new-folder", label: "New folder" },
  { id: "rename", label: "Rename" },
  { id: "move", label: "Move" },
  { id: "rescan", label: "Rescan" },
  { id: "delete", label: "Delete", destructive: true },
];

const fileActions: FileAction[] = [
  { id: "open", label: "Open" },
  { id: "rename", label: "Rename" },
  { id: "move", label: "Move" },
  { id: "delete", label: "Delete", destructive: true },
];

export function fileContextActions(kind: string, isRoot?: boolean): FileAction[] {
  if (isRoot) return rootActions;
  return kind === "directory" ? directoryActions : fileActions;
}

