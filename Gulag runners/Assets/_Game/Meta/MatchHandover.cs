using UnityEngine;

namespace GulagRunners.Meta
{
    /// <summary>
    /// Who the arena is playing, carried over the one gap in the game where it would
    /// otherwise be lost.
    ///
    /// The menu and the arena are separate scenes, so the moment MenuDirector asks for the
    /// arena every object in the menu is destroyed -- the search screen among them, holding
    /// the only copy of the pairing. What stood in for it is worse than nothing standing in
    /// for it: the arena settles the series against an opponent rating TYPED INTO THE
    /// SCENE, a hand-set field that defaults to 1500, so every match ever played has been
    /// scored against a number somebody chose and not against the player the matchmaker
    /// actually paired them with. <see cref="MatchSearch"/> knew the real one all along --
    /// <see cref="MatchFound.OpponentRating"/>, from whoever did the pairing -- and dropped
    /// it on the floor at the scene load. Glicko-2 moves a rating against another rating
    /// (docs/09), so a wrong opponent does not stop the ladder: it makes the ladder move
    /// confidently in response to nothing, which is the one failure a player cannot see and
    /// cannot be told about. Carrying one struct across one scene load is the entire job,
    /// and there is deliberately nothing else in here.
    ///
    /// What it does NOT carry is how sure the ladder was about them. MatchFound has a
    /// rating and no deviation, so whatever records the result still supplies that half
    /// itself. That is a real error and a much smaller one -- a deviation set too low makes
    /// the match count for more than it should, too high for less -- and it lives in the
    /// shape of the struct rather than in here.
    ///
    /// It is a static and not an object that survives the load, because
    /// <see cref="GulagRunners.Net.OnlineMatch"/> already learnt the other way the hard
    /// way: DontDestroyOnLoad is the obvious answer and it keeps the whole GameObject,
    /// children and all, so the arena came up with the entire search UI floating over it
    /// still saying "waiting to be let through". Moving a struct is cheaper than moving a
    /// scene.
    ///
    /// NOT folded into OnlineMatch, which carries a pairing of its own, for two reasons.
    /// That one exists only once a relay has opened, and a relay is the exception rather
    /// than the rule: docs/09 makes the first five matches bots by design, and docs/29
    /// keeps the practice opponent under every pool that is ever put behind the search
    /// screen, so most matches never open one. It also carries a different half of the
    /// pairing -- the seed and which end of the wire this device took, which are the
    /// session's answers. The rating is the matchmaker's, and nobody was carrying it.
    ///
    /// WHY IT IS NOT A ONE-SHOT. The obvious shape is Take(out MatchFound), consumed by
    /// the first reader, and it breaks the rematch: MatchResultView can replay a series
    /// without going back to the menu, and the second match of that series would find the
    /// handover already spent and have no rating to settle against. So reading is free and
    /// forgetting is a decision somebody makes out loud -- the menu when a new search
    /// begins, the arena when it is done with the result.
    ///
    /// WHY IT IS CLEARED ON ENTERING PLAY. Every arena scene in this project is opened on
    /// its own and run constantly, which is the normal way to work on one, and an arena
    /// entered that way must NOT be told it is fighting whoever the last run was paired
    /// with. A static would do exactly that: the editor can be told not to reload the
    /// domain between Play sessions, and then yesterday's opponent is still sitting here
    /// with their rating, ready to have a result recorded against them. <see cref="Clear"/>
    /// runs before the first scene of every Play session and is the whole defence.
    /// </summary>
    public static class MatchHandover
    {
        static MatchFound _found;
        static bool _waiting;

        /// <summary>
        /// Whether anything has been handed over at all this session, and whether the line
        /// about finding nothing has been said. Both exist only so that line is said ONCE
        /// and only while it means something -- see <see cref="Waiting"/>.
        /// </summary>
        static bool _everHanded;
        static bool _said;

