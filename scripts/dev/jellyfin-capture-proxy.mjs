#!/usr/bin/env node
// Jellyfin capture proxy — a temporary investigation tool.
//
// A logging reverse proxy: point a Jellyfin client (e.g. Infuse) at this proxy and it forwards
// every request to an upstream server (your real Jellyfin OR Prismedia) while writing the full
// request/response to a JSONL log. Use it to capture exactly what Infuse calls and what a server
// returns, so Prismedia's Jellyfin-compatibility layer can be matched against ground truth.
//
// It does not modify either server and forwards auth headers untouched, so the client signs in
// exactly as normal. Binary/streaming responses (video segments, images) are streamed through
// without buffering and logged as metadata only; JSON/text/m3u8 bodies are captured (size-capped).
//
// Usage:
//   node scripts/dev/jellyfin-capture-proxy.mjs --upstream http://jellyfin.local:8096 --port 8099 --out capture.jsonl
//
// Then point Infuse at http://<this-machine-LAN-IP>:8099 and browse/play. Watch the live console
// summary, and read the JSONL at --out for full detail.
//
// Flags:
//   --upstream <url>   Required. Base URL of the server to forward to (http or https).
//   --port <n>         Listen port for the proxy. Default 8099.
//   --out <path>       JSONL capture file. Default ./jellyfin-capture.jsonl
//   --max-body <bytes> Max captured body size for text/JSON. Default 262144 (256 KiB).
//   --insecure         Accept self-signed/invalid TLS certs on an https upstream.

import http from "node:http";
import https from "node:https";
import fs from "node:fs";
import zlib from "node:zlib";
import { URL } from "node:url";

function parseArgs(argv) {
  const args = { port: 8099, out: "jellyfin-capture.jsonl", maxBody: 262144, insecure: false };
  for (let i = 2; i < argv.length; i++) {
    const flag = argv[i];
    const next = () => argv[++i];
    if (flag === "--upstream") args.upstream = next();
    else if (flag === "--port") args.port = Number(next());
    else if (flag === "--out") args.out = next();
    else if (flag === "--max-body") args.maxBody = Number(next());
    else if (flag === "--insecure") args.insecure = true;
    else throw new Error(`Unknown flag: ${flag}`);
  }
  if (!args.upstream) throw new Error("Missing required --upstream <url>");
  return args;
}

const args = parseArgs(process.argv);
const upstream = new URL(args.upstream);
const upstreamClient = upstream.protocol === "https:" ? https : http;
const logStream = fs.createWriteStream(args.out, { flags: "a" });

// Capture text-ish bodies (JSON, plain text, HLS playlists); stream everything else through.
function isCapturableContentType(contentType) {
  if (!contentType) return false;
  const value = contentType.toLowerCase();
  return (
    value.includes("json") ||
    value.includes("text/") ||
    value.includes("mpegurl") || // .m3u8 playlists
    value.includes("xml")
  );
}

function nowIso() {
  return new Date().toISOString();
}

let requestCounter = 0;

