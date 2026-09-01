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

    /// <summary>THE ADOPTION RULE, as one function, because the coroutine
    /// that calls it cannot be run without a live server and this is the part
    /// that must never be wrong. Adopt unless the account has nothing to say
    /// AND this device has something to lose.</summary>
    public static bool ShouldAdopt(bool serverIsFresh, bool localHasProgress)
    {
        return !(serverIsFresh && localHasProgress);
    }

    /// <summary>Has this device's career actually been played? Scrap alone is
    /// not enough (a new career may start with a stake), so ownership and the
    /// stable count too.</summary>
    public static bool LocalHasProgress()
    {
        if (Career.Data == null) return false;
        return Career.Data.scrap != 0
            || Career.Data.inventory.Count > 0
            || Career.Data.stable.Count > 0;
    }

    /// <summary>How many inventory rows the last sync adopted from the server.</summary>
    public static int lastInventoryAdopted;
    /// <summary>True when the last sync deliberately adopted NOTHING because the
    /// account was empty and this device held a played career. See the guard.</summary>
    public static bool lastAdoptionSkipped;

    /// <summary>Has THIS session reached the wallet at least once? The shop's
    /// signed-in gate (owen's offline decision) reads this: browse always,
    /// buy only when the server has answered us this session.</summary>
    public static bool SessionOnline { get { return lastServerBalance >= 0; } }

    /// <summary>Probes and benches that WANT the editor to sync set this
    /// deliberately. It exists because of a measured incident, 2026-08-13:
    /// owen signed into the gate in the EDITOR with a fresh dev account, and
    /// server-wins adoption did exactly what it says — flattened his real
    /// career's 6,513 scrap to the account's 500 (the −6013 is in his ledger).
    /// A BUILD can never hit this: the gate forces sign-in before the career
    /// exists, so a device save never has a pre-wallet balance. The editor
    /// career is OWNER STATE and predates the wallet, so in the editor the
    /// sync only runs when a probe asks for it.</summary>
    public static bool editorOptIn;

    /// <summary>Fire a sync if signed in and none is running. Called after
    /// the login gate boots the career; safe to call from anywhere.</summary>
    public static void Kick()
    {
        if (Application.isEditor && !editorOptIn) return;
        if (!LadderClient.SignedIn || running != null) return;
        var go = new GameObject("EconomySync");
        DontDestroyOnLoad(go);
        running = go.AddComponent<EconomySync>();
    }

    void Start() { StartCoroutine(Sync()); }

    IEnumerator Sync()
    {
        lastFlushed = 0; lastSettled = 0; lastReversed = 0; lastResult = "";
        lastInventoryAdopted = 0; lastAdoptionSkipped = false;
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

        LadderClient.WalletState wallet = null; string werr = null;
        yield return LadderClient.GetWalletState((w, e) => { wallet = w; werr = e; });
        if (werr == null)
        {
            long bal = wallet.balance;

            // ---- THE FRESH-ACCOUNT GUARD -------------------------------
            // Adoption is server-wins, which is correct when the server has
            // something to say and CATASTROPHIC when it does not: a brand new
            // account answers balance 0 / inventory [] / ledger [], and
            // adopting that over a played career erases it. The shipped iOS
            // gate made this unreachable (sign-in precedes the career), but
            // PLAY AS GUEST on the web build does not — a guest can earn
            // scrap and parts and only then sign in, which is exactly the
            // shape that cost 6,013 scrap on 2026-08-13.
            //
            // So: if the account has never transacted and owns nothing, while
            // this device holds a career that clearly HAS been played, adopt
            // nothing this sync and say so. Queued claims and purchases have
            // already been flushed above and are idempotent, so the server
            // catches up on its own; nothing is lost by waiting.
            bool localHasProgress = LocalHasProgress();
            if (!ShouldAdopt(wallet.IsFresh, localHasProgress))
            {
                lastServerBalance = bal;
                lastAdoptionSkipped = true;
                lastResult = "wallet sync skipped - this account is empty and this device has a career;"
                           + " refusing to overwrite it";
            }
            else
            {
                lastServerBalance = bal;
                // Server wins — recorded as a Txn so the audit stays true. The
                // delta is usually the rounding gap between the client's optimistic
                // settle and the server's canonical arithmetic, or the whole
                // balance on a first sync.
                long delta = bal - Career.Data.scrap;
                if (delta != 0) Career.Txn((int)delta, "wallet sync - server balance adopted");

                // ---- INVENTORY, same rule as the balance -------------------
                // Ownership is server-truth (docs/Server_Economy_Design_2026-08-13.md:
                // "the wallet and the inventory move server-side and the client
                // becomes a cache"). The server already returns it on every
                // wallet call; until now the client parsed the balance and threw
                // the rest away, which is why parts did not follow a player to a
                // second device while their scrap did.
                lastInventoryAdopted = AdoptInventory(wallet.inventory);

                if (Career.autosave) Career.Save();
                lastResult = string.IsNullOrEmpty(lastResult)
                    ? "synced: " + lastSettled + " claims settled, balance " + bal
                      + ", " + lastInventoryAdopted + " part rows adopted" : lastResult;
            }
        }
        else if (string.IsNullOrEmpty(lastResult)) lastResult = "wallet: " + werr;

        running = null;
        Destroy(gameObject);
    }

    /// <summary>Replace the local part cache with the server's answer, and
    /// return how many rows were adopted.
    ///
    /// A REPLACE and not a merge, deliberately: the server's row set IS
    /// ownership (`count > 0` is the rule, enforced by a CHECK constraint),
    /// so a part the server does not list is a part you do not own. Merging
    /// would let a local row the server has never heard of survive forever,
    /// which is precisely the save-editor hole server-side inventory exists
    /// to close.
    ///
    /// The caller has already established the account is not empty, so this
    /// cannot be the fresh-account wipe.</summary>
    /// <summary>Bench seam — drives the REAL adopter, not a copy of it.</summary>
    public static int TestAdoptInventory(System.Collections.Generic.List<CareerItem> server)
    { return AdoptInventory(server); }

    static int AdoptInventory(System.Collections.Generic.List<CareerItem> server)
    {
        if (server == null) return 0;
        Career.Data.inventory.Clear();
        foreach (var row in server)
        {
            if (row.count <= 0) continue;
            // ⚠ ADOPTED ROWS MUST GO THROUGH THE SAME NORMALISATION AS LOADED
            // ONES. Career.Load() applies save migrations to inventory — the
            // pinned-material move, and the V2.2 edge-sentinel split — and a
            // row injected here has never seen them. Today the server cannot
            // actually hold an unpinned row (part_prices is generated from the
            // editor's defs and pinned mats emit only what they Accept, so a
            // purchase cannot create one), which makes this belt-and-braces
            // rather than a live fix; it is here because the day that stops
            // being true, the failure is silent and looks like lost parts.
            var d = CareerDB.Def(row.partId);
            string mat = d != null ? d.EffectiveMat(row.mat) : row.mat;
            Career.AddItem(row.partId, mat, row.count);   // merges duplicates
        }
        return Career.Data.inventory.Count;
    }
}
}
