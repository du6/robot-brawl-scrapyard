// ⚠ EDITOR AND DEVELOPMENT BUILDS ONLY — see CategoryBench.cs's header for why
// every harness in this project carries this guard.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
// ===========================================================================
// InventorySyncBench.cs — the parts half of wallet sync (Tier 1).
//
// PURE DATA. No scene, no play mode, no server, no fight:
//
//     RobotBrawl.Phase0.InventorySyncBench.RunPure();
//
// WHY THIS IS A BENCH AND NOT A LOOK.
//
// The defect this closes was invisible for weeks and green the whole time:
// GET /v1/wallet has ALWAYS answered with `inventory`, the server has always
// held the rows, purchases have always been transactional — and the client
// parsed `balance` and threw the rest away. Nothing failed. Scrap followed a
// player to a second device and their parts did not, and no check anywhere
// asked whether it should. So the first thing measured here is simply: does
// the parser keep what the server sent.
//
// The second is the dangerous one. Adoption is server-wins, which is right
// when the server has something to say and destructive when it does not. A
// brand-new account answers balance 0 / inventory [] / ledger [], and
// adopting that over a played career erases it. That is not a hypothetical:
// EconomySync's own header records 2026-08-13, when signing into a fresh dev
// account flattened a 6,513-scrap career to 500. The shipped iOS build made
// it unreachable because its gate forces sign-in BEFORE a career exists —
// and PLAY AS GUEST on the web build removed exactly that protection. A
// guest can now earn scrap and parts and only then sign in, which is the
// precise shape of the incident. Section B is that rule, over its whole
// truth table rather than the one case someone remembered.
//
// ⚠ CAREER ISOLATION. These checks mutate Career.Data, so the bench swaps in
// a scratch career, holds autosave down for its whole run, and restores the
// real one in a finally. Hard rule 5: owner state is sacred.
// ===========================================================================

