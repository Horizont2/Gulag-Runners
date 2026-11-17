using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Networking.Transport.Relay;
using Unity.Services.Multiplayer;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace GulagRunners.Net
{
    /// <summary>
    /// Turning "you two are matched" into a relay both ends can actually talk through.
    ///
    /// A session is not a connection. It is a reliable, authenticated channel between
    /// exactly these two players and nobody else -- which makes it the one thing a
    /// relay handshake needs and cannot otherwise get: somewhere to leave a join code
    /// where only the other player will find it.
    ///
    /// So the session is used as a meeting place and nothing more. One end makes an
    /// allocation, writes its code into a session property, and listens; the other
    /// reads the code and joins it. Both end up holding a RelayServerData, which is all
    /// <see cref="RelayTransport"/> wants and the last thing either of them needs from
    /// the service.
    ///
    /// The session's OWN relay was the obvious alternative -- SessionOptions has a
    /// WithRelayNetwork for it -- and it is the wrong shape here. That exists so the
    /// SDK can drive a netcode for you, and this project already has one: a
    /// deterministic simulation whose entire wire format is six bytes of thumb. Letting
    /// the SDK own the transport would mean handing those six bytes to a layer built to
    /// replicate state, which is the one thing a deterministic simulation must never be
    /// told.
    /// </summary>
    public static class RelayHandshake
    {
        /// <summary>Where the join code is left. Both ends read the same key.</summary>
        public const string JoinKey = "relay-join";

        /// <summary>
        /// UDP, not DTLS or WSS. The payload is already an unreliable datagram of
        /// thumb positions that is re-sent sixteen times over; encrypting it costs a
        /// handshake and some latency to protect six bytes that are worthless the tick
        /// after they are read.
        /// </summary>
        const string Protocol = "udp";

        /// <summary>How long to wait for the other end to leave its code.</summary>
        const float Patience = 20f;

        public struct Opened
        {
            public bool Ok;
            public RelayServerData Relay;
            public string Said;

            /// <summary>
            /// Which end of the relay this device took: true if it allocated.
            ///
            /// Returned rather than passed in, because the answer is the SESSION's and not
            /// the caller's — see <see cref="Open"/>. Everything downstream hangs off it:
            /// the transport binds as host or connects to one, LanLink turns it into which
            /// fighter this device drives, and NetMatch points the camera, the health bars
            /// and the ladder at that fighter. One fact, read once, used by all of them.
            /// </summary>
            public bool Host;
        }

        /// <summary>
        /// Makes or joins the relay for this session.
        ///
        /// WHO ALLOCATES IS THE SESSION'S HOST, and this used to be decided from the two
        /// player ids instead (MatchAgreement.LocalPlayer: the lower id drives fighter one
        /// and therefore allocates). Both devices did reach the same answer from the ids,
        /// which is what the argument was for -- and the answer had nothing to do with the
        /// one fact that matters here: the join code is left in a session property, and
        /// ONLY THE SESSION'S HOST MAY WRITE ONE. Unity's SDK refuses the rest, in those
        /// words.
        ///
        /// So on about half of all pairings -- whenever the service's host was the higher
        /// id -- the device that believed it was hosting called AsHost on a session it did
        /// not own and was refused ("only the host can do this"), while the real host sat
        /// in WaitForCode for twenty seconds waiting for a code nobody was allowed to
        /// leave, and then said the other player never opened a relay. Two different
        /// messages, one cause, and neither of them mentions the session.
        ///
        /// Asking the session removes the disagreement rather than making the two answers
        /// agree: there is exactly one host, both devices can see which one it is, and the
        /// service enforces it. The role taken comes back in <see cref="Opened.Host"/> so
        /// that the transport and the fighter index are the same fact and cannot drift.
        /// </summary>
        public static async Task<Opened> Open(ISession session, CancellationToken stop)
        {
            bool host = session != null && session.IsHost;

            try
            {
                // THE OTHER END, AND THEN BACK AGAIN. Three attempts, alternating, because
                // believing one side of this over the other is how two devices wait for
                // each other until both give up -- which is the whole bug this file is
                // about. It cannot make two relays: the service allows exactly one player
                // to write the join code, so a second allocator is refused rather than
                // accommodated.
                //
                // The third attempt is the one that is easy to miss. If the session called
                // BOTH ends the guest, both wait out the patience and both then try to
                // allocate; the real host succeeds, the other is refused -- and by then the
                // code it was waiting for is finally there, so it has to be allowed to go
                // back and read it.
                bool asHost = host;
                Opened last = new Opened { Ok = false, Said = "nothing attempted" };
                string first = null;

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    last = await Try(session, asHost, stop);
                    if (last.Ok)
                    {
                        if (attempt > 0)
                            Debug.LogWarning(
                                $"relay: the session called this device the " +
                                $"{(host ? "host" : "guest")} and that did not work " +
                                $"({first}); opened it as the {(asHost ? "host" : "guest")} " +
                                $"on attempt {attempt + 1}.");
                        last.Host = asHost;
                        return last;
                    }

                    if (stop.IsCancellationRequested) break;
                    first ??= last.Said;
                    asHost = !asHost;
                }

                last.Said = first == last.Said ? last.Said : $"{first}; then: {last.Said}";
                last.Host = host;
                return last;
            }
            catch (OperationCanceledException)
            {
                return new Opened { Ok = false, Said = "cancelled", Host = host };
            }
            catch (Exception e)
            {
                Debug.LogWarning($"relay handshake failed -- {e.Message}\n" +
                                 "Relay has to be enabled on the dashboard, and the " +
                                 "device has to be signed in.");
                return new Opened { Ok = false, Said = e.Message, Host = host };
            }
        }

        /// <summary>
        /// One end of the handshake, with its failure returned rather than thrown, so the
        /// caller can try the other end without unwinding.
        /// </summary>
        static async Task<Opened> Try(ISession session, bool asHost, CancellationToken stop)
        {
            try
            {
                return asHost ? await Allocate(session, stop) : await Join(session, stop);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                return new Opened { Ok = false, Said = e.Message };
            }
        }

        static async Task<Opened> Allocate(ISession session, CancellationToken stop)
        {
            // One peer, because a duel is two people. A relay sized for more is a relay
            // a third party could walk into.
            Allocation made = await RelayService.Instance.CreateAllocationAsync(1);
            string code = await RelayService.Instance.GetJoinCodeAsync(made.AllocationId);
            if (stop.IsCancellationRequested) return Gave("cancelled");

            IHostSession owner = session.AsHost();
            owner.SetProperty(JoinKey,
                              new SessionProperty(code, VisibilityPropertyOptions.Member));
            await owner.SavePropertiesAsync();

            return new Opened
            {
                Ok = true,
                Relay = AllocationUtils.ToRelayServerData(made, Protocol),
                Said = $"hosting relay {code}",
            };
        }

        static async Task<Opened> Join(ISession session, CancellationToken stop)
        {
            string code = await WaitForCode(session, stop);
            if (string.IsNullOrEmpty(code))
                return Gave("the other player never opened a relay");

            JoinAllocation joined = await RelayService.Instance.JoinAllocationAsync(code);
            return new Opened
            {
                Ok = true,
                Relay = AllocationUtils.ToRelayServerData(joined, Protocol),
                Said = $"joined relay {code}",
            };
        }

        /// <summary>
        /// Waits for the other end's code to appear on the session.
        ///
        /// Polled rather than driven off the session's Changed event, and that is a
        /// choice about which failure is cheaper. An event can arrive before this is
        /// listening -- the host may well have written the code while the guest was
        /// still signing in -- and a handshake that misses its own notification waits
        /// for ever with nothing to show. Asking every half second cannot miss anything
        /// that has already happened, and twenty seconds of it is forty requests.
        /// </summary>
        static async Task<string> WaitForCode(ISession session, CancellationToken stop)
        {
            float waited = 0f;
            while (waited < Patience)
            {
                if (stop.IsCancellationRequested) return null;

                if (session.Properties != null &&
                    session.Properties.TryGetValue(JoinKey, out SessionProperty said) &&
                    said != null && !string.IsNullOrEmpty(said.Value))
                    return said.Value;

                await Task.Delay(500, stop);
                waited += 0.5f;

                try { await session.RefreshAsync(); } catch (Exception) { }
            }
            return null;
        }

        static Opened Gave(string said) => new Opened { Ok = false, Said = said };
    }
}
