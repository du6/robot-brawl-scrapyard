#!/usr/bin/env zsh
# ===========================================================================
# telegram_link.zsh — learn owen's chat id, once.
#
#   1. Open Telegram, find @OwensClaudeBot, send it anything ("hi").
#   2. zsh telegram_link.zsh
#
# Writes .telegram_chat.local (gitignored). After that, notify.zsh works.
#
# A bot cannot start a conversation on Telegram — the human must speak first,
# and only then does getUpdates carry a chat id. That is a platform rule, not
# a missing feature here.
#
# Pass --watch to poll until the first message arrives instead of failing.
# ===========================================================================
set -u
HERE="${0:A:h}"
TOKEN_FILE="$HERE/.telegram_token.local"
CHAT_FILE="$HERE/.telegram_chat.local"
[[ -f "$TOKEN_FILE" ]] || { print -u2 "no $TOKEN_FILE"; exit 2; }
TOKEN=$(<"$TOKEN_FILE")

find_chat() {
  curl -s --max-time 20 "https://api.telegram.org/bot$TOKEN/getUpdates" | python3 -c '
import json,sys
d=json.load(sys.stdin)
for u in d.get("result",[]):
    m=u.get("message") or u.get("edited_message") or {}
    c=m.get("chat") or {}
    if c.get("id") and c.get("type")=="private":
        print(c["id"], c.get("username") or c.get("first_name") or "")
        break
'
}

if [[ "${1:-}" == "--watch" ]]; then
  print "waiting for a message to @OwensClaudeBot (Ctrl-C to stop)…"
  while :; do
    OUT=$(find_chat)
    [[ -n "$OUT" ]] && break
    sleep 3
  done
else
  OUT=$(find_chat)
fi

if [[ -z "$OUT" ]]; then
  print -u2 "no message found. Open Telegram, message @OwensClaudeBot, then re-run (or use --watch)."
  exit 1
fi

CHAT=${OUT%% *}
WHO=${OUT#* }
print -r -- "$CHAT" > "$CHAT_FILE"
chmod 600 "$CHAT_FILE"
print "linked to $WHO — chat id saved to .telegram_chat.local"
print "test it:  zsh $HERE/notify.zsh --test"
