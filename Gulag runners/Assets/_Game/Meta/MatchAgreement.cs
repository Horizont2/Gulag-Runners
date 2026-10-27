namespace GulagRunners.Meta
{
    /// <summary>
    /// The two things a matched pair must agree on, worked out from what they both
    /// already know rather than sent between them.
    ///
    /// A matchmaking service says "you two are playing". It does not say which of you
    /// drives the first fighter, and it does not say what number the arena is dealt
    /// from -- and in a deterministic simulation both of those are load-bearing. Two
    /// devices that each decide for themselves are not playing one match: they are
    /// playing two that agree tick by tick and diverge about everything else, which is
    /// the failure this whole project is built to make impossible.
    ///
    /// They could be negotiated. A handshake is what the LAN link does, and it works.
    /// But a round trip that can be deleted should be: both ends are handed the same
    /// session id and the same pair of player ids by the service, so both can REACH the
    /// same answer without a packet. Nothing to lose, nothing to time out, nothing to
    /// re-send.
    ///
    /// Pure, mirrored in docs/refs/ladder_mirror.py, and tested there -- because the
    /// one way this fails is subtle, device-dependent, and silent.
    /// </summary>
    public static class MatchAgreement
    {
        /// <summary>
        /// Which fighter this device drives: 0 or 1, and never the same as the other
        /// device's answer.
        ///
        /// ORDINAL, not the default comparison, and that is the whole point of this
        /// being a named function instead of a line of code. string.CompareTo is
        /// culture-sensitive: two devices with different system languages can order the
        /// same pair of ids differently, and then BOTH players drive fighter one while
        /// nobody drives fighter two. It would work perfectly in testing, on two phones
        /// set to the same language, and break for somebody else.
        /// </summary>
        public static int LocalPlayer(string mine, string theirs)
        {
            if (string.IsNullOrEmpty(mine)) return string.IsNullOrEmpty(theirs) ? 0 : 1;
            if (string.IsNullOrEmpty(theirs)) return 0;

            int order = string.CompareOrdinal(mine, theirs);

            // Identical ids are one player matched with themselves, which a service
            // should never do. Answer 0 rather than throwing: a fighter in an odd match
            // is better than a crash on the loading screen, and the checksum will say
            // so within the second.
            return order <= 0 ? 0 : 1;
        }

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
