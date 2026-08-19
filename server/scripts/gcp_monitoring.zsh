#!/bin/zsh
# §M4's monitoring/alerting, as a script rather than console clicks — §M1
# requires that nothing about this project's cloud setup exists only as a
# thing somebody once clicked.
#
# Creates, and is safe to re-run:
#   * four log-based metrics extracted from the reaper's "rbmetrics" line
#   * an email notification channel
#   * four alert policies
#   * an uptime check on /healthz/
#
# WHY LOG-BASED AND NOT A REAL METRIC EXPORTER: the app is one Cloud Run
# service with no sidecar, and the numbers are already computed on a timer by
# the reaper. Writing them to stdout costs nothing and needs no credentials.
# The cost is that the extractor is a REGEX over the log text — reword the
# message and every metric here goes flat rather than red. tests/api_smoke.sh
# section M asserts that line's exact shape for that reason.
set -e
PROJECT_ID="${PROJECT_ID:-robot-brawl-ladder}"
REGION="${REGION:-us-central1}"
ALERT_EMAIL="${ALERT_EMAIL:-leondu167@gmail.com}"
API="https://monitoring.googleapis.com/v3/projects/${PROJECT_ID}"
TOKEN() { gcloud auth print-access-token; }

# --------------------------------------------------------------- metrics
# One metric per number we alert on. DELTA/DISTRIBUTION because that is what
# a log-based metric with a value extractor produces. The conditions below
# align with ALIGN_PERCENTILE_99, not ALIGN_MAX: Monitoring refuses MAX on a
# DELTA distribution outright. p99 over a 5-minute window of samples taken
# every 30s is "the worst value seen", which is what these thresholds mean.
mk_metric() {  # <name> <display> <regex-capture> <unit>
  local name="$1" disp="$2" rx="$3" unit="$4"
  cat > "/tmp/rb_m_${name}.yaml" <<EOF
filter: |-
  resource.type="cloud_run_revision"
  resource.labels.service_name="rb-api"
  textPayload:"rbmetrics"
valueExtractor: REGEXP_EXTRACT(textPayload, "${rx}")
metricDescriptor:
  metricKind: DELTA
  valueType: DISTRIBUTION
  unit: "${unit}"
  displayName: ${disp}
bucketOptions:
  exponentialBuckets:
    numFiniteBuckets: 16
    growthFactor: 2
    scale: 1
EOF
  if gcloud logging metrics describe "$name" --project "$PROJECT_ID" >/dev/null 2>&1; then
    gcloud logging metrics update "$name" --config-from-file="/tmp/rb_m_${name}.yaml" --project "$PROJECT_ID" >/dev/null
    echo "  updated $name"
  else
    gcloud logging metrics create "$name" --config-from-file="/tmp/rb_m_${name}.yaml" --project "$PROJECT_ID" >/dev/null
    echo "  created $name"
  fi
}

echo "log-based metrics:"
mk_metric rb_queue_oldest_ready_s     "Oldest READY job age"        'oldest_ready_s=([0-9]+)'      s
mk_metric rb_queue_oldest_heartbeat_s "Oldest worker heartbeat age" 'oldest_heartbeat_s=([0-9]+)'  s
mk_metric rb_queue_failed             "FAILED jobs"                 'failed=([0-9]+)'              1
mk_metric rb_snapshot_oldest_pending_s "Oldest PENDING snapshot age" 'oldest_pending_s=([0-9]+)'   s

