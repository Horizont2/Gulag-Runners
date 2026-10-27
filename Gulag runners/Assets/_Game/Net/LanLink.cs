using System;
using System.Collections.Generic;
using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Net
{
    /// <summary>Which end of the wire this device is.</summary>
    public enum LinkRole { Off, Host, Guest }

    /// <summary>
    /// Two phones on one Wi-Fi, exchanging nothing but thumbs.
    ///
    /// The simulation is deterministic -- fixed point, no engine, no float, its own
    /// assembly with noEngineReferences -- and its entire input is a TickInput: three
    /// bytes of flags and two shorts. So there is nothing to replicate. Both devices run
    /// the same match and send each other six bytes a tick, which is why this is a UDP
    /// socket and not Netcode for GameObjects: NGO replicates STATE, and state is the one
    /// thing a deterministic simulation must never be told.
    ///
    /// Lockstep, not rollback. A tick may run when BOTH inputs for it have arrived, and
    /// until then the match waits. On a LAN that wait is a millisecond or two; over the
    /// internet it would be visible and rollback would be the answer, which is the work
    /// this leaves room for rather than does.
    ///
    /// Four things that make UDP survivable without inventing TCP:
    ///
    ///   redundancy   every packet carries the last <see cref="Carried"/> inputs, not
    ///                just the newest. A dropped packet is covered by the next one, so
    ///                there is nothing to acknowledge and nothing to retransmit.
    ///   a beacon     the host shouts on a broadcast port so the guest can find it
    ///                without anybody typing an IP address on a phone.
    ///   a seed       the host decides what the round is dealt from and hands it over in
    ///                the handshake, so both devices start the same match rather than two
    ///                that merely look alike.
    ///   a checksum   each side sends a hash of its own simulation every second. If a
    ///                deterministic simulation ever stops being deterministic, the two
    ///                matches drift apart in silence -- and silence is the one failure
    ///                this design cannot afford, so it is reported loudly instead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LanLink : MonoBehaviour
    {
        /// <summary>How many past inputs ride along in every packet.</summary>
        public const int Carried = 16;

        /// <summary>How many ticks of input are kept. A power of two, for the mask.</summary>
        const int Ring = 256;
        const int Mask = Ring - 1;

        const byte PacketHello = 1;      // guest -> host: let me in
        const byte PacketWelcome = 2;    // host -> guest: you are in, deal from this
        const byte PacketInput = 3;
        const byte PacketBeacon = 4;   // read by LanTransport, not here
        const byte PacketCheck = 5;
        const byte PacketRematch = 6;    // a new match, on an agreed tick
        const byte PacketPing = 7;       // what time do you make it
        const byte PacketPong = 8;       // the same number, straight back

        [Header("Ports")]
        [Tooltip("Where the match itself talks. Any free high port; both devices must " +
                 "agree, and they do because this is the same build.")]
        public int matchPort = 47801;

        [Tooltip("Where the host shouts so a guest can find it without typing an IP.")]
        public int beaconPort = 47802;

        [Header("Lockstep")]
        [Tooltip("How many ticks ahead of the simulation the input is sent.\n\n" +
                 "This is the whole latency budget. A tick is 16.7 ms, so 4 covers a " +
                 "67 ms round trip -- comfortable on a Wi-Fi, tight over anything else. " +
                 "Raising it hides jitter and makes your own controls feel further away, " +
                 "because your thumb now takes this many ticks to reach your own fighter " +
                 "too: lockstep delays BOTH players equally, which is what keeps the two " +
                 "simulations identical.")]
        [Range(2, 20)] public int inputDelay = 4;

        [Tooltip("Set the delay from the round trip actually measured, instead of " +
                 "holding the number above.\n\n" +
                 "On a LAN the two are the same and this changes nothing. Over anything " +
                 "else it is the difference between a game and a slideshow: lockstep " +
                 "waits for the other player, so a budget smaller than the round trip " +
                 "means waiting on EVERY tick. Four ticks is 67 ms, which a Wi-Fi " +
                 "clears and the internet does not.\n\n" +
                 "Costs what it buys. The delay is applied to your own thumb as well as " +
                 "theirs -- that is what keeps the two simulations identical -- so a " +
                 "slow link feels slow rather than stuttering. Rollback is how you stop " +
                 "paying it, and this is not rollback.")]
        public bool measureDelay = true;

        [Tooltip("The most the measurement may ask for. Past about 12 ticks (200 ms) " +
                 "the controls are further away than most people will put up with, and " +
                 "the honest answer is that the link is too slow for lockstep.")]
        [Range(4, 20)] public int maxDelay = 12;

        [Tooltip("Ticks between simulation checksums. 0 never checks, which is only " +
                 "right if you enjoy silent desyncs.")]
        [Range(0, 600)] public int checkEvery = 60;

        [Tooltip("Ticks of warning before a rematch takes effect. Half a second, so the " +
                 "packet saying so has time to arrive and be re-sent if it does not.\n\n" +
                 "A rematch cannot simply happen when the button is tapped: the two " +
                 "devices would re-deal the arena on two different ticks, which is a " +
                 "desync with a button on it. It is scheduled instead, and both devices " +
                 "do it on the same tick as they do everything else.")]
        [Range(10, 180)] public int rematchLead = 30;

        [Header("Debug")]
        public string state = "off";
        public int sent;
        public int received;
        public int stalls;

        /// <summary>Which end this is, and therefore which fighter it drives.</summary>
        public LinkRole Role => _role;

        /// <summary>0 for the host, 1 for the guest. The other one is the remote.</summary>
        public int LocalPlayer => _role == LinkRole.Guest ? 1 : 0;
        public int RemotePlayer => 1 - LocalPlayer;

        /// <summary>True once both ends have said hello.</summary>
        public bool Connected => _connected;

        /// <summary>
        /// The number this match is dealt from. The host picks it and the guest is told,
        /// because two devices that deal their own loot are playing two different arenas
        /// -- which is a desync nobody would read as one, since both look perfectly fine
        /// on their own screen.
        /// </summary>
        public uint MatchSeed => _seed;

        /// <summary>Set when the two simulations stop agreeing. Nothing clears it.</summary>
        public string Desync => _desync;

        /// <summary>
        /// The tick the match is on. The clock sets it; the link needs it only to put a
        /// rematch a little way into the future rather than right now.
        /// </summary>
        public int Now { get; set; }

        /// <summary>The worst round trip seen lately, in milliseconds. 0 until measured.</summary>
        public int RoundTrip => _rtt;

        /// <summary>
        /// How many ticks ahead input should be sent, from the round trip actually
        /// measured. Falls back to the inspector's number while nothing is known.
        ///
        /// The two devices need not agree on this, which is worth saying because it
        /// looks like they must: every packet carries the ABSOLUTE tick its input
        /// belongs to, so a device that sends further ahead is simply a device that
        /// stalls less. Nothing about the simulation depends on it.
        /// </summary>
        public int SuggestedDelay
        {
            get
            {
                if (!measureDelay || _rtt <= 0) return inputDelay;

                // One tick is 16.67 ms. Half the round trip is the one-way wait, and the
                // input has to arrive BEFORE the tick it belongs to -- so the budget is
                // the one-way trip plus a tick of slack for a frame landing badly.
                int ticks = (_rtt * 3 / 100) + 2;
                return Mathf.Clamp(ticks, inputDelay, Mathf.Max(inputDelay, maxDelay));
            }
        }

        /// <summary>How many hosts have been heard from. Safe to read from a frame.</summary>
        public int FoundCount => _wire != null ? _wire.FoundCount : 0;

        /// <summary>The first host heard from, if any. Safe to read from a frame.</summary>
        public bool TryFound(out string address)
        {
            address = null;
            return _wire != null && _wire.TryFound(out address);
        }

        // Each slot says which tick it holds. The ring is 256 ticks -- 4.3 seconds -- and
        // a match is minutes, so every slot is reused many times over. A plain "have I got
        // this one" flag would read as true for ever after the first lap and freeze both
        // sides on inputs four seconds stale, which is exactly what it did before the tick
        // number was stored beside the input.
        readonly TickInput[] _mine = new TickInput[Ring];
        readonly int[] _mineAt = new int[Ring];
        readonly TickInput[] _theirs = new TickInput[Ring];
        readonly int[] _theirsAt = new int[Ring];
        readonly Dictionary<int, uint> _theirChecks = new Dictionary<int, uint>();
        readonly Dictionary<int, uint> _myChecks = new Dictionary<int, uint>();

        readonly object _gate = new object();

        LanTransport _wire;
        volatile LinkRole _role = LinkRole.Off;
        volatile bool _connected;
        volatile string _desync;
        volatile bool _desyncReported;
        uint _seed;
        volatile int _rematchAt = -1;
        volatile int _rematchDone = -1;
        uint _rematchSeed;
        volatile int _rtt;
        readonly int[] _trips = new int[8];
        int _tripAt;
        float _nextBeacon;
        float _nextPing;
        float _nextHello;
        float _askedAt;

        // ----------------------------------------------------------------- opening up

        /// <summary>Wait here for somebody to join. This device is player 0.</summary>
        public void Host()
        {
            Stop();
            _role = LinkRole.Host;

            // The host owns the deal. Any number will do as long as it is not the same one
            // every match, or every match plays out in the same arena.
            _seed = unchecked((uint)DateTime.UtcNow.Ticks) * 2654435761u + 1u;

            // Learning the peer is the host's half of a LAN handshake: it cannot know who
            // will join, so the first datagram names them.
            if (!Wire().Open(learnPeer: true)) { _role = LinkRole.Off; return; }
            state = $"hosting on {LanTransport.LocalAddress()}:{matchPort}";
        }

        /// <summary>Join a host by address. This device is player 1.</summary>
        public void Join(string address)
        {
            Stop();
            _role = LinkRole.Guest;

            // Not learning: a guest knows who it is talking to, and a stray packet from
            // anything else on the network must not turn the match around.
            if (!Wire().Open(learnPeer: false)) { _role = LinkRole.Off; return; }
            if (!_wire.PointAt(address))
            {
                state = _wire.Describe;
                Stop();
                return;
            }

            _askedAt = Time.unscaledTime;
            _nextHello = 0f;                 // Update sends the first one immediately
            state = $"joining {address}:{matchPort}";
        }

        /// <summary>Listen for hosts shouting, so nobody has to type an address.</summary>
        public void Discover()
        {
            Wire().Discover();
            state = _wire.Describe;
        }

        public void Stop()
        {
            _connected = false;
            _role = LinkRole.Off;
            _rematchAt = -1;
            _rematchDone = -1;
            Now = 0;

            if (_wire != null)
            {
                _wire.Received -= Take;
                _wire.Close();
                _wire = null;
            }

            lock (_gate)
            {
                for (int i = 0; i < Ring; i++) { _mineAt[i] = -1; _theirsAt[i] = -1; }
                _theirChecks.Clear();
                _myChecks.Clear();

                // The next peer is a different link. Carrying a LAN's two milliseconds
                // into a connection across the country would set the budget from the
                // wrong wire and stall every tick until the samples aged out.
                Array.Clear(_trips, 0, _trips.Length);
                _tripAt = 0;
                _rtt = 0;
            }
            state = "off";
        }

        /// <summary>
        /// The wire this match runs on. One today; the seam is INetTransport, and a
        /// relay slots in here without the protocol below noticing.
        /// </summary>
        LanTransport Wire()
        {
            if (_wire == null)
            {
                _wire = new LanTransport(matchPort, beaconPort);
                _wire.Received += Take;
            }
            return _wire;
        }

        void Awake()
        {
            lock (_gate)
                for (int i = 0; i < Ring; i++) { _mineAt[i] = -1; _theirsAt[i] = -1; }
        }

        void OnDestroy() => Stop();
        void OnApplicationQuit() => Stop();

        // ------------------------------------------------------------------ the thumbs

        /// <summary>Hands over this device's input for a tick, and sends it.</summary>
        public void SendLocal(int tick, TickInput input)
        {
            if (tick < 0) return;

            // FROZEN ONCE SENT. While the match is stalled waiting for the other player,
            // this is called again every frame for the same tick -- and the thumb has
            // moved since. Letting the newer sample overwrite it means the peer can have
            // simulated one version of that tick while this device later simulates
            // another, which is a desync caused by the very code meant to prevent one.
            lock (_gate)
                if (_mineAt[tick & Mask] != tick)
                {
                    _mine[tick & Mask] = input;
                    _mineAt[tick & Mask] = tick;
                }

            if (_wire == null || !_wire.Ready) return;

            // The newest input and the ones before it, so a lost packet costs nothing.
            byte[] packet = new byte[2 + 4 + Carried * 8];
            int at = 0;
            packet[at++] = PacketInput;
            packet[at++] = (byte)Carried;
            Write(packet, ref at, tick);

            lock (_gate)
                for (int i = 0; i < Carried; i++)
                {
                    int t = tick - i;
                    int slot = t & Mask;
                    TickInput one = t >= 0 && _mineAt[slot] == t ? _mine[slot] : default;
                    Write(packet, ref at, (int)one.Flags);
                    Write(packet, ref at, one.AimX);
                    Write(packet, ref at, one.AimY);
                }

            _wire.Send(packet, at);
            sent++;
        }

        /// <summary>The other player's input for a tick, if it has arrived.</summary>
        public bool TryRemote(int tick, out TickInput input)
        {
            if (tick < 0) { input = default; return true; }
            lock (_gate)
            {
                bool got = _theirsAt[tick & Mask] == tick;
                input = got ? _theirs[tick & Mask] : default;
                return got;
            }
        }

        /// <summary>This device's own input for a tick, as it was when it was sent.</summary>
        public TickInput Local(int tick)
        {
            if (tick < 0) return default;
            lock (_gate)
                return _mineAt[tick & Mask] == tick ? _mine[tick & Mask] : default;
        }

        // ------------------------------------------------------------- the round trip

        /// <summary>
        /// How long a packet takes to get there and back, measured rather than assumed.
        ///
        /// The WORST of the last few, not the average. Lockstep does not care what the
        /// link usually does: one late input stalls the tick and everybody sees it, so
        /// the budget has to cover the bad trips, and smoothing them away is smoothing
        /// away the only samples that matter.
        /// </summary>
        void Ping()
        {
            if (_wire == null || !_wire.Ready) return;

            byte[] packet = new byte[1 + 4];
            int at = 0;
            packet[at++] = PacketPing;
            Write(packet, ref at, Stamp());
            _wire.Send(packet, at);
        }

        void TakePong(byte[] data)
        {
            if (data.Length < 5) return;
            int at = 1;
            int sent = ReadInt(data, ref at);
            int trip = Stamp() - sent;

            // The clock wraps every 24 days, and a reading from before a wrap is not a
            // round trip, it is arithmetic. Nothing sensible is over ten seconds either.
            if (trip < 0 || trip > 10000) return;

            lock (_gate)
            {
                _trips[_tripAt] = trip;
                _tripAt = (_tripAt + 1) % _trips.Length;

                int worst = 0;
                for (int i = 0; i < _trips.Length; i++)
                    if (_trips[i] > worst) worst = _trips[i];
                _rtt = worst;
            }
        }

        /// <summary>Milliseconds since midnight-ish. Only differences are ever read.</summary>
        static int Stamp() => (int)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond
                                    & 0x3FFFFFFF);

        // ---------------------------------------------------------------- the rematch

        /// <summary>
        /// "Again", from either phone.
        ///
        /// The host decides when, because somebody has to and two devices each deciding
        /// for themselves is how a rematch button becomes a desync. A guest's tap is a
        /// request; the host turns it into a tick that both devices will pass through.
        /// </summary>
        public void AskRematch()
        {
            if (_role == LinkRole.Host) ScheduleRematch();
            else SendRematch(-1, 0u);
        }

        void ScheduleRematch()
        {
            if (_rematchAt >= 0) return;             // one is already on its way
            _rematchSeed = unchecked((uint)DateTime.UtcNow.Ticks) * 2654435761u + 1u;
            _rematchAt = Now + Mathf.Max(10, rematchLead);
            SendRematch(_rematchAt, _rematchSeed);
        }

        /// <summary>True on the first tick at or past the agreed one, once.</summary>
        public bool TryRematch(int tick, out uint dealtFrom)
        {
            dealtFrom = _rematchSeed;
            int at = _rematchAt;
            if (at < 0 || tick < at) return false;
            _rematchAt = -1;
            _rematchDone = at;
            return true;
        }

        void SendRematch(int at, uint dealtFrom)
        {
            if (_wire == null || !_wire.Ready) return;

            byte[] packet = new byte[1 + 4 + 4];
            int cursor = 0;
            packet[cursor++] = PacketRematch;
            Write(packet, ref cursor, at);
            Write(packet, ref cursor, unchecked((int)dealtFrom));
            _wire.Send(packet, cursor);
        }

        // ---------------------------------------------------------------- the checksum

        /// <summary>
        /// Says what this device thinks the match looks like, and complains if the other
        /// one disagrees. A deterministic simulation that stops being deterministic does
        /// it quietly: both players go on playing, each in their own match, each sure the
        /// other one is behaving oddly. Better to be told.
        /// </summary>
        public void Checkpoint(int tick, uint hash)
        {
            if (checkEvery <= 0) return;

            lock (_gate)
            {
                _myChecks[tick] = hash;
                Forget(_myChecks, tick);
                Compare(tick);
            }

            if (_wire == null || !_wire.Ready) return;

            byte[] packet = new byte[1 + 4 + 4];
            int at = 0;
            packet[at++] = PacketCheck;
            Write(packet, ref at, tick);
            Write(packet, ref at, unchecked((int)hash));
            _wire.Send(packet, at);
        }

        /// <summary>Holds the gate. Sets Desync; the main thread does the shouting.</summary>
        void Compare(int tick)
        {
            if (_desync != null) return;
            if (!_myChecks.TryGetValue(tick, out uint mine)) return;
            if (!_theirChecks.TryGetValue(tick, out uint theirs)) return;
            if (mine == theirs) return;

            _desync = $"the two matches stopped agreeing at tick {tick}: "
                      + $"this device has {mine:X8}, the other has {theirs:X8}";
        }

        /// <summary>Checkpoints older than a minute will never be matched up now.</summary>
        static void Forget(Dictionary<int, uint> checks, int now)
        {
            if (checks.Count < 64) return;
            List<int> stale = new List<int>();
            foreach (KeyValuePair<int, uint> pair in checks)
                if (pair.Key < now - 3600) stale.Add(pair.Key);
            for (int i = 0; i < stale.Count; i++) checks.Remove(stale[i]);
        }

        // -------------------------------------------------------------------- the wire

        void Update()
        {
            // A desync is found on whichever thread notices; it is said out loud here,
            // because Debug.LogError with a context object belongs on the main thread.
            if (_desync != null && !_desyncReported)
            {
                _desyncReported = true;
                Debug.LogError($"{name}: {_desync}\nEverything after this point is two "
                               + "different fights. Something in the simulation is not "
                               + "deterministic -- a float, a Time, a Random, or an "
                               + "iteration order that is not the same on both.", this);
            }

            if (_wire == null) return;

            // Whatever the wire needs doing for itself: nothing for a socket, and for
            // a relay driver it is everything that is not sending.
            _wire.Pump();

            // Both ends, whichever they are, and before the host's early return below.
            // The budget has to be measured from the device that will be WAITING, and
            // that is both of them.
            if (_connected && Time.unscaledTime >= _nextPing)
            {
                _nextPing = Time.unscaledTime + 0.25f;
                Ping();
            }

            if (_role == LinkRole.Host)
            {
                // Say it again until it fires. One lost packet here restarts this phone
                // and not the other one, which is two games from then on.
                if (_rematchAt >= 0) SendRematch(_rematchAt, _rematchSeed);

                // Shout, so a guest can find this device without typing anything.
                if (Time.unscaledTime < _nextBeacon) return;
                _nextBeacon = Time.unscaledTime + 1f;
                Shout();
                return;
            }

            if (_role == LinkRole.Guest && !_connected)
            {
                // Knock until the door opens. UDP loses the first packet often enough on a
                // phone waking its radio that a single hello is a coin toss.
                if (Time.unscaledTime < _nextHello) return;
                _nextHello = Time.unscaledTime + 0.5f;
                Say(PacketHello);

                if (Time.unscaledTime - _askedAt > 8f)
                    state = "no answer. Is the other phone hosting, and on this Wi-Fi?";
            }
        }

        void Shout() => _wire?.Shout();

        /// <summary>
        /// One datagram, from whatever is carrying them. MAY BE ON ANY THREAD: a socket
        /// has its own, and answering a ping from it is what makes the reading the wire
        /// rather than how busy this end's frame was. Everything it touches is behind
        /// the gate or volatile.
        /// </summary>
        void Take(byte[] data, int length)
        {
            if (data == null || length < 1) return;

            switch (data[0])
            {
                case PacketHello:
                    // Only a host answers a hello. A guest that answered one would
                    // start a pair of devices saying hello to each other for ever.
                    //
                    // Nobody records who said it: the transport knows where a datagram
                    // came from and this layer does not want to -- on a relay there is
                    // no address to record.
                    if (_role != LinkRole.Host) break;
                    Welcome();
                    _connected = true;
                    break;

                case PacketWelcome:
                    if (_role != LinkRole.Guest || _connected) break;
                    if (length < 5) break;
                    int at = 1;
                    _seed = unchecked((uint)ReadInt(data, ref at));
                    _connected = true;
                    break;

                case PacketInput:
                    // Input alone is enough for the HOST to call itself connected: it
                    // has the seed already, so there is nothing left for it to learn.
                    //
                    // Never for the guest. The seed arrives in the welcome and nowhere
                    // else, so a guest that started the match on an input packet whose
                    // welcome had been lost would deal its arena from zero while the
                    // host dealt from the real number -- two different matches, agreed
                    // on tick by tick. It keeps knocking until the welcome lands.
                    if (_role == LinkRole.Host) _connected = true;
                    TakeInput(data);
                    break;

                case PacketCheck:
                    TakeCheck(data);
                    break;

                case PacketPing:
                {
                    // Straight back, unchanged and from the receiving thread, so the
                    // reading is the wire and not how busy the other end's frame was.
                    if (length < 5) break;
                    byte[] pong = new byte[5];
                    pong[0] = PacketPong;
                    Array.Copy(data, 1, pong, 1, 4);
                    if (_wire != null && _wire.Ready) _wire.Send(pong, 5);
                    break;
                }

                case PacketPong:
                    TakePong(data);
                    break;

                case PacketRematch:
                    if (length < 9) break;
                    int cursor = 1;
                    int when = ReadInt(data, ref cursor);
                    uint dealt = unchecked((uint)ReadInt(data, ref cursor));
                    if (when < 0) { if (_role == LinkRole.Host) ScheduleRematch(); }
                    else if (_role == LinkRole.Guest && when > _rematchDone)
                    {
                        // The host re-sends this until it fires, because losing it
                        // restarts one phone and not the other. Taking it once is
                        // this side's half of that: a copy arriving after the tick
                        // has passed must not deal a second time.
                        _rematchSeed = dealt;
                        _rematchAt = when;
                    }
                    break;
            }
        }

        void Welcome()
        {
            if (_wire == null || !_wire.Ready) return;

            byte[] packet = new byte[1 + 4];
            int at = 0;
            packet[at++] = PacketWelcome;
            Write(packet, ref at, unchecked((int)_seed));
            _wire.Send(packet, at);
        }

        void TakeInput(byte[] data)
        {
            int at = 1;
            int carried = data[at++];
            if (carried <= 0 || data.Length < 2 + 4 + carried * 8) return;

            int newest = ReadInt(data, ref at);
            lock (_gate)
                for (int i = 0; i < carried; i++)
                {
                    int flags = ReadInt(data, ref at);
                    short x = ReadShort(data, ref at);
                    short y = ReadShort(data, ref at);
                    int t = newest - i;
                    if (t < 0) continue;

                    // Never overwrite an input already taken: the redundant copies are the
                    // SAME input, and a late packet must not rewrite history under a
                    // simulation that has already run on it. A slot holding a NEWER tick is
                    // left alone too -- that input is still wanted, and this one is past.
                    int slot = t & Mask;
                    if (_theirsAt[slot] >= t) continue;
                    _theirs[slot] = new TickInput((InputFlags)flags, x, y);
                    _theirsAt[slot] = t;
                }
            received++;
        }

        void TakeCheck(byte[] data)
        {
            if (data.Length < 9) return;
            int at = 1;
            int tick = ReadInt(data, ref at);
            uint hash = unchecked((uint)ReadInt(data, ref at));
            lock (_gate)
            {
                _theirChecks[tick] = hash;
                Forget(_theirChecks, tick);
                Compare(tick);
            }
        }

        void Say(byte kind)
        {
            if (_wire == null || !_wire.Ready) return;
            _wire.Send(new[] { kind }, 1);
        }

        // ------------------------------------------------------------------- plumbing

        static void Write(byte[] into, ref int at, int value)
        {
            into[at++] = (byte)(value & 0xFF);
            into[at++] = (byte)((value >> 8) & 0xFF);
            into[at++] = (byte)((value >> 16) & 0xFF);
            into[at++] = (byte)((value >> 24) & 0xFF);
        }

        static void Write(byte[] into, ref int at, short value)
        {
            into[at++] = (byte)(value & 0xFF);
            into[at++] = (byte)((value >> 8) & 0xFF);
        }

        static int ReadInt(byte[] from, ref int at)
        {
            int v = from[at] | (from[at + 1] << 8) | (from[at + 2] << 16) | (from[at + 3] << 24);
            at += 4;
            return v;
        }

        static short ReadShort(byte[] from, ref int at)
        {
            short v = (short)(from[at] | (from[at + 1] << 8));
            at += 2;
            return v;
        }




    }
}
