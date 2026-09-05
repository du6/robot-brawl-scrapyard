#!/bin/zsh
# Release build 18 (version 2.2.0) — the three outward steps a Claude Code
# session in auto mode cannot take: UPLOAD the IPA, create the 2.2.0 App Store
# version and attach the build, SUBMIT it for review. Each stage is explicit;
# nothing runs without a stage argument, and `status` is read-only.
#
#   zsh server/scripts/release_ios_build18.zsh status    # what ASC has now
#   zsh server/scripts/release_ios_build18.zsh upload    # altool → TestFlight (10-20 min processing)
#   zsh server/scripts/release_ios_build18.zsh version   # encryption=no, create 2.2.0, What's New, attach build 18
#   zsh server/scripts/release_ios_build18.zsh submit    # POST review submission + item, submitted=true
#   zsh server/scripts/release_ios_build18.zsh swap      # build 19+: cancel the open submission, attach the new build, resubmit
#
# The IPA is where the headless build left it (a git worktree of
# release/ios-build18, commit 61000cf + bench reconciliation), archived and
# exported on 2026-09-04 — see docs/Build18_iOS_Port_2026-09-04.md. If /tmp
# has been cleared, rebuild: BuildIOS.Build → the archive/export chain in
# docs/HANDOVER_iOS_Launch_2026-08-16.md §"the build-9 ritual" step 4.
set -e
HERE="${0:A:h}"
source "$HERE/asc_jwt_env.sh"
IPA="${IPA:-/tmp/rb_b18/build/export/RobotBrawlBoltBlade.ipa}"
API="https://api.appstoreconnect.apple.com/v1"
VERSION="2.2.0"
BUILD_NO="${BUILD_NO:-19}"   # 18 shipped to review 2026-09-04; 19 swaps it in with the QA round-1 fixes
read -r -d '' WHATS_NEW <<'EOF' || true
The Rookie Warm-Up: a pre-built starter robot (SCRAPPER) that is ready to fight the moment you open the game, a step-by-step guide with a ghost hand that shows you where to tap, and reward boxes for your first bolt, first weld, first purchase and first fight. Lose your first fight and a rescue crate arrives with the parts to fix what went wrong. Plus: the build now loads your active robot at boot, and the workshop opens faster.
EOF

