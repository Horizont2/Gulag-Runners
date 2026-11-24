using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// What shape a model is, read off its own meshes: which way it points, which way it is
    /// flat, and how far past the hand it reaches.
    ///
    /// One place, because three things need the same answer and they must not be allowed to
    /// disagree: <see cref="ItemModels"/> scales a weapon by it, <see cref="WeaponSocket"/>
    /// grips it by it, and <see cref="PlayerArm"/> turns the hand by it. When they disagreed
    /// the weapon was drawn along one line and swung along another, which is the one kind of
    /// wrong a player cannot work around.
    ///
    /// MEASURED rather than configured, and the measurement is stated here so it can be
    /// argued with:
    ///
    ///   WHICH WAY IT POINTS is the direction the mesh reaches FURTHEST FROM THE GRIP.
    ///       The grip is the model's own origin -- every weapon in a pack is authored held,
    ///       because that is the only way a weapon prefab can be dropped onto a hand -- so
    ///       the question "which way does this point" is "which way is there most of it".
    ///
    ///       Three older rules are still worth naming, because all three shipped and all
    ///       three were visibly wrong on the same two weapons:
    ///
    ///       the LONGEST AXIS OF THE BOX measures hi-lo, which counts the butt behind the
    ///           hand as part of the blade and counts an axe head reaching sideways as
    ///           length. On the pack this game ships, the axe's box is 0.68 across and 0.87
    ///           along -- close enough that the rule refused to answer at all -- and the
    ///           scythe's 0.66 against 0.88 passed and came out 37 degrees off the haft.
    ///       the DIRECTION TO THE BOX'S CENTRE is pulled sideways by anything heavy at the
    ///           top. It is exact for the nine symmetrical weapons and 35 and 45 degrees out
    ///           on the axe and the scythe, whose heads hang off one side.
    ///       the DIRECTION OF THE FURTHEST VERTEX is the subtlest of the three and was the
    ///           last to be believed. It is exact for a sword, whose furthest point IS its
    ///           tip, and it points at the far corner of the head on anything that has one:
    ///           36.7 degrees off the haft on the axe, 37.3 on the scythe, 11.7 on the mace.
    ///           It replaced the box rule precisely because the box cannot see a model
    ///           authored a few degrees off its own axis -- and traded that small error for
    ///           a large one. What it was genuinely better at, the cross section, is kept:
    ///           see Spread.
    ///
    ///       Reach from the grip is exact for all eleven: +Y, by between 1.21x (the scythe)
    ///       and 13.45x (the spear) over the furthest it reaches any other way.
    ///
    ///   WHICH WAY IT IS FLAT is the thinnest axis across that one. A sword, an axe and a
    ///       scythe are all a plane with a handle; which way that plane faces decides
    ///       whether the player sees a blade or a line, and nothing was deciding it. Round
    ///       weapons -- the mace, the club, the flanged mace -- have no flat and say so,
    ///       which is also an answer: their roll is free and may be left to the clip.
    ///
    /// Off sharedMesh rather than Renderer.bounds, because every model this runs on is
    /// switched off at the time and an inactive renderer has no bounds.
    /// </summary>
    public static class ModelShape
    {
        /// <summary>
        /// How much further a weapon must reach along its own length than across it before
        /// that counts as pointing somewhere. 1.0 would answer for a cube; the tightest
        /// real weapon in the pack is the scythe at 1.21.
        /// </summary>
        public const float ClearlyLongest = 1.10f;

        /// <summary>
        /// And how much wider than thick it must be to count as having a flat. Below this
        /// it is a round thing and the roll genuinely does not matter.
        ///
        /// Measured over the eleven weapons this game ships, on the BLADE rather than on
        /// the whole model (see <see cref="BladeFrom"/>), the two groups land a long way
        /// either side of it: the three round ones are 1.00, 1.05 and 1.14, and the eight
        /// flat ones are 2.00, 2.48, 3.25, 5.20, 7.97, 11.15, 14.09 and 20.12. There is
        /// nothing between 1.14 and 2.00, so this number is not a tuned threshold -- it is
        /// a line drawn through the middle of an empty gap.
        /// </summary>
        public const float ClearlyFlat = 1.40f;

        /// <summary>
        /// Where the blade starts, as a share of the reach out of the grip. The cross
        /// section is measured past this and nowhere else.
        ///
        /// THIS IS THE WHOLE FIX FOR A SWORD DRAWN EDGE-ON, and the model that proves it is
        /// the pack's gladius. Its blade is 5.2 times wider than it is thick -- as flat as
        /// anything in the game -- and its pommel and guard are round, 13.0 cm either way
        /// against the blade's 10.3. The guard is therefore the widest thing on the model in
        /// BOTH directions, so the whole-model cross section came out 13.0 by 13.0, a ratio
        /// of exactly 1.00, and the measurement called a short sword round and handed its
        /// roll to the animation clip. A weapon whose roll is left to the clip is a weapon
        /// that is flat-on in one pose and edge-on in the next, which in a side-on game is
        /// a sword that is sometimes a line.
        ///
        /// The grip end of a weapon is round BY DESIGN -- a pommel, a ring guard, a wrapped
        /// handle -- and that is exactly the part of the model that says nothing about which
        /// way the steel faces. So it is not measured. What the player is looking at is the
        /// blade, and the blade is the part that is not in the fist.
        ///
        /// A half, and the answer does not depend on it: at any share between a quarter and
        /// three fifths every one of the eleven weapons gives the same verdict, and the
        /// three round ones never move off 1.00 to 1.14 at all. A half is simply the most
        /// defensible sentence -- the outer half of the weapon is the half further from the
        /// hand.
        /// </summary>
        public const float BladeFrom = 0.5f;

        /// <summary>How a model is held, in the model root's own space.</summary>
        public struct Grip
        {
            /// <summary>Which way the weapon points. Unit, along one of the model's axes.</summary>
            public Vector3 Blade;

            /// <summary>The normal of its flat, or zero when it is round.</summary>
            public Vector3 Flat;

            /// <summary>From the grip to the far end, along <see cref="Blade"/>.</summary>
            public float Reach;

            /// <summary>And the other way: the butt that sticks out behind the hand.</summary>
            public float Behind;

            /// <summary>The widest it is across the blade.</summary>
            public float Across;

            /// <summary>And the thinnest, which is the flat if it has one.</summary>
            public float Thick;

            /// <summary>
            /// Where the steel actually ENDS: the offset from the grip to the model's
            /// furthest vertex, in the model root's own space.
            ///
            /// Not the same thing as <see cref="Blade"/> times <see cref="Reach"/>, and the
            /// difference is the whole reason this is here. Blade is the axis the weapon is
            /// gripped along and Reach is how far it goes along it -- which is what the
            /// hitbox is, so it is what the fit matches. A head that hangs off the side
            /// reaches FURTHER than that, diagonally, and on the pack's axe it reaches a
            /// quarter further: 54 cm of drawn steel on a 43.5 cm segment. The player sees
            /// the axe connect and nothing happens.
            ///
            /// From the vertices when the importer kept them, and from the box's furthest
            /// corner when it did not -- which over-states it, and over-stating is the safe
            /// direction for a number whose only job is to be squeezed under a cap.
            /// </summary>
            public Vector3 Corner;
        }

        /// <summary>The model's extent in its own root's space, meshes and all.</summary>
        public static bool Box(GameObject root, out Bounds bounds)
        {
            bounds = default;
            if (root == null) return false;

            bool any = false;
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;

            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null)
                    any = Grow(ref bounds, any, mf.sharedMesh.bounds,
                               toRoot * mf.transform.localToWorldMatrix);

            foreach (SkinnedMeshRenderer smr in
                     root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null)
                    any = Grow(ref bounds, any, smr.sharedMesh.bounds,
                               toRoot * smr.transform.localToWorldMatrix);

            return any && bounds.size.sqrMagnitude > 1e-8f;
        }

        /// <summary>
        /// How this model is held, or false when it will not say clearly.
        ///
        /// FALSE MATTERS AS MUCH AS THE ANSWER, and that is the whole reason it is allowed
        /// to refuse. PlayerArm turns the hand by the rotation that puts the blade on the
        /// arm's line, so a wrong answer wrenches the wrist by up to a right angle -- and
        /// Unity's humanoid solver will bend the forearm and the shoulder to honour a hand
        /// rotation it cannot otherwise reach. The visible result is an arm that refuses to
        /// go below horizontal on some weapons and not others, which is exactly how this
        /// was first reported.
        /// </summary>
        public static bool Measure(GameObject root, out Grip grip)
        {
            grip = default;

            // WHICH WAY IT POINTS IS THE AXIS IT REACHES FURTHEST ALONG, and the rule that
            // used to stand here instead -- the direction of the furthest VERTEX -- is why
            // three weapons hung crooked in the hand however the socket was tuned.
            //
            // The argument for the vertex was that the furthest point from the grip is the
            // tip, so the line to it is the weapon. That is true of a sword and false of
            // anything with a head. An axe's furthest vertex is the far CORNER OF ITS
            // BLADE, which hangs off the side of the haft; a mace's is a spike on the ball.
            // Measured on the eleven weapons this game ships, as the angle between the
            // vertex rule's answer and the weapon's own haft:
            //
            //   axe 36.7 degrees, scythe 37.3, mace 11.7, club 3.3, hammer 2.9, rest 0.0
            //
            // Those first three are exactly the ones reported as not pointing where the arm
            // points, and no amount of turning the socket could have fixed them: PlayerArm
            // overwrites the hand's rotation every frame with the one that puts THIS answer
            // on the arm's line, so the answer is the only thing that decides.
            //
            // Reach from the grip, along the model's own axes, is exact for all eleven: +Y
            // every time, by between 1.21x (the scythe) and 13.45x (the spear) over the
            // furthest the model reaches any other way. A head that hangs off the side
            // cannot beat a haft along its own length, which is the property the vertex
            // rule threw away.
            //
            // What the vertices are still read for is the cross section -- see Spread --
            // because the box genuinely cannot say which way a blade FACES when the model
            // was authored at an angle. A model whose haft is not on an axis at all, like
            // this pack's scythe, is what ItemModel.bladeAxis is for; the measurement is
            // allowed to be approximate as long as it is never confidently wrong.
            if (!Box(root, out Bounds box)) return false;

            Vector3 lo = box.min, hi = box.max;

            // The six ways out of the grip. Ordered so that i ^ 1 is the other end of the
            // same axis, which is what the butt behind the hand is measured as.
            float[] reach = { hi.x, -lo.x, hi.y, -lo.y, hi.z, -lo.z };

            int far = 0;
            for (int i = 1; i < 6; i++)
                if (reach[i] > reach[far]) far = i;

            int axis = far >> 1;
            float across = 0f;
            for (int i = 0; i < 6; i++)
                if ((i >> 1) != axis && reach[i] > across) across = reach[i];

            if (reach[far] <= 0f) return false;
            if (reach[far] < across * ClearlyLongest) return false;

            grip.Blade = Axis(axis) * ((far & 1) == 1 ? -1f : 1f);
            grip.Reach = reach[far];
            grip.Behind = Mathf.Max(0f, reach[far ^ 1]);

            // The box's own furthest corner, as the fallback. Spread overwrites it with the
            // real furthest vertex whenever the mesh can be read, which is every weapon
            // this game ships.
            grip.Corner = new Vector3(
                Mathf.Abs(hi.x) >= Mathf.Abs(lo.x) ? hi.x : lo.x,
                Mathf.Abs(hi.y) >= Mathf.Abs(lo.y) ? hi.y : lo.y,
                Mathf.Abs(hi.z) >= Mathf.Abs(lo.z) ? hi.z : lo.z);

            // The cross section about that line, from the vertices when the importer kept
            // them and from the box when it did not. Past the grip, which is where the
            // steel is -- see BladeFrom.
            if (Spread(root, grip.Blade, grip.Reach, ref grip)) return true;

            Vector3 size = box.size;
            int a = (axis + 1) % 3, b = (axis + 2) % 3;
            int thin = size[a] <= size[b] ? a : b;
            int wide = thin == a ? b : a;

            grip.Across = size[wide];
            grip.Thick = size[thin];
            grip.Flat = grip.Across >= grip.Thick * ClearlyFlat ? Axis(thin) : Vector3.zero;
            return true;
        }

        /// <summary>
        /// The cross section about a line that has already been chosen: how wide the model
        /// is across it, how thin, and which way that thin direction faces. False when the
        /// vertices cannot be read, and the caller falls back to the box.
        ///
        /// Mesh data is only there when the importer was told to keep it (Read/Write), so
        /// this is allowed to fail. The eleven weapons this game ships are marked readable;
        /// a pack dropped in tomorrow may not be, and its roll will be left to the clip
        /// rather than decided wrongly.
        ///
        /// It does NOT touch Blade or Reach. It used to choose both, by taking the furthest
        /// vertex as the tip, and that is the fault this file now exists to not repeat: a
        /// cloud is the right thing to ask how wide a blade is and the wrong thing to ask
        /// which way a weapon points, because the furthest point of an axe is a corner of
        /// its head.
        /// </summary>
        static bool Spread(GameObject root, Vector3 blade, float reach, ref Grip grip)
        {
            if (root == null) return false;

            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
            Vector3 u = Mathf.Abs(blade.x) < 0.9f
                ? Vector3.Normalize(Vector3.Cross(blade, Vector3.right))
                : Vector3.Normalize(Vector3.Cross(blade, Vector3.up));
            Vector3 v = Vector3.Cross(blade, u);

            // Where the steel starts. Everything nearer the hand than this is grip, and a
            // grip is round on purpose -- see BladeFrom for the model that made this
            // necessary.
            float from = reach > 0f ? reach * Mathf.Clamp01(BladeFrom) : float.NegativeInfinity;

            float uMin = 0f, uMax = 0f, vMin = 0f, vMax = 0f;
            float uu = 0f, vv = 0f, uv = 0f;
            int n = 0;
            int all = 0;
            Vector3 corner = Vector3.zero;

            // Twice over when it has to be: once past the grip, and -- only if that left
            // too little to measure -- over the whole model, which is what this did before.
            // A weapon so short or so oddly authored that half of it holds no vertices is a
            // weapon the old answer was no worse about.
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = mf.sharedMesh;
                    if (mesh == null || !mesh.isReadable) continue;

                    Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                    Vector3[] points = mesh.vertices;
                    for (int i = 0; i < points.Length; i++)
                    {
                        Vector3 p = m.MultiplyPoint3x4(points[i]);
                        if (pass == 0)
                        {
                            all++;

                            // The furthest vertex, over the WHOLE model and not just the
                            // blade region: it is where the drawn steel ends, which is a
                            // question about the model and not about the grip.
                            if (p.sqrMagnitude > corner.sqrMagnitude) corner = p;
                        }
                        if (pass == 0 && Vector3.Dot(p, blade) < from) continue;

                        float a = Vector3.Dot(p, u), b = Vector3.Dot(p, v);
                        if (n == 0) { uMin = uMax = a; vMin = vMax = b; }
                        else
                        {
                            if (a < uMin) uMin = a;
                            if (a > uMax) uMax = a;
                            if (b < vMin) vMin = b;
                            if (b > vMax) vMax = b;
                        }
                        uu += a * a; vv += b * b; uv += a * b;
                        n++;
                    }
                }

                // Enough of the blade to mean something. Eight is a box; fewer than that and
                // one stray vertex decides which way a sword faces.
                //
                // The reset below only ever happens between the two passes. Letting it run
                // at the end of the second would throw away the answer it had just found and
                // report no vertices at all.
                if (n >= 8 || pass == 1 || all == 0) break;

                uMin = uMax = vMin = vMax = 0f;
                uu = vv = uv = 0f;
                n = 0;
            }

            if (n == 0) return false;

            if (corner.sqrMagnitude > 1e-12f) grip.Corner = corner;

            float wide = uMax - uMin, thin = vMax - vMin;
            grip.Across = Mathf.Max(wide, thin);
            grip.Thick = Mathf.Min(wide, thin);
            grip.Flat = Vector3.zero;

            // WHICH WAY IT IS FLAT, from the spread of the cloud rather than from the box.
            //
            // A sword's blade is a plane, and a plane authored at 20 degrees to the model's
            // axes has a BOX that is nearly square -- so the box rule calls it round and
            // leaves the roll to chance. The second moments do not care how the model was
            // oriented: the smaller eigenvector of the two-by-two across the blade is the
            // normal of the plane, whatever angle it sits at.
            if (grip.Across >= grip.Thick * ClearlyFlat)
            {
                uu /= n; vv /= n; uv /= n;
                float half = 0.5f * (uu + vv);
                float diff = 0.5f * (uu - vv);
                float r = Mathf.Sqrt(diff * diff + uv * uv);
                float small = half - r;                      // the lesser eigenvalue

                // Its eigenvector, taken the stable way: for a 2x2 the row that is not
                // degenerate gives it, and uv == 0 means the axes are already the answer.
                Vector2 e = Mathf.Abs(uv) > 1e-12f
                    ? new Vector2(small - vv, uv).normalized
                    : (uu <= vv ? new Vector2(1f, 0f) : new Vector2(0f, 1f));

                grip.Flat = (u * e.x + v * e.y).normalized;
            }

            return true;
        }

        static Vector3 Axis(int i) =>
            i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        static bool Grow(ref Bounds bounds, bool any, Bounds mesh, Matrix4x4 m)
        {
            Vector3 c = mesh.center, e = mesh.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x,
                                                 (i & 2) == 0 ? -e.y : e.y,
                                                 (i & 4) == 0 ? -e.z : e.z);
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!any && i == 0) bounds = new Bounds(p, Vector3.zero);
                else bounds.Encapsulate(p);
                any = true;
            }
            return any;
        }
    }
}
