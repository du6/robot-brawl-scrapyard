// ===========================================================================
// RobotCategory.cs — Multiplayer v3, M1: the ladder's weight categories.
//
// Design doc: Multiplayer_V3_Design_Doc.md §1.2.
//
// §5.2's rule is one implementation, zero drift: the worker IS the server
// side, and the worker runs THIS code. So the authoritative table lives here,
// next to RobotSnapshot, and the API never computes a category — it stores
// what the worker reports (Program.cs, /v1/worker/jobs/{id}/validate-result:
// "the API stores it and does not second-guess it").
//
// ⚠ THE IDS ARE A DATABASE CONTRACT. server/RobotBrawl.Api/Migrations/
// 001_init.sql constrains BOTH snapshots.category and ratings.category to
//     ('FEATHER','LIGHT','MIDDLE','HEAVY','SUPER')
// A sixth name added here is an INSERT that fails at the far end of a job,
// after the work is already done. CategoryBench asserts the exact set.
//
// ⚠ MASS ONLY — the size box is deliberately NOT part of this rule.
// OWEN DECISION 2026-08-09. §1.2 as written defines the natural category as
// the smallest class satisfying BOTH a weight cap AND a per-class size box,
// and states the boxes are "already tuned, already enforced by Validate()".
// That sentence is stale. The size box was REMOVED from the game on
// 2026-08-03 (baabe3a — owen: "we already have the weight limit. why do we
// also need size limit?"): League.sizeBox went from the struct and all five
// league definitions, BuilderManager.OverSizeBox and SizeBoxLine were deleted,
// and CareerValidate has checked mass alone ever since. Reinstating boxes here
// would give the game two different definitions of legal — which is precisely
// the drift §5.2 exists to prevent — so the ladder enforces the rule the game
// actually enforces.
//
// Two further reasons the box would not have earned its place even if it had
// survived: Feather and Light share one box and Middle and Heavy share
// another, so a box never separates those pairs — it can only push a robot up
// a class or out of the sport entirely; and anything over 2.0 m tall fits no
// box at all, which would make a robot legal with no category, a state the
// ladder has nowhere to put (ratings.category is NOT NULL).
//
// THE OPEN COST, written down so it is not rediscovered as a surprise: weight
// bounds density, not size. Under a 1,500 kg cap one Beam is 463 kg in
// Tungsten and 25 kg in ABS, so a legal FEATHERWEIGHT can be a 59-beam ABS
// tower roughly 35 m tall in a 14 m arena (Size_Box_On_Build_Bar_2026-08-03).
// In career the only opponent is the AI and nobody builds that; on the ladder
// the opponent is a human looking for exactly this. NOTHING HAS MEASURED
// WHETHER A TOWER ACTUALLY WINS. If it does, the lever is density or an
// explicit reach cap, applied to the whole game — NOT a size box quietly added
// back into this file, and NOT a ladder-only special case career does not
// share. BuilderManager.CareerValidate's docstring says the same thing at the
// other place someone would be tempted.
//
// The caps are Career.cs's five league caps, unchanged and in the same order
// (1,500 / 2,000 / 2,800 / 4,000 / 5,500 kg). The boundary is CareerValidate's
// boundary — `mass > cap` is over — so a robot exactly ON a cap is legal in
// that class. This matters more here than in career: §1.2 resets a robot's
// rating to placement whenever a re-upload changes its natural category, so a
// boundary that is one kilogram out does not raise an error, it silently moves
// a robot to a different ladder and wipes what it earned. Every boundary in
// this table is therefore benched from BOTH sides.
// ===========================================================================

namespace RobotBrawl.Phase0
{
    public static class RobotCategory
    {
        public struct WeightClass
        {
            /// <summary>The token stored in the database. See the contract note above.</summary>
            public readonly string id;
            /// <summary>Player-facing name (§1.2's table).</summary>
            public readonly string label;
            /// <summary>Inclusive: mass == capKg is legal in this class.</summary>
            public readonly int capKg;

            public WeightClass(string i, string l, int c) { id = i; label = l; capKg = c; }
        }

        /// <summary>Ascending by cap. "Smallest satisfying" is a first-match
        /// scan, so the ORDER IS THE RULE — CategoryBench asserts the table is
        /// strictly ascending rather than trusting that it looks sorted.</summary>
        public static readonly WeightClass[] LADDER =
        {
            new WeightClass("FEATHER", "Featherweight", 1500),
            new WeightClass("LIGHT",   "Lightweight",   2000),
            new WeightClass("MIDDLE",  "Middleweight",  2800),
            new WeightClass("HEAVY",   "Heavyweight",   4000),
            new WeightClass("SUPER",   "Super-heavy",   5500),
        };

        public static int HeaviestCapKg { get { return LADDER[LADDER.Length - 1].capKg; } }

        /// <summary>§1.2's natural category: the smallest class whose weight
        /// cap the robot meets. Returns null when NO class can hold it —
        /// callers must treat null as a validation failure and never as
        /// "unrated", because a snapshot with no category cannot be placed on
        /// any ladder (ratings.category is NOT NULL). Reason() says why.</summary>
        public static string Assign(int massKg)
        {
            if (massKg <= 0) return null;
            for (int i = 0; i < LADDER.Length; i++)
                if (massKg <= LADDER[i].capKg) return LADDER[i].id;
            return null;
        }

        /// <summary>Why Assign() returned null; null when it did not. Phrased
        /// like CareerValidate's over-cap message so the ladder and the builder
        /// refuse a robot in the same words.</summary>
        public static string Reason(int massKg)
        {
            if (massKg <= 0)
                return "build has no mass — nothing to weigh in";
            if (massKg > HeaviestCapKg)
                return string.Format(
                    "{0} kg over the heaviest category ({1} kg limit, build is {2} kg).",
                    massKg - HeaviestCapKg, HeaviestCapKg, massKg);
            return null;
        }

        public static bool IsKnown(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < LADDER.Length; i++) if (LADDER[i].id == id) return true;
            return false;
        }

        /// <summary>Inclusive cap for a category id; -1 for an unknown id.</summary>
        public static int CapKg(string id)
        {
            for (int i = 0; i < LADDER.Length; i++) if (LADDER[i].id == id) return LADDER[i].capKg;
            return -1;
        }

        /// <summary>Player-facing name; the id itself for an unknown id, so a
        /// UI never renders an empty string where a class name belongs.</summary>
        public static string Label(string id)
        {
            for (int i = 0; i < LADDER.Length; i++) if (LADDER[i].id == id) return LADDER[i].label;
            return id ?? "";
        }

        /// <summary>§1.2: a robot may challenge into its natural category or
        /// any heavier one, never lighter. Written here rather than at the
        /// challenge site so "heavier" means one thing in the whole game.</summary>
        public static bool MayChallengeInto(string natural, string target)
        {
            int a = IndexOf(natural), b = IndexOf(target);
            return a >= 0 && b >= 0 && b >= a;
        }

        static int IndexOf(string id)
        {
            for (int i = 0; i < LADDER.Length; i++) if (LADDER[i].id == id) return i;
            return -1;
        }
    }
}
