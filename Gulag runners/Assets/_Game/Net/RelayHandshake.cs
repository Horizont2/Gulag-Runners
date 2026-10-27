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
        }

        /// <summary>
        /// Makes or joins the relay for this session.
        ///
        /// <paramref name="host"/> must be the end MatchAgreement made fighter one.
        /// Not a coin toss and not whoever asked first: both devices reach that answer
        /// from the player ids alone, so exactly one of them allocates and exactly one
        /// waits. Two allocators would make two relays and meet in neither.
        /// </summary>
        public static async Task<Opened> Open(ISession session, bool host,
                                              CancellationToken stop)
        {
            try
            {
                return host ? await Allocate(session, stop) : await Join(session, stop);
            }
            catch (OperationCanceledException)
            {
                return new Opened { Ok = false, Said = "cancelled" };
            }
            catch (Exception e)
            {
                Debug.LogWarning($"relay handshake failed -- {e.Message}\n" +
                                 "Relay has to be enabled on the dashboard, and the " +
                                 "device has to be signed in.");
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
