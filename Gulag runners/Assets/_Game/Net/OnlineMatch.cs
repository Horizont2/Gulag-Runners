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
    /// It outlives the scene, because the pairing happens in the menu and is spent in
    /// the arena, and a relay opened in one must still be open in the other. That makes
    /// it the only object in the project that survives a load -- worth knowing, and
    /// worth it being exactly one.
    ///
    /// It is also the search screen's gate. Opening a relay takes a second or two of
    /// real network, and that wait is what MatchSearch's Loading and Waiting phases are
    /// for: the bar is honest because it is held by something actually happening.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OnlineMatch : MonoBehaviour, IMatchGate
    {
        /// <summary>The one in flight, for the arena to find after the load.</summary>
        public static OnlineMatch Live { get; private set; }

        [Header("Wiring")]
        [Tooltip("The search screen. Wire its Paired event to Begin, or leave this and " +
                 "it is found in the scene.")]
        public MatchSearch search;

        [Header("Debug")]
        public string state = "idle";
        public int localPlayer;
        public bool relayOpen;

        RelayTransport _wire;
        CancellationTokenSource _cancel;
        MatchFound _pairing;
        bool _have;

        /// <summary>
        /// MatchSearch holds its Loading phase on this. False until the relay is up,
        /// so the screen waits for the network rather than for a guessed delay.
        /// </summary>
        public bool OpponentReady => _have && (_pairing.AgainstBot || relayOpen);

        void Awake()
        {
            if (Live != null && Live != this) { Destroy(gameObject); return; }
            Live = this;
            DontDestroyOnLoad(gameObject);

            if (search == null) search = FindFirstObjectByType<MatchSearch>();
        }

        void OnDestroy()
        {
            if (Live == this) Live = null;
            Drop();
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

            relayOpen = true;
            Say(opened.Said);
            Ready?.Invoke();
        }

        /// <summary>
        /// Hands the arena its match. Called by NetMatch when the scene is up, because
        /// the arena is the thing that knows when it is ready to be told.
        /// </summary>
        public bool TryTake(out INetTransport wire, out bool host, out uint seed,
                            out int player)
        {
            wire = _wire;
            host = _pairing.LocalPlayer == 0;
            seed = _pairing.Seed;
            player = _pairing.LocalPlayer;
            return _have && relayOpen && _wire != null;
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

            if (_wire != null) { _wire.Close(); _wire = null; }
            relayOpen = false;
            _have = false;
            Say("idle");
        }

        void Say(string said)
        {
            state = said;
        }
    }
}
