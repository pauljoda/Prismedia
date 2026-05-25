import { dev } from "$app/environment";
import { error } from "@sveltejs/kit";

export function shouldExposeDevRoutes(isDevBuild = dev): boolean {
  return isDevBuild;
}

export function assertDevRouteVisible(isDevBuild = dev): void {
  if (!shouldExposeDevRoutes(isDevBuild)) {
    throw error(404, "Not found");
  }
}
