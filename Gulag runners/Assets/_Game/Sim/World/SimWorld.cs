namespace GulagRunners.Sim
{
    // ---------------------------------------------------------------- the shutters (docs/01)
    //
    // These live here, in the file of the thing that holds them, rather than in one of their
    // own. They are three small types describing a piece of SimWorld, and a separate file for
    // them bought nothing except one more asset that has to be imported before the arena will
    // compile at all — which is exactly what went wrong: a build with SimWorld.cs present and
    // GateDef.cs not yet imported fails on this one line and nowhere else, and the message
    // sends you looking for a missing using directive that was never the problem.

    /// <summary>When a gate shuts. docs/01 gives the last minute two separate beats.</summary>
    public enum GateWhen : byte
    {
        /// <summary>
        /// During the closing phase, in order, spread across the shutter window: the outer
        /// rooms going one after another until only the middle is left.
        /// </summary>
        Closing = 0,

        /// <summary>
        /// On the first tick of sudden death — the ceiling of docs/01's central hall, which
        /// is what puts both fighters on one platform.
        /// </summary>
        SuddenDeath = 1
    }

    /// <summary>Which way a gate shoves a body that it shuts on, or behind.</summary>
    public enum GateToward : byte
    {
        MinusX = 0,
        PlusX = 1,
        MinusY = 2,
        PlusY = 3
    }

    /// <summary>
    /// One steel shutter: a piece of the arena that is not there until the clock says so.
    ///
    /// docs/01's closing phase shuts the outer rooms one after another until only the central
    /// hall is left. That is a change to collision, not a change to the picture, so it has to
    /// live in the deterministic layer — and the arena itself is baked once and never mutated,
    /// so a gate cannot be a box that gets added. Instead every gate's boxes ARE baked, from
    /// the start, and simply do not count until the gate's bit goes up: the only state is a
    /// bitmask, which both devices compute from the frame number and so can never disagree
    /// about (docs/06).
    ///
    /// The shove is the part that stops it being a bug. A shutter that merely appears can seal
    /// a fighter into a wing with no way out, which is not "the rooms are closing" — it is the
    /// match ending by accident. So on the tick a gate shuts, anything on its far side comes
    /// through.
    /// </summary>
    public struct GateDef
    {
        /// <summary>
        /// The doorway the shutter fills: the union of its boxes. Used for the shove and for
        /// the view, not for collision — collision is the baked solids that point at this gate.
        /// </summary>
        public Aabb Box;

        /// <summary>How deep it reaches.</summary>
        public Span Span;

        /// <summary>0 shuts first. Ties keep the order the scene listed them in.</summary>
        public int Order;

        /// <summary>Which beat of the last minute this gate belongs to.</summary>
        public GateWhen When;

        /// <summary>Which way a body caught on the wrong side is brought.</summary>
        public GateToward Toward;

        /// <summary>
        /// Where a body shoved by this gate ends up, given how wide and tall that body is: just
        /// clear of the shutter, on the side it herds towards.
        /// </summary>
        public FixVec2 Clear(FixVec2 feet, Fix halfWidth, Fix height, Fix skin)
        {
            switch (Toward)
            {
                case GateToward.MinusX: return new FixVec2(Box.MinX - halfWidth - skin, feet.Y);
                case GateToward.PlusX:  return new FixVec2(Box.MaxX + halfWidth + skin, feet.Y);
                case GateToward.MinusY: return new FixVec2(feet.X, Box.MinY - height - skin);
                default:                return new FixVec2(feet.X, Box.MaxY + skin);
            }
        }

        /// <summary>
        /// Is this body on the side the gate is shutting off? True for a body standing in the
        /// doorway as well as one stranded past it, because both have to come through.
        ///
        /// Judged on the body's own middle rather than its edges: a fighter brushing the
        /// shutter from the inside is already where the gate wants them, and shoving them for
        /// it would move them for free every time a wall shuts next to them.
        /// </summary>
        public bool Behind(FixVec2 feet, Fix height)
        {
            Fix midY = feet.Y + height / 2;
            switch (Toward)
            {
                case GateToward.MinusX: return feet.X > Box.MinX;
                case GateToward.PlusX:  return feet.X < Box.MaxX;
                case GateToward.MinusY: return midY > Box.MinY;
                default:                return midY < Box.MaxY;
            }
        }
    }

    /// <summary>
    /// A wall that closes the arena by GROWING into it.
    ///
    /// Three attempts to get this right, and each failure taught the same lesson a different
    /// way. Plates that switch on carve the room into cells. Thinner plates do the same thing
    /// in smaller steps. A solid wall that slides in reads better than both and still leaves
    /// dead ground behind it — the eye sees a slab travelling across a floor, with a gap
    /// opening up behind, and reads the gap as the level ending.
    ///
    /// What the arena closing actually looks like is the far end being eaten. So the wall is
    /// anchored at the arena's edge and its INNER FACE advances: at nothing it has no
    /// thickness at all, at full close it fills everything out to the edge of the last room.
    /// There is never a gap behind it, because behind it is the edge of the world.
    ///
    /// Still one box whose shape is a pure function of the frame, so nothing is allocated,
    /// nothing is mutated inside a tick, and two devices on the same frame compute the same
    /// wall to the millimetre (docs/06).
    /// </summary>
    public struct WallDef
    {
        /// <summary>The box at full close: from the arena's edge to the last room's face.</summary>
        public Aabb Box;

        /// <summary>How deep it reaches. A wall one lane deep is a wall you step behind.</summary>
        public Span Span;

        /// <summary>
        /// Which way the inner face advances — and therefore which way a body caught by it
        /// is pushed. The anchored side is the opposite one: a wall growing towards +X is
        /// anchored at its own MinX, which is the arena's left edge.
        /// </summary>
        public GateToward Grows;

        /// <summary>The wall at a given closeness, 0 nothing and 1 the whole box.</summary>
        public Aabb At(Fix progress)
        {
            Fix width = Box.MaxX - Box.MinX;
            Fix height = Box.MaxY - Box.MinY;

            switch (Grows)
            {
                case GateToward.PlusX:
                    return new Aabb(Box.MinX, Box.MinY, Box.MinX + width * progress, Box.MaxY);
                case GateToward.MinusX:
                    return new Aabb(Box.MaxX - width * progress, Box.MinY, Box.MaxX, Box.MaxY);
                case GateToward.MinusY:
                    return new Aabb(Box.MinX, Box.MaxY - height * progress, Box.MaxX, Box.MaxY);
                default:
                    return new Aabb(Box.MinX, Box.MinY, Box.MaxX, Box.MinY + height * progress);
            }
        }
    }

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

        /// <summary>
        /// The steel shutters of docs/01's closing phase, in the order the scene listed them.
        /// Empty is an arena that never closes, which is every arena until one is authored
        /// with gates in it.
        /// </summary>
        public GateDef[] Gates = System.Array.Empty<GateDef>();

        /// <summary>
        /// The walls that close the arena by moving. Empty is an arena that never closes.
        /// </summary>
        public WallDef[] Walls = System.Array.Empty<WallDef>();

        /// <summary>
        /// How shut the walls are, 0 to 1. The only other thing about the arena that changes
        /// during a match, and like <see cref="GatesShut"/> it is written from the clock
        /// before anything moves and is a pure function of the frame.
        /// </summary>
        public Fix WallProgress;

        /// <summary>Where wall i stands this tick.</summary>
        public Aabb WallAt(int i) => Walls[i].At(WallProgress);

        /// <summary>Is wall i in the way of a body on this slice?</summary>
        public bool WallReaches(int i, Fix depth, Fix halfDepth) =>
            Walls[i].Span.Reaches(depth, halfDepth);

        // ------------------------------------------------------------------ the poison
        //
        // The other way to take an arena away: from BELOW. Where a shutter is collision that
        // appears, the poison is no collision at all -- a level, and a cost for being under
        // it. So it needs no boxes, no gate bits and no shove; three numbers say everything
        // about it, and two of them are baked with the arena.

        /// <summary>
        /// The pool at full flood, as the arena was authored: where the surface starts
        /// (MinY), where it stops (MaxY), and how wide the flooded ground is.
        ///
        /// An empty box is an arena with no poison in it, which is every arena until one is
        /// authored with a pool — the same way an arena with no gates never closes.
        /// </summary>
        public Aabb Poison;

        /// <summary>How deep the pool reaches. A walkway behind it stays dry.</summary>
        public Span PoisonZ = Span.Everywhere;

        /// <summary>
        /// How full the pool is, 0 to 1. Written from the clock before anything moves, like
        /// <see cref="GatesShut"/> and <see cref="WallProgress"/>, and a pure function of the
        /// frame for the same reason: two devices cannot disagree about it and a rollback
        /// needs nothing but the frame to put the water back.
        /// </summary>
        public Fix PoisonRise;

        /// <summary>Is there a pool in this arena at all.</summary>
        public bool HasPoison => Poison.MaxY > Poison.MinY;

        /// <summary>Where the surface stands this tick.</summary>
        public Fix PoisonY => Poison.MinY + (Poison.MaxY - Poison.MinY) * PoisonRise;

        /// <summary>
        /// Is this body in the poison: under the surface, inside the flooded ground, and on a
        /// slice the pool reaches.
        ///
        /// The feet, not the chest. The surface is a line a body is either above or below, and
        /// a rule measured at the waist is a rule that has to explain itself to somebody
        /// standing in it up to the knees.
        /// </summary>
        public bool InPoison(FixVec2 feet, Fix depth, Fix halfDepth) =>
            HasPoison && feet.Y < PoisonY &&
            feet.X > Poison.MinX && feet.X < Poison.MaxX &&
            PoisonZ.Reaches(depth, halfDepth);

        /// <summary>
        /// Which gate each solid belongs to, parallel to Solids. -1 — and any solid past the
        /// end of this array — is a wall that was always there.
        /// </summary>
        public int[] SolidGate = System.Array.Empty<int>();

        /// <summary>
        /// Which gates are shut: one bit per gate, lowest bit for Gates[0].
        ///
        /// This is the only part of the arena that changes during a match, and it is one word.
        /// Whoever owns the tick writes it from the match clock before anything moves, and it
        /// is a pure function of the frame number, so two devices cannot drift apart over it
        /// (docs/06) and a rollback needs nothing but the frame to rebuild it.
        /// </summary>
        public uint GatesShut;

        /// <summary>
        /// Does this solid block anything yet. A solid belonging to a gate that has not shut is
        /// not there at all — which is how a shutter can be baked with the rest of the arena
        /// and still appear halfway through the match.
        /// </summary>
        public bool SolidLive(int i)
        {
            if (i >= SolidGate.Length) return true;
            int gate = SolidGate[i];
            return gate < 0 || (GatesShut & (1u << gate)) != 0;
        }

        /// <summary>
        /// Is solid i in the way of a body on this slice: both there yet, and deep enough.
        ///
        /// Every query in the motor goes through this rather than asking the span directly, so
        /// a gate that has not shut is skipped by all of them at once. Written the other way
        /// about — a gate check bolted onto each loop — the one loop somebody forgets is a
        /// shutter you can walk through.
        /// </summary>
        public bool SolidAt(int i, Fix depth, Fix halfDepth) =>
            SolidLive(i) && SolidSpan(i).Reaches(depth, halfDepth);

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

        /// <summary>
        /// Half a ladder's own THICKNESS — the rails, not the space it leans through.
        ///
        /// The depth span of a leaning ladder is its thickness PLUS however far it leans, and
        /// taking half of that for a stand-off is the whole of "he climbs the air beside it".
        /// Arena_Mine's ladders are 0.10 m thick and lean 0.9 m, so half the span is 0.50 and
        /// the body was parked 0.45 m in front of the rungs it was supposedly holding.
        ///
        /// The lean is already measured per ladder, so subtracting it is exact rather than a
        /// guess, and an upright ladder — where the lean is zero — is unchanged.
        /// </summary>
        public Fix LadderHalfThickness(int index)
        {
            Span rungs = LadderSpan(index);
            Fix depth = rungs.Max - rungs.Min;
            if (index >= 0 && index < LadderLeanZ.Length)
            {
                Span lean = LadderLeanZ[index];
                Fix walked = lean.Max - lean.Min;
                if (walked < Fix.Zero) walked = -walked;        // foot and head, not min and max
                depth = depth > walked ? depth - walked : Fix.Zero;
            }
            return depth / 2;
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
