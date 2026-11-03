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

        RelayTransport _wire;
        readonly byte[] _loaded = { PacketLoaded };
        float _nextSay;
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
            _have && (_pairing.AgainstBot || (relayOpen && theyAreLoaded));

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

            // Datagrams, and the one that matters is a single byte sent once. So it is
            // sent again four times a second until the other side's answer comes back,
            // which costs nothing and removes the case where the whole match waits on
            // one lost packet.
            if (!weAreLoaded || theyAreLoaded) return;
            if (Time.unscaledTime < _nextSay) return;
            _nextSay = Time.unscaledTime + 0.25f;
            _wire.Send(_loaded, 1);
        }

        /// <summary>
        /// Said by the search screen when this device's arena is up and holding at the
        /// gate. MenuDirector has loaded it and is waiting to be released.
        /// </summary>
        public void WeHaveLoaded()
        {
            weAreLoaded = true;
            _nextSay = 0f;
        }

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

            if (search == null) { Say("no search screen"); return; }

            _pairing = search.Opponent;
            _have = true;
            localPlayer = _pairing.LocalPlayer;

            // A practice opponent is not on the other side of a relay. Nothing to open,
            // nothing to wait for, and the arena runs on its own clock exactly as it
            // has all along.
            if (_pairing.AgainstBot) { Say("practice opponent, no relay needed"); return; }

            if (UgsQueue.Current == null)
            {
                Say("matched, but the session is gone");
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

            if (!opened.Ok) { Say($"relay failed: {opened.Said}"); return; }

            _wire = new RelayTransport(opened.Relay, host);
            if (!_wire.Open()) { Say($"relay: {_wire.Describe}"); _wire = null; return; }

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
            relayOpen = false;
            weAreLoaded = false;
            theyAreLoaded = false;
            _have = false;
            Say("idle");
        }

        void Say(string said)
        {
            state = said;
        }
    }
}