using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public static class InventorySyncBench
    {
        static int passed, failed;
        static readonly List<string> log = new List<string>();
        public static string report = "";

        static void Check(bool ok, string what)
        {
            if (ok) passed++; else failed++;
            log.Add((ok ? "PASS  " : "FAIL  ") + what);
        }
        static void Note(string s) { log.Add("      " + s); }

        /// <summary>A real /v1/wallet body, shaped exactly as Program.cs emits
        /// it — balance, recent[], inventory[] — so the parser is measured
        /// against the server's actual contract and not a convenient one.</summary>
        const string WALLET_JSON =
            "{\"balance\":1240," +
            "\"recent\":[" +
              "{\"delta\":500,\"reason\":\"purse\",\"matchId\":null,\"at\":\"2026-09-01T04:00:00\"}," +
              "{\"delta\":-88,\"reason\":\"purchase\",\"matchId\":null,\"at\":\"2026-09-01T04:05:00\"}]," +
            "\"inventory\":[" +
              "{\"partId\":\"wheel\",\"mat\":\"Rubber\",\"count\":4}," +
              "{\"partId\":\"beam\",\"mat\":\"Aluminum\",\"count\":6}," +
              "{\"partId\":\"spinner\",\"mat\":\"Carbon Fiber\",\"count\":1}]}";

        /// <summary>A brand-new account: no history, nothing owned.</summary>
        const string FRESH_JSON = "{\"balance\":0,\"recent\":[],\"inventory\":[]}";

#if UNITY_EDITOR
        /// <summary>Batch-mode entry point, so this can be compiled AND run
        /// without a live editor:
        ///   Unity -quit -batchmode -projectPath . \
        ///         -executeMethod RobotBrawl.Phase0.InventorySyncBench.RunPureBatch
        /// Exits non-zero on any failure so the shell is honest about it.</summary>
        public static void RunPureBatch()
        {
            bool ok = RunPure();
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(ok ? 0 : 1);
        }
#endif

        /// <summary>Returns true when every check passed.</summary>
        public static bool RunPure()
        {
            passed = failed = 0; log.Clear(); report = "";

            var realCareer = Career.Data;
            using (Career.SuspendAutosave())
            {
                try
                {
                    SectionA_Parse();
                    SectionB_AdoptionRule();
                    SectionC_Adopt();
                    SectionD_FreshAccountCannotWipe();
                }
                finally
                {
                    Career.Data = realCareer;      // owner state is sacred
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("InventorySyncBench — passed " + passed + ", failed " + failed);
            if (passed == 0) sb.AppendLine("NOTHING RAN — this is not a pass.");
            foreach (var l in log) sb.AppendLine(l);
            report = sb.ToString();
            Debug.Log(report);
            return failed == 0 && passed > 0;
        }

        // -- A. the parser keeps what the server sent -----------------------
        static void SectionA_Parse()
        {
            log.Add("-- A. /v1/wallet parsing --");
            var w = LadderClient.ParseWallet(WALLET_JSON);
            Check(w != null, "a real wallet body parses");
            if (w == null) return;

            Check(w.balance == 1240, "balance survives (" + w.balance + ")");
            Check(w.inventory.Count == 3,
                  "ALL THREE inventory rows survive — the bug was dropping them (" + w.inventory.Count + ")");
            Check(w.ledgerEntries == 2, "ledger entries counted (" + w.ledgerEntries + ")");

            // A material with a space in it is the one most likely to be
            // mangled by a hand-rolled parser, so it is in the fixture.
            bool cf = false;
            foreach (var it in w.inventory)
                if (it.partId == "spinner" && it.mat == "Carbon Fiber" && it.count == 1) cf = true;
            Check(cf, "a multi-word material round-trips (\"Carbon Fiber\")");

            var fresh = LadderClient.ParseWallet(FRESH_JSON);
            Check(fresh != null && fresh.IsFresh,
                  "an untouched account reports IsFresh");
            Check(w.IsFresh == false, "an account with history does NOT report IsFresh");

            Check(LadderClient.ParseWallet("{\"error\":\"nope\"}") == null,
                  "a body with no balance parses to null rather than to zero");

            // count <= 0 is not ownership. The server filters these, but the
            // cache must not depend on the server remembering to.
            var zero = LadderClient.ParseWallet(
                "{\"balance\":5,\"recent\":[],\"inventory\":[{\"partId\":\"wheel\",\"mat\":\"Rubber\",\"count\":0}]}");
            Check(zero != null && zero.inventory.Count == 0,
                  "a zero-count row is not ownership and is dropped");
        }

        // -- B. the rule that stops a fresh account eating a career ---------
        static void SectionB_AdoptionRule()
        {
            log.Add("-- B. adoption rule, whole truth table --");
            // Server has history, device has a career -> adopt (the normal case).
            Check(EconomySync.ShouldAdopt(false, true),
                  "established account + played device  -> ADOPT");
            // Server has history, device is blank -> adopt (a second device).
            Check(EconomySync.ShouldAdopt(false, false),
                  "established account + blank device   -> ADOPT (this is the feature)");
            // Fresh account, blank device -> adopting nothing costs nothing.
            Check(EconomySync.ShouldAdopt(true, false),
                  "fresh account + blank device         -> ADOPT (harmless)");
            // Fresh account, played device -> THE INCIDENT. Must refuse.
            Check(EconomySync.ShouldAdopt(true, true) == false,
                  "fresh account + played device        -> REFUSE (the 2026-08-13 shape)");
        }

        // -- C. adoption replaces the cache and normalises rows -------------
        static void SectionC_Adopt()
        {
            log.Add("-- C. adoption --");
            Career.Data = new CareerData();
            Career.AddItem("wheel", "Rubber", 99);        // stale local row
            Career.AddItem("ghostpart", "Aluminum", 3);   // server has never heard of it

            var w = LadderClient.ParseWallet(WALLET_JSON);
            int n = EconomySync.TestAdoptInventory(w.inventory);

            Check(n == 3, "three rows adopted (" + n + ")");
            Check(Career.CountOf("wheel", "Rubber") == 4,
                  "a stale local count is REPLACED by the server's, not added to (99 -> "
                  + Career.CountOf("wheel", "Rubber") + ")");
            Check(Career.CountOf("ghostpart", "Aluminum") == 0,
                  "a part the server does not list is not owned — this is the save-editor hole");
            Check(Career.CountOf("beam", "Aluminum") == 6, "a part the device never had arrives");
            Note("adoption is a REPLACE by design: the server's row set IS ownership.");
        }

        // -- D. the guard, end to end on real career state ------------------
        static void SectionD_FreshAccountCannotWipe()
        {
            log.Add("-- D. a fresh account cannot wipe a played career --");

            Career.Data = new CareerData();
            Check(EconomySync.LocalHasProgress() == false,
                  "an untouched career reports no progress");

            Career.AddItem("wheel", "Rubber", 4);
            Check(EconomySync.LocalHasProgress(),
                  "owning parts alone counts as progress");

            var fresh = LadderClient.ParseWallet(FRESH_JSON);
            Check(EconomySync.ShouldAdopt(fresh.IsFresh, EconomySync.LocalHasProgress()) == false,
                  "signing a played GUEST career into a brand-new account adopts NOTHING");

            // And prove the parts would in fact have been destroyed, so this
            // check is measuring a real consequence and not a mood.
            EconomySync.TestAdoptInventory(fresh.inventory);
            Check(Career.CountOf("wheel", "Rubber") == 0,
                  "...because adopting it WOULD have emptied the cache (proof the guard matters)");
        }
    }
}
#endif
