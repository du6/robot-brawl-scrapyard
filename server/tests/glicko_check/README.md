# glicko_check — the only cover for `RobotBrawl.Api/Glicko2.cs`

```sh
cd server/tests/glicko_check && dotnet run     # exit 0 = correct
```

`sql_bench` covers the SQL and `api_smoke` covers the endpoints, but neither
can check a rating *algorithm*: a subtly wrong Glicko-2 still produces a
plausible leaderboard, and nobody notices for a season.

So this runs the worked example from Mark Glickman's **"Example of the
Glicko-2 system" (2013)** — the published fixture, with the published answer:

| | r' | RD' | sigma' |
|---|---|---|---|
| paper | 1464.06 | 151.52 | 0.05999 |
| ours | 1464.05 | 151.52 | 0.06000 |

Within the paper's own 2-decimal rounding. It also checks the idle case: a
rating period with no games must leave the rating alone and WIDEN the
deviation, which is the "idle robots go soft" property §2.1 chose Glicko-2
for in the first place.

This is why `Glicko2.Update` takes a *list* of opponents when the ladder only
ever passes one — the paper's example uses three, and a
single-opponent-only implementation could only be tested against itself.

It is a separate project on purpose: it compiles `Glicko2.cs` directly via
`<Compile Include>`, so it needs no reference to the API and cannot affect
the API's own build. A fresh clone still builds `server/RobotBrawl.Api`
without ever touching this directory.
