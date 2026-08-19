# Prediction, written BEFORE the change (house rule #6) — gusset ×1.5 → ×4.0

1. IDENTITY: the conn threshold for a gusseted seam scales by exactly 4.0/1.5
   = ×2.6667 versus today. The MatrixBench fixture's spindle↔spinner seam went
   2700 → 4050 at ×1.5; at ×4.0 it should read 2700 → 10800 exactly, and the
   neighbouring ungusseted beam↔spindle seam must stay 2700.
2. DIRECTIONALITY is unchanged: the wrong face still buys nothing.
3. MASS is unchanged: +10 kg per weld. This is a strength change only.
4. GAMEPLAY, and this is the part I expect to disappoint: the mutual-disarm
   RATE will barely move. The sweep measured global BREAK_K saturating at
   ×1.5 (40%→27%, and ×2.0 identical), because per
   Mutual_Disarm_Root_Cause_2026-08-09 THE LIMB FAILS, NOT THE WEAPON. A
   stronger weapon seam does not help when the seam that breaks is one joint
   upstream. Expect `shed` to fall only slightly and `struct` (parts destroyed
   outright) to RISE, because energy that can no longer separate a joint has
   to go somewhere.
5. HARDENED ENEMIES GET TOUGHER. EnemyRoster gives them gussetFaces=63, so
   this raises every seam on those bots by the same factor. Career difficulty
   moves; CareerBench bands may shift.
