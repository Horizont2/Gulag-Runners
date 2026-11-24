using UnityEngine;

namespace GulagRunners.Meta
{
    /// <summary>
    /// The pool the search screen actually searches in: a real queue with the practice
    /// opponent held underneath it, and the rules in <see cref="Matchmaker"/> applied to
    /// the pair of them.
    ///
    /// THIS IS THE THING THAT WAS MISSING, and its absence is the whole of "pressing PLAY
    /// and nothing happens". <see cref="MatchSearch"/> chose ONE pool: the component in its
    /// Queue slot, or <see cref="BotQueue"/> when that slot was empty. Every scene that
    /// ships has something in the slot, so the branch that ran was the service -- and the
    /// moment it ran, every rule this project had written about searching stopped being
    /// executed:
    ///
    ///   Matchmaker.OfferBot      never called. SearchRules.BotAfterSeconds -- the twenty-five
    ///                            seconds docs/09 asks for -- was a number in the inspector
    ///                            that nothing read. An empty pool therefore searched FOR
    ///                            EVER, with the bar pinned full after 13.6 s, which is the
    ///                            state a player reads as the game being broken. They are
    ///                            not wrong: nothing was ever going to happen.
    ///   Ladder.PoolFor           never called. docs/09 makes the first five matches bots BY
    ///                            DESIGN and tells the player so; instead a fresh install
    ///                            with no rating was put into the live pool, where
    ///                            Ladder.MayMeet refuses every human being in it. A ticket
    ///                            nobody may be matched with is a ticket that can only wait
    ///                            out the service's own timeout.
    ///   QueueState.Failed        reached the screen as a line of text and nothing else. No
    ///                            project linked, no network, Matchmaker switched off on the
    ///                            dashboard: every one of them ended on a dead screen whose
    ///                            only live control was the cross.
    ///
    /// So the floor goes back UNDER the service rather than beside it. The service is asked
    /// first and always wins while it is working; the bot is what the search falls to when
    /// the service says no, or says nothing for long enough that waiting has become the
    /// worse of the two mistakes.
    ///
    /// A plain object and not a component, like <see cref="BotQueue"/>: there is nothing
    /// here for a designer to set that is not already on the search screen's own Rules, and
    /// a MonoBehaviour would be one more thing to wire into a scene to get behaviour the
    /// scene already asked for by putting a queue in the slot.
    /// </summary>
    public sealed class MatchPool : IMatchQueue
    {
        /// <summary>The service, or null when there is none to ask.</summary>
        readonly IMatchQueue _live;

        /// <summary>The floor. Always here, because docs/09 always promises it.</summary>
        readonly BotQueue _bots;

        /// <summary>
        /// How long the whole search may run before the floor is taken, whatever the service
        /// is doing.
        ///
        /// Belt as well as braces. <see cref="Matchmaker.OfferBot"/> already gives up at
        /// BotAfterSeconds and that is the number a designer tunes -- but it is reached
        /// through Cast(), and therefore through StartBand and MaxBand, so a rules struct
        /// somebody has flattened can push the clock out a long way. A search is a screen
        /// somebody is staring at: it gets a ceiling that does not depend on three other
        /// numbers being sensible.
        /// </summary>
        public const float HardCeiling = 45f;

        /// <summary>Enough matches to be out of every protected pool. See <see cref="Allowed"/>.</summary>
        const int Settled = 1000;

        double _rating;
        int _played;
        bool _liveGaveUp;
        string _liveSaid = "";
        bool _tookBots;

        public MatchPool(IMatchQueue live, SearchRules rules)
        {
            _live = live;
            _bots = new BotQueue(rules);
        }