        /// <summary>
        /// True when a match was handed over and nothing has taken or forgotten it.
        ///
        /// Answering false is the ordinary case and not a fault, so it is not a warning: an
        /// arena opened straight from the project window has nobody on the other side, and
        /// whatever reads this falls back to a practice opponent. It is still worth SAYING,
        /// because "the result was not recorded" and "the opponent never arrived" look
        /// identical from the console otherwise -- so the first read that finds nothing
        /// explains itself, once, and then stops. Once rather than per frame because this
        /// is a property something may well poll, and a line a second teaches people to
        /// filter the console rather than read it.
        /// </summary>
        public static bool Waiting
        {
            get
            {
                if (!_waiting && !_everHanded && !_said)
                {
                    _said = true;
                    Debug.Log("MatchHandover: nothing was handed over, so this arena was "
                              + "entered without a search -- there is no opponent here and "
                              + "no result to record against one. Expected when a scene is "
                              + "opened on its own; said once per session.");
                }

                return _waiting;
            }
        }

        /// <summary>
        /// Who the arena is playing. Meaningless unless <see cref="Waiting"/>.
        ///
        /// Reading it does not spend it, which is what lets a rematch be fought against the
        /// same person at the same rating. When nothing is waiting this is a zeroed
        /// MatchFound rather than the last one handed over: a caller that forgets to look at
        /// Waiting then reads a blank name and a rating of zero, which is wrong in a way
        /// somebody notices, instead of the previous opponent's real rating, which is wrong
        /// in a way that silently records a second match against them.
        /// </summary>
        public static MatchFound Current => _found;

        /// <summary>
        /// The menu, immediately before the arena scene is loaded.
        ///
        /// Called on the path that actually commits to the load and not when the opponent is
        /// first found, because the two are a beat apart and the cross is still live in that
        /// beat: handing over at the moment of pairing leaves a cancelled match sitting here
        /// waiting to be played by whatever loads the arena next.
        ///
        /// Taken by reference -- MatchFound is a struct with six fields and this is the one
        /// call on the critical path of a scene load.
        /// </summary>
        public static void Hand(in MatchFound found)
        {
            _found = found;
            _waiting = true;
            _everHanded = true;

            string who = string.IsNullOrEmpty(found.OpponentName)
                ? "an unnamed opponent" : found.OpponentName;

            // One line per match, naming the rating the result will be settled against.
            // Without it, a ladder that moves by the wrong amount is a number changing in
            // PlayerPrefs with nothing anywhere saying what it was measured against.
            Debug.Log($"MatchHandover: {who}"
                      + (found.AgainstBot ? " (practice)" : "")
                      + $", rating {found.OpponentRating:0}, seed {found.Seed}"
                      + $" -- this device drives fighter {found.LocalPlayer + 1}.");
        }

        /// <summary>
        /// A new search begins, or the arena is done with it.
        ///
        /// Both halves matter and the first is the one that bites. A pairing left here after
        /// its match is over is a pairing the NEXT match inherits, and the next match then
        /// settles the ladder against a rating belonging to somebody who is no longer in it
        /// -- twice against the same opponent, from one evening of play, with nothing on
        /// screen looking wrong.
        ///
        /// The opponent is cleared and not merely disarmed, for the reason
        /// <see cref="Current"/> gives.
        /// </summary>
        public static void Forget()
        {
            _found = default;
            _waiting = false;
        }

        /// <summary>
        /// Nothing is waiting at the start of a Play session, whatever the last one left
        /// here.
        ///
        /// SubsystemRegistration, so it has run before the first scene's Awake and before
        /// anything can ask. The same mechanism and the same reason as OnlineMatch's own
        /// Forget: a static survives leaving and re-entering Play mode when the domain is
        /// not reloaded, and this project is developed by opening one arena scene and
        /// running it over and over.
        ///
        /// There is no Application.quitting hook beside this one on purpose. It would clear
        /// the same statics a moment later in the same session and change nothing: this
        /// runs on the way IN, which is the only moment that can be relied on -- a Play
        /// session stopped by the editor, or an app killed by the phone, never reaches its
        /// own shutdown.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Clear()
        {
            _found = default;
            _waiting = false;
            _everHanded = false;
            _said = false;
        }
    }
}
