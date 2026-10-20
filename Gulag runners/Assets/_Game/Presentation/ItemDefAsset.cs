using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// One weapon, one piece of armour or one utility item, as a file you can open.
    ///
    /// The numbers live in <see cref="ItemTable"/>, which is a C# file inside the engine-free
    /// simulation assembly — deliberately, because the simulation may not allocate, may not
    /// touch a float, and must read identically on both devices (docs/06). The cost of that
    /// is that nobody can see the table without opening the code, and every balance pass is a
    /// recompile.
    ///
    /// So this is the authoring surface: metres and seconds and percentages in the inspector,
    /// converted to fixed point on the way in, applied once at load. The code table is still
    /// what the game falls back to and still what defines which items exist; an asset only
    /// overrides the numbers for one of them.
    /// </summary>
    [CreateAssetMenu(menuName = "Gulag Runners/Item", fileName = "Item")]
    public sealed class ItemDefAsset : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Which item in the table this file is the numbers for. An asset cannot " +
                 "invent an item: the id list is the simulation's, and adding to it is a code " +
                 "change because the whole round state is serialised by id (docs/06).")]
        public ItemId item = ItemId.None;

        public ItemKind kind = ItemKind.Weapon;

        [Tooltip("0 fists, 1 white, 2 blue, 3 gold. Drives the glow, and nothing else.")]
        [Range(0, 3)] public int tier = 1;

        [Tooltip("How often this comes up against the others of its tier. 0 keeps it out of " +
                 "chests altogether, which is what fists are.")]
        public int weight = 100;

        [Header("Weapon")]
        [Tooltip("Damage per hit, against no armour and no guard.")]
        public float damage = 8f;

        [Tooltip("Seconds from pressing attack to the hit landing. Lower is faster.")]
        public float attackSeconds = 0.25f;

        [Tooltip("How far in front of the body the hit reaches, in metres. docs/02 makes this " +
                 "the whole difference between two weapons — and it is also what the model is " +
                 "scaled to, so a number changed here changes the picture as well.")]
        public float reach = 0.95f;

        [Tooltip("Hits, or chest openings, left in it. 0 never breaks.")]
        public int durability = 14;

        [Tooltip("How many times faster than bare hands this opens a chest.")]
        public float prySpeed = 2f;

        [Tooltip("Share of the damage that goes through a raised guard, when that is more " +
                 "than the ordinary chip. 0 uses the ordinary chip.")]
        [Range(0f, 1f)] public float blockPierce;

        [Header("Armour")]
        [Tooltip("Share of incoming damage removed. docs/02: T1/T2/T3 = 15/30/45%.")]
        [Range(0f, 1f)] public float damageReduction;

        [Tooltip("What a raised guard lets through for whoever carries this. 0 is the " +
                 "ordinary chip, which is what everything that is not a shield wants.")]
        [Range(0f, 1f)] public float blockChip;

        [Tooltip("Extra frames on the parry window. The shield's reason to exist.")]
        public int parryBonusFrames;

        [Header("Utility")]
        public int heal;

        static Fix M(float v) => Fix.FromMilli(Mathf.RoundToInt(v * 1000f));
        static int Permille(float v) => Mathf.RoundToInt(Mathf.Clamp01(v) * 1000f);

        public ItemDef ToDef() => new ItemDef
        {
            Id = item,
            Kind = kind,
            Tier = (byte)Mathf.Clamp(tier, 0, 3),
            Weight = Mathf.Max(0, weight),
            Damage = M(damage),
            AttackFrames = Mathf.Max(1, Mathf.RoundToInt(attackSeconds *
                                                         PlayerMotor.TicksPerSecond)),
            Reach = M(reach),
            Durability = Mathf.Max(0, durability),
            PrySpeed = M(prySpeed),
            BlockPierce = Permille(blockPierce),
            DamageReduction = M(damageReduction),
            BlockChipPermille = Permille(blockChip),
            ParryBonusFrames = Mathf.Max(0, parryBonusFrames),
            Heal = Mathf.Max(0, heal)
        };

        /// <summary>Fills this file in from the code table. Used when the set is generated.</summary>
        public void ReadFrom(in ItemDef d)
        {
            item = d.Id;
            kind = d.Kind;
            tier = d.Tier;
            weight = d.Weight;
            damage = d.Damage.ToMilli() / 1000f;
            attackSeconds = d.AttackFrames / (float)PlayerMotor.TicksPerSecond;
            reach = d.Reach.ToMilli() / 1000f;
            durability = d.Durability;
            prySpeed = d.PrySpeed.ToMilli() / 1000f;
            blockPierce = d.BlockPierce / 1000f;
            damageReduction = d.DamageReduction.ToMilli() / 1000f;
            blockChip = d.BlockChipPermille / 1000f;
            parryBonusFrames = d.ParryBonusFrames;
            heal = d.Heal;
        }
    }
}
