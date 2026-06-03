#!/usr/bin/env python3
"""
Playback parity harness: A/B the Prismedia and Jellyfin stream-decision engines.

For each file in the shared Sample Media library it ffprobes ground truth, then
POSTs the SAME browser DeviceProfile (profiles.json) to BOTH servers' PlaybackInfo
endpoint and records each verdict (DirectPlay / Remux / Transcode + chosen codecs +
transcode reasons). Optionally measures time-to-first-segment for the produced stream.

The point is concrete, re-runnable evidence of where Jellyfin direct-plays/remuxes a
file that Prismedia force-transcodes (a perf gap) or produces a stream the browser
cannot actually decode (a "won't play" gap) — and a regression metric to prove the
fixes land.

Usage:
  python3 parity.py                      # all profiles, no TTFF, markdown to stdout + JSON
  python3 parity.py --profiles chrome-mac,firefox
  python3 parity.py --ttff               # also time first-segment delivery (slower)
  python3 parity.py --out results        # write results/<timestamp>.{md,json}

Prereqs: jf-chair bench provisioned (./provision-jellyfin.sh), Prismedia API on :8008,
docker postgres reachable for the Prismedia id->path map, ffprobe on PATH.
"""
import argparse
import json
import os
import subprocess
import sys
import time
import urllib.parse
import urllib.request

PRISMEDIA = os.environ.get("PRISMEDIA_URL", "http://localhost:8008")
PRISMEDIA_KEY = os.environ.get("PRISMEDIA_KEY", "skabi-ravi-paya")
JELLYFIN = os.environ.get("JF_URL", "http://localhost:8098")
JF_USER = os.environ.get("JF_USER", "admin")
JF_PASS = os.environ.get("JF_PASS", "benchpass123")
FFPROBE = os.environ.get("FFPROBE", os.path.expanduser("~/.local/bin/ffprobe"))
PG_CONTAINER = os.environ.get("PG_CONTAINER", "docker-postgres-1")
HERE = os.path.dirname(os.path.abspath(__file__))


def http(method, url, headers=None, body=None, timeout=30):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers=headers or {})
    if data is not None:
        req.add_header("Content-Type", "application/json")
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return r.status, r.read()


def jget(url, timeout=30):
    s, b = http("GET", url, timeout=timeout)
    return json.loads(b)


# ---- ground truth -----------------------------------------------------------
def ffprobe(path):
    out = subprocess.run(
        [FFPROBE, "-v", "quiet", "-print_format", "json", "-show_format", "-show_streams", path],
        capture_output=True, text=True,
    ).stdout
    data = json.loads(out) if out else {}
    v = next((s for s in data.get("streams", []) if s.get("codec_type") == "video"), {})
    a = next((s for s in data.get("streams", []) if s.get("codec_type") == "audio"), {})
    transfer = v.get("color_transfer", "")
    side = json.dumps(v.get("side_data_list", []))
    if "dovi" in side.lower() or "DOVI configuration" in side:
        rng = "DOVI"
    elif transfer == "smpte2084":
        rng = "HDR10"
    elif transfer == "arib-std-b67":
        rng = "HLG"
    else:
        rng = "SDR"
    fr = v.get("avg_frame_rate", "0/1")
    try:
        num, den = fr.split("/")
        fps = round(int(num) / int(den), 2) if int(den) else 0
    except Exception:
        fps = 0
    return {
        "container": os.path.splitext(path)[1].lstrip(".").lower(),
        "vcodec": v.get("codec_name", "?"),
        "vprofile": v.get("profile", ""),
        "level": v.get("level", ""),
        "bitdepth": v.get("bits_per_raw_sample") or ("10" if "10" in (v.get("pix_fmt") or "") else "8"),
        "range": rng,
        "height": v.get("height", ""),
        "fps": fps,
        "acodec": a.get("codec_name", "?"),
        "achannels": a.get("channels", ""),
    }


# ---- decision parsing -------------------------------------------------------
def norm_codec(c):
    c = (c or "").lower()
    return {"h265": "hevc", "h.265": "hevc", "h.264": "h264", "avc": "h264"}.get(c, c)


def classify(media_source, source_vcodec=""):
    """Reduce a Jellyfin/Prismedia MediaSource into a verdict string + detail.

    A stream is a remux (video copy) when the target VideoCodec equals the source
    codec, or is literally 'copy', or the URL is the dedicated remux rendition.
    Jellyfin expresses HLS video-copy by naming the source codec, not 'copy'.
    """
    if media_source is None:
        return "ERROR", "", ""
    turl = media_source.get("TranscodingUrl")
    supports_direct = media_source.get("SupportsDirectPlay")
    if not turl:
        if supports_direct:
            return "DirectPlay", "", ""
        return "NoSource", "", ""
    q = urllib.parse.parse_qs(urllib.parse.urlparse(turl).query)
    vcodec = norm_codec(q.get("VideoCodec", [""])[0])
    acodec = (q.get("AudioCodec", [""])[0] or "").lower()
    reasons = q.get("TranscodeReasons", q.get("transcodeReasons", [""]))[0]
    src = norm_codec(source_vcodec)
    is_video_copy = (media_source.get("VideoStreamCopy")
                     or vcodec == "copy" or "remux" in turl.lower()
                     or (src and vcodec == src))
    if is_video_copy:
        return "Remux", f"v=copy a={acodec or 'aac'}", reasons
    return "Transcode", f"v={vcodec or 'h264'} a={acodec or 'aac'}", reasons


