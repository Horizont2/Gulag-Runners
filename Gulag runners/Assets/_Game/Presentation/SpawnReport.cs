using System.Text;
using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Why a fighter cannot stand up, walk, or climb where the scene says they should.
    ///
    /// Every bug this project has had in collision has looked the same from the scene view:
    /// the art is right there, and the fighter falls through it, is shoved out of it, or walks
    /// past a ladder as though it were painted on. The scene view cannot show the reason,
    /// because the reason is never the art — it is what the bake made of it, and that is a
    /// flat list of boxes with a depth span each.
    ///
    /// So this asks the simulation the same questions the motor asks, at one position, and
    /// writes the answers down. In particular it asks each one TWICE — once as the motor asks
    /// it, and once with depth ignored — because the difference between those two answers is
    /// the diagnosis. A floor that exists but is off the lane and a floor that does not exist
    /// are the same fall, and they want opposite fixes.
    /// </summary>
    public static class SpawnReport
    {
        /// <summary>
        /// A full report for one body standing at one place on one lane.
        /// <paramref name="searchDistance"/> is how far down to look for a floor, in metres.
        /// </summary>
        public static string Explain(string who, SimWorld world, in MoveConfig cfg,
                                     Vector2 feet, float depth, float searchDistance)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(who).Append(" — spawn report\n");

            if (world == null)
            {
                sb.Append("  no world: nothing is baked, so nothing can be stood on.");
                return sb.ToString();
            }

            FixVec2 position = new FixVec2(ToFix(feet.x), ToFix(feet.y));
            Fix z = ToFix(depth);
            Fix halfZ = cfg.BodyDepth / 2;
            Aabb body = Aabb.FromFeet(position, cfg.BodyWidth, cfg.BodyHeight);

            sb.Append($"  at      x {feet.x:0.00}  y {feet.y:0.00}  on lane z {depth:0.00}\n");
            sb.Append($"  body    {M(cfg.BodyWidth)} wide, {M(cfg.BodyHeight)} tall, " +
                      $"{M(cfg.BodyDepth)} deep; lane reach {M(cfg.LaneReach)}\n");
            sb.Append($"  world   {world.Solids.Length} solid, {world.OneWay.Length} one-way, " +
                      $"{world.Ladders.Length} ladder, {world.Gates.Length} gate\n");

            Ground(sb, world, in cfg, position, z, searchDistance);
            Stuck(sb, world, in cfg, in body, z, halfZ);
            Ladders(sb, world, in cfg, in body, z);

            return sb.ToString();
        }

        /// <summary>Is there a floor under the feet, and if not, is there one off the lane.</summary>
        static void Ground(StringBuilder sb, SimWorld world, in MoveConfig cfg,
                           FixVec2 position, Fix z, float searchDistance)
        {
            sb.Append("\n  GROUND\n");

            Fix reach = ToFix(searchDistance);
            if (PlayerMotor.TryFindGroundBelow(position, z, world, in cfg, reach, out Fix y))
            {
                float drop = (position.Y - y).ToMilli() / 1000f;
                sb.Append($"    floor at y {M(y)}, {drop:0.00} m below the feet. Fine.\n");
                return;
            }

            sb.Append($"    NO floor within {searchDistance:0.00} m below. This fighter falls.\n");

            // The same search with the lane test taken out. If this finds something, the floor
            // is modelled — it is simply not on this fighter's slice of the level, which is a
            // different mistake with a different fix.
            int best = -1;
            Fix bestY = Fix.Zero;
            Fix half = cfg.BodyWidth / 2;
            Fix lowest = position.Y - reach;

            for (int i = 0; i < world.Solids.Length; i++)
            {
                Aabb s = world.Solids[i];
                if (!world.SolidLive(i)) continue;
                if (s.MinX >= position.X + half || s.MaxX <= position.X - half) continue;
                if (s.MaxY > position.Y || s.MaxY < lowest) continue;
                if (best >= 0 && s.MaxY <= bestY) continue;
                best = i;
                bestY = s.MaxY;
            }

            if (best < 0)
            {
                sb.Append("    Nothing is modelled under this spot at all, on any lane. " +
                          "The floor is missing from the bake, not from the lane.\n");
                return;
            }

            Span span = world.SolidSpan(best);
            sb.Append($"    But solid #{best} IS under it, at y {M(bestY)}, " +
                      $"z {M(span.Min)}..{M(span.Max)}.\n");
            sb.Append($"    -> It is off this lane: the body stands at z {M(z)} and reaches " +
                      $"{M(cfg.BodyDepth / 2)} either side. Either move the fighter's Plane Z " +
                      "onto the floor, or move the floor onto the plane. This is the usual " +
                      "cause of a fighter who falls through art they are standing on.\n");
        }

        /// <summary>Is the body inside something — the reason a first step snaps back.</summary>
        static void Stuck(StringBuilder sb, SimWorld world, in MoveConfig cfg,
                          in Aabb body, Fix z, Fix halfZ)
        {
            sb.Append("\n  STUCK\n");

            int live = 0, offLane = 0, firstLive = -1, firstOff = -1;
            for (int i = 0; i < world.Solids.Length; i++)
            {
                if (!body.Overlaps(in world.Solids[i])) continue;

                if (world.SolidAt(i, z, halfZ))
                {
                    if (firstLive < 0) firstLive = i;
                    live++;
                }
                else
                {
                    if (firstOff < 0) firstOff = i;
                    offLane++;
                }
            }

            if (live == 0)
            {
                sb.Append("    The body is clear of every live solid.\n");
                if (offLane > 0)
                    sb.Append($"    ({offLane} solid(s) overlap it on other lanes, which is " +
                              "normal: that is the level behind and in front of the fight.)\n");
                return;
            }

            Aabb s = world.Solids[firstLive];
            Span span = world.SolidSpan(firstLive);
            sb.Append($"    The body is INSIDE {live} live solid(s). First one, #{firstLive}:\n");
            sb.Append($"      x {M(s.MinX)}..{M(s.MaxX)}  y {M(s.MinY)}..{M(s.MaxY)}  " +
                      $"z {M(span.Min)}..{M(span.Max)}\n");
            sb.Append("    -> Nothing pushes a body out of a box it already overlaps, so every " +
                      "step is refused and the fighter is put back. If this box is the ground " +
                      "slab the floor below was dug into, the slab still fills that space: " +
                      "either hollow it, or mark the part in the way Ignore.\n");
        }

        /// <summary>Every ladder in reach, and for the ones that are not, why not.</summary>
        static void Ladders(StringBuilder sb, SimWorld world, in MoveConfig cfg,
                            in Aabb body, Fix z)
        {
            sb.Append("\n  LADDERS\n");

            if (world.Ladders.Length == 0)
            {
                sb.Append("    None in the whole world. Anything meant to be a ladder baked as " +
                          "something else — a plain collider becomes a ladder only through a " +
                          "SimCollider set to Ladder, or a layer in the baker's Ladder Layers. " +
                          "With neither, it bakes SOLID, which is exactly a ladder you cannot " +
                          "climb and can walk into.\n");
                return;
            }

            int touching = 0;
            for (int i = 0; i < world.Ladders.Length; i++)
            {
                Aabb l = world.Ladders[i];
                if (!body.Overlaps(in l)) continue;
                touching++;

                Span span = world.LadderSpan(i);
                bool ok = PlayerMotor.LaneFor(span, z, in cfg, out Fix lane);
                float away = (lane - z).ToMilli() / 1000f;

                sb.Append($"    #{i} x {M(l.MinX)}..{M(l.MaxX)}  y {M(l.MinY)}..{M(l.MaxY)}  " +
                          $"z {M(span.Min)}..{M(span.Max)}  ");
                sb.Append(ok
                    ? "touching, lane reachable — CLIMBABLE\n"
                    : $"touching, but its lane is {away:0.00} m away, past the " +
                      $"{M(cfg.LaneReach)} reach — OUT OF LANE\n");
            }

            if (touching == 0)
                sb.Append("    None of them touches the body here. A ladder is mounted by " +
                          "standing in its box or on top of it, so check the ladder's X and Y " +
                          "against the feet above.\n");
        }

        static Fix ToFix(float v) => Fix.FromMilli(Mathf.RoundToInt(v * 1000f));
        static string M(Fix v) => (v.ToMilli() / 1000f).ToString("0.00");
    }
}
