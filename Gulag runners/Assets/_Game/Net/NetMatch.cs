using UnityEngine;
using GulagRunners.Game;
using GulagRunners.Sim;

namespace GulagRunners.Net
{
    /// <summary>
    /// The clock, while two devices are playing one match.
    ///
    /// Each fighter normally pumps its own accumulator off Time.deltaTime. That is right
    /// on one device and impossible across two, because the two phones do not agree about
    /// when a frame is and never will. So this takes the clock off them and runs the whole
    /// match itself, on one rule:
    ///
    ///     a tick may run when BOTH inputs for it have arrived, and not before.
    ///
    /// That is lockstep, and it is the correct answer here rather than the simple one.
    /// The simulation is deterministic by construction -- fixed point, its own assembly
    /// with noEngineReferences, chests dealt from a constant sequence -- so two devices
    /// fed identical inputs produce identical matches, and the only thing that has to
    /// cross the network is six bytes of thumb. Nothing is replicated, nothing is
    /// corrected, and there is no authority: both devices are simply right.
    ///
    /// The price is honest: if the other player's input is late, this device waits. On a
    /// Wi-Fi that is a millisecond or two and invisible. Over the internet it would be a
    /// visible freeze, and the answer there is rollback -- predict the missing input,
    /// simulate on, and re-simulate when the truth arrives. Everything here is arranged so
    /// that can be added without moving anything else: the inputs are already stored per
    /// tick, and the simulation is already a pure function of them.
    ///
    /// Besides the clock it does one other thing, and it is the thing that makes the
    /// difference between a connection and a game: it decides which fighter is YOU. The
    /// scene is built for one phone, so fighter one is the human and fighter two is a bot,
    /// on BOTH devices. On connecting, the guest's thumb, camera, health bar and saved
    /// progress are all moved over to fighter two, and the bots are switched off.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]          // before the fighters would have ticked themselves
    public sealed class NetMatch : MonoBehaviour
    {
        [Header("Wiring")]
        public LanLink link;
        public MatchState match;

        [Tooltip("The camera that follows whoever is holding this phone. Left empty it " +
                 "takes the one in the scene, which is what a one-camera arena wants.")]
        public SideViewCamera view;

        [Tooltip("The HUD, the result screen and the thing that writes to saved progress. " +
                 "All three carry a \"which fighter is me\" and all three are wrong on the " +
                 "guest's phone until this corrects them. Left empty they are found.")]
        public HudView hud;
        public MatchResultView result;
        public MatchRecorder recorder;

        [Tooltip("Most ticks to run in one frame while catching up after a stall. Without " +
                 "a cap, a long wait is repaid as a lurch.")]
        [Range(1, 16)] public int catchUpLimit = 6;

        [Header("Debug")]
        public int tick;
        public string state = "not connected";
        public int stalledFrames;

        [Tooltip("Ticks of input budget actually in use, and the round trip it came " +
                 "from. Read these when the match feels laggy: a delay that keeps " +
                 "climbing is the link, and stalls at a delay that is not climbing are " +
                 "packet loss.")]
        public int delay;
        public int roundTripMs;

        float _accumulator;
        bool _primed;
        bool _holding;
        int _takenFor = -1;
        int _sentTo = -1;
        TickInput _queued;
        PlayerController _controls;

        void Awake()
        {
            if (link == null) link = FindFirstObjectByType<LanLink>();
            Adopt();
            if (match == null) match = FindFirstObjectByType<MatchState>();
            if (view == null) view = FindFirstObjectByType<SideViewCamera>();
            if (hud == null) hud = FindFirstObjectByType<HudView>();
            if (result == null) result = FindFirstObjectByType<MatchResultView>();
            if (recorder == null) recorder = FindFirstObjectByType<MatchRecorder>();
        }

        /// <summary>
        /// Takes the match the menu arranged, if there was one.
        ///
        /// OnlineMatch is the only object that survives the load into this scene, and
        /// it survives carrying exactly three things: a relay already open to the other
        /// player, which of the two fighters this device drives, and the number the
        /// arena is dealt from. All three are what the LAN handshake exists to discover
        /// -- so when they are already known there is nothing to discover, and the link
        /// is simply told.
        /// </summary>
        void Adopt()
        {
            if (link == null || OnlineMatch.Live == null) return;
            if (!OnlineMatch.Live.TryTake(out INetTransport wire, out bool host,
                                          out uint seed, out int _)) return;

            link.Adopt(wire, host, seed);
        }

        void OnDisable() => ReleaseClock();

