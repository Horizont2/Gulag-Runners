using UnityEngine;
using UnityEngine.Events;
using GulagRunners.Meta;
using GulagRunners.Net;

namespace GulagRunners.Game
{
    /// <summary>
    /// Something that knows whether the other runner has finished loading.
    ///
    /// There is no net layer yet, and this is the shape of the hole where one goes. The
    /// alternative was to invent a wait -- spin for a plausible second and call it the
    /// opponent -- which is the same lie as a progress bar that is not measuring anything,
    /// and it would have to be found and removed the day a real one arrives.
    /// </summary>
    public interface IMatchGate
    {
        /// <summary>True once the other side says it is in.</summary>
        bool OpponentReady { get; }
    }

    /// <summary>
    /// The search screen, from pressing PLAY to the arena appearing.
    ///
    /// ONE screen with five phases, and the bar is a different measurement in each. That
    /// is the whole design. A single bar pretending to be progress while nothing is
    /// loading is the oldest lie on a loading screen, and a player who waits through it
    /// twice stops reading it:
    ///
    ///   Searching  the net being cast, 0 at your own rating and 1 at the widest it goes.
    ///              A search has no length, so this measures the one thing that IS moving.
    ///   Found      full, held a beat -- long enough to read who, short enough not to wait.
    ///   Loading    the real one. MenuDirector's LoadSceneAsync progress and nothing else.
    ///   Waiting    full and still. Their load, not yours.
    ///   Starting   the hand-off.
    ///
    /// It creates nothing and draws nothing: the readouts go out as UnityEvents, exactly
    /// like <see cref="MenuUI"/>, so the screen can be drawn by uGUI today and by
    /// something else later. Wire Phase Changed to a MenuLabel.Set, Bar Filled to a
    /// MenuBar.Fill, and the cancel button to Cancel.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchSearch : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Runs the walk through the gate and loads the arena. Found when empty.")]
        public MenuDirector director;

        [Tooltip("The ladder and the purse, for who is searching. Found when empty.")]
        public PlayerProgress progress;

        [Tooltip("Optional. Something implementing IMatchGate.\\n\\n" +
                 "Empty means the opponent is ready the moment this side is, which is the " +
                 "truth while there is no net layer: there IS nobody else. The waiting " +
                 "phase then flashes past rather than being faked, and that is correct.")]
        public MonoBehaviour opponentGate;

        [Tooltip("The pool this searches in. Anything implementing IMatchQueue.\n\n" +
                 "Left empty, everybody is paired with a practice opponent — which is " +
                 "not a stub: docs/09 makes the first five matches bots by design, and " +
                 "is equally clear that twenty-five seconds of an empty queue is a " +
                 "player deciding the game is dead. That floor stays under every real " +
                 "pool that is ever put here.")]
        public MonoBehaviour queueSource;

        [Tooltip("Optional. The stills behind it all.")]
        public MenuBackdrop backdrop;

        [Header("Rules")]
        public SearchRules rules = SearchRules.Default;

        [Tooltip("Seconds the found opponent is shown before the walk begins.")]
        [Range(0.2f, 3f)] public float foundSeconds = 1.1f;

        [Tooltip("Shown while your arena is up and theirs is not. {0} is who, {1} is " +
                 "the seconds left before the match starts without waiting any " +
                 "longer.\n\n" +
                 "This phase used to say nothing and hold a full bar, which is the " +
                 "shape of a crash whether or not it is one -- and for a while it was " +
                 "one, because the gate had no way past a lost packet.")]
        public string waitingForThem = "{0} IS STILL LOADING… {1}";

