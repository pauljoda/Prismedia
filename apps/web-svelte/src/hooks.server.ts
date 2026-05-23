import type { Handle } from "@sveltejs/kit";
import { PAGE_CACHE_CONTROL } from "$lib/server/cache-policy";

function requestMethodCanUseBrowserCache(method: string) {
  return method === "GET" || method === "HEAD";
}

function isSvelteKitDataRequest(pathname: string) {
  return pathname.endsWith("/__data.json");
}

function isPageDocumentResponse(event: Parameters<Handle>[0]["event"], response: Response) {
  if (!requestMethodCanUseBrowserCache(event.request.method)) return false;
  if (event.url.pathname.startsWith("/api/")) return false;
  if (response.headers.has("Cache-Control")) return false;
  if (isSvelteKitDataRequest(event.url.pathname)) return true;

  const contentType = response.headers.get("Content-Type") ?? "";
  if (contentType.toLowerCase().includes("text/html")) return true;

  const accept = event.request.headers.get("Accept") ?? "";
  return accept.toLowerCase().includes("text/html");
}

function appendVary(headers: Headers, value: string) {
  const existing = headers.get("Vary");
  if (!existing) {
    headers.set("Vary", value);
    return;
  }

  if (existing === "*") return;

  const values = existing.split(",").map((part) => part.trim().toLowerCase());
  if (!values.includes(value.toLowerCase())) {
    headers.set("Vary", `${existing}, ${value}`);
  }
}

export const handle: Handle = async ({ event, resolve }) => {
  const response = await resolve(event);

  if (isPageDocumentResponse(event, response)) {
    response.headers.set("Cache-Control", PAGE_CACHE_CONTROL);
    appendVary(response.headers, "Cookie");
  }

  return response;
};
