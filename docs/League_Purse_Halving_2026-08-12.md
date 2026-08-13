# The league pays half — the economy moved because the AI could not (2026-08-12)

Owen's call. The measured problem: CareerBench's surviving verdicts say the
league is too easy at the top — **CEILING L4 at 92%** (a robot two classes
below the flagship wins nearly always) and **STRETCH failing L3/L4/L5 in the
same direction**. The honest lever for that is opponent tuning, but owen has
low confidence in the AI robots, so the ECONOMY moved instead: win less from
beating easy AI, earn more (relatively) on the ARENA, whose purses and season
podium (300/150/100) did not change and now buy twice the progression.

## What halved

- **All 14 career contest purses** (`CareerDB.Leagues`): L1 125/150 · L2
  200/225/250 · L3 350/400/450/475 · L4 600/700/800/900 · L5 1500.
- `FIRST_WIN_BONUS` 150 → 75; `WIN_DMG_K` 0.5 → 0.25 (win-side package moves
  together, so fixed bonuses cannot come to dominate the purse).
- The legacy profile path, for consistency: rungs 125→500, the five
  challenges 200–500, exhibition flat purse 150 → 75, same dmg-slope halving.

## What deliberately did NOT halve

- **Loss consolation** (both paths) — it pays the struggling player, not the
  winning one; the problem being treated is winners earning too fast.
- **Entry fees** — a cost, not a reward. Fee/purse ratios therefore doubled
  (L5: 200/1500 ≈ 13%), which is a real, intended sharpening.
- Every fraction that scales WITH the purse (re-entry 40%, underdog ×1.6 cap).
- ARENA: stake purses, season payouts — unchanged, per the intent.

## Consequences to know

- The R1-CRITIC anti-grind ordering still holds (max exhibition win 175 vs
  loss cap 100), re-verified by inspection of the same guard.
- Budget upgrades and early material buys are now ~2x the fights they were —
  the profile-path R2-CRITIC pacing note ("~9,200 one-time income") reads
  double-stale; pacing IS the intent, but if the endgame starves, the knob is
  documented there.
- The career design doc's section-5 numbers are now HISTORICAL — the tables
  in code carry the live values and the rationale.
- CareerBench measures WIN RATES, not scrap, so it neither covers nor
  contradicts this change; the cover is CareerSmoke's settle checks, which
  derive from WinPay and passed at the new values: **128/128**.
- ⚠ Noted in passing: CareerSmoke's widened legibility check (94 labels,
  `41bf109`) failed on every run earlier today and PASSED on this one, with
  no font-touching change in between — the pt measurement is evidently
  sensitive to the Game view's simulated dpi. Treat that check's verdict as
  environment-dependent until someone pins the device profile.