        /// <summary>
        /// The pool, and the only copy of it. SearchBuilder used to carry its own and
        /// overwrite this one, which is two lists that have to agree -- the same trap as
        /// two scrim gradients. The builder now reads whatever is here.
        ///
        /// Every number is taken out of the game as it stands, not out of the design
        /// documents. A tip that is nearly true is worse than no tip: it is the interface
        /// telling a player something they will be punished for believing. Where each
        /// came from:
        ///
        ///   siren        MatchConfig.SirenFrames 90, at 0:25, and SirenDirection
        ///   noise        NoiseLevel -- crouching is Silent, running is Loud
        ///   prying       ChestConfig: 150 frames bare, half that with a tool, ToolWear 2,
        ///                BareNoise Medium against ToolNoise Loud
        ///   desperation  DesperationHealthPermille 300, DesperationDamagePermille 1400,
        ///                and CombatMotor applies it to the ATTACKER, whatever is in hand
        ///   parry        ParryFrames 12, and Parry() sets StaggerTimer to StaggerFrames 48
        ///   combo        MaxCombo 3, ComboWindowFrames 20
        ///   the rooms    180+1320+90+2010 = 3600 frames, so Closing opens at 1:00 and
        ///                ShutterFrames takes the rooms to 2:00. The ceiling is a
        ///                different thing at a different time: sudden death, 2:20.
        /// </summary>
        [Header("Tips")]
        [Tooltip("Shown one at a time, top left. The other half of what docs/09 asks the " +
                 "daily contracts to do: teach the mechanics. A player stares at this " +
                 "screen twice a match and at nothing else while they wait.\n\n" +
                 "Every number in them is read out of the live config. Check one before " +
                 "you change it.")]
        [TextArea(2, 4)]
        public string[] tips =
        {
            "THE SIREN AT 0:25\nGIVES A DIRECTION,\nNEVER A POSITION.",
            // This used to say crouching was silent and running the loudest thing you
            // could do. Crouching was taken out of the motor, and the tip outlived it by
            // the same route the three guard tips did -- it quotes no number, so the pins
            // below had nothing to catch it with. The rule that does catch it now is in
            // tips_check: a tip may not name a mechanic PlayerMotor never enters.
            "A STEP IS LOUD ABOVE\n2.5 M/S. A FULL RUN IS 3.\nEASE OFF AND HE LOSES YOU.",
            "A TOOL HALVES THE PRY,\n2.5 S TO 1.25 — AND COSTS\nTWO SWINGS AND THE NOISE.",
            "BELOW 30% HEALTH\nYOUR BLOWS HIT 40% HARDER.\nSO DO HIS.",
            "A PARRY IS TWELVE FRAMES\nWIDE AND STAGGERS HIM\nFOR EIGHT TENTHS.",
            // Three tips stood here and taught, in turn, a guard button and then a
            // dodge -- and each time the mechanic went and the tip stayed, so the search
            // screen spent weeks explaining a control the player does not have. These
            // three teach what is actually there: one ground speed, a blade that defends
            // by being in the way, and a body that is always hittable.
            "THERE IS NO GUARD AND NO\nROLL. YOUR BLADE IS THE ONLY\nTHING THAT STOPS HIS.",
            "ONE GROUND SPEED, ALWAYS.\nDISTANCE IS SOMETHING YOU\nWALK, NOT SOMETHING YOU SPEND.",
            "NOTHING MAKES YOU\nUNTOUCHABLE. IF THE BLADE\nREACHES YOU, IT LANDS.",
            // This used to say that three blows chain and the window between them is a
            // third of a second, off MaxCombo and ComboWindowFrames -- and both fields are
            // still in CombatConfig and are read by NOTHING. The three-hit string went with
            // the phase machine (docs/30), so the tip outlived its mechanic the same way the
            // guard tips did, and the pin in tips_check could not see it because the fields
            // it quotes had not been deleted, only orphaned.
            //
            // What actually happens now is the stun stack: the first blow of a string is
            // floored so that landing always buys the next move, and every one after it is
            // worth ComboStunDecayPermille of the last -- 620, so 38% less each time. A
            // string ends after ComboStackFrames without being hit.
            "THE FIRST BLOW BUYS\nYOUR NEXT. EVERY ONE AFTER\nHOLDS HIM 38% LESS.",
            "THE ROOMS START SHUTTING\nAT 1:00. THE CEILING\nCOMES LATER, AND LOWER.",
        };

