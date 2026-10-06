namespace GulagRunners.Sim
{
    /// <summary>
    /// The static collision of one arena, as plain data.
    /// Built once from the seed (or, for now, baked from the Unity scene) and never mutated
    /// during a tick, so both clients hold an identical world without any network traffic.
    /// See docs/05-arena-generation.md.
    /// </summary>
    public sealed class SimWorld
    {
        /// <summary>Walls, floors and ceilings. Block from every direction.</summary>
        public Aabb[] Solids = System.Array.Empty<Aabb>();

        /// <summary>Platforms you can jump up through and drop down from.</summary>
        public Aabb[] OneWay = System.Array.Empty<Aabb>();

        /// <summary>Ladders and hatches: the connections between floors (docs/05).</summary>
        public Aabb[] Ladders = System.Array.Empty<Aabb>();

        /// <summary>
        /// Chests, in the order the scene lists them. The order is part of the world: a round's
        /// chest states are an array parallel to this one, so both devices must agree on it.
        /// </summary>
        public ChestDef[] Chests = System.Array.Empty<ChestDef>();

        /// <summary>Index of the ladder the box touches, or -1.</summary>
        public int FindLadder(in Aabb body)
        {
            for (int i = 0; i < Ladders.Length; i++)
                if (body.Overlaps(in Ladders[i])) return i;
            return -1;
        }

        public Fix LadderCentreX(int index)
        {
            Aabb l = Ladders[index];
            return l.MinX + (l.MaxX - l.MinX) / 2;
        }
    }
}
