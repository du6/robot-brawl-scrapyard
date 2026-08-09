# Brief for a Claude Code CLI session — written 2026-08-09

You are Claude Code running on owen's Mac, in
`/Users/leondu/Setup Guide In-Editor Tutorial`. This brief exists because
the work on this project has split cleanly in two, and you are the better
tool for one half.

**Read `../CLAUDE.md` first**, then `SESSION_HANDOVER_2026-08-09_late.md`
(current), then this. Everything referenced here is mirrored into `docs/`
— you do not need network access or the claude.ai Project to work.

---

## 0. Check `_relay/inbox/` first

A Cowork session may have left work for you. `../_relay/README.md` is the
protocol — read it once and you will not need owen to explain anything.
The short version: lowest-numbered `.req.md` in `_relay/inbox/` is yours,
`mv` it to `_relay/working/`, do it, write the result to
`_relay/outbox/<same-number>.res.md`, `mv` the request to
`_relay/archive/`, and keep `_relay/STATUS.cli.md` current.

**There is a request waiting: `001-clone-build.req.md`.** It is §3 of this
brief, written up properly, and it is a good first job — small, entirely
shell, and it closes the last open question on the git thread.

A result file is expected to carry commands and exit codes verbatim,
numbers rather than adjectives, an explicit list of what you could not
verify, and **anything in the request that turned out to be wrong.** That
last one is not politeness — the requests are written by a session that
cannot run your commands, and one has already been confidently wrong.

---

## 1. The split, and why it exists

Work on this project has been driven from **Cowork**, which reaches the
Mac two ways: a mounted copy of this folder (read/write) and the **Unity
MCP bridge**, which compiles and runs C# inside owen's live editor. That
is how every bench in this project gets run.

What a Cowork session **cannot** do: run anything in the macOS shell. Its
`bash` is a separate Linux VM (aarch64) with the project folder mounted
and **no network, no `dotnet`, no `psql`, no Homebrew, and no route to the
Mac's `localhost:5000`**. All of that was confirmed empirically, not
assumed.

So:

| | Cowork session | you (CLI) |
|---|---|---|
| edit files here | yes | yes |
| `git`, `rm` | **no** (see §5) | yes |
| `dotnet`, `psql`, `curl localhost:5000` | **no** | yes |
| run an edit-mode Unity bench | **yes** | **yes — measured** |
| run a PLAY-MODE Unity bench | **yes** | **unproven** |

> ⚠ **SUPERSEDED — read `docs/HANDOVER_TO_CLI.md` instead.** The paragraph
> below is wrong and is kept only as the record of how it went wrong. A CLI
> session CAN drive the editor: `unity-mcp` was registered and connected the
> whole time, and `CategoryBench.RunPure()` returned **44 pass, 0 fail**
> from one. Nobody who wrote "probably cannot" ever tested it. Play-mode
> benches remain unproven from a CLI session — that part is still open.

**The consequence you must internalise: you probably cannot verify
Unity-side work.** VerbBench, ReplayBench, MatrixBench, ProgramBench,
CareerSmoke, CategoryBench and the rest run inside play mode over the MCP
bridge. If you change something under `Assets/` without that bridge, say
plainly that it is **unverified** and leave it for a Cowork session or
owen to bench. Do not report it as green. This project's whole discipline
is that a claim without a measurement is not a result.

### Ownership, so two sessions never fight

- **You own `server/**`, `.gitignore`, git, and the shell.**
- **A Cowork session owns `Assets/**` and the editor.**
- `docs/` and `CLAUDE.md`: either, but say so in your handover.

---

## 2. Option zero — try this before accepting the split

`unity-mcp` is a **local MCP server on this Mac**. Cowork reaches it
proxied through the desktop app. If you register the same server with
Claude Code, you get shell **and** git **and** the editor — strictly more
than either session has today, and the split above stops mattering.

Worth ten minutes before anything else:

```sh
claude mcp list                 # is unity-mcp already here?
```

If not, find how the desktop app launches it (Unity: *Project Settings →
Unity MCP Server → Integrations* shows the configured clients) and add the
same command with `claude mcp add`. Confirm it works by running the
cheapest bench in the project — it is pure data, needs no play mode, and
takes under a second:

```
RobotBrawl.Phase0.CategoryBench.RunPure()
```

Expect **44 pass, 0 fail** and a rewritten
`Assets/Phase1/qa_category_bench.txt`.

⚠ Gotcha carried from the Cowork side: the desktop app spawns MCP servers
**at app launch**, so a config that reads "Configured" can still be
invisible to a running session. If you hit that class of problem, a full
restart with Unity already open is the fix.

⚠ Unity `RunCommand` traps, if you do get the bridge: game types live in
namespace `RobotBrawl.Phase0` (`RobotBrawl.Phase0.BuilderManager`, not
`BuilderManager` and not `global::BuilderManager`); the command class must
be named `CommandScript` and be `internal`; and `result.Log` only
substitutes `{0}` and `{1}` — pre-format anything longer.