        /// <summary>
        /// Where the search has got to, as the screen should read it.
        ///
        /// It never answers Failed, and that is the point rather than an oversight: with a
        /// practice opponent underneath every pool there is always a match coming, so there
        /// is never a moment at which the honest answer is "no pairing is possible". A
        /// service that failed is reported in <see cref="Status"/> -- next to the opponent
        /// it fell back to -- instead of as a dead end.
        /// </summary>
        public QueueState State
        {
            get
            {
                if (_tookBots || _liveGaveUp || _live == null) return _bots.State;
                if (_bots.State == QueueState.Found) return QueueState.Found;
                if (_live.State == QueueState.Failed) return QueueState.Looking;
                return _live.State;
            }
        }

        /// <summary>
        /// A sentence for the screen: the service's while it has one, and otherwise why a
        /// practice opponent is being offered instead of a person.
        /// </summary>
        public string Status { get; private set; } = "";

        /// <summary>Why the service was given up on, or empty while it is still being asked.</summary>
        public string LiveGaveUpBecause => _liveGaveUp ? _liveSaid : "";

        public void Begin(double rating, int matchesPlayed)
        {
            _rating = rating;
            _played = matchesPlayed;
            _liveGaveUp = false;
            _liveSaid = "";
            _tookBots = false;
            Status = "";

            _bots.Begin(rating, matchesPlayed);

            // THE PLACEMENT MATCHES USED TO SKIP THE SERVICE ENTIRELY, and between this
            // and the two lines it was reasoning from -- Ladder.MayMeet refusing every
            // human to anybody inside the training pool, and Matchmaker.OfferBot handing
            // that pool a bot on the frame PLAY was pressed -- the game had no online
            // matchmaking at all. Not a thin queue: NO TICKET WAS EVER SUBMITTED. On a
            // fresh install both devices are inside their first five matches, so two
            // people pressing PLAY together were each given their own practice match, and
            // the only way out of the pool that forbade pairing was to play the bots it
            // was handing out.
            //
            // The reasoning was sound and its premise was wrong. A training player's
            // ticket IS allowed to be matched now -- see Ladder.MayMeet -- so it is
            // submitted, and the floor under it waits ten seconds instead of none.
            if (_live == null)
            {
                _liveGaveUp = true;
                return;
            }

            _live.Begin(rating, matchesPlayed);
        }

        public void Poll(float seconds)
        {
            // The service first, so a real opponent beats the floor on any frame they both
            // arrive.
            if (!_liveGaveUp)
            {
                _live.Poll(seconds);

                // Not while it is holding a pairing. Giving up on the tick the answer
                // arrived would throw away a real opponent for being four hundredths late.
                if (_live.State == QueueState.Failed)
                    GiveUpOnTheService(string.IsNullOrEmpty(_live.Status)
                                           ? "NO CONNECTION" : _live.Status);
                else if (seconds >= HardCeiling && _live.State != QueueState.Found)
                    GiveUpOnTheService("THE QUEUE IS EMPTY");
            }

            // The floor, and only ever on the terms Matchmaker already sets -- immediately
            // while placing, otherwise once the net is fully cast AND the clock has run out.
            // The one exception is a service that has given up: that IS its answer to "is
            // anybody coming", and it does not need a clock to confirm it.
            _bots.Poll(_liveGaveUp ? Mathf.Max(seconds, HardCeiling) : seconds);

            Say();
        }

