# The Docker image builds, runs, and passes the whole bench — 2026-08-09

`docker-compose.yml` and the `Dockerfile` have been in this repo since M1 and
had **never been built**, because owen had no container runtime. He installed
`colima` + `docker` on request; this is the first build.

**`robotbrawl-api:dev`, 354 MB. `api_smoke` 166/166 against the container.
0 errors in the container log across the entire run.**

## What was verified

| | |
|---|---|
| build | `docker build -t robotbrawl-api:dev .` — clean, 15 steps |
| contents | `Sql/` 5 files, `Migrations/` 8 — the API reads both from `AppContext.BaseDirectory` at boot |
| user | runs as **`app` (uid 1654)**, not root; `/var/lib/robotbrawl/blobs` writable by it |
| boot | listens on `0.0.0.0:8080`, `/healthz` answers `{"ok":true}` |
| migrations | applied through **version 8** from inside the container |
| **the whole suite** | **`api_smoke` 166 passed, 0 failed, 0 skipped** |

Run against the host Postgres via `host.docker.internal`, so the thing under
test was the image rather than a new database.

## The `.dockerignore` mattered

Committed the day before this build, from inspection alone: without it,
`COPY RobotBrawl.Api/ RobotBrawl.Api/` drags the host's `obj/` — six absolute
`/Users/leondu` paths — over the container's own restore. The build was
performed **with** that file in place, so whether it would have failed
without one is now untested; the reasoning stands and the file stays.

## Two red herrings, neither the container's fault

**Six failures on the first bench run** were leftover database state: I ran
`api_smoke` directly instead of through `run_local.sh`, which truncates
first. A bench that assumes a clean slate says so by failing.

**Ten failures on the second run** were the **auth rate limiter** — 10
requests/min per IP, and I had just spent the budget a minute earlier. Every
downstream failure cascaded from one `429` on login. `api_smoke`'s own header
warns about precisely this:

> *"A re-run inside the same minute shows 429s rather than the expected
> codes — that is the limiter working, not a break."*

Waiting for the window and resetting the database gave 166/166. Worth
recording because both looked exactly like "the container is broken" and
neither was — the third and fourth time today a red result was the
measurement's fault rather than the product's.

## Still not done

- **`docker compose up` does not work here.** Homebrew's `docker` formula
  ships the CLI without the Compose plugin, and there is no
  `~/.docker/cli-plugins`. One command fixes it — `brew install
  docker-compose` — and it was not run, because installing software on owen's
  machine is his call. The compose file itself is unexercised as a result.
- **The image has only ever run against a host Postgres.** The `db` service
  in `docker-compose.yml`, its healthcheck and the `depends_on` ordering are
  all still unproven.
- **GCP** — `deploy_api.zsh` expects Secret Manager entries `rb-jwt-secret`,
  `rb-worker-key`, `rb-pg-conn`, and none exist. No project, no billing
  alert, nothing pushed to a registry.
- **`linux/amd64`.** This built on `aarch64` (Apple silicon). Cloud Run
  defaults to amd64, so the image that works here is not the image that will
  run there until it is built with `--platform`.