---

## 3. Job 1 — prove a fresh clone builds

⚠ **Correction, made 2026-08-09 while verifying this brief.** An earlier
draft told you to fix the git index. **owen already did, at 11:10 today.**
Verified by decoding `.git` directly rather than by trusting the handover:
HEAD is **`651d5670`**, parent `a456ac0`, message *"Untrack .NET build
output; track the hand-written server csproj"*. In HEAD's tree,
`server/RobotBrawl.Api/RobotBrawl.Api.csproj` is present, and the index
carries **19 `server/` entries with zero under `bin/` or `obj/`**. The
handover chain still says this is outstanding; it is not. Trust `git`
over any doc, including this one.

**What is actually left is the acceptance test, which nobody has run.**
The point of that commit was that a fresh clone can build the API. That
was never demonstrated — no session before you had both `git` and
`dotnet`:

```sh
cd "/Users/leondu/Setup Guide In-Editor Tutorial"
git status                      # see §8 on what is uncommitted
git clone . /tmp/rb-clone-check
cd /tmp/rb-clone-check/server/RobotBrawl.Api
export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec"
dotnet build
```

A clean build from a fresh clone is the acceptance. Delete the clone
afterwards. If it fails, the failure names the next missing tracked file
and you are still the only session that can fix it.

**Two small things the same pass should sweep up:**

- **`server/qa_api_server.log` is still tracked** even though `.gitignore`
  line 110 now lists it. Ignore rules do not untrack — it needs
  `git rm --cached`. It is a run log with machine-specific paths.
- **`server/qa_sql_bench.txt` is untracked.** Its two siblings
  (`qa_api_smoke.txt`, `qa_api_server.log`) are tracked, and the `qa_*`
  artifacts are tracked on purpose because the records quote them. Pick
  one rule for all three and apply it.

**And commit the Unity side.** `Assets/Phase1/Scripts/RobotCategory.cs`
and `CategoryBench.cs` are **not in the index** — new today, proven
(44/44), and existing in exactly one copy. Start with `git status`: I
could not determine from `.git` alone how much of 2026-08-08–09's
`Assets/` work is already committed, and guessing at that is precisely
the kind of thing this project's records get wrong.

## 4. Job 2 — prove the server still boots green

One command. It handles the keg-only Homebrew PATH itself:

```sh
zsh server/tests/run_local.sh
```

It resets the dev database, builds, boots, waits for `/healthz`, runs
`sql_bench.sh`, runs `api_smoke.sh`, stops, and leaves three files on
disk: `server/qa_api_server.log`, `server/qa_sql_bench.txt`,
`server/qa_api_smoke.txt`.

**Expected, as of 2026-08-09: `sql_bench` 34/34 · `api_smoke` 37/37, 0
skipped · 0 lines in the server log mentioning exception or error.**
Anything less is a regression against a measured baseline, not a mystery.

Environment, for when something breaks:

- `dotnet@8` (8.0.129) and `postgresql@16`, both **Homebrew keg-only**.
- Postgres is a **local brew install, not Docker** — owen has no Docker,
  and the Dockerfile has never been built. `docker-compose.yml` is
  aspirational; ignore it.
- Local DB is `rb`/`rb`/`rb` (user/password/database) on `localhost`.
- The API refuses to boot without `PG_CONN`, `JWT_SECRET`, `WORKER_KEY`
  and `BLOB_ROOT`; `run_local.sh` supplies dev-only values inline. That
  refusal is deliberate — a default signing key is a key everyone has.
- Port 5000. The script aborts early and tells you if it is already in
  use.

---

## 5. Job 3 — the FIGHT half of the worker contract

This is the real server work, it is entirely C# and SQL, and **it is fully
benchable without Unity** — which makes it exactly your job.

M1's VALIDATE path is complete and proven end to end on the API side. The
FIGHT path does not exist at all: **no match-create endpoint, no
match-result endpoint, no replay upload.** §5.3 of
`Multiplayer_V3_Design_Doc.md` specifies the four-step lifecycle in prose
and none of steps 1–4 exists in code. The schema is ready — `matches`,
and `match_jobs.kind = 'FIGHT'` with a CHECK enforcing job shape — and as
of 2026-08-09 a FIGHT claim already returns both snapshots' storage URLs
and hashes plus `arena` and `seeds`, so the worker side of the contract is
waiting on nothing.

Suggested order, smallest first:

1. **`/v1/auth/register` is not transactional.** The user INSERT and the
   500-scrap `SIGNING_BONUS` ledger INSERT share a connection with no
   transaction. Small, safe, and there is already a bench to re-run.
2. **Pin the category mapping from the server side.** `SnapshotMeta.
   category` is `""` for "no category"; `ValidateResult.Category` is
   `string?` stored as SQL NULL. The `snapshots` CHECK allows NULL but
   **not** the empty string, so a worker that sends `""` fails at the far
   end of a job. Add the `api_smoke` checks that prove NULL is accepted
   and `""` is refused, before a worker exists to get it wrong.
