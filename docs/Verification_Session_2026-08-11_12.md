# Verification session, 2026-08-11 → 08-12 — consolidated report

Assembled by the session's program manager (PM-2), addendum by its engineer,
graduated to `docs/` by team-lead. Five agents (marketing, UX, three
playtesters in succession, PM ×2, engineer) drove the built iOS Simulator
player, the editor, and the source against each other for one continuous
session. **This report does not assert any result that was not measured, and
it says so wherever that forced an item to stay open.**

## 0. The short answer

Twenty-five board items: **ten closed with evidence, six fixed but
unverified on device, six carried open, three are owen's to decide.** The
single most valuable output is not a fix: **the project's entire 44 pt
touch-floor coverage is six named controls on one tab, and nothing anywhere
measures whether one control is drawn on top of another** — found because a
tester refused to leave a measurement at "points that worked".

## 1. Closed with evidence

| # | What | Evidence |
|---|---|---|
| 2 | CapsuleCollider stripping (`link.xml`, `e03a264`) | Device: **0 errors** on FINAL vs 11-at-launch/383-lifetime before. Severity was understated: without it every tester met red error text on first launch (the console is error-triggered, not flag-gated, on the sim). |
| 9 | PROGRAM told paying customers to re-buy owned sensors (`ccd221d`) | Device: one bought Tilt moved to "— BUILD tab" while the five unowned stayed "— SHOP" — the proof standard was set before the measurement and could have failed. |
| 13 | Drag starting on a text field was swallowed (`f8bce8f`) | Device: the panel visibly scrolled. Observes the case the fix *changed*. The "focus" half was the Simulator's hardware-keyboard setting, not the product. |
| 16 | TEST DRIVE's bottom half dead | Three independent routes: device bracket (53% live, top-aligned), structural refutation from `MkButton` (the visual box IS the raycast rect), editor raycast ladder naming **`cbviewport`** (55%, different parent, raycastTarget at alpha 0.15). Fix `3a61d72`, ladder 11/0. |
| 5 (captions) | Tab captions | Copy on-device; index-walk 6/6 **driven by index, not name**. |
| 19 | Material-keyed palette counts (`7f92884`) | Device: "0 free · 1 in Steel" rendering, correctly absent when truly unowned. |
| 23 | Stale draft palette | **Isolated**: NEW-DRAFT entry path only; and a *correct* draft palette shows **no counts at all**, so the fix is a rendering change, not a recolour. |
| 14 | Tip text under its own controls | Root-caused: `InsetBar` overwrites `offsetMax.x` wholesale, eating a commented 206-unit reservation. Fix in `3a61d72` (compose, both axes); `msgText`'s silent 14→10 regression repaired by the same change. |
| 15 | "Tap the robot" does nothing | **Localised positively** (not fixed): nothing-selected drag orbits; selected drag makes the ghost *track the pointer live*; tap and press-drag-release do nothing, silently. Fault is in the commit branch. |
| 18 | targetGraphic | Refuted by runtime (Unity auto-assigns from the same GameObject). |

Also measured: **the starter crate affords the full 4-wheel beam assembly**
(wheels 4, chassis 1, battery, gyro, beams 6, brackets 4 free on an empty
bench) — retires the affordability premise of the tutorial-assembly
question. And a **real negative from source**: raycast and visual clip
boundaries coincide by construction throughout this UI (full-rect stencils,
`softness` unset) — that defect class does not exist here.

## 2. Fixed but not verified on device — named, not implied

No device operator remains. These ship on editor evidence only:
- **#16's fix** — device check is a tap on the bottom third. The first
  attempt (`SetAsLastSibling` placed before the occluders were created)
  **changed nothing and looked exactly like the approved fix** — only the
  re-run ladder caught it. The call is now at end-of-method with a guard
  comment.
- **#14's fix** — device check: tip text inside its reservation.
- **#24** (`fe0c265`) — **two of five edges device-verified**: LEFT ✅,
  TOP-content ✅ (the hidden control was "+ HAT"), TOP-background
  *cannot-determine*, BOTTOM *not covered* (a stolen swipe, not a wrong
  pixel), RIGHT *unverified rather than passed* (no notch to test against).
- **#12** (`b0056c9`) — needs a **damaged** battery; a healthy one renders
  identically in both versions.
- **#7** — bench-verified; the on-device WIN was never produced (an
  immobile robot cannot win).
- **#1 client half** (`023ef7c`) — UNMEASURED, not failed; the earlier
  "unfixed" read was the ROBOTS card (fielding status), not the ARENA row.
  Trap: the ARENA list renders only when `MyRobots.Count > 0`.

## 3. Carried open

- **#15 — placement dead in built players.** The one open defect a player
  meets in the first minute. Lead: a commit path that refuses **without
  writing `message`**. Needs a **two-producer instrument** (picking-path ray
  vs render camera), not a round-trip.
- **#8** — rescoped: the type pass has no per-panel concept; not a list fix.
- **#25** — two post-launch benches: a swept Button floor check (fail on
  unresolvable names; fail on dpi<1 instead of returning 99f), and a
  separate occlusion bench.
- **#11 predictions 3-5** — both fights were undriven; needs a driven fight.
- **The dpi question** — `3a61d72` says TEST DRIVE's rect is "69.9pt,
  comfortably over the floor", but the ladder's own 197px at sf≈1.79 implies
  `TouchRow()` at its 110-unit **ceiling**, which binds only above dpi≈730 —
  where the rect is 44pt *or less* and a bound ceiling is a shipped floor
  breach on every TouchRow-sized control, invisible to the check (it divides
  by the same dpi). Resolving datum: `Screen.dpi` as the build reports it.
