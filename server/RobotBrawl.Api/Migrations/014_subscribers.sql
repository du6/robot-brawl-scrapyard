-- ===========================================================================
-- Robot Brawl — mailing list for launch news and season results (owen,
-- 2026-08-18). The website had no working call to action at all: three
-- "Coming soon to the App Store" buttons, none of them a link, so a visitor
-- who was sold had nowhere to go. This is where they go.
--
-- ⚠ THIS TABLE HOLDS PERSONAL DATA FROM MEMBERS OF THE PUBLIC, which nothing
-- else in this schema does — `users` holds accounts people knowingly created
-- inside the game. That difference is why the columns below exist:
--
--   * consent_source and consent_at record WHERE and WHEN someone opted in.
--     Without them there is no way to answer "why are you emailing me?", and
--     that answer is a legal requirement in most of the places this site is
--     reachable from.
--   * unsub_token is generated at signup, not on first send. An unsubscribe
--     link has to work from the very first email, and a token minted later is
--     a token that does not exist when the first batch goes out.
--   * unsubscribed_at is a TOMBSTONE, not a delete. If the row vanished, the
--     next signup with the same address would look brand new and start the
--     mail again — the classic way a list keeps emailing someone who left.
--     Re-subscribing is an explicit clear of this column.
--   * verified_at is nullable and NOTHING SETS IT YET. Double opt-in is the
--     right end state; this migration deliberately does not pretend to have
--     it. Read a NULL as "claimed this address, has not proven it" and do not
--     mail anything that matters to an unverified row.
--
-- Email is stored lowercased and CITEXT-free: the unique index is on lower()
-- so "Owen@x.com" and "owen@x.com" cannot both subscribe, without taking a
-- dependency on an extension that has to be installed on Cloud SQL.
-- ===========================================================================
BEGIN;

CREATE TABLE subscribers (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    email           text        NOT NULL,
    -- What they asked to hear about. Both default TRUE because both boxes are
    -- ticked on the form; an unticked box must land here as FALSE, so the API
    -- passes the value through rather than relying on the default.
    wants_updates   boolean     NOT NULL DEFAULT true,
    wants_seasons   boolean     NOT NULL DEFAULT true,
    consent_source  text        NOT NULL,
    consent_at      timestamptz NOT NULL DEFAULT now(),
    verified_at     timestamptz,
    unsubscribed_at timestamptz,
    unsub_token     text        NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX subscribers_email_key ON subscribers (lower(email));
CREATE UNIQUE INDEX subscribers_unsub_token_key ON subscribers (unsub_token);
-- The only query a send job runs: everyone still subscribed who wants this
-- kind of mail. Partial, because the unsubscribed are never a target.
CREATE INDEX subscribers_live_idx ON subscribers (unsubscribed_at)
    WHERE unsubscribed_at IS NULL;

INSERT INTO schema_version (version) VALUES (14);

COMMIT;