        /// <summary>
        /// The pairing, once -- and checked, which the live path never was.
        ///
        /// <see cref="Ladder.MayMeet"/> and the widening band are written down, mirrored in
        /// docs/refs/ladder_mirror.py and asserted against docs/09, and on the service path
        /// they were enforced by a rule on the Unity dashboard or by nothing at all. A rule
        /// that may not exist is not a rule. So whoever the service hands back is measured
        /// against the same test the bot floor is held to, and a pair the ladder refuses is
        /// put back rather than played -- the search carries on, and the floor is still
        /// under it.
        /// </summary>
        public bool TryTake(out MatchFound found)
        {
            if (!_tookBots && !_liveGaveUp && _live.TryTake(out found))
            {
                if (Allowed(in found)) return true;

                Debug.LogWarning(
                    $"Matchmaking: the service paired this player (rating {_rating:0}, "
                    + $"{_played} matches) with {Describe(in found)}, which Ladder.MayMeet "
                    + "refuses -- docs/09 does not let anybody inside their first twenty "
                    + "matches meet a Platinum, however thin the queue is. The pairing has "
                    + "been put back. Check the match rule on the Unity dashboard against "
                    + "the rating attribute the ticket actually sends.");

                GiveUpOnTheService("NOBODY IN RANGE");
            }

            if (!_bots.TryTake(out found)) { found = default; return false; }

            _tookBots = true;

            // Say why, on the one line the player will actually read. BotQueue's own name
            // covers the two cases it knows about -- placing, and a thin queue -- and cannot
            // know about the third, which is the service having fallen over. A practice
            // match offered without a reason, in a build whose networking is simply not
            // configured, is how "the game only ever gives me bots" becomes a mystery
            // instead of a one-line fix.
            // And for the training pool too, which used to be the one place this was
            // suppressed. A first install is exactly where a build with no project linked
            // and no queue on the dashboard needs to say so out loud: it is the difference
            // between a one-line fix and "the game only ever gives me bots".
            if (!string.IsNullOrEmpty(_liveSaid))
                found.OpponentName = $"{_liveSaid} · A BOT INSTEAD";

            Say();
            return true;
        }

        public void Cancel()
        {
            if (_live != null) _live.Cancel();
            _bots.Cancel();
            _tookBots = false;
            Status = "";
        }

        void GiveUpOnTheService(string why)
        {
            _liveGaveUp = true;
            _liveSaid = why;

            // Leaving matters more than it looks. A ticket abandoned in the pool keeps being
            // matched, and the other player is then paired with somebody who has walked away
            // -- which to them is a match that never starts.
            _live.Cancel();
        }

        /// <summary>
        /// May this pairing be played? The ladder's own rules, on the numbers the service
        /// actually sent.
        ///
        /// A bot is always allowed: there is no human on the other side for the
        /// protection to protect anybody from.
        ///
        /// Their match COUNT is not in MatchFound and cannot be -- the service does not send
        /// it -- so the test runs from this side only. That is the half that matters: the
        /// protection docs/09 asks for is for the newcomer, and this device is the one that
        /// knows whether that is us. The opponent is treated as settled, which is the
        /// strictest reading available from here. An opponent with no rating at all (the
        /// property never arrived) is read as the median rather than refused, because
        /// refusing them is refusing the average player for the crime of being new.
        /// </summary>
        bool Allowed(in MatchFound found)
        {
            if (found.AgainstBot) return true;

            double theirs = found.OpponentRating <= 0.0 ? 1500.0 : found.OpponentRating;
            return Ladder.MayMeet(_played, _rating, Settled, theirs);
        }

        static string Describe(in MatchFound found) =>
            string.IsNullOrEmpty(found.OpponentName)
                ? $"an unnamed opponent on {found.OpponentRating:0}"
                : $"{found.OpponentName} on {found.OpponentRating:0} "
                  + $"({Ladder.Name(found.OpponentRating)})";

        /// <summary>
        /// The line under the phase, when there is something to say the phase does not
        /// already say.
        ///
        /// Three cases, and the middle one is the one worth having. While the service is
        /// working this says nothing, and the screen shows its own league-and-seconds line.
        /// Once the floor is in hand it names the opponent. In BETWEEN -- the service has
        /// given up and the floor has not yet come due -- it says why, because that gap can
        /// be twenty seconds long, and a screen that explains itself is a wait rather than a
        /// hang.
        /// </summary>
        void Say()
        {
            if (_tookBots || _bots.State == QueueState.Found)
            {
                Status = _bots.Status;
                return;
            }

            if (!_liveGaveUp) { Status = _live.Status; return; }

            Status = string.IsNullOrEmpty(_liveSaid)
                ? "" : $"{_liveSaid} · PRACTICE MATCH INSTEAD";
        }
    }
}