# ---------------------------------------------------------------- channel
echo "notification channel:"
CH=$(curl -s -H "Authorization: Bearer $(TOKEN)" "${API}/notificationChannels" \
     | python3 -c "
import json,sys
d=json.load(sys.stdin).get('notificationChannels',[])
print(next((c['name'] for c in d if c.get('labels',{}).get('email_address')=='${ALERT_EMAIL}'), ''))")
if [ -z "$CH" ]; then
  CH=$(curl -s -X POST -H "Authorization: Bearer $(TOKEN)" -H 'Content-Type: application/json' \
       "${API}/notificationChannels" -d "{
         \"type\": \"email\",
         \"displayName\": \"owen (ladder alerts)\",
         \"labels\": {\"email_address\": \"${ALERT_EMAIL}\"}
       }" | python3 -c "import json,sys;print(json.load(sys.stdin).get('name',''))")
  echo "  created $CH"
else
  echo "  reusing $CH"
fi
[ -z "$CH" ] && { echo "could not create a notification channel"; exit 1; }

# --------------------------------------------------------------- policies
# ON MISSING DATA. Every queue policy uses EVALUATION_MISSING_DATA_INACTIVE,
# and that is a deliberate choice rather than the default being left alone:
# min-instances=0 means the app is NOT RUNNING whenever nobody is polling, so
# these metrics are absent most of the time by design. Firing on absence would
# page for an idle ladder, every night. The cost is real and worth stating —
# a queue that backs up while nothing polls is invisible to these four. The
# uptime check below is what covers "is it alive", by generating the traffic
# that makes the metrics exist in the first place.
mk_policy() {  # <display> <metric> <threshold> <duration> <doc>
  local disp="$1" metric="$2" thr="$3" dur="$4" doc="$5"
  local existing
  existing=$(curl -s -H "Authorization: Bearer $(TOKEN)" "${API}/alertPolicies" \
    | python3 -c "
import json,sys
d=json.load(sys.stdin).get('alertPolicies',[])
print(next((p['name'] for p in d if p.get('displayName')=='''${disp}'''), ''))")
  cat > /tmp/rb_policy.json <<EOF
{
  "displayName": "${disp}",
  "combiner": "OR",
  "conditions": [{
    "displayName": "${disp}",
    "conditionThreshold": {
      "filter": "metric.type=\"logging.googleapis.com/user/${metric}\" AND resource.type=\"cloud_run_revision\"",
      "aggregations": [{
        "alignmentPeriod": "300s",
        "perSeriesAligner": "ALIGN_PERCENTILE_99",
        "crossSeriesReducer": "REDUCE_MAX"
      }],
      "comparison": "COMPARISON_GT",
      "thresholdValue": ${thr},
      "duration": "${dur}",
      "trigger": {"count": 1},
      "evaluationMissingData": "EVALUATION_MISSING_DATA_INACTIVE"
    }
  }],
  "notificationChannels": ["${CH}"],
  "alertStrategy": {"autoClose": "86400s"},
  "documentation": {"content": "${doc}", "mimeType": "text/markdown"}
}
EOF
  if [ -n "$existing" ]; then
    curl -s -X PATCH -H "Authorization: Bearer $(TOKEN)" -H 'Content-Type: application/json' \
      "https://monitoring.googleapis.com/v3/${existing}?updateMask=conditions,notificationChannels,documentation,alertStrategy" \
      -d @/tmp/rb_policy.json >/dev/null
    echo "  updated ${disp}"
  else
    curl -s -X POST -H "Authorization: Bearer $(TOKEN)" -H 'Content-Type: application/json' \
      "${API}/alertPolicies" -d @/tmp/rb_policy.json \
      | python3 -c "
import json,sys
d=json.load(sys.stdin)
print('  created' if 'name' in d else '  FAILED: '+json.dumps(d)[:300])"
  fi
}

echo "alert policies:"
# 15 minutes. A VALIDATE normally completes in seconds; this is the threshold
# for 'a job exists and nothing is working it', which is what a dead worker
# fleet looks like from the API's side.
# ⚠ 2100s, NOT 900. Until 2026-08-19 the scheduler ran every 5 minutes, so a
# job older than 15 was unambiguously wrong. The API now starts the worker on
# demand and the SCHEDULE is a 30-minute safety net — so a single missed nudge
# leaves a job waiting up to 30 minutes and then collects it automatically. At
# 900 this alerted on a condition that self-heals, and an alert that cries wolf
# gets muted, which costs more than the alert was worth. 2100 fires only when
# the safety net has ALSO failed to run.
mk_policy "ladder: jobs are not being worked" rb_queue_oldest_ready_s 2100 "300s" \
  "A job has been READY for over 15 minutes. Usually means no worker is polling. Check the worker fleet, then GET /v1/admin/metrics with the worker key."
# The reaper returns a job at 300s. Seeing 900 means the reaper itself is not
# running — a different fault from a stuck worker, and it needs the API looked
# at rather than the workers.
mk_policy "ladder: the reaper is not reaping" rb_queue_oldest_heartbeat_s 900 "300s" \
  "A CLAIMED job has gone 15 minutes without a heartbeat. The reaper returns them at 5 minutes, so this means the reaper loop is not running — look at the API, not the workers."
mk_policy "ladder: jobs are failing" rb_queue_failed 5 "600s" \
  "More than 5 FAILED jobs. Check last_error in match_jobs; a payload that cannot be fetched fails every worker that claims it."
# The one a player feels. 30 minutes of 'still checking your robot' is the
# point at which it reads as broken rather than busy.
mk_policy "ladder: uploaded robots are stuck" rb_snapshot_oldest_pending_s 1800 "600s" \
  "A snapshot has been PENDING for over 30 minutes — a player uploaded a robot and it has not been validated. Note this can be non-zero while the job queue is empty and clean, which means a snapshot has no job at all."

# ----------------------------------------------------------- uptime check
# Hits /healthz/ WITH THE TRAILING SLASH — on owen's network the slashless
# form is intercepted and answers a Google 404. That is a local quirk and not
# what Google's probers see, but the check is written the same way so the
# thing being monitored is the thing that gets curl'd by hand.
#
# This also does the work of keeping the metrics alive: each probe wakes an
# instance, which runs a reaper pass, which emits a line. Without it, a
# scale-to-zero service produces no queue metrics at all.
echo "uptime check:"
HOST=$(gcloud run services describe rb-api --project "$PROJECT_ID" --region "$REGION" --format='value(status.url)' | sed 's|https://||')
EXISTING=$(curl -s -H "Authorization: Bearer $(TOKEN)" "${API}/uptimeCheckConfigs" \
  | python3 -c "
import json,sys
d=json.load(sys.stdin).get('uptimeCheckConfigs',[])
print(next((u['name'] for u in d if u.get('displayName')=='rb-api health'), ''))")
if [ -z "$EXISTING" ]; then
  curl -s -X POST -H "Authorization: Bearer $(TOKEN)" -H 'Content-Type: application/json' \
    "${API}/uptimeCheckConfigs" -d "{
      \"displayName\": \"rb-api health\",
      \"monitoredResource\": {\"type\": \"uptime_url\", \"labels\": {\"host\": \"${HOST}\"}},
      \"httpCheck\": {\"path\": \"/healthz/\", \"port\": 443, \"useSsl\": true, \"validateSsl\": true},
      \"period\": \"300s\",
      \"timeout\": \"10s\"
    }" | python3 -c "
import json,sys
d=json.load(sys.stdin)
print('  created for ${HOST}' if 'name' in d else '  FAILED: '+json.dumps(d)[:300])"
else
  echo "  reusing $EXISTING"
fi


# ======================================================================
# LAUNCH MONITORING — added 2026-08-19, for the day real players arrive.
#
# ⚠ THE FOUR POLICIES ABOVE ALL WATCH THE QUEUE. They answer "is the ladder
# processing work", and they answered it well enough for a project with one
# user. NONE of them notices the API returning 500s, taking ten seconds to
# answer, running out of database connections, or hitting its instance
# ceiling — which is most of what actually goes wrong on a launch day.
#
# These use Cloud Run's and Cloud SQL's BUILT-IN metrics rather than the
# log-regex above, so they cannot be silenced by rewording a log line.
# ======================================================================

# <display> <filter> <threshold> <duration> <aligner> <reducer> <doc> [denominatorFilter]
#
# ⚠ THE PAYLOAD IS BUILT BY PYTHON, NOT BY A HEREDOC, and that is not style.
# A Monitoring filter is full of double quotes — metric.type="run.googleapis…"
# — and interpolating one into a JSON heredoc produces invalid JSON every
# time. The first run of this section failed six for six on exactly that.
# json.dumps escapes it correctly and cannot be got wrong later.
mk_builtin() {
  local disp="$1" filt="$2" thr="$3" dur="$4" align="$5" reduce="$6" doc="$7" denom="${8:-}"
  local existing
  existing=$(curl -s -H "Authorization: Bearer $(TOKEN)" "${API}/alertPolicies" \
    | DISP="$disp" python3 -c "
import json,os,sys
want=os.environ['DISP']
d=json.load(sys.stdin).get('alertPolicies',[])
print(next((p['name'] for p in d if p.get('displayName')==want), ''))")

  DISP="$disp" FILT="$filt" THR="$thr" DUR="$dur" ALIGN="$align" RED="$reduce" \
  DOC="$doc" DENOM="$denom" CHAN="$CH" python3 - > /tmp/rb_launch_policy.json <<'PYEOF'
import json, os
agg = {"alignmentPeriod": "300s",
       "perSeriesAligner": os.environ["ALIGN"],
       "crossSeriesReducer": os.environ["RED"]}
cond = {
    "filter": os.environ["FILT"],
    "aggregations": [agg],
    "comparison": "COMPARISON_GT",
    "thresholdValue": float(os.environ["THR"]),
    "duration": os.environ["DUR"],
    "trigger": {"count": 1},
    # Same reasoning as the queue policies: min-instances=0 means these
    # metrics are legitimately absent when nobody is using the app, and
    # firing on absence would page for an idle night.
    "evaluationMissingData": "EVALUATION_MISSING_DATA_INACTIVE",
}
if os.environ.get("DENOM"):
    cond["denominatorFilter"] = os.environ["DENOM"]
    cond["denominatorAggregations"] = [agg]
print(json.dumps({
    "displayName": os.environ["DISP"],
    "combiner": "OR",
    "conditions": [{"displayName": os.environ["DISP"], "conditionThreshold": cond}],
    "notificationChannels": [os.environ["CHAN"]],
    "alertStrategy": {"autoClose": "86400s"},
    "documentation": {"content": os.environ["DOC"], "mimeType": "text/markdown"},
}))
PYEOF

  if [ -n "$existing" ]; then
    curl -s -X PATCH -H "Authorization: Bearer $(TOKEN)" -H 'Content-Type: application/json' \
      "https://monitoring.googleapis.com/v3/${existing}?updateMask=conditions,notificationChannels,documentation,alertStrategy" \
      -d @/tmp/rb_launch_policy.json >/dev/null
    echo "  updated ${disp}"
  else
    curl -s -X POST -H "Authorization: Bearer $(TOKEN)" -H 'Content-Type: application/json' \
      "${API}/alertPolicies" -d @/tmp/rb_launch_policy.json \
      | python3 -c "
import json,sys
d=json.load(sys.stdin)
print('  created' if 'name' in d else '  FAILED: '+json.dumps(d)[:300])"
  fi
}

RUN_SVC='resource.type="cloud_run_revision" AND resource.labels.service_name="rb-api"'
SQL_INST="resource.type=\"cloudsql_database\" AND resource.labels.database_id=\"${PROJECT_ID}:rb-db\""

echo "launch policies:"

# THE ONE OWEN ASKED FOR. A RATIO, not a count: 50 errors means nothing
# without knowing whether it was out of 100 requests or 100,000.
# ⚠ 10 minutes, not 5, and the reason is arithmetic: at 3am with four
# requests an hour, ONE 500 is a 25% error rate. A longer window makes the
# breakage persist before it wakes anyone. It is still not a volume floor —
# Monitoring cannot express "and more than N requests" in one condition — so
# treat a 3am page as "look, then judge", not "the site is down".
mk_builtin "api: 5xx error rate is high" \
  "metric.type=\"run.googleapis.com/request_count\" AND ${RUN_SVC} AND metric.labels.response_code_class=\"5xx\"" \
  0.05 "600s" ALIGN_RATE REDUCE_SUM \
  "More than 5% of requests returned 5xx for 10 minutes. Read the logs first: a deploy that boots but cannot reach the database looks exactly like this, and so does an exhausted connection pool. Roll back with gcloud run services update-traffic rb-api --to-revisions=PREVIOUS=100." \
  "metric.type=\"run.googleapis.com/request_count\" AND ${RUN_SVC}"

mk_builtin "api: responses are slow (p95 > 3s)" \
  "metric.type=\"run.googleapis.com/request_latencies\" AND ${RUN_SVC}" \
  3000 "600s" ALIGN_PERCENTILE_95 REDUCE_MAX \
  "95th percentile latency above 3s for 10 minutes. Usual causes in order: the database is the bottleneck (see the two db alerts), a cold-start storm after a deploy, or requests queueing for a pooled connection."

# ⚠ THE CEILING, AND IT IS LOW ON PURPOSE. maxScale is 4. Sitting at 4 is not
# a fault — it is demand meeting the wall. Raising maxScale REQUIRES raising
# the DB connection ceiling first.
mk_builtin "api: at the instance ceiling (4)" \
  "metric.type=\"run.googleapis.com/container/instance_count\" AND ${RUN_SVC}" \
  3.5 "600s" ALIGN_MAX REDUCE_MAX \
  "rb-api has been at its maxScale of 4 for 10 minutes. Not a fault, a capacity wall. Before raising maxScale, raise the database ceiling: 4 instances x Maximum Pool Size 5 = 20 against a db-f1-micro that accepts about 25. Raising one without the other trades a queue for 'too many clients'."

mk_builtin "db: connections near the ceiling" \
  "metric.type=\"cloudsql.googleapis.com/database/postgresql/num_backends\" AND ${SQL_INST}" \
  18 "300s" ALIGN_MAX REDUCE_MAX \
  "Postgres backends above 18. db-f1-micro accepts about 25 and the API fleet is bounded to 20. Above 18 means either the bound was lost from rb-pg-conn, or something outside the fleet is connecting. Every endpoint dies together when this ceiling is reached, sign-in included."

mk_builtin "db: CPU is saturated" \
  "metric.type=\"cloudsql.googleapis.com/database/cpu/utilization\" AND ${SQL_INST}" \
  0.85 "600s" ALIGN_MEAN REDUCE_MAX \
  "Cloud SQL CPU above 85% for 10 minutes. db-f1-micro is a SHARED-CORE instance, so sustained load is throttled rather than served, and players see everything get slow. The fix is a tier change, which is a cost decision."

mk_builtin "db: disk is filling" \
  "metric.type=\"cloudsql.googleapis.com/database/disk/utilization\" AND ${SQL_INST}" \
  0.85 "600s" ALIGN_MEAN REDUCE_MAX \
  "Cloud SQL disk above 85%. Replays and snapshots live in GCS, not the database, so this means row growth or WAL. PITR is ON, which retains WAL."

echo
echo "done. review at https://console.cloud.google.com/monitoring/alerting?project=${PROJECT_ID}"
echo
# PROVEN BY DRILL, 2026-08-09: a job was queued and deliberately left unworked
# until oldest_ready_s held above 900 for 8 minutes. "ladder: jobs are not
# being worked" fired and the email arrived — owen confirmed it.
#
# That confirmation is the only kind available. Cloud Monitoring does not
# expose incidents through its public API, so a script cannot check whether a
# policy delivers; it can only check that one exists. If you change the
# channel, the extractor, or the log line, RE-RUN THE DRILL rather than
# trusting that this still works:
#
#   1. POST /v1/snapshots on the live API and do not work the job
#   2. watch GET /v1/admin/metrics until oldestReadyAgeS > 900
#   3. wait 5 more minutes, then ask owen whether the mail arrived
#   4. retire the fixture: claim the job and POST validate-result legal:false
echo "to prove it still DELIVERS, run the drill — see the comment at the end of this script."
