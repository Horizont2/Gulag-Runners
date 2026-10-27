using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
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
        const byte PacketBeacon = 4;
        const byte PacketCheck = 5;
        const byte PacketRematch = 6;    // a new match, on an agreed tick

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
        [Range(2, 12)] public int inputDelay = 4;

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

        /// <summary>How many hosts have been heard from. Safe to read from a frame.</summary>
        public int FoundCount { get { lock (_gate) return _found.Count; } }

        /// <summary>The first host heard from, if any. Safe to read from a frame.</summary>
        public bool TryFound(out string address)
        {
            lock (_gate)
            {
                address = _found.Count > 0 ? _found[0] : null;
                return address != null;
            }
        }

        readonly List<string> _found = new List<string>();

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

        UdpClient _match;
        UdpClient _beacon;
        Thread _listen;
        Thread _beaconListen;
        volatile bool _running;
        volatile LinkRole _role = LinkRole.Off;
        volatile bool _connected;
        volatile IPEndPoint _peer;
        volatile string _desync;
        volatile bool _desyncReported;
        uint _seed;
        volatile int _rematchAt = -1;
        volatile int _rematchDone = -1;
        uint _rematchSeed;
        float _nextBeacon;
        float _nextHello;
        float _askedAt;
        IPAddress[] _broadcasts = Array.Empty<IPAddress>();
        object _multicastLock;

        // ----------------------------------------------------------------- opening up

        /// <summary>Wait here for somebody to join. This device is player 0.</summary>
        public void Host()
        {
            Stop();
            _role = LinkRole.Host;

            // The host owns the deal. Any number will do as long as it is not the same one
            // every match, or every match plays out in the same arena.
            _seed = unchecked((uint)DateTime.UtcNow.Ticks) * 2654435761u + 1u;

            Open();
            if (_role == LinkRole.Off) return;
            state = $"hosting on {LocalAddress()}:{matchPort}";
        }

        /// <summary>Join a host by address. This device is player 1.</summary>
        public void Join(string address)
        {
            if (!IPAddress.TryParse(address, out IPAddress ip))
            {
                state = $"\"{address}\" is not an address";
                return;
            }

            Stop();
            _role = LinkRole.Guest;
            Open();
            if (_role == LinkRole.Off) return;

            _peer = new IPEndPoint(ip, matchPort);
            _askedAt = Time.unscaledTime;
            _nextHello = 0f;                 // Update sends the first one immediately
            state = $"joining {address}:{matchPort}";
        }

        /// <summary>Listen for hosts shouting, so nobody has to type an address.</summary>
        public void Discover()
        {
            if (_beaconListen != null) return;
            try
            {
                _beacon = new UdpClient();
                _beacon.Client.SetSocketOption(SocketOptionLevel.Socket,
                                               SocketOptionName.ReuseAddress, true);
                _beacon.Client.EnableBroadcast = true;
                _beacon.Client.Bind(new IPEndPoint(IPAddress.Any, beaconPort));
            }
            catch (Exception e)
            {
                state = $"cannot listen for hosts: {e.Message}";
                _beacon = null;
                return;
            }

            HoldBroadcastsOpen();
            _running = true;
            _beaconListen = new Thread(BeaconLoop) { IsBackground = true };
            _beaconListen.Start();
        }

        public void Stop()
        {
            _running = false;
            _connected = false;
            _role = LinkRole.Off;
            _peer = null;
            _rematchAt = -1;
            _rematchDone = -1;
            Now = 0;

            try { _match?.Close(); } catch (Exception) { }
            try { _beacon?.Close(); } catch (Exception) { }
            _match = null;
            _beacon = null;
            _listen = null;
            _beaconListen = null;
            ReleaseBroadcasts();

            lock (_gate)
            {
                for (int i = 0; i < Ring; i++) { _mineAt[i] = -1; _theirsAt[i] = -1; }
                _theirChecks.Clear();
                _myChecks.Clear();
                _found.Clear();
            }
            state = "off";
        }

        void Awake()
        {
            lock (_gate)
                for (int i = 0; i < Ring; i++) { _mineAt[i] = -1; _theirsAt[i] = -1; }
        }

        void Open()
        {
            try
            {
                _match = new UdpClient();
                _match.Client.SetSocketOption(SocketOptionLevel.Socket,
                                              SocketOptionName.ReuseAddress, true);
                _match.Client.Bind(new IPEndPoint(IPAddress.Any, matchPort));
                _match.EnableBroadcast = true;
            }
            catch (Exception e)
            {
                state = $"cannot open port {matchPort}: {e.Message}";
                _match = null;
                _role = LinkRole.Off;
                return;
            }

            _broadcasts = Broadcasts();
            HoldBroadcastsOpen();
            _running = true;
            _listen = new Thread(Listen) { IsBackground = true };
            _listen.Start();
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

            IPEndPoint peer = _peer;
            UdpClient socket = _match;
            if (peer == null || socket == null) return;

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

            try { socket.Send(packet, at, peer); sent++; } catch (Exception) { }
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
            IPEndPoint peer = _peer;
            UdpClient socket = _match;
            if (peer == null || socket == null) return;

            byte[] packet = new byte[1 + 4 + 4];
            int cursor = 0;
            packet[cursor++] = PacketRematch;
            Write(packet, ref cursor, at);
            Write(packet, ref cursor, unchecked((int)dealtFrom));
            try { socket.Send(packet, cursor, peer); } catch (Exception) { }
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

            IPEndPoint peer = _peer;
            UdpClient socket = _match;
            if (peer == null || socket == null) return;

            byte[] packet = new byte[1 + 4 + 4];
            int at = 0;
            packet[at++] = PacketCheck;
            Write(packet, ref at, tick);
            Write(packet, ref at, unchecked((int)hash));
            try { socket.Send(packet, at, peer); } catch (Exception) { }
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

            if (_match == null) return;

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

        void Shout()
        {
            byte[] packet = { PacketBeacon };
            for (int i = 0; i < _broadcasts.Length; i++)
            {
                try
                {
                    _match.Send(packet, packet.Length,
                                new IPEndPoint(_broadcasts[i], beaconPort));
                }
                catch (Exception) { }
            }
        }

        void Listen()
        {
            IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                byte[] data;
                try { data = _match.Receive(ref from); }
                catch (Exception) { return; }          // closed, or the app is going away
                if (data == null || data.Length < 1) continue;

                switch (data[0])
                {
                    case PacketHello:
                        // Only a host answers a hello. A guest that answered one would
                        // start a pair of devices saying hello to each other for ever.
                        if (_role != LinkRole.Host) break;
                        _peer = new IPEndPoint(from.Address, from.Port);
                        Welcome();
                        _connected = true;
                        break;

                    case PacketWelcome:
                        if (_role != LinkRole.Guest || _connected) break;
                        if (data.Length < 5) break;
                        int at = 1;
                        _seed = unchecked((uint)ReadInt(data, ref at));
                        _peer = new IPEndPoint(from.Address, from.Port);
                        _connected = true;
                        break;

                    case PacketInput:
                        if (_peer == null) _peer = new IPEndPoint(from.Address, from.Port);
                        TakeInput(data);
                        break;

                    case PacketCheck:
                        TakeCheck(data);
                        break;

                    case PacketRematch:
                        if (data.Length < 9) break;
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
        }

        void Welcome()
        {
            IPEndPoint peer = _peer;
            UdpClient socket = _match;
            if (peer == null || socket == null) return;

            byte[] packet = new byte[1 + 4];
            int at = 0;
            packet[at++] = PacketWelcome;
            Write(packet, ref at, unchecked((int)_seed));
            try { socket.Send(packet, at, peer); } catch (Exception) { }
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

        void BeaconLoop()
        {
            IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                byte[] data;
                try { data = _beacon.Receive(ref from); }
                catch (Exception) { return; }
                if (data == null || data.Length < 1 || data[0] != PacketBeacon) continue;

                string who = from.Address.ToString();
                lock (_gate)
                    if (!_found.Contains(who)) _found.Add(who);
            }
        }

        void Say(byte kind)
        {
            IPEndPoint peer = _peer;
            UdpClient socket = _match;
            if (peer == null || socket == null) return;
            try { socket.Send(new[] { kind }, 1, peer); } catch (Exception) { }
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

        /// <summary>
        /// Everywhere worth shouting. 255.255.255.255 is the obvious one and the one most
        /// likely to be dropped: plenty of Wi-Fi chipsets and routers pass the directed
        /// broadcast for their own subnet and nothing else, so both go out.
        /// </summary>
        static IPAddress[] Broadcasts()
        {
            List<IPAddress> where = new List<IPAddress> { IPAddress.Broadcast };
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    foreach (UnicastIPAddressInformation one in
                             nic.GetIPProperties().UnicastAddresses)
                    {
                        if (one.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        IPAddress mask = one.IPv4Mask;
                        if (mask == null) continue;

                        byte[] a = one.Address.GetAddressBytes();
                        byte[] m = mask.GetAddressBytes();
                        if (a.Length != 4 || m.Length != 4) continue;
                        for (int i = 0; i < 4; i++) a[i] = (byte)(a[i] | ~m[i]);

                        IPAddress edge = new IPAddress(a);
                        if (!where.Contains(edge)) where.Add(edge);
                    }
                }
            }
            catch (Exception) { }
            return where.ToArray();
        }

        /// <summary>This device's address on the local network, for reading aloud.</summary>
        public static string LocalAddress()
        {
            // The interface list first: Dns.GetHostEntry answers "127.0.0.1" on plenty of
            // Android builds, which is the one address that is no use to the other phone.
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    foreach (UnicastIPAddressInformation one in
                             nic.GetIPProperties().UnicastAddresses)
                        if (one.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(one.Address))
                            return one.Address.ToString();
                }
            }
            catch (Exception) { }

            try
            {
                foreach (IPAddress ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (ip.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ip))
                        return ip.ToString();
            }
            catch (Exception) { }
            return "unknown";
        }

        /// <summary>
        /// Android drops broadcast and multicast traffic to save the radio unless something
        /// asks it not to. Without this the beacon simply never arrives on a good many
        /// phones -- and it fails silently, which is the worst way for a thing to fail.
        /// </summary>
        void HoldBroadcastsOpen()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_multicastLock != null) return;
            try
            {
                using (AndroidJavaClass player =
                       new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity =
                       player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject wifi =
                       activity.Call<AndroidJavaObject>("getSystemService", "wifi"))
                {
                    AndroidJavaObject held =
                        wifi.Call<AndroidJavaObject>("createMulticastLock", "GulagRunners");
                    held.Call("setReferenceCounted", false);
                    held.Call("acquire");
                    _multicastLock = held;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{name}: no multicast lock ({e.Message}). Finding a host "
                                 + "automatically may not work; the address still can be "
                                 + "typed in.", this);
            }
#endif
        }

        void ReleaseBroadcasts()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_multicastLock == null) return;
            try
            {
                AndroidJavaObject held = (AndroidJavaObject)_multicastLock;
                held.Call("release");
                held.Dispose();
            }
            catch (Exception) { }
#endif
            _multicastLock = null;
        }
    }
}