        [Tooltip("Seconds each tip is held. Long enough to read three lines twice.")]
        [Range(2f, 12f)] public float tipSeconds = 6f;

        [Tooltip("Seconds each still is held, separately from the tips.\n\n" +
                 "Longer on purpose. A tip is a sentence somebody is reading and a still " +
                 "is a picture they are not, and on one timer every change of picture " +
                 "landed on the line they had just started.")]
        [Range(3f, 20f)] public float backdropSeconds = 10f;

        [Header("Readouts")]
        [Tooltip("\"SEARCHING\", \"OPPONENT FOUND\", \"LOADING THE ARENA\"...")]
        public UnityEvent<string> phaseChanged;

        [Tooltip("The line under it: the league and the seconds, or the seed.")]
        public UnityEvent<string> detailChanged;

        [Tooltip("0 to 1. What it MEANS depends on the phase — see the class comment.")]
        public UnityEvent<float> barFilled;

        [Tooltip("The tip, top left.")]
        public UnityEvent<string> tipChanged;

        [Tooltip("True while this screen should be on at all.")]
        public UnityEvent<bool> shownChanged;

        [Tooltip("Fired the moment an opponent is found, with true when it is a bot. " +
                 "Read Opponent for who they are and the number the arena is dealt from.")]
        public UnityEvent<bool> Paired;

        [Header("Debug")]
        public SearchPhase phase = SearchPhase.Cancelled;
        public float elapsed;

        IMatchGate _gate;
        IMatchQueue _queue;

        /// <summary>
        /// The same object as <see cref="_queue"/>, kept typed so the screen can ask the
        /// one question only the composed pool can answer: why the service was given up on.
        /// </summary>
        MatchPool _pool;

        MatchFound _opponent;
        bool _running;

        /// <summary>
        /// Who this device was paired with, and the number the arena is dealt from.
        /// Read it after Paired fires: the arena needs the seed, and the result screen
        /// needs to know whether that was a person.
        ///
        /// This copy dies with the menu. What the ARENA reads is
        /// <see cref="MatchHandover.Current"/>, handed over in Found() in the same breath
        /// as the call that asks for the arena.
        /// </summary>
        public MatchFound Opponent => _opponent;
        float _held;
        int _tip = -1;
        float _tipHeld;
        float _stillHeld;

        /// <summary>No gate wired means nobody to wait for, and that is not a stub.</summary>
        bool OpponentReady => _gate == null || _gate.OpponentReady;

        void Awake()
        {
            if (director == null) director = FindFirstObjectByType<MenuDirector>();
            if (progress == null) progress = PlayerProgress.Instance;
            if (progress == null) progress = FindFirstObjectByType<PlayerProgress>();
            _gate = opponentGate as IMatchGate;

            if (opponentGate != null && _gate == null)
                Debug.LogWarning($"{name}: {opponentGate.GetType().Name} is in the " +
                                 "Opponent Gate slot but does not implement IMatchGate, so " +
                                 "nothing is being waited for.", this);

            IMatchQueue live = queueSource as IMatchQueue;

            if (queueSource != null && live == null)
                Debug.LogWarning($"{name}: {queueSource.GetType().Name} is in the Queue " +
                                 "slot but does not implement IMatchQueue, so the search " +
                                 "will fall back to practice opponents.", this);

            // WHATEVER IS IN THE SLOT, THE FLOOR GOES UNDER IT. An empty slot is not an
            // error and not a stub -- it is the floor docs/09 describes, where everybody
            // gets the practice opponent -- and a slot with a service in it does not
            // REPLACE that floor, which is what it used to do and is the whole of the
            // matchmaking fault. MatchPool's own summary has the account: with a service
            // in the slot, OfferBot, BotAfterSeconds and Ladder.PoolFor were never
            // reached, so an empty pool searched for ever and a failed one ended on a
            // dead screen.
            _pool = new MatchPool(live, rules);
            _queue = _pool;

            shownChanged?.Invoke(false);
        }

