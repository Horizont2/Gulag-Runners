using System.Threading;
using UnityEngine;
using UnityEngine.Events;
using GulagRunners.Game;
using GulagRunners.Meta;

namespace GulagRunners.Net
{
    /// <summary>
    /// The last link: from "you two are matched" in the menu to two fighters swinging
    /// at each other in the arena.
    ///
    /// Everything either side of it already existed and neither knew about the other.
    /// <see cref="MatchSearch"/> found an opponent and had nowhere to put them;
    /// <see cref="LanLink"/> could run a match and had no idea one had been arranged.
    /// This is the piece in between, and the reason it is small is that both halves
    /// were built to be met.
    ///
    /// The pairing outlives the scene, because it happens in the menu and is spent in
    /// the arena, and a relay opened in one must still be open in the other. The
    /// COMPONENT does not: it is held in statics below, and this object dies with the
    /// menu like everything else there.
    ///
    /// That distinction was learnt the hard way. DontDestroyOnLoad was the obvious
    /// answer and it is wrong here, because this component sits on the search screen --
    /// and DontDestroyOnLoad keeps the whole GameObject, children and all. The arena
    /// came up with the entire search UI floating over it, still saying "waiting to be
    /// let through". Moving three fields is cheaper than moving a scene.
    ///
    /// It is also the search screen's gate. Opening a relay takes a second or two of
    /// real network, and that wait is what MatchSearch's Loading and Waiting phases are
    /// for: the bar is honest because it is held by something actually happening.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OnlineMatch : MonoBehaviour, IMatchGate
    {
        /// <summary>The one in flight, while the menu that owns it is still loaded.</summary>
        public static OnlineMatch Live { get; private set; }

        // What crosses the scene load. Statics rather than a surviving object, and
        // cleared on entering play because Unity can be told not to reload the domain,
        // and then yesterday's match would still be here.
        static RelayTransport _carried;
        static MatchFound _carriedPairing;
        static bool _carriedHave;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Forget()
        {
            _carried = null;
            _carriedPairing = default;
            _carriedHave = false;
        }

        [Header("Wiring")]
        [Tooltip("The search screen. Wire its Paired event to Begin, or leave this and " +
                 "it is found in the scene.")]
        public MatchSearch search;

        [Header("Debug")]
        public string state = "idle";
        public int localPlayer;
        public bool relayOpen;
        public bool weAreLoaded;
        public bool theyAreLoaded;

        [Header("Rules")]
        [Tooltip("Seconds to wait for the other phone to say its arena is up before " +
                 "going in anyway. 0 waits for ever, which is a hang and not a " +
                 "safety feature: one lost datagram and both players sit on the " +
                 "search screen with an opponent found and nothing happening.\n\n" +
                 "Going early costs little. Lockstep will not run a tick until both " +
                 "inputs are in, so whoever arrives first waits in the arena instead " +
                 "of in the menu.")]
        [Range(0f, 30f)] public float readyTimeout = 6f;

        RelayTransport _wire;
        readonly byte[] _loaded = { PacketLoaded };
        float _nextSay;
        float _loadedAt;
        CancellationTokenSource _cancel;
        MatchFound _pairing;
        bool _have;

        /// <summary>
        /// The byte that says "my arena is up and I am holding at the gate". Nine,
        /// because LanLink's protocol uses one to eight and ignores anything else --
        /// so one of these arriving late, after the arena has adopted the wire, costs
        /// a switch statement's default branch and nothing more.
        /// </summary>
        const byte PacketLoaded = 9;

        /// <summary>
        /// MatchSearch holds its Loading phase on this, and what it has to mean is
        /// "the other phone is standing at the gate too".
        ///
        /// It used to mean "the relay is open", which is true a second or two before
        /// the other device has finished loading, and that gap is the whole of the
        /// complaint that one player is fine and the other lags. The faster phone let
        /// itself into the arena and started its clock; lockstep then refused to run a
        /// tick until the slower phone's input arrived, so the fast one banked every
        /// frame of the wait and replayed it at six ticks a frame the moment it could.
        /// One player saw a normal match begin. The other saw it begin at six times
        /// speed and then settle, and nothing about that says "your opponent was still
        /// loading".
        ///
        /// So each side says when it is loaded, and neither goes through until both
        /// have. Against a bot there is nobody to wait for and this is true at once.
        /// </summary>
        public bool OpponentReady =>
            _have && (_pairing.AgainstBot || theyAreLoaded || GaveUpWaiting);

        /// <summary>
        /// Why there is no match, or empty while there still might be one.
        ///
        /// OpponentReady used to answer TRUE when the relay was not open — the term read
        /// `|| !relayOpen`, meaning "nobody to wait for". That is true of a practice
        /// opponent, which the line above already covers, and catastrophically false of a
        /// relay that FAILED: a handshake that threw, a dashboard with Relay switched
        /// off, two devices that both believed they were the host and so both allocated.
        /// The gate opened instantly, the arena loaded, and the player was put alone in
        /// it with the other fighter standing still — believing they were online, because
        /// every screen up to that point had said so.
        ///
        /// A failure has to be a failure. The search screen reads this and says it, and
        /// nothing goes through the gate on a pairing that did not happen.
        /// </summary>
        public string Broken { get; private set; } = "";

        /// <summary>
        /// We have been standing at the gate long enough that waiting is now the worse
        /// of the two mistakes.
        ///
        /// A gate with no way past it is not a safety feature, it is a hang, and that
        /// is what the first version of this was: lose one byte on a datagram relay and
        /// both players sit on the search screen for ever with an opponent found. The
        /// cost of going early is measured and small -- lockstep will not run a tick
        /// until both inputs are in, so the device that arrives first waits in the
        /// arena instead of in the menu, and NetMatch no longer banks that wait. The
        /// cost of not going is the match.
        /// </summary>
        /// And only ever with a relay OPEN. Going early is cheap when the other device is
        /// at the far end of a link that works and one byte went missing; it is the whole
        /// fault above when there is no link at all.
        public bool GaveUpWaiting =>
            weAreLoaded && relayOpen && readyTimeout > 0f &&
            Time.unscaledTime - _loadedAt > readyTimeout;

        void Awake()
        {
            if (Live != null && Live != this) { Destroy(this); return; }
            Live = this;

            if (search == null) search = FindFirstObjectByType<MatchSearch>();
        }

        /// <summary>
        /// The menu unloading is NOT the match ending -- it is the moment before the
        /// arena needs it. So this lets go of the component and keeps the relay.
        /// Drop is for a player who left, and is called by hand.
        /// </summary>
        void OnDestroy()
        {
            if (Live == this) Live = null;
        }

        /// <summary>
        /// Keeps the relay alive and the two sides talking while the arena loads.
        ///
        /// Nothing used to pump it here. The relay was opened in the menu and then left
        /// untouched until LanLink adopted it in the arena, which for a transport that
        /// has to be driven once a frame means several seconds of a connection nobody
        /// is reading -- and no way for the two devices to say anything to each other
        /// during exactly the wait that needed saying something.
        /// </summary>
        void Update()
        {
            if (_wire == null) return;
            _wire.Pump();

            if (!weAreLoaded) return;

            // KEEP SAYING IT, even after they have answered. This stopped at
            // `theyAreLoaded` and that one word is why a match connected on one phone
            // and hung on the other: whichever device loaded SECOND received the
            // first one's announcement, set theyAreLoaded, and then returned here
            // before ever sending its own. The first device waited for a packet that
            // was now never going to be sent. Exactly one player got in.
            //
            // Announcing is one byte. There is no version of this worth saving it on.
            if (Time.unscaledTime < _nextSay) return;
            _nextSay = Time.unscaledTime + (theyAreLoaded ? 1f : 0.25f);
            _wire.Send(_loaded, 1);
        }

        /// <summary>
        /// Said by the search screen when this device's arena is up and holding at the
        /// gate. MenuDirector has loaded it and is waiting to be released.
        /// </summary>
        public void WeHaveLoaded()
        {
            if (weAreLoaded) return;
            weAreLoaded = true;
            _loadedAt = Time.unscaledTime;
            _nextSay = 0f;
        }

        /// <summary>How long this side has been holding at the gate, for the screen.</summary>
        public float WaitedFor =>
            weAreLoaded ? Time.unscaledTime - _loadedAt : 0f;

        void Heard(byte[] data, int length)
        {
            if (length > 0 && data[0] == PacketLoaded) theyAreLoaded = true;
        }

        /// <summary>
        /// Wire MatchSearch's Paired event here. Opens the relay while the arena loads.
        /// </summary>
        public void Begin()
        {
            Drop();

            if (search == null) { Fail("no search screen"); return; }

            _pairing = search.Opponent;
            _have = true;
            localPlayer = _pairing.LocalPlayer;

            // A practice opponent is not on the other side of a relay. Nothing to open,
            // nothing to wait for, and the arena runs on its own clock exactly as it
            // has all along.
            if (_pairing.AgainstBot) { Say("practice opponent, no relay needed"); return; }

            if (UgsQueue.Current == null)
            {
                Fail("MATCHED, BUT THE SESSION IS GONE");
                return;
            }

            _cancel = new CancellationTokenSource();
            Say("opening the relay…");
            _ = Open(_cancel.Token);
        }

        async System.Threading.Tasks.Task Open(CancellationToken stop)
        {
            // Whoever drives fighter one allocates. Both devices reached that from the
            // player ids alone, so exactly one allocates and exactly one waits -- two
            // allocators would make two relays and meet in neither.
            bool host = _pairing.LocalPlayer == 0;

            RelayHandshake.Opened opened =
                await RelayHandshake.Open(UgsQueue.Current, host, stop);

            if (stop.IsCancellationRequested) return;

            if (!opened.Ok) { Fail($"COULD NOT REACH THE OTHER PLAYER: {opened.Said}"); return; }

            _wire = new RelayTransport(opened.Relay, host);
            if (!_wire.Open()) { Fail($"RELAY: {_wire.Describe}"); _wire = null; return; }

            // Listen before announcing: the other device may be loaded already, and a
            // "loaded" that arrives between opening the wire and subscribing to it is
            // one both sides then wait for forever.
            _wire.Received += Heard;

            relayOpen = true;
            _carried = _wire;
            _carriedPairing = _pairing;
            _carriedHave = true;

            Say(opened.Said);
            Ready?.Invoke();
        }

        /// <summary>
        /// Hands the arena its match. Called by NetMatch when the scene is up, because
        /// the arena is the thing that knows when it is ready to be told.
        /// </summary>
        public static bool TryTake(out INetTransport wire, out bool host, out uint seed,
                                    out int player)
        {
            wire = _carried;
            host = _carriedPairing.LocalPlayer == 0;
            seed = _carriedPairing.Seed;
            player = _carriedPairing.LocalPlayer;
            return _carriedHave && _carried != null;
        }

        /// <summary>Fired once the relay is up and the match can start.</summary>
        public UnityEvent Ready;

        /// <summary>The match is over, or the player left. Lets the relay go.</summary>
        public void Drop()
        {
            if (_cancel != null)
            {
                _cancel.Cancel();
                _cancel.Dispose();
                _cancel = null;
            }

            if (_wire != null) { _wire.Received -= Heard; _wire.Close(); _wire = null; }
            if (_carried != null) { _carried.Close(); }
            Forget();
            Broken = "";
            relayOpen = false;
            weAreLoaded = false;
            theyAreLoaded = false;
            _loadedAt = 0f;
            _have = false;
            Say("idle");
        }

        void Say(string said)
        {
            state = said;
        }

        /// <summary>
        /// The pairing is not going to happen, and somebody has to be told.
        ///
        /// Logged as a warning as well as shown, because the shown line has one short
        /// line of screen to live in and the reason is usually a dashboard setting.
        /// </summary>
        void Fail(string why)
        {
            Broken = why;
            Say(why);
            Debug.LogWarning($"{name}: {why}. Relay and Matchmaker both have to be "
                             + "enabled on the Unity dashboard, and both devices have to "
                             + "be signed in and on the same build.", this);
        }
    }
}