        void Update()
        {
            if (link == null || match == null) return;

            if (link.Role == LinkRole.Off || !link.Connected)
            {
                ReleaseClock();
                state = link.Role == LinkRole.Off ? "not connected" : "waiting for the other player";
                return;
            }

            TakeClock();

            // The first few ticks have no input in flight yet, because input is sent
            // `inputDelay` ahead and the match has not started. Fill them in, or neither
            // device can ever run tick zero and both wait for the other for ever.
            if (!_primed)
            {
                for (int t = 0; t < link.inputDelay; t++) link.SendLocal(t, default);
                _sentTo = link.inputDelay - 1;
                _primed = true;
            }

            _accumulator += Time.deltaTime;

            int ran = 0;
            while (_accumulator >= PlayerController.SecondsPerTick && ran < catchUpLimit)
            {
                // Keep feeding the other device even while stalled: its clock is waiting
                // on this input, and a stall on both sides at once never resolves.
                //
                // How far ahead is measured, not fixed -- see LanLink.SuggestedDelay --
                // and it can therefore come DOWN when the link improves. Sending the
                // tick it then names would be sending one already sent, which SendLocal
                // rightly refuses while throwing away the newer thumb with it. So the
                // furthest tick sent only ever moves forward: a shrinking budget means
                // the clock quietly catches up to it instead.
                delay = link.SuggestedDelay;
                int want = tick + delay;
                if (want > _sentTo)
                {
                    _sentTo = want;
                    link.SendLocal(want, Mine(want));
                }
                else
                {
                    link.SendLocal(_sentTo, Mine(_sentTo));   // re-send, in case it was lost
                }

                if (!link.TryRemote(tick, out TickInput theirs))
                {
                    stalledFrames++;
                    state = $"tick {tick}: waiting for the other player";
                    return;                       // hold the accumulator; try next frame
                }

                _accumulator -= PlayerController.SecondsPerTick;
                ran++;

                // A rematch is a tick like any other: both devices pass through it and
                // both deal the same new match on it. Before the fighters step, so the
                // tick they are stepped on is already the new match's first.
                if (link.TryRematch(tick, out uint again)) NewMatch(again);

                Step(link.Local(tick), theirs);
                tick++;
                link.Now = tick;

                if (link.checkEvery > 0 && tick % link.checkEvery == 0)
                    link.Checkpoint(tick, Hash());
            }

            // A stall longer than the cap is repaid over several frames rather than in
            // one lurch, so the accumulator is trimmed rather than banked for ever.
            if (ran == catchUpLimit)
                _accumulator = Mathf.Min(_accumulator, PlayerController.SecondsPerTick);

            roundTripMs = link.RoundTrip;
            state = link.Desync
                    ?? $"tick {tick}, as player {link.LocalPlayer + 1}, {delay} tick(s) of input budget for {roundTripMs} ms";
            Draw(_accumulator / PlayerController.SecondsPerTick);
        }

        /// <summary>
        /// This device's own controls, for one tick, taken once and then held.
        ///
        /// Taken once matters twice over. The input for a tick is frozen the moment it is
        /// sent, so a second sample for the same tick would be thrown away -- and taking
        /// it would have consumed a tap on the way. While the match is stalled this runs
        /// every frame for the same tick, which is precisely when taps would go missing.
        /// </summary>
        TickInput Mine(int forTick)
        {
            if (_takenFor == forTick) return _queued;
            _takenFor = forTick;
            _queued = _controls != null ? _controls.TakeLocalInput() : default;
            return _queued;
        }

        /// <summary>One tick of the whole match, both fighters, in player order.</summary>
        void Step(TickInput mine, TickInput theirs)
        {
            // By INDEX, always, and never by "mine first". The two devices must step the
            // fighters in the same order or the shared work inside the tick -- the loot,
            // the hit resolution -- happens in two different orders and the matches part.
            PlayerController a = At(0), b = At(1);
            TickInput first = link.LocalPlayer == 0 ? mine : theirs;
            TickInput second = link.LocalPlayer == 0 ? theirs : mine;

            if (a != null) a.StepOnce(first);
            if (b != null) b.StepOnce(second);
        }

        void Draw(float alpha)
        {
            for (int i = 0; i < match.fighters.Count; i++)
                if (match.fighters[i] != null) match.fighters[i].RenderAt(alpha);
        }

        PlayerController At(int index) =>
            match != null && index >= 0 && index < match.fighters.Count
                ? match.fighters[index] : null;

        void TakeClock()
        {
            if (_holding) return;
            _holding = true;

            // Whose thumb this is. The scene gives the on-screen controls to one fighter
            // and a brain to the other, and that is true on both phones -- so the guest's
            // thumb is wired to fighter one here and drives fighter two over the wire.
            // Find it BEFORE the brains go off, or every fighter looks human.
            _controls = Human();

            for (int i = 0; i < match.fighters.Count; i++)
            {
                PlayerController f = match.fighters[i];
                if (f == null) continue;
                f.ExternallyClocked = true;

                // And neither fighter is a bot any more. The brain would go on thinking
                // -- harmlessly, since its answer is now discarded -- but a bot still
                // running behind a human-controlled body is the kind of thing somebody
                // spends an afternoon on when the fighter does something unexplained.
                FighterBrain brain = f.GetComponentInChildren<FighterBrain>(true);
                if (brain != null) brain.enabled = false;
            }

            BeMe(link.LocalPlayer);

            // Both devices throw away whatever match they were in the middle of and deal
            // the same one, from the number the host chose. Without this the two phones
            // are connected and still playing two different arenas.
            match.StartFresh(link.MatchSeed);

            tick = 0;
            _accumulator = 0f;
            _takenFor = -1;
            _sentTo = -1;
        }

