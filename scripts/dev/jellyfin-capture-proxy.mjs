#!/usr/bin/env node
import fs from "node:fs";
import http from "node:http";
import https from "node:https";
import net from "node:net";
import path from "node:path";
import { fileURLToPath } from "node:url";

const target = new URL(process.env.PRISMEDIA_TARGET ?? "http://127.0.0.1:8008");
const listenHost = process.env.JELLYFIN_CAPTURE_HOST ?? "0.0.0.0";
const listenPort = Number.parseInt(process.env.JELLYFIN_CAPTURE_PORT ?? "8096", 10);
const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, "..", "..");
const logPath = process.env.JELLYFIN_CAPTURE_LOG ??
  path.join(repoRoot, ".tmp", "jellyfin-capture.jsonl");
const redactNames = new Set([
  "authorization",
  "cookie",
  "x-emby-authorization",
  "x-emby-token",
  "x-mediabrowser-token",
  "x-prismedia-api-key"
]);
const redactQueryNames = new Set([
  "apikey",
  "api_key",
  "token",
  "accesstoken",
  "pw",
  "password",
  "secret"
]);
const maxPreviewBytes = 2048;

if (Number.isNaN(listenPort) || listenPort <= 0) {
  throw new Error("JELLYFIN_CAPTURE_PORT must be a positive integer.");
}

fs.mkdirSync(path.dirname(logPath), { recursive: true });

const server = http.createServer(async (req, res) => {
  const startedAt = Date.now();
  const body = await readRequestBody(req);
  const upstreamUrl = new URL(req.url ?? "/", target);
  const requestEntry = {
    at: new Date().toISOString(),
    method: req.method,
    path: redactUrl(req.url ?? "/"),
    headers: redactHeaders(req.headers),
    bodyPreview: previewBody(body, req.headers["content-type"])
  };

  const originalHost = req.headers.host ?? `${listenHost}:${listenPort}`;
  const remoteAddress = req.socket.remoteAddress;
  const upstreamHeaders = {
    ...req.headers,
    host: target.host,
    "x-forwarded-host": originalHost,
    "x-forwarded-proto": "http",
    "x-forwarded-for": appendForwardedFor(req.headers["x-forwarded-for"], remoteAddress)
  };
  delete upstreamHeaders.connection;
  delete upstreamHeaders["proxy-connection"];
  delete upstreamHeaders["content-length"];

  const client = target.protocol === "https:" ? https : http;
  const upstream = client.request(upstreamUrl, {
    method: req.method,
    headers: {
      ...upstreamHeaders,
      "content-length": body.length
    }
  }, upstreamResponse => {
    res.writeHead(upstreamResponse.statusCode ?? 502, upstreamResponse.headers);
    let responseBytes = 0;
    const responsePreviewChunks = [];
    let responsePreviewBytes = 0;
    upstreamResponse.on("data", chunk => {
      responseBytes += chunk.length;
      if (responsePreviewBytes < maxPreviewBytes) {
        const available = maxPreviewBytes - responsePreviewBytes;
        const previewChunk = chunk.subarray(0, available);
        responsePreviewChunks.push(previewChunk);
        responsePreviewBytes += previewChunk.length;
      }
    });
    upstreamResponse.pipe(res);
    upstreamResponse.on("end", () => {
      writeEntry({
        ...requestEntry,
        status: upstreamResponse.statusCode,
        responsePreview: previewBody(
          Buffer.concat(responsePreviewChunks),
          upstreamResponse.headers["content-type"]),
        responseBytes,
        durationMs: Date.now() - startedAt
      });
    });
  });

  upstream.on("error", error => {
    const status = 502;
    const payload = JSON.stringify({ error: "capture_proxy_upstream_failed" });
    res.writeHead(status, {
      "content-type": "application/json",
      "content-length": Buffer.byteLength(payload)
    });
    res.end(payload);
    writeEntry({
      ...requestEntry,
      status,
      error: error.message,
      responseBytes: Buffer.byteLength(payload),
      durationMs: Date.now() - startedAt
    });
  });

  upstream.end(body);
});