auth() { T=$(mint_jwt); }
get()  { curl -sg -H "Authorization: Bearer $T" "$API/$1"; }
post() { curl -sg -H "Authorization: Bearer $T" -H "Content-Type: application/json" -X POST  "$API/$1" -d "$2"; }
patch(){ curl -sg -H "Authorization: Bearer $T" -H "Content-Type: application/json" -X PATCH "$API/$1" -d "$2"; }
jq_() { python3 -c "import sys,json
d=json.load(sys.stdin)
$1"; }

build_id() {
  get "builds?filter[app]=$ASC_APP_ID&filter[version]=$BUILD_NO&filter[preReleaseVersion.version]=$VERSION&limit=1" \
    | jq_ 'print(d["data"][0]["id"] if d["data"] else "")'
}
version_id() {
  get "apps/$ASC_APP_ID/appStoreVersions?filter[versionString]=$VERSION&limit=1" \
    | jq_ 'print(d["data"][0]["id"] if d["data"] else "")'
}

case "${1:-}" in
  status)
    auth
    echo "--- versions"
    get "apps/$ASC_APP_ID/appStoreVersions?limit=3&fields[appStoreVersions]=versionString,appStoreState" \
      | jq_ 'for v in d["data"]: print(" ", v["id"], v["attributes"]["versionString"], v["attributes"]["appStoreState"])'
    echo "--- builds"
    get "builds?filter[app]=$ASC_APP_ID&sort=-uploadedDate&limit=3&fields[builds]=version,processingState,usesNonExemptEncryption" \
      | jq_ 'for b in d["data"]: a=b["attributes"]; print(" ", b["id"], "build", a["version"], a["processingState"], "encryption=", a.get("usesNonExemptEncryption"))'
    echo "--- open review submissions"
    get "reviewSubmissions?filter[app]=$ASC_APP_ID&filter[state]=READY_FOR_REVIEW,WAITING_FOR_REVIEW,IN_REVIEW,UNRESOLVED_ISSUES" \
      | jq_ 'print("  none" if not d["data"] else "\n".join("  "+s["id"]+" "+s["attributes"]["state"] for s in d["data"]))'
    ;;

  upload)
    [[ -f "$IPA" ]] || { echo "no IPA at $IPA"; exit 1; }
    ls -la "$IPA"
    xcrun altool --upload-app -f "$IPA" -t ios --apiKey "$ASC_KEY_ID" --apiIssuer "$ASC_ISSUER"
    echo "uploaded. Processing takes 10-20 min; run 'status' until build $BUILD_NO reads VALID, then 'version'."
    ;;

  version)
    auth
    B=$(build_id); [[ -n "$B" ]] || { echo "build $BUILD_NO ($VERSION) not in ASC yet - upload first / wait for processing"; exit 1; }
    echo "build $BUILD_NO = $B"
    # Encryption answer FIRST (the doc's §1 order): the export uses only
    # standard https, so usesNonExemptEncryption=false, same as builds 8-17.
    patch "builds/$B" "{\"data\":{\"type\":\"builds\",\"id\":\"$B\",\"attributes\":{\"usesNonExemptEncryption\":false}}}" >/dev/null
    V=$(version_id)
    if [[ -z "$V" ]]; then
      V=$(post "appStoreVersions" "{\"data\":{\"type\":\"appStoreVersions\",\"attributes\":{\"platform\":\"IOS\",\"versionString\":\"$VERSION\"},\"relationships\":{\"app\":{\"data\":{\"type\":\"apps\",\"id\":\"$ASC_APP_ID\"}}}}}" \
        | jq_ 'print(d["data"]["id"])')
      echo "created version $VERSION = $V"
    else
      echo "version $VERSION exists = $V"
    fi
    L=$(get "appStoreVersions/$V/appStoreVersionLocalizations" | jq_ 'print(d["data"][0]["id"])')
    WN=$(python3 -c "import json,sys; print(json.dumps(sys.stdin.read().strip()))" <<< "$WHATS_NEW")
    patch "appStoreVersionLocalizations/$L" "{\"data\":{\"type\":\"appStoreVersionLocalizations\",\"id\":\"$L\",\"attributes\":{\"whatsNew\":$WN}}}" >/dev/null
    patch "appStoreVersions/$V/relationships/build" "{\"data\":{\"type\":\"builds\",\"id\":\"$B\"}}" >/dev/null
    echo "version $VERSION: What's New set, build $BUILD_NO attached. Now 'submit'."
    ;;

  swap)
    # The in-review swap (HANDOVER_iOS_Launch §1, API form): answer encryption
    # on the NEW build first, cancel the open submission, re-point the version
    # at the new build, then submit again - the no-submission window is seconds.
    auth
    B=$(build_id); [[ -n "$B" ]] || { echo "build $BUILD_NO ($VERSION) not in ASC yet - upload first / wait for processing"; exit 1; }
    V=$(version_id); [[ -n "$V" ]] || { echo "no $VERSION version"; exit 1; }
    patch "builds/$B" "{\"data\":{\"type\":\"builds\",\"id\":\"$B\",\"attributes\":{\"usesNonExemptEncryption\":false}}}" >/dev/null
    for S in $(get "reviewSubmissions?filter[app]=$ASC_APP_ID&filter[state]=READY_FOR_REVIEW,WAITING_FOR_REVIEW,IN_REVIEW,UNRESOLVED_ISSUES" | jq_ 'print("\n".join(x["id"] for x in d["data"]))'); do
      patch "reviewSubmissions/$S" "{\"data\":{\"type\":\"reviewSubmissions\",\"id\":\"$S\",\"attributes\":{\"canceled\":true}}}" | jq_ 'print("canceled", d["data"]["id"], d["data"]["attributes"]["state"])'
    done
    # Cancelling DEVELOPER_REJECTS the version for a moment; an attach fired
    # inside that moment is dropped without an error (measured 2026-09-05:
    # the first swap left 18 attached and a new submission with no items).
    # Attach, read back, and do not submit until the version says the new build.
    ok=""
    for try in 1 2 3 4 5 6 7 8; do
      patch "appStoreVersions/$V/relationships/build" "{\"data\":{\"type\":\"builds\",\"id\":\"$B\"}}" >/dev/null
      have=$(get "appStoreVersions/$V?include=build&fields[builds]=version&fields[appStoreVersions]=appStoreState" \
             | jq_ 'print(",".join(i["attributes"]["version"] for i in d.get("included",[])) + " " + d["data"]["attributes"]["appStoreState"])')
      echo "  attach try $try: version carries build [$have]"
      [[ "$have" == "$BUILD_NO "* ]] && { ok=1; break; }
      sleep 5
    done
    [[ -n "$ok" ]] || { echo "could not attach build $BUILD_NO to $VERSION - stopping before submit"; exit 1; }
    echo "version $VERSION now carries build $BUILD_NO ($B); submitting..."
    exec zsh "$0" submit
    ;;

  submit)
    auth
    V=$(version_id); [[ -n "$V" ]] || { echo "no $VERSION version - run 'version' first"; exit 1; }
    S=$(post "reviewSubmissions" "{\"data\":{\"type\":\"reviewSubmissions\",\"attributes\":{\"platform\":\"IOS\"},\"relationships\":{\"app\":{\"data\":{\"type\":\"apps\",\"id\":\"$ASC_APP_ID\"}}}}}" \
      | jq_ 'print(d["data"]["id"])')
    post "reviewSubmissionItems" "{\"data\":{\"type\":\"reviewSubmissionItems\",\"relationships\":{\"reviewSubmission\":{\"data\":{\"type\":\"reviewSubmissions\",\"id\":\"$S\"}},\"appStoreVersion\":{\"data\":{\"type\":\"appStoreVersions\",\"id\":\"$V\"}}}}}" \
      | jq_ 'print("item", d["data"]["attributes"]["state"]) if "data" in d else print("ITEM FAILED:", json.dumps(d)[:400])'
    patch "reviewSubmissions/$S" "{\"data\":{\"type\":\"reviewSubmissions\",\"id\":\"$S\",\"attributes\":{\"submitted\":true}}}" \
      | jq_ 'print("submission", d["data"]["id"], d["data"]["attributes"]["state"]) if "data" in d else print("SUBMIT FAILED:", json.dumps(d)[:400])'
    ;;

  *)
    echo "usage: $0 status|upload|version|submit|swap"; exit 2 ;;
esac
