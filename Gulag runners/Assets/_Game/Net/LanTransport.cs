using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace GulagRunners.Net
{
    /// <summary>
    /// Two devices on one network, over a plain UDP socket.
    ///
    /// Everything here was inside <see cref="LanLink"/> and is unchanged: the socket, the
    /// receiving thread, the beacon a host shouts so nobody types an address, the subnet
    /// broadcasts, and Android's multicast lock. It moved out so the lockstep above it
    /// could stop knowing what a socket is -- not because any of it was wrong.
    ///
    /// Three things it does that a relay will not have to:
    ///
    ///   it shouts      a host announces itself once a second on a broadcast port, so a
    ///                  guest can find it. That reaches exactly one subnet, which is the
    ///                  whole reason a queue and a relay exist at all.
    ///   it listens     for those shouts, on its own socket and its own thread.
    ///   it learns      who the peer is from the first datagram that arrives, because on
    ///                  a LAN nobody has told it.
    /// </summary>
    public sealed class LanTransport : INetTransport, INetDiscovery
    {
        const byte PacketBeacon = 4;      // the one packet this layer reads for itself

        readonly int _matchPort;
        readonly int _beaconPort;
        readonly object _gate = new object();
        readonly List<string> _found = new List<string>();

        UdpClient _match;
        UdpClient _beacon;
        Thread _listen;
        Thread _beaconListen;
        volatile bool _running;
        volatile IPEndPoint _peer;
        volatile bool _learnPeer;
        IPAddress[] _broadcasts = Array.Empty<IPAddress>();
        object _multicastLock;
        string _said = "off";

        public LanTransport(int matchPort, int beaconPort)
        {
            _matchPort = matchPort;
            _beaconPort = beaconPort;
        }

        public event Action<byte[], int> Received;

        public bool Ready => _peer != null && _match != null;
        public string Describe => _said;

        /// <summary>This device's address, so it can be read out loud.</summary>
        public string Address => LocalAddress();

        /// <summary>
        /// Open the match socket.
        ///
        /// <paramref name="learnPeer"/> is the host's half of a LAN handshake: it has no
        /// idea who will join, so the first datagram to arrive names the peer. A guest
        /// knows already, and must NOT learn, or a stray packet from anything else on the
        /// network would redirect the match.
        /// </summary>
        public bool Open(bool learnPeer)
        {
            Close();
            _learnPeer = learnPeer;

            try
            {
                _match = new UdpClient();
                _match.Client.SetSocketOption(SocketOptionLevel.Socket,
                                              SocketOptionName.ReuseAddress, true);
                _match.Client.Bind(new IPEndPoint(IPAddress.Any, _matchPort));
                _match.EnableBroadcast = true;
            }
            catch (Exception e)
            {
                _said = $"cannot open port {_matchPort}: {e.Message}";
                _match = null;
                return false;
            }

            _broadcasts = Broadcasts();
            HoldBroadcastsOpen();
            _running = true;
            _listen = new Thread(Listen) { IsBackground = true };
            _listen.Start();
            _said = $"open on {LocalAddress()}:{_matchPort}";
            return true;
        }

        /// <summary>Aim at a host by address. False when that is not an address.</summary>
        public bool PointAt(string address)
        {
            if (!IPAddress.TryParse(address, out IPAddress ip))
            {
                _said = $"\"{address}\" is not an address";
                return false;
            }
            _peer = new IPEndPoint(ip, _matchPort);
            _said = $"talking to {address}:{_matchPort}";
            return true;
        }

        public void Send(byte[] data, int length)
        {
            IPEndPoint peer = _peer;
            UdpClient socket = _match;
            if (peer == null || socket == null) return;
            try { socket.Send(data, length, peer); } catch (Exception) { }
        }

        public void Close()
        {
            _running = false;
            _peer = null;

            try { _match?.Close(); } catch (Exception) { }
            try { _beacon?.Close(); } catch (Exception) { }
            _match = null;
            _beacon = null;
            _listen = null;
            _beaconListen = null;
            ReleaseBroadcasts();

            lock (_gate) _found.Clear();
            _said = "off";
        }

        // ----------------------------------------------------------------- the beacon

        public int FoundCount { get { lock (_gate) return _found.Count; } }

        public bool TryFound(out string address)
        {
            lock (_gate)
            {
                address = _found.Count > 0 ? _found[0] : null;
                return address != null;
            }
        }

        /// <summary>
        /// Everywhere worth shouting. 255.255.255.255 is the obvious one and the one most
        /// likely to be dropped: plenty of Wi-Fi chipsets and routers pass the directed
        /// broadcast for their own subnet and nothing else, so both go out.
        /// </summary>
        public void Shout()
        {
            UdpClient socket = _match;
            if (socket == null) return;

            byte[] packet = { PacketBeacon };
            for (int i = 0; i < _broadcasts.Length; i++)
            {
                try
                {
                    socket.Send(packet, packet.Length,
                                new IPEndPoint(_broadcasts[i], _beaconPort));
                }
                catch (Exception) { }
            }
        }

        public void Discover()
        {
            if (_beaconListen != null) return;
            try
            {
                _beacon = new UdpClient();
                _beacon.Client.SetSocketOption(SocketOptionLevel.Socket,
                                               SocketOptionName.ReuseAddress, true);
                _beacon.Client.EnableBroadcast = true;
                _beacon.Client.Bind(new IPEndPoint(IPAddress.Any, _beaconPort));
            }
            catch (Exception e)
            {
                _said = $"cannot listen for hosts: {e.Message}";
                _beacon = null;
                return;
            }

            HoldBroadcastsOpen();
            _running = true;
            _beaconListen = new Thread(BeaconLoop) { IsBackground = true };
            _beaconListen.Start();
        }

        // ------------------------------------------------------------------- the wire

        void Listen()
        {
            IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                byte[] data;
                try { data = _match.Receive(ref from); }
                catch (Exception) { return; }     // closed, or the app is going away
                if (data == null || data.Length < 1) continue;

                // Who this is, on a network where nobody said. Only while hosting, and
                // only until somebody has spoken: a guest aimed at an address must not be
                // turned around by whatever else is on the Wi-Fi.
                if (_learnPeer && _peer == null)
                    _peer = new IPEndPoint(from.Address, from.Port);

                Received?.Invoke(data, data.Length);
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
                Debug.LogWarning($"no multicast lock ({e.Message}). Finding a host "
                                 + "automatically may not work; the address still can be "
                                 + "typed in.");
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
