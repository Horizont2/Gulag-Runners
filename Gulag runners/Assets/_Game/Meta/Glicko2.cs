using System;

namespace GulagRunners.Meta
{
    /// <summary>One player's skill, as the ladder understands it.</summary>
    [Serializable]
    public struct Rating
    {
        /// <summary>The number on the ordinary 1500-centred scale. Never shown.</summary>
        public double Value;

        /// <summary>
        /// How unsure the ladder is about it. A new player starts at 350 and settles towards
        /// 50-ish; it is what lets ten matches place somebody instead of a hundred.
        /// </summary>
        public double Deviation;

        /// <summary>How erratic their results have been. Glicko-2's sigma.</summary>
        public double Volatility;

        public static Rating Fresh => new Rating { Value = 1500, Deviation = 350, Volatility = 0.06 };
    }

    /// <summary>
    /// Glicko-2, which docs/09 asks for by name and for a reason.
    ///
    /// Elo moves everybody by the same amount whatever it knows about them, so a new player
    /// needs about a hundred matches to reach their level. Glicko carries a SECOND number --
    /// how unsure it is -- and moves an uncertain rating in large steps and a settled one in
    /// small ones. docs/09 wants somebody placed in ten matches, and with a small player pool
    /// that is the difference between a ladder and a lottery.
    ///
    /// This is the published algorithm (Glickman, 2013) and nothing else: the five steps on
    /// the Glicko-2 scale, the volatility solved by the Illinois method, then back. It is
    /// written out rather than pulled from a package so the Python mirror beside it can be
    /// checked line for line.
    ///
    /// Doubles, not Fix. Nothing here is rolled back and nothing here runs inside a tick --
    /// the ladder is settled once, after a match is already over, so the determinism docs/06
    /// demands of the simulation does not apply and fixed point would buy nothing.
    /// </summary>
    public static class Glicko2
    {
        /// <summary>The Glicko-2 scale factor: 173.7178 rating points to one unit.</summary>
        const double Scale = 173.7178;

        /// <summary>
        /// How much a rating is allowed to become more erratic in one step. Glickman suggests
        /// 0.3 to 1.2; lower is steadier. 0.5 suits a game where a single match is noisy --
        /// a round of this can be lost to one chest.
        /// </summary>
        public const double Tau = 0.5;

        const double Epsilon = 1e-6;

        /// <summary>The ladder's opinion after one match. Score is 1, 0.5 or 0.</summary>
        public static Rating Update(Rating self, Rating opponent, double score)
        {
            double mu = (self.Value - 1500.0) / Scale;
            double phi = self.Deviation / Scale;
            double muJ = (opponent.Value - 1500.0) / Scale;
            double phiJ = opponent.Deviation / Scale;

            double g = 1.0 / Math.Sqrt(1.0 + 3.0 * phiJ * phiJ / (Math.PI * Math.PI));
            double e = 1.0 / (1.0 + Math.Exp(-g * (mu - muJ)));

            double v = 1.0 / (g * g * e * (1.0 - e));
            double delta = v * g * (score - e);

            double sigma = Volatility(phi, v, delta, self.Volatility);

            double phiStar = Math.Sqrt(phi * phi + sigma * sigma);
            double phiNew = 1.0 / Math.Sqrt(1.0 / (phiStar * phiStar) + 1.0 / v);
            double muNew = mu + phiNew * phiNew * g * (score - e);

            return new Rating
            {
                Value = 1500.0 + Scale * muNew,
                Deviation = Scale * phiNew,
                Volatility = sigma,
            };
        }

        /// <summary>
        /// The one step of Glicko-2 that cannot be written in closed form: solving for the new
        /// volatility. Illinois, as the paper specifies, because plain bisection takes about
        /// three times as many iterations for the same answer.
        /// </summary>
        static double Volatility(double phi, double v, double delta, double sigma)
        {
            double a = Math.Log(sigma * sigma);

            double F(double x)
            {
                double ex = Math.Exp(x);
                double d2 = delta * delta;
                double p2 = phi * phi;
                double top = ex * (d2 - p2 - v - ex);
                double bottom = 2.0 * (p2 + v + ex) * (p2 + v + ex);
                return top / bottom - (x - a) / (Tau * Tau);
            }

            double A = a;
            double B;
            if (delta * delta > phi * phi + v)
            {
                B = Math.Log(delta * delta - phi * phi - v);
            }
            else
            {
                int k = 1;
                while (F(a - k * Tau) < 0.0 && k < 100) k++;
                B = a - k * Tau;
            }

            double fA = F(A), fB = F(B);
            int guard = 0;
            while (Math.Abs(B - A) > Epsilon && guard++ < 200)
            {
                double C = A + (A - B) * fA / (fB - fA);
                double fC = F(C);

                if (fC * fB <= 0.0) { A = B; fA = fB; }
                else { fA /= 2.0; }

                B = C; fB = fC;
            }
            return Math.Exp(A / 2.0);
        }

        /// <summary>
        /// A player who has not played gets LESS certain, not worse. Without this a rating
        /// settles once and then never moves again, and somebody who improved over a month
        /// away has to grind back through a ladder that has stopped listening.
        /// </summary>
        public static Rating Idle(Rating self, int ratingPeriods)
        {
            if (ratingPeriods <= 0) return self;

            double phi = self.Deviation / Scale;
            double sigma = self.Volatility;
            double grown = Math.Sqrt(phi * phi + ratingPeriods * sigma * sigma);

            self.Deviation = Math.Min(350.0, Scale * grown);
            return self;
        }

        /// <summary>
        /// The season reset docs/09 asks for: a SOFT one, 40% of the way back to the middle,
        /// and the uncertainty widened again so the first week of a season actually sorts
        /// people instead of confirming last season.
        ///
        /// Hard resets are the alternative and they are worse: everybody starts equal, the
        /// best players spend a week beating beginners, and the beginners quit.
        /// </summary>
        public static Rating SeasonReset(Rating self, double pull = 0.4, double minDeviation = 120.0)
        {
            self.Value = self.Value + (1500.0 - self.Value) * pull;
            self.Deviation = Math.Max(self.Deviation, minDeviation);
            return self;
        }
    }
}
