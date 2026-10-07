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

        /// <summary>
        /// How deep each solid reaches, parallel to Solids. Empty means a world with no depth
        /// at all, where everything is on every slice — which is exactly the greybox, and the
        /// reason none of this changes it.
        /// </summary>
        public Span[] SolidZ = System.Array.Empty<Span>();

        /// <summary>Platforms you can jump up through and drop down from.</summary>
        public Aabb[] OneWay = System.Array.Empty<Aabb>();
        public Span[] OneWayZ = System.Array.Empty<Span>();

        /// <summary>Ladders and hatches: the connections between floors (docs/05).</summary>
        public Aabb[] Ladders = System.Array.Empty<Aabb>();
        public Span[] LadderZ = System.Array.Empty<Span>();

        /// <summary>
        /// Chests, in the order the scene lists them. The order is part of the world: a round's
        /// chest states are an array parallel to this one, so both devices must agree on it.
        /// </summary>
        public ChestDef[] Chests = System.Array.Empty<ChestDef>();

        public Span SolidSpan(int i) => i < SolidZ.Length ? SolidZ[i] : Span.Everywhere;
        public Span OneWaySpan(int i) => i < OneWayZ.Length ? OneWayZ[i] : Span.Everywhere;
        public Span LadderSpan(int i) => i < LadderZ.Length ? LadderZ[i] : Span.Everywhere;

        /// <summary>Index of the ladder the box touches on this slice of the level, or -1.</summary>
        public int FindLadder(in Aabb body, Fix depth, Fix half)
        {
            for (int i = 0; i < Ladders.Length; i++)
                if (body.Overlaps(in Ladders[i]) && LadderSpan(i).Reaches(depth, half)) return i;
            return -1;
        }

        public Fix LadderCentreX(int index)
        {
            Aabb l = Ladders[index];
            return l.MinX + (l.MaxX - l.MinX) / 2;
        }
    }
}
