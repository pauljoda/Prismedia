import { redirect } from "@sveltejs/kit";

/** The concept areas have no page of their own; their breadcrumb opens the gallery. */
export function load() {
  redirect(307, "/concepts");
}
