# The catalog lost two parts: the saw and the bracket (2026-08-12)

Owen's calls, both hard deletes (`f0114da`, `d20817a`). Read this before
"fixing" a missing part or resurrecting either id.

## Circular saw (`spinnerSaw`)

Redundant BY ROLE with the spinner: both are unpowered rotors bolted past a
spindle. **The history has a twist worth knowing**: owen flagged this exact
pair on 2026-07-28, and the measurement then showed the SAW dominating on
every axis (reach, stored energy, edge, HP; 3.2 bites/213 dmg vs 0.6/115) —
`massMul` was invented in response, to make the spinner's "dense flywheel"
identity physically real. The post-massMul trade was never re-measured; the
cut resolves the pair the other way, keeping the disc every roster bot,
production robot and owen's own build actually carries. Verified before the
cut: **no save, no enemy recipe, no production snapshot, no kit line carried
a saw.**

## Bracket

Redundant BY USAGE, not overlap: the 0.2 m cube joiner had **zero** uses in
the campaign enemy roster, owen's save and production; its only callers were
the starter kit (four granted, none ever placed by anyone) and dev harnesses.
The kit's ~88 kg hole is deliberately NOT refilled here — what replaces it,
if anything, is the register's tutorial-assembly decision. The R-key
symmetric-shape fallback the bracket motivated stays (the battery is also a
cube, and covers that case in the R-cycle sweeps).

## Kept, explicitly

- **Ram spike** — proposed for the same cut and REPRIEVED: it is the starter
  kit's weapon, four campaign enemies are built on it (BULWARK's identity is
  the spike on purpose), and owen's own robot carries one. Not redundant
  where it lives.
- **Long beam** — real mechanical identity (fewer seams) in a game whose
  central failure mode is seam shear; premise never benched, kept anyway.

## Safety facts both cuts rest on

- The snapshot loader SKIPS unknown part ids by design (FuzzBench-hardened
  `if (def == null) continue;`) — an old save or snapshot naming a removed
  part loads minus that part, it does not crash.
- Measured after each cut: CategoryBench 44/44 · LadderClientBench 40/40 ·
  TouchSmoke 31/31 · CareerSmoke 127/1, where the 1 is the pre-existing
  94-label legibility fail (`41bf109`), bit-identical before and after.
- Owen's career save byte-identical throughout, mtime included.