server.on("upgrade", (req, socket, head) => {
  const upstreamSocket = net.connect(resolvePort(target), target.hostname, () => {
    const headers = {
      ...req.headers,
      host: target.host,
      connection: "Upgrade",
      upgrade: req.headers.upgrade
    };
    upstreamSocket.write(`${req.method} ${req.url ?? "/"} HTTP/${req.httpVersion}\r\n`);
    for (const [name, value] of Object.entries(headers)) {
      if (Array.isArray(value)) {
        for (const item of value) upstreamSocket.write(`${name}: ${item}\r\n`);
      } else if (value !== undefined) {
        upstreamSocket.write(`${name}: ${value}\r\n`);
      }
    }
    upstreamSocket.write("\r\n");
    if (head.length > 0) upstreamSocket.write(head);
    upstreamSocket.pipe(socket);
    socket.pipe(upstreamSocket);
    writeEntry({
      at: new Date().toISOString(),
      method: req.method,
      path: redactUrl(req.url ?? "/"),
      upgrade: req.headers.upgrade ?? true,
      headers: redactHeaders(req.headers)
    });
  });

  upstreamSocket.on("error", error => {
    writeEntry({
      at: new Date().toISOString(),
      method: req.method,
      path: redactUrl(req.url ?? "/"),
      status: 502,
      error: error.message
    });
    socket.destroy();
  });
});

server.listen(listenPort, listenHost, () => {
  console.log(`Jellyfin capture proxy listening on http://${listenHost}:${listenPort}`);
  console.log(`Forwarding to ${target.origin}`);
  console.log(`Writing redacted request log to ${logPath}`);
});

async function readRequestBody(req) {
  const chunks = [];
  for await (const chunk of req) {
    chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
  }

  return Buffer.concat(chunks);
}

function redactHeaders(headers) {
  return Object.fromEntries(Object.entries(headers).map(([name, value]) => [
    name,
    redactNames.has(name.toLowerCase()) ? "[redacted]" : value
  ]));
}

function redactUrl(url) {
  const parsed = new URL(url, "http://capture.local");
  for (const name of parsed.searchParams.keys()) {
    if (redactQueryNames.has(name.toLowerCase())) {
      parsed.searchParams.set(name, "[redacted]");
    }
  }

  return `${parsed.pathname}${parsed.search}`;
}

function previewBody(body, contentType) {
  if (body.length === 0) {
    return null;
  }

  const type = Array.isArray(contentType) ? contentType.join(";") : contentType ?? "";
  if (!/(json|text|x-www-form-urlencoded)/i.test(type)) {
    return `[${body.length} bytes]`;
  }

  return redactBodyText(body.toString("utf8").slice(0, 2048));
}

function redactBodyText(text) {
  return text
    .replace(/("(?:Pw|Password|Token|AccessToken|Secret)"\s*:\s*)"[^"]*"/gi, "$1\"[redacted]\"")
    .replace(/((?:pw|password|token|access_token|api_key|secret)=)[^&\s]*/gi, "$1[redacted]");
}

function writeEntry(entry) {
  const status = entry.status ?? "upgrade";
  const duration = entry.durationMs === undefined ? "" : ` ${entry.durationMs}ms`;
  console.log(`${entry.at} ${entry.method} ${entry.path} -> ${status}${duration}`);
  fs.appendFileSync(logPath, `${JSON.stringify(entry)}\n`);
}

function resolvePort(url) {
  if (url.port) {
    return Number.parseInt(url.port, 10);
  }

  return url.protocol === "https:" ? 443 : 80;
}

function appendForwardedFor(existing, remoteAddress) {
  const remote = remoteAddress?.replace(/^::ffff:/, "");
  if (!remote) {
    return existing;
  }

  if (Array.isArray(existing)) {
    return [...existing, remote].join(", ");
  }

  return existing ? `${existing}, ${remote}` : remote;
}
