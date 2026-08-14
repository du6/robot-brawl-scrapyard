// ⚠ EDITOR AND DEVELOPMENT BUILDS ONLY — 2026-08-10.
// This is a QA harness. It was compiling into the SHIPPED iOS player: 44 such
// files, ~13k lines, and the string "dev-only-worker-key" was verified present
// in build/ios global-metadata.dat. Dead weight in the binary, and a surface
// that can register accounts and start fights inside a player's app.
// Nothing in the product references any harness — checked against every
// product script, every scene and every prefab before this guard was added.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
// ===========================================================================
// ChallengeGateBench — the rule that decides who may fight whom. 2026-08-10.
//
//   RobotBrawl.Phase0.ChallengeGateBench.RunPure()     // no scene, no network
//
// PURE. No play mode, no server, under a second. The gate is now one method
// (ArenaScreen.ChallengeBlocker) precisely so it could become one, and this is
// the cheapest green in the ARENA.
//
// WHY IT EXISTS. This is the surface that SPENDS SCRAP. §1.2 says you may
// punch UP and never down, and the screen has three DIFFERENT refusals that a
// player must be able to tell apart:
//
//   "this is yours"                        — pick someone else
//   "you have no robot on the ladder yet"  — go and ENLIST
//   "you can punch up, never down"         — pick a heavier opponent
//
// Saying the wrong one is not cosmetic. It happened: a player with NO robots
// was told they could not punch down, which is the one thing that cannot be
// their problem, and that wrong sentence is part of why the missing ENLIST
// flow went unnoticed for two days. The messages are asserted here BY CASE,
// not just the yes/no, because the yes/no was never the part that was wrong.
//
// It also pins the STAKE ladder: 50 scrap per class of difference, so punching
// up costs more than fighting your own weight.
// ===========================================================================
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public static class ChallengeGateBench
    {
        static int passed, failed;
        static readonly List<string> log = new List<string>();

        static void Check(bool c, string w)
        {
            if (c) { passed++; log.Add("PASS  " + w); }
            else   { failed++; log.Add("FAIL  " + w); }
        }

        static MyRobot Bot(string name, string cat, bool active = true)
        {
            return new MyRobot { id = name, name = name, category = cat,
                                 activeSnapshotId = active ? name + "-snap" : "" };
        }

        static ScoutCard Card(string cat, bool mine = false)
        {
            return new ScoutCard { snapshotId = "target-snap", robotName = "Target",
                                   category = cat, mine = mine, massKg = 500 };
        }

        public static bool RunPure()
        {
            passed = 0; failed = 0; log.Clear();

            var go = new GameObject("challenge_gate_bench");
            var ui = go.AddComponent<ArenaScreen>();
            string savedToken = LadderClient.Token;

            try
            {
                // ---- the three refusals, by case --------------------------
                LadderClient.Token = "";
                ui.TestSetMine(new List<MyRobot> { Bot("Mine", "FEATHER") });
                Check(ui.ChallengeBlocker(Card("FEATHER")) == "sign in to challenge.",
                      "signed out: it asks you to sign in");

                LadderClient.Token = "dummy-token";
                Check(ui.ChallengeBlocker(Card("FEATHER", mine: true)) == "this is yours.",
                      "your own robot: it says so, and does not offer a fight");

                ui.TestSetMine(new List<MyRobot>());
                string noRobots = ui.ChallengeBlocker(Card("FEATHER"));
                Check(noRobots != null && noRobots.Contains("no robot on the ladder"),
                      "NO ROBOTS: it says to ENLIST -- got: " + noRobots);
                Check(noRobots != null && !noRobots.Contains("punch"),
                      "…and does NOT blame punching down, which cannot be the problem");

                // Heavier robot, lighter target: punching DOWN, which §1.2 bans.
                ui.TestSetMine(new List<MyRobot> { Bot("Heavy", "HEAVY") });
                string down = ui.ChallengeBlocker(Card("FEATHER"));
                Check(down != null && down.Contains("punch up, never down"),
                      "punching DOWN is refused, and named -- got: " + down);
                Check(down != null && down.Contains("FEATHER"),
                      "…and it names the class you cannot reach");

                // A robot with no ACTIVE snapshot is not eligible: it is
                // enlisted but unjudged, and the server would refuse it.
                ui.TestSetMine(new List<MyRobot> { Bot("Pending", "FEATHER", active: false) });
                string unjudged = ui.ChallengeBlocker(Card("FEATHER"));
                Check(unjudged != null,
                      "a robot still waiting to be validated cannot fight");
                // ⚠ AND IT MUST SAY WHY. This is the same shape as the bug
                // above: the player's robot is the RIGHT WEIGHT and simply not
                // judged yet, so "you can punch up, never down" is a sentence
                // about a rule they did not break. Waiting is not a refusal
                // they can act on by picking a different opponent.
                Check(unjudged != null && !unjudged.Contains("punch"),
                      "…and does NOT blame punching down -- got: " + unjudged);

                // ---- and the legal cases ---------------------------------
                ui.TestSetMine(new List<MyRobot> { Bot("Mine", "FEATHER") });
                Check(ui.ChallengeBlocker(Card("FEATHER")) == null,
                      "same class is allowed");
                Check(ui.ChallengeBlocker(Card("HEAVY")) == null,
                      "punching UP is allowed");

                var el = ui.EligibleFor(Card("HEAVY"));
                Check(el.Count == 1 && el[0].name == "Mine",
                      "the eligible list is exactly the robots that may answer");

                ui.TestSetMine(new List<MyRobot> {
                    Bot("F", "FEATHER"), Bot("H", "HEAVY"), Bot("P", "FEATHER", active: false) });
                var el2 = ui.EligibleFor(Card("LIGHT"));
                Check(el2.Count == 1 && el2[0].name == "F",
                      "mixed stable: only the FEATHER answers a LIGHT (HEAVY is down, P unjudged)");

                // ---- the stake ladder ------------------------------------
                // 50 per class of difference. Punching up costs more, which is
                // what makes "punch up" a decision rather than free upside.
                ui.TestSetMine(new List<MyRobot> { Bot("Mine", "FEATHER") });
                ui.SetPick(0);
                Check(ui.StakeForPick(Card("FEATHER")) == 50, "same class stakes 50");
                Check(ui.StakeForPick(Card("LIGHT")) == 100, "one class up stakes 100");
                Check(ui.StakeForPick(Card("MIDDLE")) == 150, "two classes up stakes 150");
                Check(ui.StakeForPick(Card("SUPER")) == 250, "four classes up stakes 250");

                // ---- the purse ladder ------------------------------------
                // (1 + 0.5*gap)^2 x 100, mirroring §2.3 exactly — the reward
                // half of "punch up". Two classes up pays 4x; the card must
                // SAY so, and this is the number it says.
                Check(ui.PurseForPick(Card("FEATHER")) == 100, "same class pays 100");
                Check(ui.PurseForPick(Card("LIGHT")) == 225, "one class up pays 225");
                Check(ui.PurseForPick(Card("MIDDLE")) == 400, "two classes up pays 400");
                Check(ui.PurseForPick(Card("SUPER")) == 900, "four classes up pays 900");
                Check(ui.GapForPick(Card("MIDDLE")) == 2, "gap reads 2 for two classes up");

                // ---- confirm cannot fire through a closed gate ------------
                // The board reloads while the card is open, so a pick that was
                // legal a second ago may not be. ConfirmChallenge re-asks.
                ui.TestSetCard(Card("FEATHER"));
                ui.ArmChallenge();
                Check(ui.Pending, "the confirm step arms");
                ui.TestSetMine(new List<MyRobot>());          // the stable empties underneath it
                ui.ConfirmChallenge();
                Check(!ui.Pending,
                      "confirming through a gate that has since CLOSED disarms instead of spending");
            }
            finally
            {
                LadderClient.Token = savedToken;
                if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go);
            }

            var sb = new System.Text.StringBuilder();
            foreach (var l in log) sb.Append(l).Append('\n');
            sb.Append("===== passed ").Append(passed).Append("  failed ").Append(failed).Append(" =====");
            Debug.Log("[ChallengeGate] " + sb.ToString());
            return failed == 0;
        }

        public static int Passed { get { return passed; } }
        public static int Failed { get { return failed; } }
    }
}
#endif
