import { describe, expect, it } from "vitest";
import { renderEntityDescriptionMarkdown } from "./entity-detail-markdown";

describe("entity detail markdown", () => {
  it("renders safe markdown while preserving external link protections", () => {
    const html = renderEntityDescriptionMarkdown(
      "## Summary\n\n- A **bold** [site](https://example.com)\n- Some `code`",
    );

    expect(html).toContain("<h2>Summary</h2>");
    expect(html).toContain("<ul>");
    expect(html).toContain("<strong>bold</strong>");
    expect(html).toContain("<code>code</code>");
    expect(html).toContain('href="https://example.com"');
    expect(html).toContain('target="_blank"');
    expect(html).toContain('rel="noopener noreferrer"');
  });

  it("does not render an iframe srcdoc payload as executable html", () => {
    const html = renderEntityDescriptionMarkdown(
      '<iframe srcdoc="&lt;script&gt;alert(document.domain)&lt;/script&gt;">',
    );

    expect(html).not.toContain("<iframe");
    expect(html).not.toContain("srcdoc=");
  });

  it("does not turn an encoded javascript scheme into a link", () => {
    const html = renderEntityDescriptionMarkdown(
      '<a href="&#x6a;avascript:alert(document.domain)">open</a>',
    );

    expect(html).toContain("open");
    expect(html).not.toContain("<a ");
    expect(html).not.toContain("href=");
  });

  it("renders an unsafe markdown link as text", () => {
    const html = renderEntityDescriptionMarkdown("[open](javascript:alert(document.domain))");

    expect(html).toContain("open");
    expect(html).not.toContain("<a ");
  });

  it("does not render raw html event handlers", () => {
    const html = renderEntityDescriptionMarkdown('<img src="https://example.com/a.jpg" onerror="alert(1)">');

    expect(html).not.toContain("<img");
    expect(html).not.toContain("onerror=");
  });

  it("escapes generated link attributes before rendering them", () => {
    const html = renderEntityDescriptionMarkdown('[tricky](https://example.com/?q="onclick)');

    expect(html).toContain('href="https://example.com/?q=&quot;onclick"');
    expect(html).not.toContain('onclick="alert(1)"');
  });

  it("does not render raw html inside link text", () => {
    const html = renderEntityDescriptionMarkdown(
      '[<img src=data:text/html;base64,abc onerror=alert(1)>](https://example.com)',
    );

    expect(html).toContain('href="https://example.com"');
    expect(html).not.toContain("data:");
    expect(html).not.toContain("onerror");
  });
});
