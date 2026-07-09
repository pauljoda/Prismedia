import { marked } from "marked";

const URL_VALIDATION_BASE = "https://prismedia.invalid";
const SAFE_LINK_PROTOCOLS = new Set(["http:", "https:", "mailto:"]);
const SAFE_IMAGE_PROTOCOLS = new Set(["http:", "https:"]);

function decodeUrlCharacterReferences(value: string): string {
  return value
    .replace(/&#(?:x([\da-f]+)|([\d]+));?/gi, (_reference, hex: string | undefined, decimal: string | undefined) => {
      const codePoint = Number.parseInt(hex ?? decimal ?? "", hex ? 16 : 10);
      return Number.isSafeInteger(codePoint) && codePoint <= 0x10ffff ? String.fromCodePoint(codePoint) : "";
    })
    .replace(/&colon;/g, ":")
    .replace(/&Tab;/g, "\t")
    .replace(/&NewLine;/g, "\n");
}

function escapeAttribute(value: string): string {
  return value
    .replace(/&/g, "&amp;")
    .replace(/"/g, "&quot;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

function hasSafeProtocol(value: string, protocols: ReadonlySet<string>): boolean {
  try {
    const url = new URL(decodeUrlCharacterReferences(value.trim()), URL_VALIDATION_BASE);
    return protocols.has(url.protocol);
  } catch {
    return false;
  }
}

/** Renders user-editable entity descriptions as a sanitized markdown fragment. */
export function renderEntityDescriptionMarkdown(description: string | null | undefined): string | null {
  if (!description) return null;

  const renderer = new marked.Renderer();
  renderer.html = () => "";
  renderer.link = ({ href, tokens }) => {
    const text = renderer.parser.parseInline(tokens);
    if (!hasSafeProtocol(href, SAFE_LINK_PROTOCOLS)) {
      return text;
    }

    return `<a href="${escapeAttribute(href.trim())}" target="_blank" rel="noopener noreferrer">${text}</a>`;
  };
  renderer.image = ({ href, text, title }) => {
    if (!hasSafeProtocol(href, SAFE_IMAGE_PROTOCOLS)) {
      return escapeAttribute(text);
    }

    const safeTitle = title ? ` title="${escapeAttribute(title)}"` : "";
    return `<img src="${escapeAttribute(href.trim())}" alt="${escapeAttribute(text)}"${safeTitle}>`;
  };

  return marked.parse(description, { renderer, async: false, gfm: true, breaks: true }) as string;
}