- #21, #22, #10, #20, #3, #4 — unchanged, previously scoped.

## 4. Owen's register — decisions, never scheduled as work

1. Monetisation fork — premium $6.99 zero-IAP vs F2P SKU ladder,
   presented un-averaged. Both analysts: never make scrap purchasable;
   PITR on before money moves.
2. `MkInput` contrast (1.23:1 on every shipped dock input).
3. PITR on `rb-db`.
4. The 43% mutual disarm — levers swept; and nobody has asked whether 43%
   is wrong.
5. Worker latency vs cost.
6. Balance: CEILING L4 (92%) and the systematic STRETCH failures survive
   the 3× sample; FLOOR L4 / CEILING L5 were false alarms.
7. Tutorial assembly — guided step / pre-built starter / "match one
   teaches by defeat" (the loss pays +40 and the player survives the
   distance). The crate-affordability premise is now measured.
8. Store logistics — BattleBots trademark, Small Business Program
   enrollment, name availability.
9. Copy riders (parts-in-stock wording; ellipsis-on-truncation).
10. Does `[dev]` appear on a real TestFlight build — a question, not a
    derivation.

## 5. Instrument catalogue — seven artifacts, each produced a confident wrong reading

1. Wheel events (iOS ignores them). 2. Allowlist-filtered invisible
occluder windows. 3. Sub-frame synthetic taps (the pointer path samples a
LEVEL per frame). 4. Console scrollback misread as a live stream.
5. Undriven fights completing inside one capture round-trip. 6. The
Simulator hardware keyboard (suppresses the on-screen one — two passes of
"the field won't focus"). 7. Two fixtures with identical signatures cannot
discriminate a state transition.

Convention adopted: every geometry figure quotes its unit; pixel figures
quote the capture scale. **A wrong unit label is more dangerous than a
missing one** — a bare number invites the question, a mislabelled one
closes it.

## 6. Method findings — the durable output

1. A verification that cannot fail in the direction the defect lies (three
   instances, including a ratio used to validate a unit assumption —
   ratios are invariant under rescaling).
2. A prediction set is not a falsifier if every branch predicts the same
   reading. **The `[ray]` instrument turned out to be the worked example
   after all** (correction, post-assembly): its pre-registered vacuity test
   skewed the matrix BETWEEN the two calls — a two-matrix mismatch the real
   code path cannot produce — so it vouched for a sensitivity the readout
   does not have. A one-camera round trip returns ~0 for ANY consistent
   matrix; all three prediction branches produced the same reading, and
   prediction 3 fires on NEITHER clause. The tester who called the readout
   "not evidence" was right in substance and was argued out of it with the
   unrepresentative test; the engineer retracted on re-reading their own
   instrument. The correct instrument compares two PRODUCERS (picking ray
   vs render camera), never one round-tripped. A vacuity test must exercise
   the same code path it vouches for. Two riders from the participants'
   own ledger entries: the PM who accepted the refutation never asked what
   the test DID — took a note about a test as the test, one indirection
   past "check the artifact" — and the tester was argued out of a right
   answer by two agents with more context and better tools. **When an
   agent overturns a tester's call, the reversal must carry the test's
   code path, not its result** — a tester cannot audit a number they are
   handed.
3. A compound prediction fires only on the clause the measurement
   addresses.
4. Before refining a measurement, check whether the hypotheses differ in
   the projection you are measuring (a top pivot makes "undersized" and
   "occluded below" identical by construction).
5. Earlier and cheaper: does the hypothesis predict anything a user could
   see?
6. A measurement whose readout is a state change beats one whose readout
   is a boundary you interpret.
7. Not a missing abstraction — an existing one a second call site ignored.
   What makes a swept check work is that it **fails loudly without being
   consulted**.
8. A label is a claim with its definition detached.
9. The tree moves faster than the messages about it — open the file before
   writing about it.
10. The surface and the source fail differently, and neither is redundant:
    source is stronger for mechanism, weaker for completeness.

## 7. Process findings

- Twice a playtester had to refuse something a PM asked for (production
  auth; the editor bridge). Both requests were casual, inside messages
  about something else. **The boundary held because the brief NAMED the
  forbidden tool — write briefs that specific rather than trusting testers
  to infer the line.**
- Two people ran the same suspicion one command apart with opposite
  outcomes; the variable was whether a check happened between the thought
  and the send.
- The suspected channel fault did not exist (delivery is bursty; "no fault
  to localise" is a real result).
- Prediction discipline's value is not the hit rate: the two findings that
  outlive the session came from predictions that FAILED.

## 8. Engineer's addendum — what to inherit

> The instruments were wrong as often as the code. A measurement printed
> PASS while comparing one sample with itself; a fix changed nothing while
> its diff looked exactly like the approved one; a status column printed
> fractions as integers; and the readout that settled a dispute only
> counted because it had been checked, beforehand, that it could read
> non-zero. Four of those would have shipped as greens. The habit that
> caught them was asking, before each result, **what a passing value would
> have to look like if the instrument were dead** — and making the output
> say which case it is in.

## 9. Session ledger

~20 commits (engineer 18 incl. `8f0b444`; team-lead: build tooling,
CLAUDE.md corrections `b9241e5`). Builds cut: dev, release BEFORE/AFTER
matched pair, FINAL, FINAL3, FINAL4 (installed). Career save
**byte-identical across every play session, mtime `1786354063` — never
written.** Nothing pushed. Nothing submitted to the production server by
any agent at any point.
