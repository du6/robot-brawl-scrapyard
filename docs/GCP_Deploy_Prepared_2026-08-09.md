# GCP: everything short of spending money — 2026-08-09

owen had already created the project. This is what was found, what was fixed,
and the one decision left.

## Found already done

| | |
|---|---|
| project | `robot-brawl-ladder`, ACTIVE |
| billing | linked, `01DB5D-E5656E-C7E6FF` |
| **budget alarm** | `robot-brawl 25/mo` — armed, exactly as §7 asks |
| buckets | `…-snapshots`, `…-replays` |
| Artifact Registry | repo `rb` (DOCKER), `us-central1` |
| APIs | run, sqladmin, storage, artifactregistry, compute, monitoring, billingbudgets |
| Cloud SQL | **none — nothing was costing anything** |

## A real bug in a checked-in script

**`gcp_bootstrap.zsh` never enabled `secretmanager` or `cloudbuild`**, and
`deploy_api.zsh` needs both — it runs `gcloud builds submit` and
`--set-secrets`. Anyone following the scripts in the documented order hits a
wall at the deploy step, with an error naming an API the scripts never
mention. Found by running them for real rather than reading them.

Fixed, and the script now also prints the three `gcloud secrets create`
commands it does **not** run, with the note to pipe values in rather than
pass them as arguments where they land in shell history and the process list.

## Done

- **`secretmanager` + `cloudbuild` enabled.**
- **`rb-jwt-secret` and `rb-worker-key` created**, 64 bytes each, generated
  with `openssl rand -base64 48` piped straight into `gcloud secrets create
  --data-file=-`. The values never touched a variable, a file, the terminal
  or any transcript.
- **Image built by Cloud Build and pushed** to
  `us-central1-docker.pkg.dev/robot-brawl-ladder/rb/api:20260809-205026`,
  58 seconds, `sha256:2e1cbec6…`.

### Cloud Build needed a service account, and that is not in the script

The first submit failed `PERMISSION_DENIED` despite the caller being
`roles/owner`. Newer projects retire the legacy
`PROJECT@cloudbuild.gserviceaccount.com` default, so a build must name a
service account. Fixed by granting the compute service agent
`logging.logWriter`, `artifactregistry.writer` and `storage.objectAdmin`, and
submitting with `--service-account` and
`--default-buckets-behavior=regional-user-owned-bucket`.

**`deploy_api.zsh` does not do this and will fail the same way.** Left
unpatched deliberately: it is a deploy script that has never run end to end,
and guessing at the rest of its fixes without executing it would be inventing
confidence.

### The amd64 risk is resolved

The local image was `aarch64` and Cloud Run defaults to amd64. Cloud Build
builds remotely, and the pushed image inspects as **`ARCH: amd64  OS:
linux`**. Verified rather than assumed, because this is the failure that
appears only as a container that will not start.

## The one thing left, and it costs money

`PG_CONN` needs a Postgres. The smallest Cloud SQL instance is roughly
**$10–15/month** and would be this project's first real spend. Nothing has
been created.
