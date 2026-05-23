---
sidebar_position: 5
title: Python Plugin
description: Build a native Python plugin from scratch.
---

# Python Plugin

Python plugins run as a subprocess, exchanging JSON over stdin/stdout. They're the right pick when a Python library makes the work much easier than reimplementing it in TypeScript.

## What you ship

```text
my-plugin/
├── manifest.yml
├── main.py
├── lib/                ← optional helpers
│   └── helpers.py
└── requirements.txt    ← optional, your concern (Prismedia does not pip-install for you)
```

The user installs the directory; Prismedia reads `manifest.yml`, spawns the script with the `manifest.script` command, sends the execution envelope on stdin, and reads the result on stdout.

## The wire protocol

Prismedia writes one JSON document on the subprocess's stdin (then closes stdin). The plugin runs to completion and writes one JSON document on stdout. That's the whole protocol.

**Single-item envelope:**

```json
{
  "prismedia_version": 1,
  "action": "movieByName",
  "auth": {
    "TMDB_API_KEY": "abc123def456"
  },
  "input": {
    "title": "Blade Runner",
    "date": "1982"
  }
}
```

**Batch envelope** (only sent if `capabilities.supportsBatch` is true):

```json
{
  "prismedia_version": 1,
  "action": "movieByName",
  "auth": { "TMDB_API_KEY": "abc123def456" },
  "batch": [
    { "id": "row-uuid-1", "input": { "title": "The Matrix" } },
    { "id": "row-uuid-2", "input": { "title": "The Matrix Reloaded" } }
  ]
}
```

**Single-item response:**

```json
{
  "ok": true,
  "result": {
    "title": "Blade Runner",
    "releaseDate": "1982-06-25",
    "...": "..."
  }
}
```

**Batch response:**

```json
{
  "ok": true,
  "results": [
    { "id": "row-uuid-1", "result": { "title": "The Matrix", "...": "..." } },
    { "id": "row-uuid-2", "result": null }
  ]
}
```

**Error response:**

```json
{
  "ok": false,
  "error": "TMDB rate-limited; try again later"
}
```

The plugin can also exit with a non-zero exit code to signal an error (the executor catches both shapes).

## A complete minimal plugin

```python title="main.py"
#!/usr/bin/env python3
import json
import sys
import urllib.request
import urllib.parse

TMDB_BASE = "https://api.themoviedb.org/3"

def http_json(url):
    with urllib.request.urlopen(url, timeout=15) as resp:
        return json.load(resp)

def search_movie(title, key):
    qs = urllib.parse.urlencode({"api_key": key, "query": title})
    data = http_json(f"{TMDB_BASE}/search/movie?{qs}")
    results = data.get("results") or []
    return results[0] if results else None

def fetch_detail(movie_id, key):
    qs = urllib.parse.urlencode({
        "api_key": key,
        "append_to_response": "credits,images",
    })
    return http_json(f"{TMDB_BASE}/movie/{movie_id}?{qs}")

def normalize(detail):
    return {
        "title": detail.get("title"),
        "originalTitle": detail.get("original_title"),
        "overview": detail.get("overview"),
        "releaseDate": detail.get("release_date"),
        "runtime": detail.get("runtime"),
        "genres": [g["name"] for g in detail.get("genres", [])],
        "studioName": (detail.get("production_companies") or [{}])[0].get("name"),
        "cast": [
            {"name": c["name"], "character": c.get("character"), "order": c.get("order")}
            for c in (detail.get("credits") or {}).get("cast", [])[:20]
        ],
        "posterCandidates": [
            {
                "url": f"https://image.tmdb.org/t/p/original{p['file_path']}",
                "language": p.get("iso_639_1"),
                "width": p.get("width"),
                "height": p.get("height"),
                "aspectRatio": p.get("aspect_ratio"),
            }
            for p in (detail.get("images") or {}).get("posters", [])
        ],
        "backdropCandidates": [
            {
                "url": f"https://image.tmdb.org/t/p/original{b['file_path']}",
                "width": b.get("width"),
                "height": b.get("height"),
                "aspectRatio": b.get("aspect_ratio"),
            }
            for b in (detail.get("images") or {}).get("backdrops", [])
        ],
        "logoCandidates": [],
        "externalIds": {"tmdb": str(detail["id"])},
        "rating": detail.get("vote_average"),
    }

def handle_movie_by_name(input_data, auth):
    key = auth.get("TMDB_API_KEY")
    if not key:
        return {"ok": False, "error": "TMDB_API_KEY not configured"}

    title = input_data.get("title") or input_data.get("name")
    if not title:
        return {"ok": True, "result": None}

    hit = search_movie(title, key)
    if not hit:
        return {"ok": True, "result": None}

    return {"ok": True, "result": normalize(fetch_detail(hit["id"], key))}

def main():
    envelope = json.load(sys.stdin)
    action = envelope.get("action")
    auth = envelope.get("auth", {})

    if action == "movieByName":
        if "batch" in envelope:
            results = [
                {"id": item["id"], "result": handle_movie_by_name(item["input"], auth).get("result")}
                for item in envelope["batch"]
            ]
            json.dump({"ok": True, "results": results}, sys.stdout)
        else:
            result = handle_movie_by_name(envelope.get("input", {}), auth)
            json.dump(result, sys.stdout)
        return

    json.dump({"ok": False, "error": f"unknown action: {action}"}, sys.stdout)

if __name__ == "__main__":
    main()
```