        /// <summary>Wire PLAY to this instead of to MenuDirector.Play.</summary>
        public void Begin()
        {
            if (_running) return;

            _running = true;

            // Whoever the last search found is not in this one. A pairing left behind by
            // the match before is one the next arena would be handed as its opponent, and
            // the ladder would settle a second result against somebody who is no longer in
            // it -- the same person beaten twice, out of one evening, with nothing on any
            // screen looking wrong. MatchHandover's own summary has the rest of it.
            MatchHandover.Forget();

            _queue.Begin(progress != null ? progress.rating : 1500.0,
                         progress != null ? progress.matchesPlayed : 0);
            elapsed = 0f;
            _held = 0f;
            _tip = -1;
            _tipHeld = float.MaxValue;      // forces the first tip on the first frame
            _stillHeld = 0f;                // the backdrop already has one up
            Enter(SearchPhase.Searching);
            shownChanged?.Invoke(true);
        }

        /// <summary>
        /// The cross, top right.
        ///
        /// Only while there is something to come back from. Once the arena is loading, the
        /// other runner is already committed to this match: letting one side walk out of a
        /// match the other has joined is how a ladder learns to hate you.
        /// </summary>
        public void Cancel()
        {
            if (!_running) return;
            if (phase != SearchPhase.Searching && phase != SearchPhase.Found) return;

            _running = false;
            _queue.Cancel();

            // AND THE RELAY, which nothing was letting go of. The cross is live through the
            // Found beat, and Paired has already fired by then -- so OnlineMatch is part way
            // through a handshake that carries on after the player has walked away: it
            // allocates a relay nobody will join, writes a join code into a session that
            // UgsQueue.Cancel has just left, and parks the result in the statics it hands to
            // whatever loads an arena next. Drop cancels the task, closes the wire and
            // clears those statics, which is all four problems.
            if (_gate is OnlineMatch online) online.Drop();
            else if (OnlineMatch.Live != null) OnlineMatch.Live.Drop();

            // The cross puts the load back (Rewind, below) and the pairing goes back with
            // it. Nothing is waiting this early as the code stands -- the handover is made
            // in Found(), after the beat, and the cross is dead from that moment on -- but
            // the SEED is already written at the moment of pairing, a beat earlier, because
            // the director needed it sooner. A handover that one day follows it there would
            // leave a cancelled opponent sitting in a static with nothing to clear them.
            MatchHandover.Forget();

            Enter(SearchPhase.Cancelled);
            shownChanged?.Invoke(false);
            if (director != null) director.Rewind();
        }

        void Update()
        {
            if (!_running) return;

            elapsed += Time.deltaTime;
            Tips();
            Backdrops();

            switch (phase)
            {
                case SearchPhase.Searching: Searching(); break;
                case SearchPhase.Found: Found(); break;
                case SearchPhase.Loading: Loading(); break;
                case SearchPhase.Waiting: Waiting(); break;
            }
        }

