# The ARENA, photographed and judged — 2026-08-10

> ⚠ **SUPERSEDED THE SAME DAY, and deliberately kept.** Everything §1 and §2
> describe was fixed on 2026-08-10: the ARENA got an entry point (the ARENA
> tab), and then all five surfaces were ported to UGUI inside the dock. The
> screenshots this document refers to are gone; `docs/shots/07..16` are the
> ported screens. It is kept because the FINDINGS are why the port happened,
> and because §2.4's shared-status leak was reproduced in the port hours after
> being written down here — a document is not a defence against rebuilding the
> thing it describes. See `Assets/Phase1/qa_arena_ugui.txt`.

`docs/shots/*.png`, produced by `RobotBrawl.Phase0.ArenaShots.Run("/tmp/arena_shots")`
in play mode against a local API. Re-runnable; that is the point.

`CLAUDE.md` §7.5 called the ARENA layout "placeholder and largely unjudged".
It was unjudged for a harder reason than nobody getting round to it.

---

## 1. THE ARENA CANNOT BE OPENED

`ArenaScreen`'s script GUID (`97cc5b1041667...`) appears in **no scene and no
prefab**, and **no product code constructs it**. The only things that have ever
instantiated it are benches.

So the board, the scouting card, the challenge flow, the inbox, the replay
launcher, sign-in, the shop and the new enlist panel are all live, working code
with **no entry point in the game**. The screenshots in `docs/shots/` exist
only because `ArenaShots` builds the screen itself.

**This is why the missing ENLIST flow survived two days.** Nobody could reach
the screen to notice the button was not there. It is the same defect as
`HANDOVER_2026-08-10` §3 and §6a — a green, working, *uncalled* thing — and
this is the third instance in three days.

The bottom navigation is visible in every shot and settles it:

    BUILD | LEAGUE | ROBOTS | SHOP | TROPHIES | PROGRAM

Six tabs, and none of them is the ladder. LEAGUE is the single-player league.
**Where the ARENA lives is owen's call** — a seventh tab is the obvious answer
and it is not mine to make, because the tab bar is the game's spine and it is
already full at six.

⚠ Note also that **SHOP is already a tab**, and `ArenaScreen` draws its own
shop. Two shops, one of them unreachable, spending different currencies.
Worth a decision before either gets styled.

---

## 2. What the layout actually does wrong

Judged from the shots, worst first. None of this is subjective polish.

1. **It collides with the tutorial banner.** `03`/`04`/`05` show the ARENA
   header — "ARENA  http://localhost:5000" — with the builder's own tip strip
   ("pick a part below, then tap the robot to bolt it on") and SKIP TIPS drawn
   straight through it. Two UI systems, same band, neither aware of the other.
2. **The panel runs under the tab bar.** Its bottom-left corner disappears
   behind BUILD. Anything placed low in the panel is unreachable.
3. **The API base URL is shown to the player.** `http://localhost:5000` in the
   header is a debug affordance sitting in the product.
4. **Stale status leaks between panels.** "8 ranked" — the board's status —
   sits directly above ENLIST A ROBOT in `04` and `05`. One `status` string is
   shared by every surface, so each one inherits whatever the last said.
5. **Dead space.** The enlist panel ends at ~40% height and leaves ~350px of
   empty box, because the panel is sized for the board.
6. **The category buttons are a ragged 3+3 grid.** P4P is visibly narrower
   than FEATHER and LIGHT; MIDDLE/HEAVY/SUPER wrap to a second row with
   different widths again.
7. **Long robot names wrap and make rows uneven** (`03`), so the board reads as
   a list of different-sized things rather than a table.
8. **No visual identity.** Default `GUI.skin` grey against a bottom nav that
   has real styling. The ARENA looks like a debug overlay because it is one.

## 3. What is already right, and should not be "fixed"

- The **enlist copy is good and does not overclaim**: "a match worker checks it
  is legal and sets its weight class before it appears on the board." It says
  PENDING without saying PENDING.
- The **empty state** (`05`) is the best surface here: it explains what
  enlisting *is* before explaining why it cannot happen yet.
- **`provisional` is shown, not hidden** — the board is honest about its own
  confidence.
- The **re-enlist rule is stated where the decision is made**: "re-enlisting
  under a name you already use replaces that robot's build and keeps its
  rating."

## 4. The one thing to do before styling anything

Give the ARENA an entry point. Styling a screen nobody can open is the
expensive way to find out the tab bar had no room for it.