def playback_info(base, item_id, profile, auth_header=None, query=""):
    url = f"{base}/Items/{item_id}/PlaybackInfo{query}"
    body = {
        "DeviceProfile": profile,
        "EnableDirectPlay": True,
        "EnableDirectStream": True,
        "EnableTranscoding": True,
        "AllowVideoStreamCopy": True,
        "AllowAudioStreamCopy": True,
        "MaxStreamingBitrate": profile.get("MaxStreamingBitrate", 120000000),
        "AutoOpenLiveStream": True,
    }
    headers = dict(auth_header or {})
    try:
        s, b = http("POST", url, headers=headers, body=body, timeout=40)
        data = json.loads(b)
        sources = data.get("MediaSources") or []
        return sources[0] if sources else None
    except Exception as e:
        return {"_error": str(e)}


def _with_key(url, key_query):
    if key_query and "api_key=" not in url:
        return url + ("&" if "?" in url else "?") + key_query
    return url


def _first_child(playlist):
    return next((ln.strip() for ln in playlist.splitlines()
                 if ln.strip() and not ln.startswith("#")), None)


def ttff(base, transcoding_url, auth_header=None, key_query=""):
    """Time from playlist request to the FIRST DECODABLE SEGMENT's first byte.

    Follows one level of master->variant playlist indirection so the number is
    real time-to-first-segment (cold-start transcode cost), not just the time to
    fetch the multivariant playlist. Best effort; returns None on failure.
    """
    if not transcoding_url:
        return None
    url = _with_key(transcoding_url if transcoding_url.startswith("http") else base + transcoding_url, key_query)
    headers = auth_header or {}
    t0 = time.time()
    try:
        with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=120) as r:
            playlist = r.read().decode(errors="ignore")
        child = _first_child(playlist)
        if not child:
            return None
        child_url = _with_key(child if child.startswith("http") else url.rsplit("/", 1)[0] + "/" + child, key_query)
        # If the first child is itself a playlist (master->variant), descend one level.
        if ".m3u8" in child_url.split("?")[0]:
            with urllib.request.urlopen(urllib.request.Request(child_url, headers=headers), timeout=120) as r:
                variant = r.read().decode(errors="ignore")
            seg = _first_child(variant)
            if not seg:
                return None
            seg_url = _with_key(seg if seg.startswith("http") else child_url.rsplit("/", 1)[0] + "/" + seg, key_query)
        else:
            seg_url = child_url
        with urllib.request.urlopen(urllib.request.Request(seg_url, headers=headers), timeout=120) as r:
            r.read(1)
        return round(time.time() - t0, 2)
    except Exception:
        return None


# ---- library mapping --------------------------------------------------------
def prismedia_map():
    sql = ("select e.id, f.path from entities e join entity_files f on f.entity_id=e.id "
           "where e.kind_code in ('video','movie') and f.role='source';")
    out = subprocess.run(
        ["docker", "exec", PG_CONTAINER, "psql", "-U", "prismedia", "-d", "prismedia",
         "-F", "\t", "-tAc", sql], capture_output=True, text=True).stdout
    m = {}
    for line in out.strip().splitlines():
        if "\t" in line:
            eid, path = line.split("\t", 1)
            m[os.path.basename(path)] = {"id": eid, "path": path}
    return m


def jellyfin_setup():
    auth = ('MediaBrowser Client="parity", Device="cli", DeviceId="parity-cli", Version="1.0"')
    s, b = http("POST", f"{JELLYFIN}/Users/AuthenticateByName",
                headers={"X-Emby-Authorization": auth},
                body={"Username": JF_USER, "Pw": JF_PASS})
    data = json.loads(b)
    token = data["AccessToken"]
    uid = data["User"]["Id"]
    items = jget(f"{JELLYFIN}/Items?IncludeItemTypes=Movie,Episode,Video&Recursive=true"
                 f"&Fields=Path&userId={uid}&api_key={token}")
    jmap = {}
    for it in items.get("Items", []):
        p = it.get("Path")
        if p:
            jmap[os.path.basename(p)] = it["Id"]
    return token, uid, jmap


