#!/bin/zsh
# The browser game's funnel, read out of Cloud Run's request log.
#
# There is no analytics product behind this. The page fires one no-cors request
# per event at a deliberately-nonexistent path on our own API; Cloud Run logs
# every request including 404s, so the request log IS the funnel. Events carry
# an ephemeral random id (i=) generated in memory per page load and never
# persisted, which is what lets hits be grouped into sessions without anything
# that could follow a person anywhere.
#
#   usage: zsh web_funnel.zsh [freshness] [--all]
#          freshness  e.g. 1d, 7d, 30d   (default 7d)
#          --all      include this machine's own hits (default: excluded)
set -e
FRESH="${1:-7d}"
INCLUDE_MINE="${2:-}"
SVC='resource.type="cloud_run_revision" AND resource.labels.service_name="rb-api"'
PATH_F='httpRequest.requestUrl:"/v1/beacon/web-play"'

MINE=$(curl -s -m 10 https://api.ipify.org 2>/dev/null || echo "")
gcloud logging read "$SVC AND $PATH_F" --freshness="$FRESH" --limit=10000 \
  --format='value(httpRequest.requestUrl,httpRequest.remoteIp,httpRequest.userAgent)' \
  2>/dev/null > /tmp/_funnel_raw.tsv || true

MINE="$MINE" INCLUDE_MINE="$INCLUDE_MINE" FRESH="$FRESH" python3 <<'PY'
import os, re, collections
mine, include = os.environ.get("MINE",""), os.environ.get("INCLUDE_MINE","") == "--all"
ev = collections.defaultdict(dict)      # id -> {event: extra}
dev_of, skipped = {}, 0
for line in open("/tmp/_funnel_raw.tsv"):
    p = line.rstrip("\n").split("\t")
    if len(p) < 2: continue
    url, ip = p[0], p[1]; ua = p[2] if len(p) > 2 else ""
    if not include and mine and ip == mine: skipped += 1; continue
    q = dict(re.findall(r"[?&]([a-z]+)=([^&\s]*)", url))
    e, sid = q.get("e"), q.get("i") or ("ip:" + ip)   # pre-id hits fall back to IP
    if not e: continue
    ev[sid][e] = q
    dev_of.setdefault(sid, ua)

def dev(ua):
    for k in ("iPhone","iPad","Android","Macintosh","Windows"):
        return_k = {"Macintosh":"Mac"}.get(k,k)
        if k in ua: return return_k
    return "other"

STEPS = [("open","opened the page"), ("ready","game became playable"),
         ("boot","scene first frame"),
         ("door","chose a door"), ("build","placed a part"),
         ("saved","founded a robot"), ("fight","started a fight"),
         ("result","finished a fight"), ("return","came back later")]
total = len(ev)
print(f"== Robot Brawl web funnel · last {os.environ['FRESH']} ==")
if skipped: print(f"   ({skipped} hits from this machine excluded; pass --all to include)")
if not total: print("   no sessions recorded yet."); raise SystemExit
print(f"   {total} sessions\n")
prev = None
for key, label in STEPS:
    n = sum(1 for s in ev.values() if key in s)
    if key == "return":
        print(f"   {'':<22} {'':>6}")
        print(f"   {label:<22} {n:>6}   {round(100*n/total):>3}% of all sessions")
        continue
    bar = "#" * round(30 * n / total) if total else ""
    drop = "" if prev is None or prev == 0 else f"   ({n-prev:+d} from previous step)"
    print(f"   {label:<22} {n:>6}   {round(100*n/total):>3}%  {bar}{drop}")
    prev = n

# ---- THE WALL ----------------------------------------------------------
# The number this whole exercise exists to produce: of the people who were
# actually SHOWN the sign-in screen, how many walked away without pressing
# anything — not even PLAY AS GUEST. Sessions that skipped the wall (a stored
# session, or a guest who already answered) are excluded, because counting
# them as non-bouncers would flatter the number and counting them as bouncers
# would invent one.
shown = [s for s in ev.values() if s.get("gate", {}).get("d") == "shown"]
skipped = sum(1 for s in ev.values() if s.get("gate", {}).get("d") == "skip")
if shown:
    chose = sum(1 for s in shown if "door" in s)
    bounced = len(shown) - chose
    print(f"\n   THE SIGN-IN WALL")
    print(f"     shown it        {len(shown):>4}")
    print(f"     pressed a door  {chose:>4}   {round(100*chose/len(shown))}%")
    print(f"     WALKED AWAY     {bounced:>4}   {round(100*bounced/len(shown))}%  <- bounced off the wall")
auto = sum(1 for s in ev.values() if s.get("gate", {}).get("d") == "auto")
if skipped:
    print(f"     (+{skipped} sessions never saw it: stored session or returning guest)")
if auto:
    print(f"     (+{auto} sessions booted straight to the workshop: the web wall is gone)")

# ---- THE LOAD GAP ------------------------------------------------------
# Half of phone arrivals never became playable and the beacon could not say
# whether they left or died (2026-09-04). Now it can: `load` marks progress
# milestones (the parser keeps the LAST hit per name and they fire in order,
# so load.p is the furthest reached), `leave` (sent on pagehide, keepalive)
# carries the phase reached, `error` carries a kind. A session with no ready,
# no leave and no error is a tab that died without saying goodbye.
notready = [s for s in ev.values() if "ready" not in s]
if notready:
    furthest = collections.Counter(s.get("load", {}).get("p", "0") for s in notready)
    left  = collections.Counter(s["leave"].get("at","?") for s in notready if "leave" in s)
    errs  = collections.Counter(s["error"].get("k","?") for s in notready if "error" in s)
    silent = sum(1 for s in notready if "leave" not in s and "error" not in s)
    print(f"\n   THE LOAD GAP  ({len(notready)} sessions never became playable)")
    print("     furthest milestone:   " + ", ".join(f"{k}% x{v}" for k,v in sorted(furthest.items(), key=lambda kv: int(kv[0]) if kv[0].isdigit() else -1)))
    if left:  print("     left, at phase:       " + ", ".join(f"{k} x{v}" for k,v in left.most_common()))
    if errs:  print("     errored, kind:        " + ", ".join(f"{k} x{v}" for k,v in errs.most_common()))
    print(f"     silent (no leave, no error - the tab died?): {silent}")
booted = sum(1 for s in ev.values() if "boot" in s)
readyn = sum(1 for s in ev.values() if "ready" in s)
if readyn: print(f"   playable -> first scene frame: {booted}/{readyn}" + ("" if booted >= readyn else f"   ({readyn-booted} lost between the loader and the scene)"))

wins = sum(1 for s in ev.values() if s.get("result",{}).get("w") == "1")
fin  = sum(1 for s in ev.values() if "result" in s)
if fin: print(f"\n   of {fin} finished fights, {wins} were WINS ({round(100*wins/fin)}%)")
doors = collections.Counter(s["door"].get("d","?") for s in ev.values() if "door" in s)
if doors: print("   doors taken: " + ", ".join(f"{k} {v}" for k,v in doors.most_common()))
else:     print("   doors taken: none")
byd = collections.Counter(dev(dev_of.get(sid,"")) for sid in ev)
print("   devices: " + ", ".join(f"{k} {v}" for k,v in byd.most_common()))
slow = [int(s["ready"]["s"]) for s in ev.values() if "ready" in s and s["ready"].get("s","").isdigit()]
if slow:
    slow.sort()
    print(f"   load seconds: median {slow[len(slow)//2]}s · p90 {slow[int(len(slow)*0.9)-1 if len(slow)>1 else 0]}s · slowest {slow[-1]}s")
PY
