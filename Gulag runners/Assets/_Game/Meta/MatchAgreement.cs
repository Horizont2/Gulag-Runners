namespace GulagRunners.Meta
{
    /// <summary>
    /// The thing a matched pair must agree on and nobody sends them: the number the
    /// arena is dealt from.
    ///
    /// A matchmaking service says "you two are playing". It does not say what number the
    /// arena is dealt from -- and in a deterministic simulation that is load-bearing: two
    /// devices that each roll their own are not playing one match, they are playing two
    /// that agree tick by tick and diverge about every chest, which is the failure this
    /// whole project is built to make impossible. Both ends hold the same session id, so
    /// both can REACH the same number without a packet. Nothing to lose, nothing to time
    /// out, nothing to re-send.
    ///
    /// WHICH FIGHTER EACH DEVICE DRIVES used to live here too, worked out by comparing
    /// the two player ids. It has gone to the session, which answers it already: the
    /// host drives fighter one. The id comparison agreed with itself on both devices and
    /// disagreed with the SERVICE, which lets only the session's host write the relay's
    /// join code -- see RelayHandshake.Open for what that cost. Two answers to one
    /// question is a worse fault than a round trip.
    ///
    /// Pure, mirrored in docs/refs/ladder_mirror.py, and tested there -- because the
    /// one way this fails is subtle, device-dependent, and silent.
    /// </summary>
    public static class MatchAgreement
    {
        /// <summary>
        /// The number this match's loot is dealt from, folded out of the session id.
        ///
        /// The arena is not sent, it is DEALT -- so the number has to be the same on
        /// both devices or they fight in two different rooms, each looking perfectly
        /// correct. The session id is the one string the service guarantees both ends
        /// have and nobody else does, which makes it exactly the right thing to fold.
        ///
        /// FNV-1a: not for strength, which is not wanted here, but because it is four
        /// lines and gives the same answer on every machine. A hash that varied by
        /// runtime -- string.GetHashCode, which Unity is free to randomise per process
        /// -- would deal two different arenas from one id and look like a netcode bug
        /// for a week.
        /// </summary>
        public static uint Seed(string sessionId)
        {
            uint h = 2166136261u;
            if (!string.IsNullOrEmpty(sessionId))
            {
                for (int i = 0; i < sessionId.Length; i++)
                    unchecked
                    {
                        h = (h ^ sessionId[i]) * 16777619u;
                    }
            }

            // Zero is the one value that means something else: LanLink reads a seed of
            // nought as "nobody has told me yet".
            return h == 0u ? 1u : h;
        }
    }
}
