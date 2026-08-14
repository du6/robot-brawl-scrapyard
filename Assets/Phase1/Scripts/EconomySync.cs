using UnityEngine;
using System.Collections;

namespace RobotBrawl.Phase0
{
// ============================================================================
// ECONOMY SYNC — client B's wallet half (docs/Server_Economy_Design_2026-08-13.md).
//
// The wallet is server-truth and the local career is a CACHE. Offline play
// keeps working exactly as before — settles credit the local scrap and
// first-win claims queue in the save — and this is the reconciler: flush
// every queued claim (a 409 means the server already paid it; settled either
// way), fetch the wallet, and ADOPT the server balance as a Txn so the
// ledger audit (TxnSum == scrap) stays true through the correction.
//
// It only ever runs SIGNED IN. Signed-out careers (every bench, the editor
// dev door, owen's own save today) never enqueue and never sync — their
// economy stays pure local, which is why the whole suite is untouched by
// this file existing.
//
// Failure posture: a claim that errors STAYS QUEUED for the next sync; a
// wallet fetch that errors adopts nothing. Nothing here can lose a claim —
// the attempt ids make every retry idempotent server-side.
// ============================================================================
public class EconomySync : MonoBehaviour
{
    public static EconomySync running;

    /// <summary>Bench-readable outcome of the last sync.</summary>
    public static string lastResult = "";
    public static int lastFlushed, lastSettled, lastReversed;
    public static long lastServerBalance = -1;

    /// <summary>Has THIS session reached the wallet at least once? The shop's
    /// signed-in gate (owen's offline decision) reads this: browse always,
    /// buy only when the server has answered us this session.</summary>
    public static bool SessionOnline { get { return lastServerBalance >= 0; } }

    /// <summary>Fire a sync if signed in and none is running. Called after
    /// the login gate boots the career; safe to call from anywhere.</summary>
    public static void Kick()
    {
        if (!LadderClient.SignedIn || running != null) return;
        var go = new GameObject("EconomySync");
        DontDestroyOnLoad(go);
        running = go.AddComponent<EconomySync>();
    }

    void Start() { StartCoroutine(Sync()); }

    IEnumerator Sync()
    {
        lastFlushed = 0; lastSettled = 0; lastReversed = 0; lastResult = "";
        // Flush oldest-first; stop on the first real error so order is kept
        // and nothing is skipped past a transient failure.
        while (Career.Data.pendingClaims.Count > 0)
        {
            var cl = Career.Data.pendingClaims[0];
            int paid = -1; bool already = false; string err = null;
            yield return LadderClient.ClaimPurse(cl, (p, a, e) => { paid = p; already = a; err = e; });
            if (err != null) { lastResult = "claim " + cl.contestId + ": " + err; break; }
            Career.Data.pendingClaims.RemoveAt(0);
            lastSettled++;
            if (!already) lastFlushed++;
        }

        // Purchases after claims (money in before money out), oldest-first.
        // A refusal COMPENSATES — the till's optimistic apply is reversed —
        // and the purchase is dropped; a transient error keeps it queued.
        while (Career.Data.pendingPurchases.Count > 0)
        {
            var p = Career.Data.pendingPurchases[0];
            bool settled = false; string refused = null, perr = null;
            yield return LadderClient.Purchase(p, (s, r, e) => { settled = s; refused = r; perr = e; });
            if (perr != null) { lastResult = "purchase " + p.partId + ": " + perr; break; }
            Career.Data.pendingPurchases.RemoveAt(0);
            if (settled) { lastSettled++; }
            else { Career.ReversePurchase(p, refused); lastReversed++; }
        }

        long bal = -1; string werr = null;
        yield return LadderClient.GetWallet((b, e) => { bal = b; werr = e; });
        if (werr == null)
        {
            lastServerBalance = bal;
            // Server wins — recorded as a Txn so the audit stays true. The
            // delta is usually the rounding gap between the client's optimistic
            // settle and the server's canonical arithmetic, or the whole
            // balance on a first sync.
            long delta = bal - Career.Data.scrap;
            if (delta != 0) Career.Txn((int)delta, "wallet sync — server balance adopted");
            if (Career.autosave) Career.Save();
            lastResult = string.IsNullOrEmpty(lastResult)
                ? "synced: " + lastSettled + " claims settled, balance " + bal : lastResult;
        }
        else if (string.IsNullOrEmpty(lastResult)) lastResult = "wallet: " + werr;

        running = null;
        Destroy(gameObject);
    }
}
}
