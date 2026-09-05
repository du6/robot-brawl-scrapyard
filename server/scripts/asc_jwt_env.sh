#!/bin/zsh
# App Store Connect API auth. Lived in a session scratchpad twice and was lost
# twice (recreated 2026-09-01, lost again 2026-09-04) — so it now lives here.
# Source it, then `T=$(mint_jwt)` and pass `-H "Authorization: Bearer $T"`.
# Contains no secret: the issuer and key id are public identifiers; the
# private key stays in ~/.appstoreconnect/private_keys/.
#
# ⚠ ES256 BY HAND, because this Mac has neither PyJWT nor `cryptography`.
# openssl signs ECDSA in DER (SEQUENCE { INTEGER r, INTEGER s }) but JWS wants
# the RAW pair r||s, 32 bytes each, left-padded. Handing Apple the DER blob
# base64'd gets a 401 that looks exactly like a bad key, so the conversion
# below is the whole trick.
#
# ⚠ ASC query strings carry `[` `]` (filter[app]=…): curl globs them into a
# "bad range in URL" error unless you pass `-g`. Every call: `curl -sg`.
ASC_ISSUER="83e01f1e-c25e-46fe-842d-efa72fd7250d"
ASC_KEY_ID="PG23GXN3RN"
ASC_KEY="$HOME/.appstoreconnect/private_keys/AuthKey_${ASC_KEY_ID}.p8"
ASC_APP_ID="6801680303"          # club.cyberduck.robotbrawl

mint_jwt() {
  [[ -f "$ASC_KEY" ]] || { echo "no key at $ASC_KEY" >&2; return 1; }
  ASC_ISSUER="$ASC_ISSUER" ASC_KEY_ID="$ASC_KEY_ID" ASC_KEY="$ASC_KEY" python3 - <<'PY'
import base64, json, os, subprocess, time

def b64u(b): return base64.urlsafe_b64encode(b).rstrip(b"=").decode()

kid, iss, key = os.environ["ASC_KEY_ID"], os.environ["ASC_ISSUER"], os.environ["ASC_KEY"]
now = int(time.time())
header  = {"alg": "ES256", "kid": kid, "typ": "JWT"}
payload = {"iss": iss, "iat": now, "exp": now + 1200, "aud": "appstoreconnect-v1"}
si = b64u(json.dumps(header,  separators=(",", ":")).encode()) + "." + \
     b64u(json.dumps(payload, separators=(",", ":")).encode())

der = subprocess.run(["openssl", "dgst", "-sha256", "-sign", key],
                     input=si.encode(), capture_output=True, check=True).stdout

# DER: 30 <len> 02 <lr> R 02 <ls> S   -> raw r||s, each left-padded to 32 bytes
def unwrap(d):
    assert d[0] == 0x30
    i = 2 + (2 if d[1] == 0x81 else 0)          # skip long-form length byte
    out = []
    for _ in range(2):
        assert d[i] == 0x02
        ln = d[i + 1]
        v = d[i + 2 : i + 2 + ln].lstrip(b"\x00")
        out.append(v.rjust(32, b"\x00"))
        i += 2 + ln
    return b"".join(out)

print(si + "." + b64u(unwrap(der)))
PY
}
