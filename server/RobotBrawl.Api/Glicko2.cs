// ===========================================================================
// Glicko-2, per Mark Glickman's "Example of the Glicko-2 system" (2013).
//
// Design doc §2.1 picks Glicko-2 over Elo because deviation decays: a robot
// that stops fighting goes SOFT rather than squatting on a rank it earned
// months ago.
//
// This file is deliberately PURE and standalone — no database, no HTTP, no
// ASP.NET. That is what lets it be checked against Glickman's own worked
// example instead of against numbers that merely look plausible. A rating
// system that is subtly wrong still produces a leaderboard, and nobody
// notices for a season.
//
// The implementation takes a LIST of opponents even though the ladder only
// ever calls it with one. That is on purpose: the paper's worked example
// uses three, and it is the only published fixture that can prove the
// volatility iteration is right. A single-opponent-only implementation
// would be untestable against anything but itself.
// ===========================================================================
using System;
using System.Collections.Generic;

namespace RobotBrawl.Api;

public readonly record struct Rating(double R, double Rd, double Sigma);

public static class Glicko2
{
    // The Glicko-2 scale factor. Ratings are stored on the familiar ~1500
    // scale and converted in and out; 173.7178 is the constant that maps
    // between them.
    const double Scale = 173.7178;
    const double Anchor = 1500.0;
    const double Epsilon = 0.000001;

    /// <summary>One rating period. scores are 1 for a win, 0 for a loss.
    /// §2.1: "win = 1, loss = 0; no draws — judges decide", so 0.5 is never
    /// passed by this project, though the maths accepts it.</summary>
    public static Rating Update(Rating player, IReadOnlyList<Rating> opponents,
                                IReadOnlyList<double> scores, double tau)
    {
        if (opponents.Count != scores.Count)
            throw new ArgumentException("one score per opponent");
        // No games this period: the rating and volatility hold, but the
        // deviation GROWS — this is the decay that makes an idle robot's rank
        // go soft (§2.1).
        if (opponents.Count == 0)
        {
            double phiIdle = player.Rd / Scale;
            double phiStarIdle = Math.Sqrt(phiIdle * phiIdle + player.Sigma * player.Sigma);
            return new Rating(player.R, phiStarIdle * Scale, player.Sigma);
        }

        double mu = (player.R - Anchor) / Scale;
        double phi = player.Rd / Scale;

        // Step 3: v, the estimated variance of the rating based only on
        // game outcomes.
        double vInv = 0.0, deltaSum = 0.0;
        for (int i = 0; i < opponents.Count; i++)
        {
            double muJ = (opponents[i].R - Anchor) / Scale;
            double phiJ = opponents[i].Rd / Scale;
            double g = G(phiJ);
            double e = E(mu, muJ, phiJ);
            vInv += g * g * e * (1.0 - e);
            deltaSum += g * (scores[i] - e);
        }
        if (vInv <= 0.0) return player;      // degenerate; leave it alone
        double v = 1.0 / vInv;

        // Step 4: delta, the estimated improvement.
        double delta = v * deltaSum;

        // Step 5: the new volatility, by Illinois-method root finding on f.
        double sigmaPrime = NewVolatility(phi, v, delta, player.Sigma, tau);

        // Step 6-7: pre-rating-period deviation, then the new deviation.
        double phiStar = Math.Sqrt(phi * phi + sigmaPrime * sigmaPrime);
        double phiPrime = 1.0 / Math.Sqrt(1.0 / (phiStar * phiStar) + 1.0 / v);

        // Step 8: the new rating.
        double muPrime = mu + phiPrime * phiPrime * deltaSum;

        return new Rating(muPrime * Scale + Anchor, phiPrime * Scale, sigmaPrime);
    }

    /// <summary>Convenience for this ladder's one-opponent case.</summary>
    public static Rating UpdateOne(Rating player, Rating opponent, double score, double tau)
        => Update(player, new[] { opponent }, new[] { score }, tau);

    static double G(double phi) => 1.0 / Math.Sqrt(1.0 + 3.0 * phi * phi / (Math.PI * Math.PI));

    static double E(double mu, double muJ, double phiJ)
        => 1.0 / (1.0 + Math.Exp(-G(phiJ) * (mu - muJ)));

    static double NewVolatility(double phi, double v, double delta, double sigma, double tau)
    {
        double a = Math.Log(sigma * sigma);
        double delta2 = delta * delta, phi2 = phi * phi;

        double F(double x)
        {
            double ex = Math.Exp(x);
            double num = ex * (delta2 - phi2 - v - ex);
            double den = 2.0 * (phi2 + v + ex) * (phi2 + v + ex);
            return num / den - (x - a) / (tau * tau);
        }

        double A = a, B;
        if (delta2 > phi2 + v)
        {
            B = Math.Log(delta2 - phi2 - v);
        }
        else
        {
            int k = 1;
            while (F(a - k * tau) < 0.0)
            {
                k++;
                if (k > 1000) break;          // cannot happen for sane inputs
            }
            B = a - k * tau;
        }

        double fA = F(A), fB = F(B);
        int guard = 0;
        while (Math.Abs(B - A) > Epsilon && guard++ < 1000)
        {
            double c = A + (A - B) * fA / (fB - fA);
            double fC = F(c);
            if (fC * fB <= 0.0) { A = B; fA = fB; }
            else { fA = fA / 2.0; }
            B = c; fB = fC;
        }
        return Math.Exp(A / 2.0);
    }
}
