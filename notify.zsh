#!/usr/bin/env zsh
# ===========================================================================
# notify.zsh — send owen a Telegram message from @OwensClaudeBot.
#
#   zsh notify.zsh "MatrixBench 10/10, pushed"
#   some_long_command | zsh notify.zsh          # message from stdin
#   zsh notify.zsh --test                       # prove the wiring works
#
# Set up 2026-08-09. A Cowork session had Telegram and a CLI session did not;
# this closes that gap, and it is the only channel that reaches owen when he
# is not looking at the terminal.
#
# SECRETS: the token lives in .telegram_token.local and the chat id in
# .telegram_chat.local. Both are gitignored and NEITHER IS EVER PRINTED —
# same rule as .gh_token.local. A bot token is a full credential: anyone
# holding it can read and send everything the bot can.
#
# WHY A CHAT ID FILE: Telegram bots cannot open a conversation. The human
# must message the bot first; only then does getUpdates reveal the chat id.
# If .telegram_chat.local is missing, this script says so rather than
# failing with a bare 400.
# ===========================================================================
set -u
HERE="${0:A:h}"
TOKEN_FILE="$HERE/.telegram_token.local"
CHAT_FILE="$HERE/.telegram_chat.local"

[[ -f "$TOKEN_FILE" ]] || { print -u2 "no $TOKEN_FILE — paste the bot token into it (chmod 600)"; exit 2; }
[[ -f "$CHAT_FILE"  ]] || { print -u2 "no $CHAT_FILE — message @OwensClaudeBot once, then run: zsh $HERE/telegram_link.zsh"; exit 2; }

TOKEN=$(<"$TOKEN_FILE")
CHAT=$(<"$CHAT_FILE")

if [[ "${1:-}" == "--test" ]]; then
  MSG="✅ Robot Brawl: Telegram is wired up. Sent from notify.zsh on owen's Mac."
elif (( $# > 0 )); then
  MSG="$*"
else
  MSG=$(cat)                       # from a pipe
fi
[[ -n "$MSG" ]] || { print -u2 "nothing to send"; exit 2; }

# Telegram caps a message at 4096 characters. Truncate rather than let the
# API reject the whole thing — a clipped alert beats no alert.
if (( ${#MSG} > 4000 )); then
  MSG="${MSG[1,3980]}
… (truncated)"
fi

RESP=$(curl -s --max-time 25 \
  --data-urlencode "chat_id=$CHAT" \
  --data-urlencode "text=$MSG" \
  --data-urlencode "disable_web_page_preview=true" \
  "https://api.telegram.org/bot$TOKEN/sendMessage")

# Never echo $RESP raw: on some errors Telegram reflects the request back.
if print -r -- "$RESP" | grep -q '"ok":true'; then
  print "sent (${#MSG} chars)"
else
  DESC=$(print -r -- "$RESP" | sed -n 's/.*"description":"\([^"]*\)".*/\1/p')
  print -u2 "send FAILED: ${DESC:-unknown error}"
  exit 1
fi
