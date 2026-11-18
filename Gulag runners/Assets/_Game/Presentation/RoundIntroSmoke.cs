using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// The arena opens out of a bank of smoke, once, on the round that arrives in it.
    ///
    /// Four layers at four distances from the camera, all of them covering the frame at the
    /// start, drifting aside at four different speeds. THE SPEEDS ARE THE POINT: a single
    /// sheet sliding off is a wipe, and a wipe is a transition between two pictures. Four
    /// sheets at four depths, the nearest moving fastest, is the camera coming out of
    /// something -- the same parallax the backdrop uses, turned on the thing in front.
    ///
    /// It only happens on the first round of an arena. Rounds two and three open with the
    /// camera already where the player's camera would put it, and the countdown is what
    /// opens them; RoundIntroCamera's firstRoundOnly is the same rule, on the move.
    ///
    /// Cutout, never blended, for the same reason the backdrop is: a blended quad draws
    /// after the opaque pass, so this would land over the interface as well as over the
    /// arena. The art is a flat band with hard ragged ends (docs/00, and
    /// docs/refs/make_intro_smoke.py).
    ///
    /// NOTHING HERE IS SIMULATED. It reads the cage progress both machines share, so the
    /// smoke clears on the same tick on both -- but the quads are presentation, built when
    /// the round opens and destroyed when it has.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoundIntroSmoke : MonoBehaviour
    {
        [Tooltip("Leave empty to find the MatchState in the scene.")]
        public MatchState matchState;

        [Tooltip("Leave empty to use the camera on this object or the main one.")]
        public Camera view;

        [Tooltip("The band. Leave empty and it is loaded from the Effects folder.")]
        public Texture2D band;

        [Tooltip("What the smoke is made of in this arena. The texture is white; this is " +
                 "what makes it a mine's dust rather than a forest's mist.")]
        public Color tint = new Color(0.62f, 0.60f, 0.58f, 1f);

        [Header("How it clears")]
        [Tooltip("Share of the hold the smoke takes to get out of the frame. The rest of " +
                 "the hold is the countdown on its own, which is what the second and third " +
                 "rounds are entirely.")]
        [Range(0.15f, 1f)] public float clearBy = 0.85f;

        [Tooltip("Share of the clearing window by which the NEAREST layer is gone. The " +
                 "furthest is gone exactly at the end, and the ones between are spread " +
                 "across. This is what makes the depth: they all leave, at different times.")]
        [Range(0.2f, 1f)] public float nearDoneBy = 0.50f;

        [Tooltip("How far each layer is offset sideways at rest, in frame widths. Without " +
                 "it the four start exactly on top of one another and the first frame of " +
                 "the round is one flat field of colour with no edge in it.")]
        [Range(0f, 0.4f)] public float stagger = 0.06f;

        [Tooltip("How much wider than the frame a layer is, as a multiple. It has to still " +
                 "cover the frame once it has started moving.")]
        [Range(1.1f, 3f)] public float coverWide = 1.35f;

        [Tooltip("How dark the NEAREST layer is against the furthest, which is the tint as " +
                 "given. Without a difference between them the four are one flat field of " +
                 "colour for the first fifth of a second: the holes in the near bank show " +
                 "the layer behind, and if that layer is the same colour there is nothing " +
                 "to see through to. Nearer is darker because it is the one furthest from " +
                 "whatever is lighting the arena.")]
        [Range(0.3f, 1f)] public float nearShade = 0.55f;

        [Tooltip("Distances from the camera, nearest first. Four is enough to read as depth " +
                 "and few enough to cost nothing.")]
        public float[] depths = { 1.1f, 1.9f, 3.0f, 4.6f };

        [Tooltip("Only on rounds up to this one. 1 is the round that arrives in the arena.")]
        [Range(0, 5)] public int firstRoundOnly = 1;

        readonly List<Transform> _layers = new List<Transform>();
        Shader _shader;
        readonly List<Material> _skins = new List<Material>();
        bool _built;
        bool _done;

        void Awake()
        {
            if (matchState == null) matchState = MatchState.Instance;
            if (view == null) view = GetComponent<Camera>();
            if (view == null) view = Camera.main;
        }

        void LateUpdate()
        {
            if (view == null || matchState == null)
            {
                if (matchState == null) matchState = MatchState.Instance;
                if (view == null) view = Camera.main;
                return;
            }

            MatchSimState m = matchState.Match;

            if (m.Phase != MatchPhase.Cages || m.Round > firstRoundOnly)
            {
                Clear();
                return;
            }

            if (_done) return;
            if (!_built) BuildLayers();

            float held = m.CageProgress(matchState.MatchRules).Raw / (float)Fix.RawOne;
            float t = Mathf.Clamp01(held / Mathf.Max(0.01f, clearBy));

            if (t >= 1f) { Clear(); _done = true; return; }

            // EVERY LAYER HAS TO LEAVE, and what differs is WHEN.
            //
            // The first version scaled each layer's travel by its depth, the way the
            // backdrop scales drift, and the preview was blunt about it: the far layers
            // moved half a frame width, never cleared the frame at all, and the reveal
            // ended on a hard cut when the whole thing was switched off. For a layer that
            // stays, drift is the right relation; for a layer that has to get out of the
            // way, arrival is not optional.
            //
            // So all four travel far enough to clear, and the NEAR ones finish early. The
            // eye reads that as the near smoke whipping past and the far bank opening
            // behind it, which is a camera coming out of something -- and it ends with the
            // frame already empty rather than with four sheets disappearing at once.
            for (int i = 0; i < _layers.Count; i++)
            {
                float depth = depths[Mathf.Min(i, depths.Length - 1)];
                float frame = FrameAt(depth);
                float side = (i % 2 == 0) ? 1f : -1f;

                // Nearest is done by this share of the window, furthest exactly at the end.
                float by = Mathf.Lerp(nearDoneBy, 1f, depths.Length <= 1
                    ? 1f : i / (float)(depths.Length - 1));
                float own = Mathf.Clamp01(t / Mathf.Max(0.01f, by));
                // Ease OUT: moving from the first frame, settling rather than stopping.
                //
                // The curve was the wrong thing to blame, and I blamed it twice. A cube, then
                // a square, then smoothstep -- and smoothstep was the worst of the three to
                // watch, because its slow start lands on top of a head start that is already
                // invisible. The real fault was the WIDTH: at 1.9 frames the quad's edge
                // begins 0.45 of a frame outside the picture, so nearly a third of every
                // layer's travel happens off screen. Nothing, nothing, nothing, then a sweep.
                //
                // Narrower quads put the edges almost at the frame, so the first thing the
                // smoke does is visible, and an ease-out then reads as a bank being drawn
                // aside rather than yanked.
                float eased = 1f - (1f - own) * (1f - own);

                // Far enough that the trailing edge is off the frame: half the quad plus
                // half the frame, and a little over.
                float clear = (coverWide + 1f) * 0.5f + 0.2f;

                // Staggered at rest, so the first frame shows four EDGES rather than one
                // flat field of colour -- which is what it showed when they all started
                // centred on top of each other.
                float start = stagger * frame * side * (1f + i * 0.35f);
                float gone = start + eased * clear * frame * side;
                float lift = eased * frame * 0.10f * ((i % 4 < 2) ? 1f : -1f);

                _layers[i].localPosition = new Vector3(gone, lift, depth);
            }
        }

        /// <summary>The width of the frame at a distance, in metres.</summary>
        float FrameAt(float depth)
        {
            float high = 2f * depth * Mathf.Tan(Mathf.Deg2Rad * view.fieldOfView * 0.5f);
            return high * Mathf.Max(0.5f, view.aspect);
        }

        void BuildLayers()
        {
            _built = true;

            // The band is assigned by Tools > Setup > "Build the round opening". Without it the
            // layers would draw as flat rectangles, which is worse than no smoke at all.
            if (band == null)
            {
                Debug.LogWarning("The intro smoke has no band texture, so the arena opens "
                                 + "without it. Run Tools > Gulag Runners > Setup > Build "
                                 + "the round opening.");
                _done = true;
                return;
            }
            Shader shader = Shader.Find("Gulag Runners / Backdrop");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("The intro smoke found no shader to draw with, so the "
                                 + "arena opens without it.");
                _done = true;
                return;
            }

            _shader = shader;

            Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            if (quad == null)
            {
                GameObject borrow = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad = borrow.GetComponent<MeshFilter>().sharedMesh;
                Destroy(borrow);
            }

            for (int i = 0; i < depths.Length; i++)
            {
                GameObject go = new GameObject("Intro Smoke " + i);
                go.transform.SetParent(view.transform, false);
                go.hideFlags = HideFlags.DontSave;

                float depth = depths[i];
                float wide = FrameAt(depth);
                float high = wide / Mathf.Max(0.5f, view.aspect);

                // Wider than the frame, because it has to still be covering it after it has
                // started moving, and taller because the ends are ragged.
                go.transform.localPosition = new Vector3(0f, 0f, depth);
                go.transform.localScale = new Vector3(wide * coverWide, high * 1.5f, 1f);

                go.AddComponent<MeshFilter>().sharedMesh = quad;
                MeshRenderer r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = SkinFor(i);
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;

                _layers.Add(go.transform);
            }
        }

        /// <summary>One material per layer, so each can be its own distance's shade.</summary>
        Material SkinFor(int i)
        {
            Material m = new Material(_shader) { hideFlags = HideFlags.DontSave };
            if (band != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", band);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", band);
            }

            float far = depths.Length <= 1 ? 1f : i / (float)(depths.Length - 1);
            float shade = Mathf.Lerp(nearShade, 1f, far);
            Color c = new Color(tint.r * shade, tint.g * shade, tint.b * shade, 1f);

            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.5f);

            _skins.Add(m);
            return m;
        }

        void Clear()
        {
            if (_layers.Count == 0) return;
            foreach (Transform t in _layers)
                if (t != null) Destroy(t.gameObject);
            _layers.Clear();
            foreach (Material m in _skins)
                if (m != null) Destroy(m);
            _skins.Clear();
        }

        void OnDisable() => Clear();
    }
}
