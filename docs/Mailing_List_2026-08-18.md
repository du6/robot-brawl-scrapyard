# The mailing list — how to fetch it and how to send

2026-08-18. Built because the website had **no working call to action at
all**: every page carried "Coming soon to the App Store" as an `<a>` with no
`href`, so a visitor who had just watched a champion's replay and wanted the
game had nothing to click. Found by driving the site as a visitor.

owen's decision, same day: **collect addresses, send updates by hand.** No
email provider. This document is what that requires.

## Getting the list out

`rb-db` has no route from a laptop — its authorised-networks list refuses one,
correctly — so the export endpoint is the only way the addresses come back.

```sh
WK=$(gcloud secrets versions access latest --secret=rb-worker-key \
     --project robot-brawl-ladder)
API=https://rb-api-902243335343.us-central1.run.app

curl -s -H "X-Worker-Key: $WK" "$API/v1/admin/subscribers?format=csv"
```

Drop `?format=csv` for JSON. It is behind the worker key, never anonymous and
never a user token: this is the most sensitive read in the API — every address
anyone has ever given us.

⚠ **The CSV *is* the send list.** With hand-sent mail there is no pipeline to
filter anyone out later, so the endpoint excludes unsubscribed rows by
default. `?all=true` includes them and exists to audit a request ("did you
actually remove me?") — **never paste an `all=true` export into a To or BCC
field.**

Columns: `email, updates, seasons, source, consent_at, verified, subscribed`.
`updates` and `seasons` are the two boxes on the form — someone who ticked
only `seasons` has not asked for launch news, and mailing them anyway is the
thing the checkboxes exist to prevent. `source` is the page they signed up
from, which is also how you tell which page converts.

## Sending an update by hand

1. **BCC, never CC or To.** One message to twenty addresses in the To field
   publishes every subscriber's address to every other subscriber. This is the
   single most common way a small list causes real harm, and it cannot be
   undone.
2. **Filter by what they asked for.** Launch news goes to `updates = True`;
   season results go to `seasons = True`.
3. **Put the unsubscribe link in the body**, every time:
   `https://cyberduck.club/unsubscribe/`
   You can prefill it per person as `?e=<address>`, but only if you are
   sending individually — with BCC there is one body, so use the bare link.
4. **Process replies.** Someone who replies "unsubscribe" instead of using the
   page is still unsubscribing. Do it for them:
   ```sh
   curl -s -X POST "$API/v1/subscribers/unsubscribe" \
     -H 'Content-Type: application/json' -d '{"email":"them@example.com"}'
   ```

### ⚠ Before the first real send: the domain has no email authentication

Measured 2026-08-18: `cyberduck.club` has **no SPF record, no DKIM, no
DMARC** — only two `google-site-verification` TXT records and Google Workspace
MX. Mail from `admin@` lands today mostly because Gmail-to-Gmail is forgiving.
A message to twenty or a hundred strangers is a different test, and an
unauthenticated domain fails it.

**And there is a trap in fixing it.** A domain may have only ONE SPF record.
Workspace needs `include:_spf.google.com` in it, so an SPF record added for
anything else must *merge*, not replace:

```
v=spf1 include:_spf.google.com ~all
```

Pasting a provider's suggested `v=spf1 include:theirthing ~all` over the top
silently breaks outbound mail from `admin@cyberduck.club` — the address on the
App Store listing and the support page.

## What is deliberately not built

- **Nothing sends email.** Capture only. A provider was considered and
  declined for now (small list); `docs/` has the comparison in the session
  record.
- **Double opt-in.** `verified_at` is in the schema and **nothing sets it**. A
  NULL means "claimed this address, has not proven it". The bench asserts rows
  come out unverified precisely so this stays visible. It is the biggest
  single deliverability win available if the list ever grows.
- **Per-recipient unsubscribe tokens are minted at signup and unused.** One
  BCC'd message is one body with one link, so the manual path uses the
  by-address endpoint instead. `GET /v1/subscribers/unsubscribe?token=…`
  already works and is what a real send pipeline should use.

## The privacy commitments this makes

Written into `/privacy/`, and they are commitments, not decoration:

- The list is **separate from a game account** in both directions.
- We store the address, the two options, the page, and the date — the last two
  so there is always an answer to "why are you emailing me?".
- Unsubscribing keeps a **tombstone**, not a delete. If the row vanished, the
  next signup with that address would look brand new and the mail would start
  again — the classic way a list keeps emailing someone who left.

## Endpoints

| route | auth | what |
|---|---|---|
| `POST /v1/subscribers` | none | sign up. Same 200 whether new or already on the list — anything else makes it an address oracle |
| `POST /v1/subscribers/unsubscribe` | none | by address; what the manual path uses |
| `GET /v1/subscribers/unsubscribe?token=` | token | per-recipient; unused until something sends mail |
| `GET /v1/admin/subscribers` | worker key | the export. `?format=csv`, `?all=true` |

Rate limit `subscribe`: 20/min per IP. It was 5 for an hour on the reasoning
"a human subscribes once" — wrong, because **the bucket is per IP**: five is
five *people* behind one office or household NAT, and unsubscribe shares the
bucket so a link-prefetching mail client can spend it before the human clicks.

Cover: `api_smoke.sh` section S, 32 checks. `run_local.sh` exports `PGURL` for
it, for the same reason it exports `TRUST_PROXY` — ten of those checks read the
row in SQL, and without it they skip while the summary still says "failed 0".
