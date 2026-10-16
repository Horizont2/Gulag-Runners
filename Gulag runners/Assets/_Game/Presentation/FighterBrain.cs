using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Hands a fighter to the computer. One tick box.
    ///
    /// The point of it is not the opponent. It is that until there is something that moves,
    /// nothing in this project can be PLAYED: the aim split, backing off with the guard up,
    /// the closing walls, the loot, every number in docs/02 — all of it only means anything
    /// against somebody who is doing something back. A bot is the cheapest way to turn a
    /// pile of tested systems into five minutes you can sit down and judge.
    ///
    /// It plays through the same door as the player and no other. It is an
    /// <see cref="IPlayerInputSource"/>; it produces the same ten bits a thumb produces; it
    /// cannot reach further than its weapon, cannot see through a floor it has no ladder to,
    /// and takes damage from the same hitboxes. A bot wired straight into the motor would
    /// prove nothing about whether the game works, because it would not be playing it.
    ///
    /// All the thinking is in <see cref="BotBrain"/>, in the simulation assembly, where it is
    /// a pure function and the Python mirror can run ten thousand ticks of it and measure the
    /// one fault that is hard to see by eye: whether it twitches.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FighterBrain : MonoBehaviour, IPlayerInputSource, IInputTakeover
    {
        [Header("The tick box")]
        [Tooltip("Hand this fighter to the computer.\n\n" +
                 "While it is on, the keyboard and the on-screen controls are ignored FOR " +
                 "THIS FIGHTER — two drivers on one body is a body that never stops walking. " +
                 "Switch it off and the fighter is yours again on the next frame, mid-round " +
                 "and without restarting anything.")]
        public bool takeOver;

        [Tooltip("How hard.\n\n" +
                 "Dummy stands and guards, for looking at a weapon or a hit reaction without " +
                 "being hit back. Sparring is a fight on one floor: closes, swings, backs " +
                 "off, blocks. Hunter does that and comes to find you, up and down ladders.")]
        public BotSkill skill = BotSkill.Sparring;

        [Header("Wiring")]
        [Tooltip("Optional. This object's own controller when empty.")]
        public PlayerController self;

        [Tooltip("Optional. The other fighter in MatchState when empty, which is what it " +
                 "should be in a 1v1.")]
        public PlayerController target;

        [Header("Tuning")]
        [Tooltip("Take the numbers from the skill above every time it changes. Off keeps " +
                 "whatever is typed below, so a setting can be tried without the skill " +
                 "picker putting it back.")]
        public bool tuningFromSkill = true;

        [Tooltip("Every number behind the opponent. Reaction Frames is the one that matters: " +
                 "it is how long a decision is held, and therefore both the bot's reaction " +
                 "time and the reason it does not vibrate.")]
        public BotConfig tuning = BotConfig.For(BotSkill.Sparring);

        [Header("Debug")]
        [Tooltip("What it is pressing, and how many frames until it may change its mind.")]
        public string pressing;

        BotState _bot;
        BotSkill _tuned;
        bool _seeded;

        public bool TakingOver => takeOver && isActiveAndEnabled && self != null;

        void Reset()
        {
            self = GetComponent<PlayerController>();
        }

        void Awake()
        {
            if (self == null) self = GetComponent<PlayerController>();
            Seed();
        }

        /// <summary>
        /// Its own stream of chance, from its own index, so two bots in one scene never roll
        /// the same numbers and move in lockstep — which reads as one opponent mirrored
        /// rather than two.
        /// </summary>
        void Seed()
        {
            uint seed = (uint)((self != null ? self.playerIndex : 0) * 2654435761u + 12345u);
            _bot = BotState.Seeded(seed);
            _tuned = skill;
            _seeded = true;
        }

        void OnValidate()
        {
            if (tuningFromSkill && skill != _tuned)
            {
                tuning = BotConfig.For(skill);
                _tuned = skill;
            }
        }

        public InputFlags Read()
        {
            if (!TakingOver) { pressing = ""; return InputFlags.None; }

            if (!_seeded) Seed();
            if (tuningFromSkill && skill != _tuned)
            {
                tuning = BotConfig.For(skill);
                _tuned = skill;
            }

            PlayerController foe = Foe();
            if (foe == null || self.World == null)
            {
                pressing = "no opponent";
                return InputFlags.None;
            }

            PlayerSimState me = self.State;
            PlayerSimState him = foe.State;

            InputFlags flags = BotBrain.Decide(ref _bot, in me, in him, self.World,
                                               in tuning, self.Config);

            pressing = $"{flags}  ({_bot.Think})";
            return flags;
        }

        /// <summary>
        /// The other fighter. Taken from the match rather than wired by hand, because the
        /// one thing this must never do is fight itself — which is what an empty slot and a
        /// GetComponent would quietly produce.
        /// </summary>
        PlayerController Foe()
        {
            if (target != null && target != self) return target;

            MatchState match = MatchState.Instance;
            if (match == null || match.fighters == null) return null;

            for (int i = 0; i < match.fighters.Count; i++)
            {
                PlayerController f = match.fighters[i];
                if (f != null && f != self) return f;
            }
            return null;
        }
    }
}