const server = http.createServer((clientReq, clientRes) => {
  const id = ++requestCounter;
  const startedAt = Date.now();
  const reqChunks = [];

  clientReq.on("data", (chunk) => {
    if (Buffer.concat(reqChunks).length < args.maxBody) reqChunks.push(chunk);
  });

  clientReq.on("end", () => {
    const requestBody = Buffer.concat(reqChunks);
    const upstreamHeaders = { ...clientReq.headers, host: upstream.host };
    // Ask upstream for plain (uncompressed) bodies so captures are readable and the
    // size cap counts real characters. We still decompress as a fallback below.
    delete upstreamHeaders["accept-encoding"];

    const upstreamReq = upstreamClient.request(
      {
        protocol: upstream.protocol,
        hostname: upstream.hostname,
        port: upstream.port || (upstream.protocol === "https:" ? 443 : 80),
        method: clientReq.method,
        path: clientReq.url,
        headers: upstreamHeaders,
        rejectUnauthorized: !args.insecure,
      },
      (upstreamRes) => {
        const contentType = upstreamRes.headers["content-type"] ?? "";
        const capture = isCapturableContentType(contentType);

        clientRes.writeHead(upstreamRes.statusCode ?? 502, upstreamRes.headers);

        const resChunks = [];
        let capturedLength = 0;
        upstreamRes.on("data", (chunk) => {
          clientRes.write(chunk);
          if (capture && capturedLength < args.maxBody) {
            resChunks.push(chunk);
            capturedLength += chunk.length;
          }
        });
        upstreamRes.on("end", () => {
          clientRes.end();
          writeRecord({
            id,
            ts: nowIso(),
            durationMs: Date.now() - startedAt,
            method: clientReq.method,
            url: clientReq.url,
            client: pickClientIdentity(clientReq.headers),
            reqHeaders: clientReq.headers,
            reqBody: decodeBody(requestBody, clientReq.headers["content-type"], args.maxBody),
            status: upstreamRes.statusCode,
            resContentType: contentType,
            resHeaders: upstreamRes.headers,
            resBody: capture
              ? decodeBody(Buffer.concat(resChunks), contentType, args.maxBody, upstreamRes.headers["content-encoding"])
              : `<${contentType || "binary"} streamed, ${upstreamRes.headers["content-length"] ?? "?"} bytes — not captured>`,
          });
        });
      },
    );

    upstreamReq.on("error", (err) => {
      clientRes.writeHead(502, { "content-type": "text/plain" });
      clientRes.end(`capture-proxy upstream error: ${err.message}`);
      writeRecord({
        id,
        ts: nowIso(),
        method: clientReq.method,
        url: clientReq.url,
        error: err.message,
      });
    });

    if (requestBody.length > 0) upstreamReq.write(requestBody);
    upstreamReq.end();
  });
});

function decodeBody(buffer, contentType, max, contentEncoding) {
  if (!buffer || buffer.length === 0) return null;
  // Fallback: if upstream compressed anyway (we strip accept-encoding, but be safe),
  // decompress before decoding. A truncated (size-capped) body may fail to inflate;
  // fall through to the raw bytes in that case.
  const enc = (contentEncoding ?? "").toLowerCase();
  if (enc) {
    try {
      if (enc.includes("gzip")) buffer = zlib.gunzipSync(buffer);
      else if (enc.includes("br")) buffer = zlib.brotliDecompressSync(buffer);
      else if (enc.includes("deflate")) buffer = zlib.inflateSync(buffer);
    } catch {
      return `<${enc}-encoded ${buffer.length} bytes — could not decompress (likely truncated by --max-body)>`;
    }
  }
  const text = buffer.subarray(0, max).toString("utf8");
  if ((contentType ?? "").toLowerCase().includes("json")) {
    try {
      return JSON.parse(text);
    } catch {
      return text;
    }
  }
  return text;
}

function pickClientIdentity(headers) {
  return {
    client: headers["x-mediabrowser-client"] ?? headers["x-emby-client"],
    device: headers["x-mediabrowser-device"] ?? headers["x-emby-device"],
    version: headers["x-mediabrowser-version"] ?? headers["x-emby-version"],
    userAgent: headers["user-agent"],
  };
}

function writeRecord(record) {
  logStream.write(`${JSON.stringify(record)}\n`);
  const status = record.error ? `ERR ${record.error}` : record.status;
  // eslint-disable-next-line no-console
  console.log(`[${record.id}] ${record.method} ${record.url} -> ${status}`);
}

server.listen(args.port, () => {
  // eslint-disable-next-line no-console
  console.log(
    `Jellyfin capture proxy listening on http://0.0.0.0:${args.port}\n` +
      `  forwarding to ${upstream.origin}\n` +
      `  writing capture to ${args.out}\n` +
      `Point your Jellyfin client at http://<this-machine-LAN-IP>:${args.port}`,
  );
});