3. **Match create + match result + replay upload**, per §5.3 — escrow and
   the match row in one transaction, worker signature and job ownership
   verified, ledger settled append-only. Extend `sql_bench.sh` and
   `api_smoke.sh` as you go.

⚠ **Two benches, two jobs — know which one covers what.** `sql_bench.sh`
is the *only* cover for `server/RobotBrawl.Api/Sql/*.sql`, including the
four-concurrent-worker `SKIP LOCKED` case. `api_smoke.sh` covers the
endpoints. `run_local.sh` runs both, in that order, and it only started
doing so on 2026-08-09 — before that a SQL change could come back
all-green from the one command having never been run against the bench
that tests it. Do not add a route that skips one of them.

⚠ **And the lesson that produced this brief's whole tone:** `api_smoke`
was 35/35 green while the claim response was returning a UUID no worker
could resolve. Each endpoint answered correctly in isolation; no consumer
had ever tried to *complete* a job. **An endpoint suite that never runs
the loop it exists to serve is testing its own reachability.** When you
add the FIGHT endpoints, add at least one check that walks the whole
lifecycle.

---

## 6. Hard rules — these are not style preferences

1. **Owner state is sacred.** The career save at
   `~/Library/Application Support/owen/Robot Brawl_ Bolt & Blade/robotbrawl_career.json`
   must come back byte-identical: md5 **`18614d0e6603869f28fb65992b7d1484`**,
   mtime 2026-08-05 21:57. Verify at session start and after anything that
   could touch it. Timestamped backups go in `career_backups/`.
2. **There is no `origin` remote.** Pushes go by full URL with a token
   read from `.gh_token.local` (gitignored — never print it). See
   `push_m0_main.zsh`, `checkin.zsh`, `push_main.zsh`. The local history
   and the curated GitHub history have **different roots**; if a push is
   rejected as non-fast-forward, work out the merge. **Never force-push.**
3. **Where a quality is measurable, measure it over everything rather
   than over a named list.**
4. **When a check fails, ask whether the CHECK is wrong before changing
   the product.** It was the check three separate times in one day.
5. **Do not loosen a threshold to make a check pass.** If one genuinely
   must be loose, say why in the file, with the measured numbers.
6. **Always run the control leg before believing a negative result.**
7. **Back up before editing**: `_claude_backups/<topic>__<file>`.

---

## 7. The file-handoff convention

owen does not want to paste terminal output. Anything that runs on his
machine should redirect into a file in the repo — `qa_*.txt` beside the
thing it tests — so a session reads the result instead of asking him to
copy a terminal. `run_local.sh` already works this way; apply it to
anything new. You have a real shell, so this matters less for you than it
did for Cowork, but keep writing the artifacts: they are quoted in the
records and they are how the *next* session knows what was true.

---

## 8. State as of this brief

**Shipped and proven 2026-08-09** (full record:
`Category_Assignment_Shipped_2026-08-09.md`):

- `Assets/Phase1/Scripts/RobotCategory.cs` — the ladder's weight
  categories, `FEATHER 1500 · LIGHT 2000 · MIDDLE 2800 · HEAVY 4000 ·
  SUPER 5500` kg, caps inclusive. **Mass only — the size box is
  deliberately not part of the rule** (owen's decision; §1.2 of the design
  doc was stale and has been amended). Do not add a box back; read the
  file header before touching it.
- `CategoryBench` **44/44** · `ReplayBench` **60/60** ·
  `sql_bench` **34/34** · `api_smoke` **37/37**.
- Career save verified byte-identical after the play-mode run.

**Open, in the order the current handover ranks them:** the VALIDATE
worker loop (Unity — not yours unless §2 works) · the ladder's 43% mutual
disarm (Unity) · **proving a clone builds (yours, §3)** · FIGHT endpoints (yours,
§5) · Docker · GCP deploy — `deploy_api.zsh` expects Secret Manager
entries `rb-jwt-secret`, `rb-worker-key`, `rb-pg-conn` and **they do not
exist** · client ENLIST UI — there is **zero `UnityWebRequest` in the
client**.

**Stale-green, not run since before 2026-08-09** — verify before trusting:
VerbBench (32) · AutonomyBench (24) · CareerSmoke (128 — run FIRST or in
its own play session, or it reports 115/128) · CanvasDragBench (31) ·
TestDebugBench (30) · TouchSmoke · HazardBench · SensorProbe ·
CareerBench.

**Do not treat a `MatrixBench.RunFloor()` failure as a regression on its
own** — it ran 6/10, 8/10 and 7/10 with no relevant code change; the
`>= 7` threshold sits at the true rate and fails about half the time.

---

## 9. When you finish

Write a dated record into `docs/` in the style of the ones already there:
what you changed, what you predicted, what you measured, and what you
deliberately did **not** do. Then tell owen, because `docs/` is a mirror —
the source of truth is the claude.ai Project attached to his Cowork
sessions, and a Cowork session needs to copy your record back or it will
work from a stale doc.
