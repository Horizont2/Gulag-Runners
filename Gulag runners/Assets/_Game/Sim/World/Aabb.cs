namespace GulagRunners.Sim
{
    /// <summary>Axis-aligned box on the gameplay plane. Min is inclusive, Max is exclusive-ish.</summary>
    public struct Aabb
    {
        public Fix MinX, MinY, MaxX, MaxY;

        public Aabb(Fix minX, Fix minY, Fix maxX, Fix maxY)
        {
            MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
        }

        /// <summary>Builds a box from a centre on X and a bottom on Y, which is how a body is described.</summary>
        public static Aabb FromFeet(FixVec2 feet, Fix width, Fix height)
        {
            Fix half = width / 2;
            return new Aabb(feet.X - half, feet.Y, feet.X + half, feet.Y + height);
        }

        public bool Overlaps(in Aabb o) =>
            MinX < o.MaxX && MaxX > o.MinX && MinY < o.MaxY && MaxY > o.MinY;

        public bool Contains(FixVec2 p) =>
            p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
    }
}
