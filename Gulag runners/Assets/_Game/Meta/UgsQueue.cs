using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;

namespace GulagRunners.Meta
{
    /// <summary>
    /// The real pool: Unity's matchmaker, with people in it.
    ///
    /// Everything about WHO MAY MEET WHOM stays where it was -- the band widening from
    /// 60 to 400, Ladder.MayMeet refusing to hand a beginner to a professional, the bot
    /// after twenty-five seconds. This only answers where the other player came from,
    /// which is the whole reason <see cref="IMatchQueue"/> exists.
    ///
    /// Two things it does NOT ask the service for, because both devices can work them
    /// out from what they already hold: which fighter this device drives, and the
    /// number the arena is dealt from. See <see cref="MatchAgreement"/> -- a round trip
    /// that can be deleted should be.
    ///
    /// Asynchronous underneath and polled on top. The service is awaited on background
    /// tasks; everything a frame reads is a volatile field set by those tasks, and
    /// <see cref="Poll"/> does almost nothing. That is deliberate: a search screen that
    /// awaited would have to care about scenes unloading under it, and this way the
    /// worst a lost task can do is leave the state at Looking.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UgsQueue : MonoBehaviour, IMatchQueue
    {
        [Header("The queue")]
        [Tooltip("The queue's name on the Unity dashboard, exactly. A name that is not " +
                 "there fails the ticket rather than falling back, which is correct: " +
                 "silently matching in the wrong queue is worse than not matching.")]
        public string queueName = "ranked-1v1";

        [Tooltip("What the rating is called in the ticket. The dashboard's match rule " +
                 "reads this same name, so the two have to agree or the rule compares " +
                 "nothing and everybody matches everybody.")]
        public string ratingAttribute = "rating";

        [Header("Signing in")]
        [Tooltip("Sign in anonymously when nobody is signed in yet.\n\n" +
                 "Anonymous is a per-INSTALL identity: reinstall and it is a new player " +
                 "on 1500. That is fine for testing and is a hole in a ranked ladder, " +
                 "which is what a real provider — Google Play Games, Sign in with Apple " +
                 "— is for before release.")]
        public bool signInAnonymously = true;

        [Header("Debug")]
        public string state = "idle";

        /// <summary>The session, while there is one. Named so the transport can find it.</summary>
        public static ISession Current { get; private set; }

        volatile int _state = (int)QueueState.Idle;
        volatile string _status = "";
        MatchFound _found;
        bool _waiting;
        CancellationTokenSource _cancel;

        public QueueState State => (QueueState)_state;
        public string Status => _status;

        public void Begin(double rating, int matchesPlayed)
        {
            Cancel();
            _cancel = new CancellationTokenSource();
            Set(QueueState.Looking, "");
            _ = Look(rating, _cancel.Token);
        }

        /// <summary>
        /// Nothing to do: the task above is the search. Kept because the interface has
        /// it and a pool that needed a heartbeat would get one here.
        /// </summary>
        public void Poll(float seconds)
        {
            state = $"{State}: {_status}";
        }

        public bool TryTake(out MatchFound found)
        {
            found = _found;
            if (!_waiting) return false;
            _waiting = false;
            return true;
        }

        public void Cancel()
        {
            _waiting = false;

            if (_cancel != null)
            {
                try { _cancel.Cancel(); } catch (Exception) { }
                _cancel.Dispose();
                _cancel = null;
            }

            // Leaving matters more than it looks. A ticket left in the pool keeps being
            // matched, and the other player is then paired with somebody who walked
            // away -- which to them is a match that never starts.
            ISession session = Current;
            Current = null;
            if (session != null) _ = Leave(session);

            Set(QueueState.Idle, "");
        }

        void OnDestroy() => Cancel();

        // ------------------------------------------------------------ everything UGS
        //
        // The whole of this component's dependency on Unity Gaming Services is below
        // this line, in three methods. That is on purpose: the shape of this API could
        // not be read from the machine this was written on, so the part most likely to
        // need correcting is the part easiest to find.

        /// <summary>
        /// Relies on Unity's synchronization context, which is worth writing down.
        ///
        /// Begin is called from a frame, so every continuation after an await here
        /// resumes on the MAIN thread -- which is what makes it safe to touch `name`
        /// and pass `this` to Debug below. If this is ever started from a worker, or
        /// run somewhere Unity's context is not installed, those two lines are the
        /// ones that will crash and they will not look like the reason.
        /// </summary>
        async Task Look(double rating, CancellationToken stop)
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                    await UnityServices.InitializeAsync();

                if (signInAnonymously && !AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                if (stop.IsCancellationRequested) return;

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    Set(QueueState.Failed, "NOT SIGNED IN");
                    return;
                }

                ISession session = await Matchmake(rating, stop);
                if (stop.IsCancellationRequested || session == null) return;

                Current = session;
                Took(session);
            }
            catch (OperationCanceledException)
            {
                // The player pressed the cross. Not a failure.
            }
            catch (Exception e)
            {
                Set(QueueState.Failed, Short(e));
                Debug.LogWarning($"{name}: matchmaking failed -- {e.Message}\n" +
                                 "Check the queue name against the dashboard, and that " +
                                 "Matchmaker and Relay are both enabled.", this);
            }
        }

        /// <summary>
        /// The one call that joins the pool.
        ///
        /// The rating rides along as a ticket attribute so the dashboard's rule can
        /// read it; its name has to match <see cref="ratingAttribute"/> and the rule,
        /// or the rule compares nothing and the band does not exist.
        /// </summary>
        Task<ISession> Matchmake(double rating, CancellationToken stop)
        {
            MatchmakerOptions ticket = new MatchmakerOptions
            {
                QueueName = queueName,
                PlayerProperties = new Dictionary<string, PlayerProperty>
                {
                    [ratingAttribute] =
                        new PlayerProperty(rating.ToString("0.###",
                            System.Globalization.CultureInfo.InvariantCulture),
                            VisibilityPropertyOptions.Public),
                },
            };

            // Relay, and two players, because a duel is two. The transport takes the
            // allocation off the session afterwards; nothing here opens a socket.
            SessionOptions session = new SessionOptions { MaxPlayers = 2 }
                .WithRelayNetwork();

            return MultiplayerService.Instance.MatchmakeSessionAsync(ticket, session, stop);
        }

        static async Task Leave(ISession session)
        {
            try { await session.LeaveAsync(); } catch (Exception) { }
        }

        /// <summary>Turns a joined session into the pairing the search screen wants.</summary>
        void Took(ISession session)
        {
            string mine = session.CurrentPlayer != null ? session.CurrentPlayer.Id : "";
            string theirs = "";
            double theirRating = 1500;

            IReadOnlyList<IReadOnlyPlayer> players = session.Players;
            for (int i = 0; players != null && i < players.Count; i++)
            {
                IReadOnlyPlayer p = players[i];
                if (p == null || p.Id == mine) continue;
                theirs = p.Id;

                if (p.Properties != null &&
                    p.Properties.TryGetValue(ratingAttribute, out PlayerProperty said) &&
                    double.TryParse(said.Value,
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out double got))
                    theirRating = got;
                break;
            }

            _found = new MatchFound
            {
                // Worked out, not asked for: both devices reach the same answer from
                // the ids they both hold. MatchAgreement says why it is ordinal.
                LocalPlayer = MatchAgreement.LocalPlayer(mine, theirs),
                Seed = MatchAgreement.Seed(session.Id),
                OpponentName = Name(theirs),
                OpponentRating = theirRating,
                AgainstBot = false,
                Join = session.Id,
            };

            _waiting = true;
            Set(QueueState.Found, _found.OpponentName);
        }

        // --------------------------------------------------------------- the trimmings

        /// <summary>
        /// Something to show for a player with no name yet.
        ///
        /// A raw id is forty characters of hexadecimal and reads as a bug. Until there
        /// is a real display name on the account, the last few characters are enough to
        /// tell two opponents apart and short enough to look deliberate.
        /// </summary>
        static string Name(string id)
        {
            if (string.IsNullOrEmpty(id)) return "AN OPPONENT";
            string tail = id.Length <= 4 ? id : id.Substring(id.Length - 4);
            return $"RUNNER_{tail.ToUpperInvariant()}";
        }

        static string Short(Exception e)
        {
            string said = e.Message ?? "";
            if (said.Length > 60) said = said.Substring(0, 60) + "…";
            return said.ToUpperInvariant();
        }

        void Set(QueueState next, string said)
        {
            _state = (int)next;
            _status = said ?? "";
            state = $"{next}: {_status}";
        }
    }
}
