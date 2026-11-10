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
    ///       Two older rules are still worth naming, because both of them shipped and both
    ///       of them were visibly wrong on the same two weapons:
    ///
    ///       the LONGEST AXIS OF THE BOX measures hi-lo, which counts the butt behind the
    ///           hand as part of the blade and counts an axe head reaching sideways as
    ///           length. On the pack this game ships, the axe's box is 0.68 across and 0.87
    ///           along -- close enough that the rule refused to answer at all -- and the
    ///           scythe's 0.66 against 0.88 passed and came out 37 degrees off the haft.
    ///       the DIRECTION TO THE BOX'S CENTRE is pulled sideways by anything heavy at the
    ///           top. It is exact for the nine symmetrical weapons and 35 and 45 degrees out
    ///           on the axe and the scythe, whose heads hang off one side.
    ///
    ///       Reach from the grip is exact for all eleven: +Y, by between 1.21x (the scythe)
    ///       and 7.1x (the sword) over the furthest it reaches any other way.
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
        /// it is a round thing and the roll genuinely does not matter -- the mace is 1.05,
        /// the sword 3.3, the axe 7.5.
        /// </summary>
        public const float ClearlyFlat = 1.40f;

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

            // THE TIP IS A VERTEX, when the mesh will say where its vertices are.
            //
            // Everything below this is the box rule, and the box is why two weapons sat
            // crooked in the hand. A box has six faces and the rule can only answer with
            // one of them, so a model authored a few degrees off its own axis is gripped
            // along the axis and hangs at that angle for the rest of the match: the pack's
            // axe is 11 degrees off and its scythe 19. Worse, the reach is then measured
            // along the axis rather than along the weapon, which is shorter by the cosine
            // -- so the fit scales the model UP to compensate and the steel overshoots the
            // hitbox it is supposed to BE. Measured on the pack: 125% of it on the axe.
            //
            // A vertex cloud has no such problem. The furthest point from the grip is the
            // tip, wherever it is, and the line from the grip to it is what the simulation
            // swings -- so pointing that line along the arm puts the drawn steel exactly
            // where the blade is. The haft may then hang at whatever angle the model was
            // authored with, which is what a real weapon does.
            if (Tip(root, out grip)) return true;

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
        /// The grip from the model's own vertices, or false when they cannot be read.
        ///
        /// Mesh data is only there when the importer was told to keep it (Read/Write), so
        /// this is allowed to fail and the box rule above is what answers then. The eleven
        /// weapons this game ships are marked readable; a pack dropped in tomorrow may not
        /// be, and it will be gripped the blunter way rather than not at all.
        /// </summary>
        static bool Tip(GameObject root, out Grip grip)
        {
            grip = default;
            if (root == null) return false;

            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
            Vector3 tip = Vector3.zero;
            float far = 0f;
            bool any = false;

            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;

                Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                Vector3[] points = mesh.vertices;
                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 p = m.MultiplyPoint3x4(points[i]);
                    float d = p.sqrMagnitude;
                    if (d > far) { far = d; tip = p; }
                    any = true;
                }
            }

            if (!any || far <= 1e-8f) return false;

            grip.Blade = tip.normalized;
            grip.Reach = Mathf.Sqrt(far);

            // And the rest of the shape, about that line: how far the butt hangs out the
            // back of the hand, how wide it is across, and which way it is flat.
            Vector3 blade = grip.Blade;
            Vector3 u = Mathf.Abs(blade.x) < 0.9f
                ? Vector3.Normalize(Vector3.Cross(blade, Vector3.right))
                : Vector3.Normalize(Vector3.Cross(blade, Vector3.up));
            Vector3 v = Vector3.Cross(blade, u);

            float behind = 0f, uMin = 0f, uMax = 0f, vMin = 0f, vMax = 0f;
            float uu = 0f, vv = 0f, uv = 0f;
            int n = 0;

            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;

                Matrix4x4 m = toRoot * mf.transform.localToWorldMatrix;
                Vector3[] points = mesh.vertices;
                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 p = m.MultiplyPoint3x4(points[i]);
                    float along = Vector3.Dot(p, blade);
                    if (-along > behind) behind = -along;

                    float a = Vector3.Dot(p, u), b = Vector3.Dot(p, v);
                    if (a < uMin) uMin = a;
                    if (a > uMax) uMax = a;
                    if (b < vMin) vMin = b;
                    if (b > vMax) vMax = b;
                    uu += a * a; vv += b * b; uv += a * b;
                    n++;
                }
            }

            grip.Behind = behind;
            float wide = uMax - uMin, thin = vMax - vMin;
            grip.Across = Mathf.Max(wide, thin);
            grip.Thick = Mathf.Min(wide, thin);

            // WHICH WAY IT IS FLAT, from the spread of the cloud rather than from the box.
            //
            // A sword's blade is a plane, and a plane authored at 20 degrees to the model's
            // axes has a BOX that is nearly square -- so the box rule calls it round and
            // leaves the roll to chance. The second moments do not care how the model was
            // oriented: the smaller eigenvector of the two-by-two across the blade is the
            // normal of the plane, whatever angle it sits at.
            if (n > 0 && grip.Across >= grip.Thick * ClearlyFlat)
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
