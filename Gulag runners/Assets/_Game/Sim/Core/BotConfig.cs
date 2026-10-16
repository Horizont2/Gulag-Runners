namespace GulagRunners.Sim
{
    /// <summary>How hard the bot is, and therefore which of its rules are switched on.</summary>
    public enum BotSkill : byte
    {
        /// <summary>
        /// Stands and takes it, with the guard up a quarter of the time. For looking at a
        /// weapon, a piece of armour or a hit reaction without being hit back.
        /// </summary>
        Dummy = 0,

        /// <summary>
        /// Keeps its distance, closes, swings, backs off and blocks. Everything a fight on
        /// one floor is made of, which is what most of this game is.
        /// </summary>
        Sparring = 1,

        /// <summary>
        /// And comes to find you: up and down ladders, towards the floor you are on.
        /// </summary>
        Hunter = 2
    }

    /// <summary>
    /// Every number behind the practice opponent.
    ///
    /// It is a config struct in the SIMULATION and not a pile of fields on a MonoBehaviour for
    /// the same reason everything else here is: the decision has to be a pure function of the
    /// state, so the Python mirror can run ten thousand ticks of it and ask whether the thing
    /// twitches. A bot that is tuned by feel in the inspector is a bot nobody can test, and
    /// the fault it will have is the one that is hardest to see by eye — a decision remade
    /// every frame, which reads as a character vibrating rather than as a character thinking.
    /// </summary>
    public struct BotConfig
    {
        public BotSkill Skill;

        /// <summary>
        /// How long a decision is held, in frames.
        ///
        /// The single most important number here. A bot that chooses afresh every tick at
        /// 60 Hz does not look fast, it looks broken: it stutters on the spot wherever two
        /// choices are close. Holding the choice is also what gives it a reaction time, which
        /// is the thing that actually makes an opponent beatable.
        /// </summary>
        public int ReactionFrames;

        /// <summary>Spread on the above, in frames, so two bots never move in lockstep.</summary>
        public int ReactionJitter;

        /// <summary>
        /// Where it wants to stand, as a fraction of its own weapon's reach in thousandths.
        /// Just inside reach: a bot that stands exactly at the tip whiffs on its own recovery.
        /// </summary>
        public int RangePermille;

        /// <summary>Closer than this fraction of reach and it backs off instead.</summary>
        public int TooClosePermille;

        /// <summary>Chance per decision of swinging when the target is in reach, in thousandths.</summary>
        public int AggressionPermille;

        /// <summary>Chance per decision of putting the guard up, in thousandths.</summary>
        public int GuardPermille;

        /// <summary>Chance per decision of rolling, when it has the stamina, in thousandths.</summary>
        public int DodgePermille;

        /// <summary>
        /// Chance per decision that a backing-off bot keeps its blade on you rather than
        /// turning to run, in thousandths.
        ///
        /// This is the aim/travel split used against the player, and it is most of what
        /// separates an opponent from a target: something that retreats with its guard
        /// towards you has to be chased properly.
        /// </summary>
        public int FightingWithdrawalPermille;

        /// <summary>
        /// How much higher or lower the target must be before the bot goes looking for a
        /// ladder rather than walking at them. About a floor.
        /// </summary>
        public Fix FloorGap;

        /// <summary>Hunter only: how far it will walk to reach a ladder.</summary>
        public Fix LadderSeek;

        public static BotConfig Default()
        {
            BotConfig c = default;
            c.Skill = BotSkill.Sparring;

            c.ReactionFrames = 14;          // 0.23 s: quick, and visibly not instant
            c.ReactionJitter = 8;

            c.RangePermille = 850;          // just inside the tip
            c.TooClosePermille = 450;

            c.AggressionPermille = 550;
            c.GuardPermille = 220;
            c.DodgePermille = 90;
            c.FightingWithdrawalPermille = 700;

            c.FloorGap = Fix.FromMilli(1200);
            c.LadderSeek = Fix.FromMilli(14000);
            return c;
        }

        /// <summary>The same numbers, softened or hardened to a skill.</summary>
        public static BotConfig For(BotSkill skill)
        {
            BotConfig c = Default();
            c.Skill = skill;

            switch (skill)
            {
                case BotSkill.Dummy:
                    c.AggressionPermille = 0;
                    c.DodgePermille = 0;
                    c.GuardPermille = 250;
                    c.FightingWithdrawalPermille = 0;
                    break;

                case BotSkill.Hunter:
                    c.ReactionFrames = 9;        // 0.15 s
                    c.ReactionJitter = 5;
                    c.AggressionPermille = 700;
                    c.DodgePermille = 160;
                    c.GuardPermille = 300;
                    break;
            }

            return c;
        }
    }
}