```yaml title="manifest.yml"
id: tmdb-py
name: TMDB (Python)
version: 0.1.0
runtime: python
script: ["python3", "main.py"]

auth:
  - key: TMDB_API_KEY
    label: TMDB API Key
    required: true
    url: https://www.themoviedb.org/settings/api

capabilities:
  movieByName: true
```

## Subprocess invocation details

The executor in `packages/plugins/src/executor.ts` does roughly this:

```ts
const [command, ...args] = manifest.script;       // ["python3", "main.py"]

const pythonPath = pluginsRootDir ?? path.dirname(installDir);
const env = {
  ...process.env,
  PYTHONPATH: pythonPath +
    (process.env.PYTHONPATH ? ':' + process.env.PYTHONPATH : ''),
};

const child = spawn(command, args, {
  cwd: installDir,
  stdio: ['pipe', 'pipe', 'pipe'],
  env,
});
```

So:

- **`cwd`** is your plugin's install directory.
- **`PYTHONPATH`** is set so `requires:` siblings resolve cleanly.
- **stderr** is captured and surfaced in worker logs and the failed-job error string.
- The first `script` item is executed exactly as written; use `python3` when that is the interpreter you require.

## Sharing helpers across plugins

If you have a helper package you want multiple plugins to share, put it in a sibling directory under `/data/plugins/` and add it to `requires:` in each plugin's manifest:

```yaml
runtime: python
script: ["python3", "main.py"]
requires:
  - musicbrainz-helpers
```

The executor adds `/data/plugins/` to `PYTHONPATH`, so `import musicbrainz_helpers` (or whatever the package name is) resolves.

## Dependencies

Prismedia does **not** run `pip install` for your plugin. If you need third-party libraries:

- **Vendor them** alongside the plugin (drop wheels or source under `lib/` and add to `sys.path`).
- **Or rely on the standard library** (the example above does — `urllib`, `json`, `sys`).

The unified Docker image ships Python 3 with the standard library only. Heavyweight requirements (pandas, requests with all extras, etc.) won't be available unless you include them in the plugin distribution.

## Errors

| What you do | Engine behavior |
| --- | --- |
| Print `{"ok": true, "result": null}` | "No match" — engine moves on. |
| Print `{"ok": true, "result": {…}}` | Normal accept path. |
| Print `{"ok": false, "error": "…"}` | Surfaced to the user as the error message. |
| Crash / non-zero exit | `PluginExecutionError` raised; UI shows "Plugin failed". |
| Print invalid JSON | Same as a crash — surfaced as a parse failure. |

## Logging

`print(...)` to **stderr**, not stdout — stdout is the response channel. Use:

```python
print("debug message", file=sys.stderr)
```

Stderr lines appear in `docker compose logs prismedia` under the worker process's output.

## Local development

The Python runtime re-spawns the subprocess on every invocation. There's no caching of imports. Edit your script and re-run identify; the change takes effect immediately.

```bash
docker compose exec prismedia ln -snf /workspace/my-plugin /data/plugins/my-plugin
```

is the same trick as for TypeScript plugins, with the bonus that you don't need a build step.

## Performance considerations

- Subprocess startup is **~50–150 ms** of Python interpreter cold start. For a single-item call that's fine; for batches of 100 items, implement `supportsBatch` and amortize the cost.
- Network calls are your wall-clock bottleneck in practice. Reuse connections (`urllib3`/`requests` Session) within one invocation; you can't reuse across invocations because the process exits.
- Don't fork or spawn child processes from the plugin itself unless you really need to — exit cleanly when you're done.

## Security notes

- Python plugins run with the worker's privileges. Only install plugins you trust.
- `auth` values are plain text in the envelope. Don't print them.
- File-system access is unrestricted; if you only need network, only do network.