        void Searching()
        {
            int played = progress != null ? progress.matchesPlayed : 0;
            double rating = progress != null ? progress.rating : 1500.0;

            // The pool's own line wins when it has one. That is the gap between the service
            // giving up and the practice opponent coming due -- up to twenty-five seconds in
            // which the league-and-seconds line is technically true and says nothing about
            // the only thing that has happened.
            string said = _pool != null ? _pool.Status : "";
            Say(string.IsNullOrEmpty(said)
                    ? Matchmaker.Detail(rules, rating, elapsed, played) : said);
            Fill(Matchmaker.Cast(rules, elapsed));

            // The bar above is this screen's: it measures how far the net has been cast
            // and nothing else knows that. WHO is on the other end is the pool's, and
            // the screen does not care whether that pool is a service with people in it
            // or the bot floor docs/09 promises -- it asks the same question either way.
            _queue.Poll(elapsed);

            // Kept although MatchPool never answers Failed any more: a pool put here by
            // hand, or a later one, may. A screen that stops dead on a failure it was not
            // told how to recover from is still better than one that hands an arena over
            // to nobody.
            if (_queue.State == QueueState.Failed)
            {
                Say(string.IsNullOrEmpty(_queue.Status) ? "NO CONNECTION" : _queue.Status);
                return;
            }

            if (!_queue.TryTake(out _opponent)) return;

            _held = 0f;
            Enter(SearchPhase.Found);
            Say(string.IsNullOrEmpty(_opponent.OpponentName)
                ? "OPPONENT FOUND" : _opponent.OpponentName);
            Fill(1f);

            // The arena is dealt from the pairing's own number, before either device
            // starts loading and without either telling the other. Set here rather than
            // in Found(), because Play() is one frame away and a director that loaded
            // on seed 0 would load the wrong arena perfectly reliably.
            // Find it if nobody wired it. There was no fallback here at all, only this
            // null test, and the field is empty in Menu.unity -- so mapSeed stayed 0, the
            // pool was dealt the same number every time, and every match ran in the arena
            // with the heaviest weight. That is the whole of "pressing PLAY just starts
            // the forest": the pool was working exactly as told, from a seed nobody set.
            if (director == null) director = FindFirstObjectByType<MenuDirector>();
            if (director != null) director.mapSeed = _opponent.Seed;
            else
                Debug.LogWarning("MatchSearch found no MenuDirector, so the arena seed goes "
                                 + "nowhere and every match will be dealt the same map.");

            Paired?.Invoke(_opponent.AgainstBot);
        }

        void Found()
        {
            _held += Time.deltaTime;
            if (_held < foundSeconds) return;

            Enter(SearchPhase.Loading);
            if (director != null)
            {
                // The pairing crosses the scene load HERE, in the same breath as the load
                // that loses it. Play() below is the only thing in the menu that asks for
                // the arena, and every object in this scene -- this one included, holding
                // the only copy of _opponent -- is gone by the time it arrives.
                //
                // Not at the moment the opponent was found, a beat earlier, although the
                // seed is set there and the temptation is to keep the two together: the
                // cross is still live through that beat, and a pairing handed over before
                // the player can refuse it would be sitting here waiting to be played by
                // whatever loads an arena next.
                MatchHandover.Hand(_opponent);

                director.waitForRelease = true;
                director.Play();
            }
        }

        void Loading()
        {
            if (director == null) { Enter(SearchPhase.Starting); Done(); return; }

            Say(director.state);
            Fill(director.LoadProgress);

            if (!director.ReadyToHandOver) return;

            // Say so, once this side is holding at the gate. The other device's screen
            // is waiting on exactly this, and until it was sent the gate opened on "the
            // relay is up" -- which is true well before either arena has finished
            // loading, and is what let one phone start the match alone.
            if (_gate is OnlineMatch online) online.WeHaveLoaded();

            if (Failed()) return;

            Enter(OpponentReady ? SearchPhase.Starting : SearchPhase.Waiting);
            if (phase == SearchPhase.Starting) Done();
        }