        /// <summary>
        /// Wire the result screen's Again here. It does not restart anything itself: it
        /// asks, and the match begins again on a tick both devices agree on.
        /// </summary>
        public void AskRematch()
        {
            if (link == null || !link.Connected) return;
            link.Now = tick;
            link.AskRematch();
            state = $"tick {tick}: asked for a rematch";
        }

        /// <summary>Everything a new match needs, on the tick both devices chose.</summary>
        void NewMatch(uint dealtFrom)
        {
            // Back to full speed first. The outcome view ramps the time scale down for
            // the last blow and puts it back when its own sequence finishes -- and a
            // rematch can land mid-ramp, which leaves the next match running at a third
            // speed for ever. Not a desync, since the clock waits either way, but it
            // reads as the whole game having broken.
            Time.timeScale = 1f;

            match.StartFresh(dealtFrom);
            if (recorder != null) recorder.Rearm();
            if (result != null) result.Dismiss();
        }

        /// <summary>The fighter the on-screen controls drive: the one with no brain on it.</summary>
        PlayerController Human()
        {
            for (int i = 0; i < match.fighters.Count; i++)
            {
                PlayerController f = match.fighters[i];
                if (f == null) continue;
                FighterBrain brain = f.GetComponentInChildren<FighterBrain>(true);
                if (brain == null || !brain.TakingOver) return f;
            }
            return At(0);
        }

        /// <summary>
        /// Point everything that has a point of view at the fighter this phone is playing.
        /// The guest is player two, and until this runs the guest is watching player one's
        /// camera, reading player one's health bar, and about to bank player one's win.
        /// </summary>
        void BeMe(int player)
        {
            PlayerController mine = At(player);
            if (view != null && mine != null)
            {
                view.player = mine;
                view.target = mine.transform;
            }
            if (hud != null) hud.localPlayer = player;
            if (result != null)
            {
                result.localPlayer = player;
                result.someoneElseRestarts = true;     // Again asks; the clock schedules
            }
            if (recorder == null) return;

            recorder.localPlayer = player;

            // The opponent is a person now, not the practice bot, so the ladder stops
            // scoring against the stand-in rating. What it does NOT do is ask the other
            // phone what its rating is: on a LAN that is a number the other device can
            // simply make up, and a ladder that believes it is not a ladder. An even
            // match against this player's own rating is the honest reading until there
            // is a server to ask (docs/09).
            recorder.rateAgainstBots = false;
            recorder.Rearm();
        }

        void ReleaseClock()
        {
            if (!_holding) return;
            _holding = false;
            _primed = false;
            tick = 0;
            _takenFor = -1;
            _sentTo = -1;
            _controls = null;
            if (result != null) result.someoneElseRestarts = false;
            if (match == null) return;
            for (int i = 0; i < match.fighters.Count; i++)
            {
                PlayerController f = match.fighters[i];
                if (f == null) continue;
                f.ExternallyClocked = false;
                FighterBrain brain = f.GetComponentInChildren<FighterBrain>(true);
                if (brain != null) brain.enabled = true;
            }
        }

        /// <summary>
        /// A number that must be the same on both devices, or the two matches have
        /// parted. Cheap and coarse on purpose: it is a tripwire, not a diff. Everything
        /// folded in is fixed point or an integer, because folding in a float would make
        /// the tripwire itself a source of disagreement.
        /// </summary>
        uint Hash()
        {
            uint h = 2166136261u;
            for (int i = 0; i < match.fighters.Count; i++)
            {
                PlayerController f = match.fighters[i];
                if (f == null) continue;
                PlayerSimState s = f.State;
                Fold(ref h, s.Position.X.Raw);
                Fold(ref h, s.Position.Y.Raw);
                Fold(ref h, s.Velocity.X.Raw);
                Fold(ref h, s.Velocity.Y.Raw);
                Fold(ref h, s.Arm.X.Raw);
                Fold(ref h, s.Arm.Y.Raw);
                Fold(ref h, s.Health);
                Fold(ref h, (int)s.Mode);
                Fold(ref h, s.Facing);
                Fold(ref h, (int)s.Inventory.Weapon);
            }
            return h;
        }

        static void Fold(ref uint h, int value)
        {
            unchecked
            {
                h = (h ^ (uint)(value & 0xFFFF)) * 16777619u;
                h = (h ^ (uint)((value >> 16) & 0xFFFF)) * 16777619u;
            }
        }
    }
}
