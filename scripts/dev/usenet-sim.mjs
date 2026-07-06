#!/usr/bin/env node
/**
 * usenet-sim — a self-contained dev usenet provider + indexer, so the full usenet acquisition loop
 * (Newznab search → grab → SABnzbd download → import) runs offline against local content.
 *
 * Two halves, one process:
 *   - An NNTP server (default :1199) serving yEnc-encoded articles from the local spool. Point the
 *     dev SABnzbd at it (host: host.docker.internal, port 1199, no SSL, any/no credentials).
 *   - An HTTP server (default :5060) speaking just enough Newznab for Prismedia's native client:
 *     `/api?t=caps`, `/api?t=search&q=…&cat=…` (RSS with NZB enclosures), and `/nzb/<id>.nzb`.
 *     Enclosure URLs use --public-url (default http://host.docker.internal:5060) because SABnzbd
 *     fetches the NZB from inside its container.
 *
 * Usage:
 *   node scripts/dev/usenet-sim.mjs post <file…> --title "Bluey.S03.1080p.WEB-DL.x264-SIM" [--category 5000]
 *   node scripts/dev/usenet-sim.mjs serve [--nntp-port 1199] [--http-port 5060] [--public-url URL]
 *   node scripts/dev/usenet-sim.mjs list
 *
 * The spool lives outside the repo (default ~/PrismediaAcquisition/usenet-sim; override with --spool
 * or USENET_SIM_SPOOL). Searches match when every query token appears in the release title's token
 * set, so ladder phrasings like "Bluey S03" hit "Bluey.S03.1080p.WEB-DL.x264-SIM".
 */
import { createHash, randomUUID } from "node:crypto";
import { mkdirSync, readFileSync, readdirSync, writeFileSync, existsSync, statSync } from "node:fs";
import { createServer as createTcpServer } from "node:net";
import { createServer as createHttpServer } from "node:http";
import { basename, join } from "node:path";
import { homedir } from "node:os";

const CRLF = Buffer.from("\r\n");
const GROUP = "alt.binaries.prismedia.sim";
const SEGMENT_BYTES = 700_000;
const YENC_LINE = 128;

// ── args ─────────────────────────────────────────────────────────
const [, , command, ...rest] = process.argv;
const flags = {};
const positional = [];
for (let i = 0; i < rest.length; i++) {
  if (rest[i].startsWith("--")) {
    flags[rest[i].slice(2)] = rest[i + 1] && !rest[i + 1].startsWith("--") ? rest[++i] : "true";
  } else {
    positional.push(rest[i]);
  }
}

const spoolDir = flags.spool ?? process.env.USENET_SIM_SPOOL ?? join(homedir(), "PrismediaAcquisition", "usenet-sim");
mkdirSync(join(spoolDir, "releases"), { recursive: true });