# ---- main -------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--profiles", default="chrome-mac,firefox,safari")
    ap.add_argument("--ttff", action="store_true")
    ap.add_argument("--only", default="", help="substring filter on filename (targeted runs)")
    ap.add_argument("--out", default="")
    args = ap.parse_args()

    profiles_all = json.load(open(os.path.join(HERE, "profiles.json")))
    profiles = {k: profiles_all[k] for k in args.profiles.split(",") if k in profiles_all}

    print("==> Authenticating to Jellyfin + listing items", file=sys.stderr)
    token, uid, jmap = jellyfin_setup()
    pmap = prismedia_map()
    shared = sorted(set(pmap) & set(jmap))
    if args.only:
        shared = [n for n in shared if args.only.lower() in n.lower()]
    print(f"==> {len(shared)} files shared between servers "
          f"(prismedia={len(pmap)} jellyfin={len(jmap)})", file=sys.stderr)

    jf_auth = {"X-Emby-Token": token}
    pm_q = f"?api_key={PRISMEDIA_KEY}"
    jf_q = f"?userId={uid}&api_key={token}"

    rows = []
    for name in shared:
        probe = ffprobe(pmap[name]["path"])
        print(f"  - {name[:55]:55} {probe['vcodec']}/{probe['vprofile']} "
              f"{probe['range']} {probe['height']}p {probe['acodec']}", file=sys.stderr)
        per_profile = {}
        for pname, profile in profiles.items():
            pm_src = playback_info(PRISMEDIA, pmap[name]["id"], profile, query=pm_q)
            jf_src = playback_info(JELLYFIN, jmap[name], profile, auth_header=jf_auth, query=jf_q)
            pm_v, pm_d, pm_r = classify(pm_src, probe["vcodec"])
            jf_v, jf_d, jf_r = classify(jf_src, probe["vcodec"])
            entry = {
                "prismedia": {"verdict": pm_v, "detail": pm_d, "reasons": pm_r,
                              "transcodingUrl": (pm_src or {}).get("TranscodingUrl")},
                "jellyfin": {"verdict": jf_v, "detail": jf_d, "reasons": jf_r,
                             "transcodingUrl": (jf_src or {}).get("TranscodingUrl")},
                "agree": pm_v == jf_v,
            }
            if args.ttff:
                entry["prismedia"]["ttff_s"] = ttff(PRISMEDIA, (pm_src or {}).get("TranscodingUrl"),
                                                    key_query=f"api_key={PRISMEDIA_KEY}")
                entry["jellyfin"]["ttff_s"] = ttff(JELLYFIN, (jf_src or {}).get("TranscodingUrl"),
                                                   auth_header=jf_auth, key_query=f"api_key={token}")
            per_profile[pname] = entry
        rows.append({"name": name, "probe": probe, "profiles": per_profile})

    md = render_markdown(rows, list(profiles), args.ttff)
    print(md)
    if args.out:
        os.makedirs(os.path.join(HERE, args.out), exist_ok=True)
        stamp = time.strftime("%Y%m%d-%H%M%S")
        base = os.path.join(HERE, args.out, stamp)
        open(base + ".md", "w").write(md)
        json.dump(rows, open(base + ".json", "w"), indent=2)
        print(f"\n==> wrote {base}.md and {base}.json", file=sys.stderr)


def render_markdown(rows, profile_names, with_ttff):
    out = ["# Playback parity: Prismedia vs Jellyfin\n",
           f"_Generated {time.strftime('%Y-%m-%d %H:%M:%S')} · identical DeviceProfile sent to both engines_\n"]
    disagreements = 0
    for pname in profile_names:
        out.append(f"\n## Profile: `{pname}`\n")
        hdr = "| File | Source | Prismedia | Jellyfin | Match |"
        sep = "|------|--------|-----------|----------|:-----:|"
        if with_ttff:
            hdr = "| File | Source | Prismedia | TTFF | Jellyfin | TTFF | Match |"
            sep = "|------|--------|-----------|:----:|----------|:----:|:-----:|"
        out += [hdr, sep]
        for r in rows:
            p = r["profiles"][pname]
            src = f"{r['probe']['vcodec']} {r['probe']['vprofile']} {r['probe']['range']} {r['probe']['height']}p {r['probe']['acodec']}{r['probe']['achannels']}"
            pm = f"**{p['prismedia']['verdict']}** {p['prismedia']['detail']}".strip()
            jf = f"**{p['jellyfin']['verdict']}** {p['jellyfin']['detail']}".strip()
            if p["jellyfin"]["reasons"]:
                jf += f"<br><sub>{p['jellyfin']['reasons']}</sub>"
            match = "✅" if p["agree"] else "❌"
            if not p["agree"]:
                disagreements += 1
            short = r["name"][:42]
            if with_ttff:
                pt = p["prismedia"].get("ttff_s"); jt = p["jellyfin"].get("ttff_s")
                out.append(f"| {short} | {src} | {pm} | {pt if pt is not None else '–'} | {jf} | {jt if jt is not None else '–'} | {match} |")
            else:
                out.append(f"| {short} | {src} | {pm} | {jf} | {match} |")
    out.append(f"\n**Decision disagreements: {disagreements}** "
               f"(each is a parity gap — Jellyfin and Prismedia choose differently for the same browser).\n")
    return "\n".join(out)


if __name__ == "__main__":
    main()
