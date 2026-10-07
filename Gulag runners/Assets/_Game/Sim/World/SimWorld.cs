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
        /// Where each ladder's centre line is at its foot and at its head, parallel to Ladders.
        /// A ladder leaning against a wall is the normal way to model one, and its bounding box
        /// is wider than the ladder: climbing the middle of the box means climbing off the
        /// rungs and out into the air. Empty means every ladder is upright.
        /// </summary>
        public Span[] LadderLean = System.Array.Empty<Span>();

        /// <summary>
        /// The same for depth: where a ladder's centre line sits in Z at its foot and at its
        /// head. A ladder propped against a walkway leans AWAY from the camera as it rises as
        /// well as along the arena, and a climb that holds one depth the whole way up pushes
        /// the body through the rungs near one end of it.
        /// </summary>
        public Span[] LadderLeanZ = System.Array.Empty<Span>();

        /// <summary>
        /// Chests, in the order the scene lists them. The order is part of the world: a round's
        /// chest states are an array parallel to this one, so both devices must agree on it.
        /// </summary>
        public ChestDef[] Chests = System.Array.Empty<ChestDef>();

        public Span SolidSpan(int i) => i < SolidZ.Length ? SolidZ[i] : Span.Everywhere;
        public Span OneWaySpan(int i) => i < OneWayZ.Length ? OneWayZ[i] : Span.Everywhere;
        public Span LadderSpan(int i) => i < LadderZ.Length ? LadderZ[i] : Span.Everywhere;

        public Fix LadderCentreX(int index) => LadderCentreAt(index, Fix.Zero, false);

        /// <summary>
        /// The middle of a ladder at a given height. Upright, that is the middle of its box;
        /// leaning, it walks across as you climb, which is the whole difference between
        /// climbing the rungs and climbing the air beside them.
        /// </summary>
        public Fix LadderCentreAt(int index, Fix y, bool atHeight = true)
        {
            Aabb l = Ladders[index];
            if (!atHeight || index >= LadderLean.Length)
                return l.MinX + (l.MaxX - l.MinX) / 2;
            return LeanAt(LadderLean[index], in l, y);
        }

        /// <summary>
        /// The depth of a ladder's centre line at a given height — where the rungs are.
        ///
        /// Not the nearest point inside its depth span: a ladder that leans has a span as deep
        /// as the whole lean, so "stay somewhere inside it" lets the body hang a metre off the
        /// rungs at one end and inside them at the other. Upright, the line is the middle of
        /// the box and there is nothing to follow.
        /// </summary>
        public Fix LadderDepthAt(int index, Fix y, Fix fallback, Fix inset)
        {
            if (index < 0 || index >= Ladders.Length) return fallback;
            if (index >= LadderLeanZ.Length) return LadderSpan(index).Nearest(fallback, inset);

            Aabb l = Ladders[index];
            return LeanAt(LadderLeanZ[index], in l, y);
        }

        /// <summary>Where a lean line stands at a height, clamped to the box's own ends.</summary>
        static Fix LeanAt(Span lean, in Aabb box, Fix y)
        {
            Fix height = box.MaxY - box.MinY;
            if (height <= Fix.Zero) return lean.Min;

            Fix t = y - box.MinY;
            if (t < Fix.Zero) t = Fix.Zero;
            if (t > height) t = height;

            return lean.Min + (lean.Max - lean.Min) * t / height;
        }
    }
}