// ── crc32 (yEnc pcrc32/crc32 trailers) ───────────────────────────
const CRC_TABLE = new Uint32Array(256).map((_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c >>> 0;
});
function crc32(buf) {
  let c = 0xffffffff;
  for (const b of buf) c = CRC_TABLE[(c ^ b) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

// ── yEnc encoding ────────────────────────────────────────────────
function yencEncode(buf) {
  const chunks = [];
  let line = [];
  for (const byte of buf) {
    const c = (byte + 42) & 0xff;
    // Criticals always escape; '.', TAB, and SPACE escape at line start so article lines stay clean
    // (dot-stuffing is still applied at the NNTP layer as a belt-and-braces).
    const critical = c === 0x00 || c === 0x0a || c === 0x0d || c === 0x3d;
    const startSensitive = line.length === 0 && (c === 0x2e || c === 0x09 || c === 0x20);
    if (critical || startSensitive) {
      line.push(0x3d, (c + 64) & 0xff);
    } else {
      line.push(c);
    }
    if (line.length >= YENC_LINE) {
      chunks.push(Buffer.from(line), CRLF);
      line = [];
    }
  }
  if (line.length > 0) chunks.push(Buffer.from(line), CRLF);
  return Buffer.concat(chunks);
}

/** One complete NNTP article (headers + blank line + yEnc body) for a segment of a file. */
function buildArticle({ messageId, fileName, fileSize, part, totalParts, begin, end, data }) {
  const headers = [
    `Message-ID: <${messageId}>`,
    `From: usenet-sim <dev@prismedia.sim>`,
    `Newsgroups: ${GROUP}`,
    `Subject: "${fileName}" yEnc (${part}/${totalParts})`,
    `Date: ${new Date().toUTCString()}`,
    "",
  ].join("\r\n");
  const body = Buffer.concat([
    Buffer.from(`=ybegin part=${part} total=${totalParts} line=${YENC_LINE} size=${fileSize} name=${fileName}\r\n`),
    Buffer.from(`=ypart begin=${begin} end=${end}\r\n`),
    yencEncode(data),
    Buffer.from(`=yend size=${data.length} part=${part} pcrc32=${crc32(data).toString(16).padStart(8, "0")}\r\n`),
  ]);
  return Buffer.concat([Buffer.from(headers + "\r\n"), body]);
}

// ── spool ────────────────────────────────────────────────────────
function releaseDirs() {
  return readdirSync(join(spoolDir, "releases"), { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name);
}
function readMeta(id) {
  return JSON.parse(readFileSync(join(spoolDir, "releases", id, "meta.json"), "utf8"));
}

// ── post ─────────────────────────────────────────────────────────
function post() {
  const title = flags.title;
  if (!title || positional.length === 0) {
    console.error('usage: usenet-sim post <file…> --title "Release.Name" [--category 5000]');
    process.exit(1);
  }

  const id = createHash("sha1").update(title).digest("hex").slice(0, 16);
  const dir = join(spoolDir, "releases", id);
  mkdirSync(join(dir, "articles"), { recursive: true });

  const files = [];
  let totalBytes = 0;
  for (const path of positional) {
    const content = readFileSync(path);
    const fileName = basename(path);
    totalBytes += content.length;
    const totalParts = Math.max(1, Math.ceil(content.length / SEGMENT_BYTES));
    const segments = [];
    for (let part = 1; part <= totalParts; part++) {
      const begin = (part - 1) * SEGMENT_BYTES;
      const data = content.subarray(begin, Math.min(begin + SEGMENT_BYTES, content.length));
      const messageId = `${id}.${files.length}.${part}.${randomUUID().slice(0, 8)}@prismedia.sim`;
      const article = buildArticle({
        messageId, fileName, fileSize: content.length, part, totalParts,
        begin: begin + 1, end: begin + data.length, data,
      });
      writeFileSync(join(dir, "articles", `${messageId}.art`), article);
      segments.push({ messageId, number: part, bytes: article.length });
    }
    files.push({ name: fileName, size: content.length, segments });
  }

  const meta = {
    id,
    title,
    category: Number(flags.category ?? 8000),
    sizeBytes: totalBytes,
    postedAt: new Date().toISOString(),
    files,
  };
  writeFileSync(join(dir, "meta.json"), JSON.stringify(meta, null, 2));
  writeFileSync(join(dir, "release.nzb"), buildNzb(meta));
  console.log(`posted ${id}  ${title}  (${files.length} file(s), ${totalBytes} bytes)`);
}

function xmlEscape(text) {
  return text.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;");
}

function buildNzb(meta) {
  const files = meta.files
    .map((file, index) => {
      const segments = file.segments
        .map((seg) => `      <segment bytes="${seg.bytes}" number="${seg.number}">${xmlEscape(seg.messageId)}</segment>`)
        .join("\n");
      return [
        `  <file poster="usenet-sim &lt;dev@prismedia.sim&gt;" date="${Math.floor(Date.now() / 1000)}" subject="${xmlEscape(`"${file.name}" yEnc (1/${file.segments.length})`)}">`,
        `    <groups><group>${GROUP}</group></groups>`,
        `    <segments>\n${segments}\n    </segments>`,
        `  </file>`,
      ].join("\n");
    })
    .join("\n");
  return `<?xml version="1.0" encoding="UTF-8"?>\n<nzb xmlns="http://www.newzbin.com/DTD/2003/nzb">\n${files}\n</nzb>\n`;
}

// ── serve: NNTP ──────────────────────────────────────────────────
function findArticle(rawId) {
  const messageId = rawId.replace(/^</, "").replace(/>$/, "");
  for (const releaseId of releaseDirs()) {
    const path = join(spoolDir, "releases", releaseId, "articles", `${messageId}.art`);
    if (existsSync(path)) return readFileSync(path);
  }
  return null;
}

/** NNTP multiline payload: dot-stuff lines starting with '.', terminate with CRLF '.' CRLF. */
function dotStuffed(buf) {
  const parts = [];
  let start = 0;
  while (start <= buf.length) {
    let end = buf.indexOf(CRLF, start);
    if (end === -1) end = buf.length;
    const line = buf.subarray(start, end);
    if (line[0] === 0x2e) parts.push(Buffer.from("."));
    parts.push(line, CRLF);
    start = end + 2;
    if (end === buf.length) break;
  }
  parts.push(Buffer.from(".\r\n"));
  return Buffer.concat(parts);
}

function splitArticle(article) {
  const divider = article.indexOf(Buffer.from("\r\n\r\n"));
  return { head: article.subarray(0, divider), body: article.subarray(divider + 4) };
}

function startNntp(port) {
  const server = createTcpServer((socket) => {
    socket.write("200 usenet-sim ready\r\n");
    let pending = Buffer.alloc(0);
    socket.on("data", (data) => {
      pending = Buffer.concat([pending, data]);
      let index;
      while ((index = pending.indexOf(CRLF)) !== -1) {
        const line = pending.subarray(0, index).toString("latin1").trim();
        pending = pending.subarray(index + 2);
        handleNntpCommand(socket, line);
      }
    });
    socket.on("error", () => {});
  });
  server.listen(port, () => console.log(`[nntp] listening on :${port} (group ${GROUP})`));
  return server;
}

function handleNntpCommand(socket, line) {
  const [verb, ...args] = line.split(/\s+/);
  const command = (verb ?? "").toUpperCase();
  switch (command) {
    case "CAPABILITIES":
      socket.write("101 capabilities\r\nVERSION 2\r\nREADER\r\n.\r\n");
      return;
    case "MODE":
      socket.write("200 reader\r\n");
      return;
    case "AUTHINFO":
      socket.write((args[0] ?? "").toUpperCase() === "USER" ? "381 password required\r\n" : "281 authenticated\r\n");
      return;
    case "GROUP":
      socket.write(`211 0 1 0 ${args[0] ?? GROUP}\r\n`);
      return;
    case "DATE":
      socket.write(`111 ${new Date().toISOString().replaceAll(/[-:TZ.]/g, "").slice(0, 14)}\r\n`);
      return;
    case "QUIT":
      socket.end("205 bye\r\n");
      return;
    case "STAT":
    case "HEAD":
    case "BODY":
    case "ARTICLE": {
      const article = args[0] ? findArticle(args[0]) : null;
      if (!article) {
        socket.write("430 no such article\r\n");
        return;
      }
      const { head, body } = splitArticle(article);
      if (command === "STAT") socket.write(`223 0 ${args[0]}\r\n`);
      else if (command === "HEAD") socket.write(Buffer.concat([Buffer.from(`221 0 ${args[0]}\r\n`), dotStuffed(head)]));
      else if (command === "BODY") socket.write(Buffer.concat([Buffer.from(`222 0 ${args[0]}\r\n`), dotStuffed(body)]));
      else socket.write(Buffer.concat([Buffer.from(`220 0 ${args[0]}\r\n`), dotStuffed(article)]));
      return;
    }
    default:
      socket.write("500 unknown command\r\n");
  }
}

// ── serve: Newznab HTTP ──────────────────────────────────────────
const CAPS_XML = `<?xml version="1.0" encoding="UTF-8"?>
<caps>
  <server title="usenet-sim"/>
  <limits max="100" default="100"/>
  <searching><search available="yes" supportedParams="q"/></searching>
  <categories>
    <category id="2000" name="Movies"><subcat id="2040" name="HD"/><subcat id="2045" name="UHD"/></category>
    <category id="3000" name="Audio"><subcat id="3010" name="MP3"/><subcat id="3040" name="Lossless"/></category>
    <category id="5000" name="TV"><subcat id="5030" name="SD"/><subcat id="5040" name="HD"/></category>
    <category id="7000" name="Books"><subcat id="7020" name="EBook"/></category>
    <category id="8000" name="Other"><subcat id="8010" name="Misc"/></category>
  </categories>
</caps>`;

function tokens(text) {
  return new Set(text.toLowerCase().split(/[^a-z0-9]+/).filter(Boolean));
}

function searchRss(query, cats, publicUrl) {
  const wanted = tokens(query ?? "");
  const items = releaseDirs()
    .map(readMeta)
    .filter((meta) => {
      const titleTokens = tokens(meta.title);
      const queryHit = wanted.size === 0 || [...wanted].every((token) => titleTokens.has(token));
      const catHit = cats.length === 0 || cats.some((cat) => Math.floor(meta.category / 1000) === Math.floor(cat / 1000));
      return queryHit && catHit;
    })
    .map((meta) => {
      const nzbUrl = `${publicUrl}/nzb/${meta.id}.nzb`;
      return [
        "    <item>",
        `      <title>${xmlEscape(meta.title)}</title>`,
        `      <guid>${xmlEscape(nzbUrl)}</guid>`,
        `      <size>${meta.sizeBytes}</size>`,
        `      <pubDate>${new Date(meta.postedAt).toUTCString()}</pubDate>`,
        `      <enclosure url="${xmlEscape(nzbUrl)}" length="${meta.sizeBytes}" type="application/x-nzb"/>`,
        `      <newznab:attr name="category" value="${meta.category}"/>`,
        `      <newznab:attr name="size" value="${meta.sizeBytes}"/>`,
        "    </item>",
      ].join("\n");
    });
  return `<?xml version="1.0" encoding="UTF-8"?>
<rss version="2.0" xmlns:newznab="http://www.newznab.com/DTD/2010/feeds/attributes/">
  <channel>
    <title>usenet-sim</title>
${items.join("\n")}
  </channel>
</rss>`;
}

function startHttp(port, publicUrl) {
  const server = createHttpServer((request, response) => {
    const url = new URL(request.url, `http://localhost:${port}`);
    console.log(`[http] ${request.method} ${url.pathname}${url.search}`);
    if (url.pathname === "/api") {
      const type = url.searchParams.get("t");
      const xml = type === "caps"
        ? CAPS_XML
        : searchRss(
            url.searchParams.get("q") ?? "",
            (url.searchParams.get("cat") ?? "").split(",").filter(Boolean).map(Number),
            publicUrl);
      response.writeHead(200, { "content-type": "application/xml" });
      response.end(xml);
      return;
    }

    const nzbMatch = url.pathname.match(/^\/nzb\/([0-9a-f]+)\.nzb$/);
    if (nzbMatch) {
      const path = join(spoolDir, "releases", nzbMatch[1], "release.nzb");
      if (existsSync(path)) {
        response.writeHead(200, { "content-type": "application/x-nzb" });
        response.end(readFileSync(path));
        return;
      }
    }

    if (url.pathname === "/") {
      response.writeHead(200, { "content-type": "application/json" });
      response.end(JSON.stringify({ releases: releaseDirs().map(readMeta).map(({ id, title, sizeBytes }) => ({ id, title, sizeBytes })) }, null, 2));
      return;
    }

    response.writeHead(404);
    response.end("not found");
  });
  server.listen(port, () => console.log(`[http] newznab on :${port} (public url ${publicUrl})`));
  return server;
}

// ── main ─────────────────────────────────────────────────────────
switch (command) {
  case "post":
    post();
    break;
  case "list":
    for (const id of releaseDirs()) {
      const meta = readMeta(id);
      console.log(`${meta.id}  cat=${meta.category}  ${meta.sizeBytes}b  ${meta.title}`);
    }
    break;
  case "serve": {
    const nntpPort = Number(flags["nntp-port"] ?? 1199);
    const httpPort = Number(flags["http-port"] ?? 5060);
    const publicUrl = (flags["public-url"] ?? `http://host.docker.internal:${httpPort}`).replace(/\/$/, "");
    startNntp(nntpPort);
    startHttp(httpPort, publicUrl);
    console.log(`[sim] spool: ${spoolDir}`);
    break;
  }
  default:
    console.error("usage: usenet-sim <post|serve|list> …  (see header comment)");
    process.exit(1);
}
