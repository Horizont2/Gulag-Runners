using System;

namespace GulagRunners.Net
{
    /// <summary>
    /// Somewhere to put bytes, and somewhere they arrive from. Nothing else.
    ///
    /// The lockstep in <see cref="LanLink"/> is the valuable part and the part that was
    /// hardest to get right: a ring that knows which tick each slot holds, sixteen
    /// redundant inputs a packet, a handshake that ends, a checksum that catches a
    /// simulation drifting. None of it has any opinion about HOW six bytes reach the
    /// other player, and all of it was written with the socket threaded through it.
    ///
    /// So this is the seam. A Wi-Fi today; Unity Relay next, because a pair matched out
    /// of a queue are strangers behind two routers and a broadcast reaches neither; and
    /// a relay of one's own after that, if the bill or the ownership ever argues for it.
    /// Each is this interface and nothing above it changes.
    ///
    /// Deliberately datagrams and not a stream. Lockstep wants the newest input and does
    /// not want the three before it re-sent in order first: every packet already carries
    /// its own tick numbers and the last sixteen inputs, so a lost one costs nothing and
    /// a late one is dropped on its own merits. A reliable ordered channel would spend
    /// latency fixing a problem this protocol does not have.
    /// </summary>
    public interface INetTransport
    {
        /// <summary>True when a datagram sent now would actually go somewhere.</summary>
        bool Ready { get; }

        /// <summary>
        /// What to tell a person: an address, a join code, or why none of it worked.
        /// Shown on the connect screen, so it is a sentence and not a status code.
        /// </summary>
        string Describe { get; }

        /// <summary>
        /// A datagram arrived. MAY BE RAISED ON ANY THREAD -- a socket has its own, and
        /// answering a ping from it is what makes the reading the wire rather than how
        /// busy the other end's frame was. Everything it reaches is behind a lock.
        /// </summary>
        event Action<byte[], int> Received;

        /// <summary>Send to the peer. Silent when there is no peer: see Ready.</summary>
        void Send(byte[] data, int length);

        /// <summary>
        /// One frame's worth of whatever this transport has to do for itself.
        ///
        /// A socket needs nothing -- it has its own thread and blocks on it. Unity
        /// Transport needs this, because its driver may not be touched from a thread
        /// at all, so accepting, reading and delivering all happen here. Called once
        /// per frame by the link, which is the only place that knows what a frame is.
        /// </summary>
        void Pump();

        /// <summary>Done. Safe to call twice, and safe to call when never opened.</summary>
        void Close();
    }

    /// <summary>
    /// Finding somebody to talk to, when nothing else is doing the finding.
    ///
    /// Only a local network can do this on its own, by shouting. A matched pair never
    /// needs it -- the queue already said who they are and how to reach them -- so a
    /// relay transport implements <see cref="INetTransport"/> and not this, and the
    /// connect screen asks for it rather than assuming it.
    /// </summary>
    public interface INetDiscovery
    {
        /// <summary>Announce, so a guest can find this device without typing anything.</summary>
        void Shout();

        /// <summary>Start listening for hosts announcing themselves.</summary>
        void Discover();

        /// <summary>The first host heard from, if any. Safe to read from a frame.</summary>
        bool TryFound(out string address);

        /// <summary>How many have been heard from. Safe to read from a frame.</summary>
        int FoundCount { get; }
    }
}
