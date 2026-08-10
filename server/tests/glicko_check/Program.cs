using System;
using RobotBrawl.Api;

// Glickman, "Example of the Glicko-2 system" (2013).
// Player: r=1500, RD=200, sigma=0.06, tau=0.5
// Opponents: (1400,30) win, (1550,100) loss, (1700,300) loss
// Published result: r'=1464.06, RD'=151.52, sigma'=0.05999
var player = new Rating(1500, 200, 0.06);
var opps = new[] { new Rating(1400,30,0.06), new Rating(1550,100,0.06), new Rating(1700,300,0.06) };
var scores = new double[] { 1, 0, 0 };
var got = Glicko2.Update(player, opps, scores, 0.5);

Console.WriteLine($"got   r={got.R:F2}  RD={got.Rd:F2}  sigma={got.Sigma:F5}");
Console.WriteLine("want  r=1464.06  RD=151.52  sigma=0.05999");
bool ok = Math.Abs(got.R - 1464.06) < 0.02
       && Math.Abs(got.Rd - 151.52) < 0.02
       && Math.Abs(got.Sigma - 0.05999) < 0.00002;
Console.WriteLine(ok ? "MATCH — implementation agrees with the paper" : "MISMATCH");

// Idle decay: no games must widen RD and leave rating alone.
var idle = Glicko2.Update(new Rating(1500,200,0.06), Array.Empty<Rating>(), Array.Empty<double>(), 0.5);
Console.WriteLine($"idle  r={idle.R:F2} (want 1500)  RD={idle.Rd:F2} (want >200)");
Environment.Exit(ok && idle.Rd > 200.0 && Math.Abs(idle.R-1500)<1e-9 ? 0 : 1);
