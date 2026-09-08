// ===========================================================================
// RBTelemetry.cs — the funnel, and nothing more than the funnel.
//
// WHY THIS EXISTS. On 2026-09-01 the browser build drew 19 outside visitors in
// two hours — more real people than the App Store had produced in three weeks
// — and the only thing we could say about any of them was that 10 reached a
// playable game. Whether one of them ever placed a part, entered the league or
// finished a fight was unmeasured, and the server could not help: career play
// is local, and every one of those visitors was a guest with no account. This
// closes that, and only that.
//
// WHAT IT SENDS. One fire-and-forget hit per event, each event AT MOST ONCE per
// page load, carrying an ephemeral random id so the hits can be grouped into a
// funnel. That id is generated in memory by the page, is never written to
// storage of any kind, and dies with the tab — it cannot follow anyone across
// sessions, days, or sites, which is what keeps the privacy policy's "no
// tracking across apps or websites" literally true rather than lawyered.
//
// WHAT IT DELIBERATELY DOES NOT SEND. No robot names, no program contents, no
// free text, no per-action stream, no identifier that outlives the tab. The
// dimensions are three: how long the payload took, which door was taken, and
// whether the first fight was won. That is the whole vocabulary.
//
// ⚠ WEB ONLY, BY CONSTRUCTION. Everything below compiles away outside a WebGL
// player, so the iOS build, the editor, and all 44 benches are untouched and
// cannot emit a single hit — a bench that quietly reported itself as a player
// would poison exactly the numbers this exists to produce.
// ===========================================================================

using System.Collections.Generic;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace RobotBrawl.Phase0
{
    public static class RBTelemetry
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void RBBeaconSend(string ev, string extra);
#endif

        /// <summary>Events already sent this page load. First occurrence is the
        /// signal; the hundredth part placed tells us nothing the first did not,
        /// and would turn a funnel into a firehose.</summary>
        static readonly HashSet<string> sent = new HashSet<string>();

        /// <summary>Bench-readable: what this session has reported so far.</summary>
        public static int Count { get { return sent.Count; } }
        public static bool Has(string ev) { return sent.Contains(ev); }
        public static void TestReset() { sent.Clear(); }

        /// <summary>Report <paramref name="ev"/> once. `extra` is appended to the
        /// query string verbatim and must be a short literal like "&amp;w=1" —
        /// never anything a player typed.</summary>
        public static void Once(string ev, string extra = "")
        {
            if (string.IsNullOrEmpty(ev) || !sent.Add(ev)) return;
            Send(ev, extra);
        }

        static void Send(string ev, string extra)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { RBBeaconSend(ev, extra ?? ""); }
            catch { /* counting must never break playing */ }
#endif
        }

        // ---- the funnel, named once so call sites cannot invent variants ----
        /// <summary>What the BOOT did about the sign-in wall: "shown" (the
        /// player was asked) or "skip" (a stored session or guest flag meant
        /// they never saw it). Separate from DOOR because Once() dedupes on
        /// the event NAME — folding both into "gate" would have let the first
        /// swallow the second — and because the pair is the whole point: a
        /// session with a `shown` and no `door` is somebody who looked at the
        /// wall and left, which is unmeasurable from the door alone.</summary>
        public const string GATE   = "gate";     // shown | skip
        /// <summary>Which door was taken: guest | signin | signup.</summary>
        public const string DOOR   = "door";
        public const string BUILD  = "build";    // first part placed
        public const string SAVED  = "saved";    // robot founded
        public const string FIGHT  = "fight";    // first league fight started
        public const string RESULT = "result";   // first fight finished
        public const string RETURN = "return";   // booted into an existing career
        public const string RESCUE = "rescue";   // first-defeat crate granted
        public const string FIGHT2 = "fight2";   // a SECOND fight started - retention in miniature
        public const string QUICK  = "quick";    // first QUICK FIGHT started (the loop, 2026-09-07)
        public const string BOX    = "box";      // first toolbox earned (3 quick wins)
        public const string STREAK = "streak";   // first 5-win streak (a crown)
    }
}
