using UnityEngine;
using UnityEngine.Events;
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

        [Header("When the other player stops answering")]
        [Tooltip("Seconds of total silence before a bot takes their fighter over.\n\n" +
                 "Lockstep cannot run without their input, so this is how long the WHOLE " +
                 "match freezes — not how long their fighter stands there. The bot picks " +
                 "up from the tick the input stopped, because there is no later moment to " +
                 "pick up from, so nobody ever gets a free swing at a statue.\n\n" +
                 "Short enough that a freeze reads as a hitch; long enough that a bad " +
                 "second on a phone's Wi-Fi does not throw somebody out of their own " +
                 "match for good. Every packet already carries sixteen past inputs — a " +
                 "quarter second of history — so total silence for whole seconds is not " +
                 "a lossy link, it is an absent player.")]
        [Range(0.5f, 15f)] public float waitForThem = 2.5f;

        [Tooltip("Seconds of silence tolerated BEFORE the other player's first input has " +
                 "ever arrived. Longer than the number above, and that is the whole point " +
                 "of it being a separate number.\n\n" +
                 "THE ONE ABOVE USED TO COVER BOTH, and the arithmetic of the two gates " +
                 "either side is what made that a bug rather than a tuning choice. " +
                 "OnlineMatch.readyTimeout lets the search screen through after six " +
                 "seconds whether or not the other phone has said its arena is up -- " +
                 "deliberately, because one lost datagram must not hang a match. So this " +
                 "clock starts at tick zero on a device whose opponent is, quite normally, " +
                 "still loading a four-megabyte scene; two and a half seconds later " +
                 "this device gave up on them and played on alone. Both halves of \"my " +
                 "opponent left immediately\" and \"I was put in an arena against a " +
                 "statue\" are this number.\n\n" +
                 "Nothing is lost by being patient here: lockstep has not run a tick yet, " +
                 "so the wait costs no simulation and no divergence. Once the first input " +
                 "HAS arrived the link is proven and the short number above takes over, " +
                 "which is the one that keeps a mid-match disconnect from freezing the " +
                 "screen.")]
        [Range(1f, 30f)] public float firstContactSeconds = 12f;

        // NOTHING IS SAID ABOUT THE OTHER PLAYER'S CONNECTION. There was a line here
        // -- OPPONENT NOT RESPONDING, with a countdown -- and an event for a bot taking
        // their fighter over. Both are gone. It is their connection, not this player's,
        // and all a countdown does to the person who can still play is tell them to
        // expect the match to go wrong. The fighter stops moving; that is the whole of
        // what this end needs to show.

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

        // What the sentence above last said, so it is only written again when it changed.
        int _saidDelay = -1, _saidTrip = -1, _saidSide = -1;

        float _accumulator;
        bool _primed;
        bool _holding;
        int _takenFor = -1;
        int _sentTo = -1;
        float _silent;
        bool _aloneNow;

        /// <summary>
        /// The other player's input has arrived at least once, so the link is PROVEN.
        ///
        /// Until it has, silence means "they have not got here yet" and not "they have
        /// gone", and those two want very different patience -- see
        /// <see cref="firstContactSeconds"/>. Cleared only by ReleaseClock, because a link
        /// that worked once and then stopped is a disconnect however long ago the once was.
        /// </summary>
        bool _everHeard;
        bool _wasPaused;
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
            if (link == null) return;
            if (!OnlineMatch.TryTake(out INetTransport wire, out bool host,
                                     out uint seed, out int _)) return;

            link.Adopt(wire, host, seed);
        }

        /// <summary>
        /// Hands the link one number for the arena this device baked, so the handshake can
        /// refuse a device that is in a different one.
        ///
        /// Lazily, and every frame until it has one, because the baker does its work in
        /// its own Awake and this runs in the same pass: asking once, early, is asking
        /// before there is an arena to ask about. Once set it is left alone -- a rebake
        /// mid-match would change the number under a link that is already agreed, and a
        /// mid-match rebake is a much bigger problem than this check.
        /// </summary>
        void TellTheLinkWhichArena()
        {
            if (link.WorldFingerprint != 0u) return;

            SimWorldBaker baker = match.worldBaker != null
                ? match.worldBaker : SimWorldBaker.Instance;
            SimWorld world = baker != null ? baker.World : null;
            if (world == null || world.Solids.Length == 0) return;

            link.WorldFingerprint = world.Fingerprint();
        }

        void OnDisable() => ReleaseClock();

        void Update()
        {
            if (link == null || match == null) return;

            TellTheLinkWhichArena();

            // Once this device has gone on without them the link may well be gone,
            // and the match is this one's alone to finish. Tearing the clock down here
            // would hand BOTH fighters back to their own accumulators mid-match.
            if (!_aloneNow && (link.Role == LinkRole.Off || !link.Connected))
            {
                // Dropped while this device was in the background: it is the one that
                // went away, so the match is lost rather than quietly continued.
                // Otherwise backgrounding is a free way not to lose.
                if (_wasPaused && _holding) { Concede(); return; }

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

                TickInput theirs;
                if (_aloneNow)
                {
                    // NOBODY TAKES THEIR FIGHTER. It used to be handed to its own brain
                    // here, and that is gone: a player who stops answering stops moving,
                    // and the match plays on without them. They lose it on the clock, or
                    // to the poison, or to whoever is still standing -- which is the
                    // honest outcome and is theirs to own rather than this device's to
                    // paper over with an opponent nobody asked for.
                    //
                    // Empty input rather than their last one, because a held direction
                    // would walk a vacated body off a ledge and read as somebody playing.
                    theirs = default;
                }
                else if (!link.TryRemote(tick, out theirs))
                {
                    stalledFrames++;
                    _silent += Time.unscaledDeltaTime;

                    // Two patiences, and which one applies is whether this link has ever
                    // carried anything. Before first contact the opponent is still arriving;
                    // after it, they have gone.
                    float patience = _everHeard
                        ? waitForThem
                        : Mathf.Max(waitForThem, firstContactSeconds);

                    if (_silent >= patience) { GoOnWithout(); return; }

                    // AND NOTHING IS SAID. There used to be a countdown on screen --
                    // OPPONENT NOT RESPONDING, 3, 2, 1 -- and it is the other player's
                    // connection, not this one's. All it does to the person who can
                    // still play is tell them to expect the match to go wrong.
                    state = $"tick {tick}: no answer for {_silent:0.0}s";

                    // A stall must not BANK the time it costs.
                    //
                    // This used to hold the accumulator, and the comment that said so
                    // read like thrift. It is the lurch: half a second of waiting for
                    // the other phone is thirty ticks owed, and they are repaid six to
                    // a frame as soon as the input arrives, so the match replays that
                    // half second at six times speed. The player who was waited FOR
                    // sees none of it, which is precisely the report -- one player is
                    // fine and the other lags.
                    //
                    // Lockstep's clock is the tick, not the wall, and both devices run
                    // the same ticks either way. So the wait is simply spent: a stall
                    // becomes a brief hold at normal speed instead of a hold followed
                    // by a fast-forward. One tick of slack is kept so an input that
                    // lands mid-frame is not pushed into the next one.
                    _accumulator = Mathf.Min(_accumulator,
                                             PlayerController.SecondsPerTick);
                    return;
                }

                // Input again, so they are back. Whatever gave up on them is undone:
                // a player who lagged and returned plays on, and the only thing they
                // owe is the time they were gone.
                _silent = 0f;
                _everHeard = true;
                if (_aloneNow) CameBack();

                _accumulator -= PlayerController.SecondsPerTick;
                ran++;

                // More than one tick in a frame is a catch-up, whatever caused it, so the
                // presentation is told to show the match as it IS rather than replay what
                // it missed. Refreshed every such frame; it lifts by itself. See
                // MatchState.ShowTheOpening.
                if (ran == 2 && match != null) match.SnapToNow();

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

            // Rebuilt only when it would READ differently. tick, delay and roundTripMs
            // each have a field of their own above, so the sentence does not repeat the
            // tick -- and a string built at 60 Hz to say what three ints already say is
            // about five kilobytes a second of garbage for a line nobody is reading while
            // they play. A desync message wins, and that one is rare by definition.
            string desync = link.Desync;
            if (desync != null)
            {
                state = desync;
            }
            else if (delay != _saidDelay || roundTripMs != _saidTrip
                     || link.LocalPlayer != _saidSide)
            {
                _saidDelay = delay;
                _saidTrip = roundTripMs;
                _saidSide = link.LocalPlayer;
                state = $"player {link.LocalPlayer + 1}, {delay} tick(s) of input budget "
                        + $"for {roundTripMs} ms";
            }
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
                //
                // takeOver, not enabled. FighterBrain.TakingOver is `takeOver &&
                // isActiveAndEnabled && self != null`, so switching the COMPONENT off is
                // one of three ways to stop it and the wrong one: docs/22 makes enabled
                // the tick box a person uses and takeOver the thing that hands a body to
                // the computer, and a tool that switches the component off leaves the
                // scene looking as though somebody did that on purpose.
                FighterBrain brain = f.GetComponentInChildren<FighterBrain>(true);
                if (brain != null) brain.takeOver = false;
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

        /// <summary>
        /// Stop waiting for them, and play on.
        ///
        /// NOBODY TAKES THEIR FIGHTER. This used to hand the body to its own brain and
        /// drop the link for good; now it does neither. A player who stops answering
        /// stops moving, and the match carries on around them until the clock, the
        /// poison or the other fighter settles it. That is the honest outcome, and it
        /// is theirs to own rather than this device's to paper over with an opponent
        /// nobody asked for.
        ///
        /// AND THE LINK IS LEFT OPEN, which is the other half. Dropping it made this a
        /// one-way door: a player whose phone hiccupped for three seconds came back to
        /// a socket that had been closed on them. Left open, the next packet that
        /// arrives is picked up by CameBack below and they simply resume.
        /// </summary>
        void GoOnWithout()
        {
            if (_aloneNow) return;
            _aloneNow = true;
            _silent = 0f;

            state = "the other player stopped answering; playing on without them";
            Debug.Log($"{name}: no input from fighter "
                      + ((link != null ? link.RemotePlayer : 1) + 1)
                      + $" for {waitForThem:0.0}s, so the match goes on without them. "
                      + "Nothing has taken their fighter and the link is still open, so "
                      + "they resume if they come back.", this);
        }

        /// <summary>
        /// They answered again, so they are back. See <see cref="GoOnWithout"/>.
        ///
        /// Everything that gave up on them is undone, and the ONE thing that must not
        /// happen here is a replay of what they missed: the ticks they were gone are
        /// spent, not owed. MatchState.SnapToNow does that half.
        /// </summary>
        void CameBack()
        {
            _aloneNow = false;
            state = "the other player is back";
            if (match != null) match.SnapToNow();
            Debug.Log($"{name}: fighter "
                      + ((link != null ? link.RemotePlayer : 1) + 1)
                      + " is answering again; the match carries on from where it is.",
                      this);
        }

        /// <summary>
        /// This device went away and the link died with it, so the match is lost.
        ///
        /// The other end has already given this fighter to a bot and is playing on.
        /// Recording a loss here is what keeps the two stories the same, and what
        /// stops stepping out of the room being a way to avoid a result.
        /// </summary>
        void Concede()
        {
            _wasPaused = false;
            ReleaseClock();
            state = "left the match while it was running";

            if (match != null && link != null) match.Concede(link.LocalPlayer);
        }

        /// <summary>
        /// A phone does not close an app, it stops it -- and a stopped app sends no
        /// input, which freezes the other player until their own clock gives up on
        /// this one. Remembered so that a link dying afterwards is read as what it
        /// is: this device left.
        /// </summary>
        void OnApplicationPause(bool paused)
        {
            if (paused && _holding) _wasPaused = true;
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
        // ONE SCREEN, ONE CAMERA USED TO LIVE HERE, in a OneCamera method, and it
        // was right about what to do and wrong about when. It ran from the point this
        // device's LINK took the clock, so a match against a bot -- which is most of a
        // player's first games -- never called it, and the arena kept its authored
        // split-screen rects: two letterboxed strips on a phone. The partner call then
        // put the split back on release, so this and LocalCamera took turns undoing each
        // other, and LocalCamera never noticed because it only wrote when its own inputs
        // had changed.
        //
        // LocalCamera owns it now, on the Cameras object of every arena, and asserts it
        // every frame from MatchState.LocalFighter -- which is the same answer BeMe below
        // sets, so there is one answer and one writer.

        void BeMe(int player)
        {
            PlayerController mine = At(player);

            // This player's OWN camera, found by the fighter it already follows.
            //
            // The arena carries one camera per fighter for a match played on one phone,
            // and Awake takes whichever FindFirstObjectByType hands back when the field
            // is left empty -- an order Unity does not define. Both devices then run the
            // SAME arbitrary camera and point it at their own fighter, which works right
            // up until it does not: the two cameras are not interchangeable, they carry
            // their own framing, shake and opening shot, and the one that is left is the
            // one LocalCamera keeps. Choosing by the fighter makes it this player's shot on
            // both phones, whatever the field was left at.
            if (mine != null)
            {
                foreach (SideViewCamera cam in FindObjectsByType<SideViewCamera>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (cam.player != mine) continue;
                    view = cam;
                    break;
                }
            }

            if (view != null && mine != null)
            {
                view.player = mine;
                view.target = mine.transform;
            }
            // On the match itself first, because that is where everything else now reads
            // it from -- the three fields below are the same answer kept three more times,
            // and they are set as well only so a HUD with Follow The Device switched off
            // still shows the right side.
            if (match != null) match.SetLocalFighter(player);

            if (hud != null) hud.localPlayer = player;
            if (result != null)
            {
                result.localPlayer = player;
                result.someoneElseRestarts = true;     // Again asks; the clock schedules
            }
            if (recorder == null) return;

            recorder.localPlayer = player;

            // The opponent is a person now, not the practice bot -- and this match still
            // must not move the ladder. What the recorder does NOT do is ask the other
            // phone what its rating is: on a LAN that is a number the other device can
            // simply make up, and a ladder that believes it is not a ladder. Until there
            // is a server to ask, a LAN match is played for its own sake (docs/09).
            //
            // Off means an unranked match, which is what this is. A LAN game is paired
            // here rather than through MatchSearch, so nothing was handed over and this
            // switch is what decides -- see MatchRecorder, where a handover would take
            // precedence if one ever arrived on this path.
            recorder.practiceCountsOnTheLadder = false;
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
            _silent = 0f;
            _everHeard = false;
            _aloneNow = false;
            _wasPaused = false;
            _controls = null;
            if (result != null) result.someoneElseRestarts = false;
            if (match == null) return;
            for (int i = 0; i < match.fighters.Count; i++)
            {
                PlayerController f = match.fighters[i];
                if (f == null) continue;
                f.ExternallyClocked = false;

                // Back to the authored state, which BrainSetup writes and docs/22
                // describes: the component on and present, takeOver off until somebody
                // or something hands that body to the computer. Leaving takeOver set
                // here would mean a disconnect turned the local player's own fighter
                // into a bot the moment the link dropped.
                FighterBrain brain = f.GetComponentInChildren<FighterBrain>(true);
                if (brain != null) { brain.enabled = true; brain.takeOver = false; }
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
