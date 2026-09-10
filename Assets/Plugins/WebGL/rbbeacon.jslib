// Bridge from game code to the page's beacon. All the beacon logic — the
// endpoint, the ephemeral session id, the failure swallowing — lives in the
// WebGL template's index.html and NOT here, so there is exactly one place that
// knows how a hit is sent and one place that knows the id.
//
// If the page did not define window.rbBeacon (an older index.html, or someone
// hosting the Build/ folder under their own page), this is a silent no-op:
// telemetry must never be able to break the game.
mergeInto(LibraryManager.library, {
  RBBeaconSend: function (evPtr, extraPtr) {
    try {
      var ev = UTF8ToString(evPtr);
      var extra = extraPtr ? UTF8ToString(extraPtr) : "";
      if (typeof window.rbBeacon === "function") window.rbBeacon(ev, extra);
    } catch (e) { /* never let counting break playing */ }
  }
});