        /// <summary>
        /// Your arena is up and theirs is not. The one moment on this screen where
        /// nothing is happening that you can see.
        ///
        /// It used to say nothing at all and hold a full bar, which is the shape of a
        /// hang whether or not it is one -- and for a while it WAS one, because the
        /// gate had no way past a lost packet. A full still bar with no words is the
        /// only state on this screen a player cannot tell from a crash.
        ///
        /// So it names who is being waited for, and counts. The count is the real
        /// timeout, not decoration: when it runs out the match starts anyway.
        /// </summary>
        /// <summary>
        /// The pairing has fallen over, so this screen stops rather than hands over.
        ///
        /// Handing over anyway is what it did, and it is the worst of the three things
        /// it could do. The gate answered "ready" whenever there was no relay, which
        /// covered both "there is nobody to wait for" and "the connection failed" -- so
        /// a failed match loaded the arena, said nothing, and left the player alone in
        /// it driving fighter one while fighter two stood still. Two players doing that
        /// at once, each in their own arena, is a bug report about one character being
        /// shared, and the real fault is four screens earlier.
        ///
        /// Stopping here is honest and costs nothing: the arena is loaded but held, the
        /// cross is still on screen, and pressing it rewinds the load.
        /// </summary>
        bool Failed()
        {
            string broken = _gate is OnlineMatch online ? online.Broken : "";
            if (string.IsNullOrEmpty(broken)) return false;

            Fill(1f);
            Say(broken.ToUpperInvariant());
            return true;
        }

        void Waiting()
        {
            Fill(1f);

            if (Failed()) return;

            if (OpponentReady)
            {
                Enter(SearchPhase.Starting);
                Done();
                return;
            }

            string who = string.IsNullOrEmpty(_opponent.OpponentName)
                ? "THE OTHER RUNNER" : _opponent.OpponentName.ToUpperInvariant();

            // Whichever deadline is actually in force -- the relay opening, or the other
            // phone saying its arena is up. This used to read readyTimeout only, which has
            // not started yet while the relay is still being opened: the count sat on zero
            // for however long the handshake took, which was up to a minute and read as a
            // hang. OnlineMatch.SecondsLeft has the account.
            float left = _gate is OnlineMatch online ? online.SecondsLeft : 0f;

            Say(left > 0f
                ? string.Format(waitingForThem, who, Mathf.CeilToInt(left))
                : string.Format(waitingForThem, who, 0).Split('…')[0].TrimEnd());
        }

        void Done()
        {
            Fill(1f);
            _running = false;
            if (director != null) director.Release();
        }

        /// <summary>
        /// The tip, top left, on its own clock.
        ///
        /// Its own, because a tip is three lines somebody is READING and a backdrop is a
        /// picture they are not. Driven off one timer, every change of picture landed on
        /// the sentence they had just started -- and the backdrop inherited the tip's
        /// interval, which is half what its own push-in is tuned for.
        /// </summary>
        void Tips()
        {
            if (tips == null || tips.Length == 0) return;

            _tipHeld += Time.deltaTime;
            if (_tipHeld < Mathf.Max(1f, tipSeconds)) return;

            _tipHeld = 0f;
            // Walked rather than rolled: a random pick repeats, and a repeat on a screen
            // somebody sees twenty times a session reads as the game being broken.
            _tip = (_tip + 1) % tips.Length;
            tipChanged?.Invoke(tips[_tip]);
        }

        void Backdrops()
        {
            if (backdrop == null) return;

            _stillHeld += Time.deltaTime;
            if (_stillHeld < Mathf.Max(1f, backdropSeconds)) return;

            _stillHeld = 0f;
            backdrop.Next();
        }

        void Enter(SearchPhase next)
        {
            phase = next;
            phaseChanged?.Invoke(Label(next));
        }

        void Say(string detail) => detailChanged?.Invoke(detail);
        void Fill(float share) => barFilled?.Invoke(Mathf.Clamp01(share));

        static string Label(SearchPhase p) => p switch
        {
            SearchPhase.Searching => "SEARCHING",
            SearchPhase.Found => "OPPONENT FOUND",
            SearchPhase.Loading => "LOADING THE ARENA",
            SearchPhase.Waiting => "WAITING FOR THE OTHER RUNNER",
            SearchPhase.Starting => "STARTING",
            _ => "",
        };
    }
}
