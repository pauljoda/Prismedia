import { error } from "@sveltejs/kit";
import { dev } from "$app/environment";
import type { PageLoad } from "./$types";

/** The concept gallery is a development-only surface: a release build has no such page. */
export const load: PageLoad = () => {
  if (!dev) error(404, "Not found");
  return {};
};
