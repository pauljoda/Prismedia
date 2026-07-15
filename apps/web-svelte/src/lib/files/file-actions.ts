import { FILE_ENTRY_KIND, type FileEntryKindCode } from "$lib/api/generated/codes";

export type FileActionId = "open" | "download" | "new-folder" | "upload" | "rename" | "move" | "rescan" | "exclude" | "remove-exclusion" | "delete";

export interface FileAction {
  id: FileActionId;
  label: string;
  destructive?: boolean;
}

const rootActions: FileAction[] = [
  { id: "open", label: "Open" },
  { id: "download", label: "Download" },
  { id: "new-folder", label: "New folder" },
  { id: "upload", label: "Upload files" },
  { id: "rescan", label: "Rescan" },
];

const directoryActions: FileAction[] = [
  { id: "open", label: "Open" },
  { id: "download", label: "Download" },
  { id: "new-folder", label: "New folder" },
  { id: "upload", label: "Upload files" },
  { id: "rename", label: "Rename" },
  { id: "move", label: "Move" },
  { id: "rescan", label: "Rescan" },
  { id: "exclude", label: "Exclude" },
  { id: "delete", label: "Delete", destructive: true },
];

const fileActions: FileAction[] = [
  { id: "open", label: "Open" },
  { id: "download", label: "Download" },
  { id: "rename", label: "Rename" },
  { id: "move", label: "Move" },
  { id: "exclude", label: "Exclude" },
  { id: "delete", label: "Delete", destructive: true },
];

const removeExclusionAction: FileAction = { id: "remove-exclusion", label: "Remove exclusion" };

export function fileContextActions(kind: FileEntryKindCode, isRoot?: boolean, excluded?: boolean): FileAction[] {
  if (isRoot) return rootActions;
  const actions = kind === FILE_ENTRY_KIND.directory ? directoryActions : fileActions;
  return excluded
    ? actions.map((action) => action.id === "exclude" ? removeExclusionAction : action)
    : actions;
}
