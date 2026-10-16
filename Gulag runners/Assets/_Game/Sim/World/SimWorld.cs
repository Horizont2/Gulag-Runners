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
    /// A wall that closes the arena by moving, rather than by appearing.
    ///
    /// The shutters above are boxes that switch on. That is right for a steel slab dropping
    /// into a doorway and wrong for the thing docs/01 actually describes: the arena getting
    /// SMALLER. Built out of switching boxes, a closing arena is a row of plates, and a
    /// fighter reads each one as "a wall appeared" rather than as the space running out —
    /// and no number of thinner plates fixes that, it only makes the steps smaller.
    ///
    /// So a wall is one box whose POSITION is a pure function of the frame. That keeps every
    /// promise the baked arena makes: nothing is allocated, nothing is mutated during a tick,
    /// and two devices given the same frame compute the same wall to the millimetre (docs/06).
    /// The art reads the same function, so the picture cannot drift from the collision.
    /// </summary>
    public struct WallDef
    {
        /// <summary>Where it ends up: the box at full close.</summary>
        public Aabb Box;

        /// <summary>How deep it reaches. A wall one lane deep is a wall you step behind.</summary>
        public Span Span;

        /// <summary>
        /// How far it travels to get there, signed, along its axis. The open position is the
        /// closed one minus this, so a wall authored where it should END needs only this one
        /// number to say where it starts.
        /// </summary>
        public Fix Travel;

        /// <summary>False travels along X — a side wall. True along Y — a ceiling.</summary>
        public bool Vertical;

        /// <summary>
        /// Where it stands at a given closeness, 0 open and 1 shut.
        ///
        /// The box is authored SHUT, so open is the box minus the travel: a wall that moves
        /// +12 m to arrive started 12 m to the left of where it now is. Written the other way
        /// about — plus the travel at zero — the two walls begin on each other's side of the
        /// arena, which is not a subtle failure but is an easy one to type.
        /// </summary>
        public Aabb At(Fix progress)
        {
            Fix back = Travel * progress - Travel;
            return Vertical
                ? new Aabb(Box.MinX, Box.MinY + back, Box.MaxX, Box.MaxY + back)
                : new Aabb(Box.MinX + back, Box.MinY, Box.MaxX + back, Box.MaxY);
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
