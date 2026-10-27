using System;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using UnityEngine;

namespace GulagRunners.Net
{
    /// <summary>
    /// Two strangers behind two routers, through Unity's relay.
    ///
    /// The third implementation of <see cref="INetTransport"/> and the reason the seam
    /// was cut: the lockstep above it does not change by a line. Same ring, same sixteen
    /// redundant inputs, same handshake, same checksum -- the bytes simply travel
    /// further and arrive later, and the budget that was measured off the round trip
    /// already knows what to do about the later.
    ///
    /// Unreliable, deliberately. Unity Transport offers a reliable ordered pipeline and
    /// it is the wrong tool here: this protocol carries the last sixteen inputs in every
    /// packet precisely so a lost one costs nothing, and a pipeline that stopped to
    /// re-send would spend latency fixing a problem that has already been solved a
    /// cheaper way. Lockstep is late when the NEWEST input is late, and nothing is
    /// gained by delivering an old one first.
    ///
    /// Pumped from a frame rather than a thread. A socket can block on its own thread;
    /// NetworkDriver cannot be touched from one, so <see cref="Pump"/> drives it and
    /// datagrams arrive on the main thread. That costs up to a frame of latency, which
    /// the input budget covers -- and buys not having to reason about the driver being
    /// disposed under a background read.
    /// </summary>
    public sealed class RelayTransport : INetTransport
    {
        readonly bool _host;
        RelayServerData _relay;

        NetworkDriver _driver;
        NetworkConnection _peer;
        byte[] _in = new byte[1400];
        bool _open;
        string _said = "off";

        /// <summary>
        /// <paramref name="host"/> decides which end listens. It must be the SAME answer
        /// the simulation reached -- MatchAgreement picks fighter one from the two
        /// player ids, and the end driving fighter one is the end that listens here, or
        /// both sides would wait to be connected to.
        /// </summary>
        public RelayTransport(RelayServerData relay, bool host)
        {
            _relay = relay;
            _host = host;
        }

        public event Action<byte[], int> Received;

        public bool Ready => _open && _peer.IsCreated;
        public string Describe => _said;

        public bool Open()
        {
            Close();

            try
            {
                NetworkSettings settings = new NetworkSettings();
                settings.WithRelayParameters(ref _relay);

                _driver = NetworkDriver.Create(settings);

                if (_driver.Bind(NetworkEndpoint.AnyIpv4) != 0)
                {
                    _said = "the relay would not bind";
                    Close();
                    return false;
                }

                if (_host) _driver.Listen();
                else _peer = _driver.Connect();

                _open = true;
                _said = _host ? "hosting through the relay" : "joining through the relay";
                return true;
            }
            catch (Exception e)
            {
                _said = $"relay: {e.Message}";
                Close();
                return false;
            }
        }

        /// <summary>
        /// One frame of the driver: accept, read, deliver. Everything this transport
        /// does that is not sending happens here.
        /// </summary>
        public void Pump()
        {
            if (!_open) return;

            _driver.ScheduleUpdate().Complete();

            // The guest connects to one endpoint and the host accepts one. A second
            // caller on a two-player relay allocation is not a spectator, it is a
            // mistake, and taking it would hand the match to whoever knocked last.
            if (_host && !_peer.IsCreated)
            {
                NetworkConnection arrived = _driver.Accept();
                if (arrived.IsCreated)
                {
                    _peer = arrived;
                    _said = "connected through the relay";
                }
            }

            NetworkEvent.Type what;
            while ((what = _driver.PopEvent(out NetworkConnection from,
                                            out DataStreamReader reader))
                   != NetworkEvent.Type.Empty)
            {
                switch (what)
                {
                    case NetworkEvent.Type.Connect:
                        _peer = from;
                        _said = "connected through the relay";
                        break;

                    case NetworkEvent.Type.Data:
                        Deliver(ref reader);
                        break;

                    case NetworkEvent.Type.Disconnect:
                        _peer = default;
                        _said = "the other player dropped";
                        break;
                }
            }
        }

        void Deliver(ref DataStreamReader reader)
        {
            int length = reader.Length;
            if (length <= 0) return;
            if (length > _in.Length) _in = new byte[length];

            // Byte at a time, which looks lazy and is not. The alternative is a
            // NativeArray per datagram and the Collections API with it, for 134 bytes
            // sixty times a second -- a cost that does not exist, bought with a type
            // that has to be disposed on every path including the ones that throw.
            for (int i = 0; i < length; i++) _in[i] = reader.ReadByte();

            Received?.Invoke(_in, length);
        }

        public void Send(byte[] data, int length)
        {
            if (!Ready || data == null || length <= 0) return;

            try
            {
                if (_driver.BeginSend(_peer, out DataStreamWriter writer) != 0) return;
                for (int i = 0; i < length; i++) writer.WriteByte(data[i]);
                _driver.EndSend(writer);
            }
            catch (Exception) { }
        }

        public void Close()
        {
            _open = false;
            _peer = default;

            if (_driver.IsCreated)
            {
                try { _driver.Dispose(); } catch (Exception) { }
            }
            _driver = default;
            _said = "off";
        }
    }
}
