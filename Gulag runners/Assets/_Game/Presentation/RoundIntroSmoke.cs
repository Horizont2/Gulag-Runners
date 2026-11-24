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
        [Range(0.15f, 1f)] public float clearBy = 0.95f;

        [Tooltip("Share of the clearing window by which the NEAREST layer is gone. The " +
                 "furthest is gone exactly at the end, and the ones between are spread " +
                 "across. This is what makes the depth: they all leave, at different times.\n\n" +
                 "LOWER is MORE spread, not less -- it is where the first layer finishes, so " +
                 "raising it bunches the four together. Measured off the preview at 0.55, " +
                 "the four leave at 1.18, 1.46, 1.74 and 2.00 seconds of the three second " +
                 "hold, which leaves a second of open arena before control comes back.")]
        [Range(0.2f, 1f)] public float nearDoneBy = 0.55f;

        [Tooltip("How far each layer is offset sideways at rest, in frame widths. Without " +
                 "it the four start exactly on top of one another and the first frame of " +
                 "the round is one flat field of colour with no edge in it.")]
        [Range(0f, 0.4f)] public float stagger = 0.06f;

        [Tooltip("How much wider than the frame a layer is, as a multiple. It has to still " +
                 "cover the frame once it has started moving.")]
        [Range(1.1f, 3f)] public float coverWide = 1.35f;

        [Tooltip("How much TALLER than the frame a layer is, as a multiple.\n\n" +
                 "Reported as \"half way through you can see the edge of the foreground " +
                 "as a texture hanging in the air\". That edge is the top or the bottom " +
                 "of the quad: the band fills its image almost to the edge, so those two " +
                 "boundaries are nearly straight lines, and at 1.5 the quad was short " +
                 "enough for one of them to come into shot as the camera moved.\n\n" +
                 "The only edge of this that should ever be seen is the ragged end of the " +
                 "bank. Height is free -- the texture stretches, and smoke has no scale.")]
        [Range(1.2f, 6f)] public float coverHigh = 3.2f;

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

        [Header("How it leaves")]
        [Tooltip("How much wider a layer is by the time it is gone. 1 is no swell.\n\n" +
                 "A bank that only slides sideways reads as a backdrop on a conveyor. One " +
                 "that grows as it goes reads as the camera coming out of it, which is the " +
                 "shot this is.")]
        [Range(1f, 1.5f)] public float swellBy = 1.12f;

        [Tooltip("Share of a layer's own travel at which it starts to dissolve. 1 never " +
                 "dissolves, which is what this did when it was drawn as a cutout: the " +
                 "last bank was simply switched off, in one frame.")]
        [Range(0.3f, 1f)] public float fadeFrom = 0.62f;

        readonly List<Transform> _layers = new List<Transform>();
        readonly List<Vector3> _rest = new List<Vector3>();
        readonly List<Color> _tones = new List<Color>();
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
                // Nearly linear, with just enough ease to not start on a jolt.
                //
                // Two separate faults got blamed on this one line, and only the second was
                // its own. The FIRST was the dead start: nothing, nothing, nothing, then a
                // sweep. A cube, a square and a smoothstep all failed to fix it because the
                // fault was the WIDTH -- at 1.9 frames the quad's edge begins 0.45 of a frame
                // outside the picture, so a third of every layer's travel happened off
                // screen. Narrower quads fixed that, not a curve.
                //
                // The SECOND is this: reported as still too fast, and measured off the
                // preview strip, the frame was clear at 1.45s of a three second hold even
                // with the window stretched to 0.95 of it. A square ease-out covers 76% of
                // its travel in the first 51% of its time, so widening the window moves the
                // end hardly at all -- the layer is simply gone before the window is spent.
                //
                // Travel needed to clear is (coverWide + 1) / 2, so the exponent that lands
                // the last layer at two seconds solves (1-t)^p = 1 - 1.052/1.375 with t at
                // 0.70, which is p = 1.2. Its speed falls from 1.2 to 0.94 across the whole
                // move: a bank being drawn steadily aside, which is what a parting does, and
                // the opposite of a curve that spends itself in the first half.
                float eased = 1f - Mathf.Pow(1f - own, 1.2f);

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

                // AND IT SWELLS AS IT GOES, which is the half of this that says the camera
                // is coming OUT of the bank rather than the bank going past it. A thing
                // that passes the lens grows; a thing on a conveyor belt does not.
                //
                // Small, and only on the way out: at 1.12 the near layer is a tenth wider
                // by the time it leaves, which nobody can name and everybody feels.
                float swell = 1f + (swellBy - 1f) * eased;
                if (i < _rest.Count)
                    _layers[i].localScale = _rest[i] * swell;

                // And it DISSOLVES over its last stretch. The cutout this used to be could
                // not: its alpha was a yes or a no, so the only way to finish was to switch
                // the layer off, and the far bank -- which finishes exactly at the end of
                // the window -- had nothing between being there and being gone. One frame
                // of cloud, then none.
                if (i < _skins.Count && _skins[i] != null)
                {
                    float out01 = Mathf.InverseLerp(fadeFrom, 1f, own);
                    float a = 1f - out01 * out01 * (3f - 2f * out01);
                    Color c = _tones[i];
                    c.a = a;
                    if (_skins[i].HasProperty("_BaseColor")) _skins[i].SetColor("_BaseColor", c);
                    if (_skins[i].HasProperty("_Color")) _skins[i].SetColor("_Color", c);
                }
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
            // The cloud shader, which blends. It used to borrow the BACKDROP's, which
            // clips -- and a clip is why this was a paper cut-out for as long as it was:
            // every edge a hard line, and a layer that could only be switched off rather
            // than dissolved, because a cutout at 49% alpha is not a faint cloud, it is no
            // cloud. Cloud.shader carries the whole argument.
            Shader shader = Shader.Find("Gulag Runners / Cloud");
            if (shader == null) shader = Shader.Find("Gulag Runners / Backdrop");
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
                Vector3 rest = new Vector3(wide * coverWide, high * coverHigh, 1f);
                go.transform.localScale = rest;

                // The size it was built at, kept, because the swell multiplies it every
                // frame. Reading the live scale back and multiplying again would compound:
                // twelve per cent a frame is a layer the size of the arena inside a second.
                _rest.Add(rest);

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

            // Only meaningful on the fallback shaders. Cloud.shader has no cutoff: it
            // blends, which is the whole point of it.
            if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.5f);

            _skins.Add(m);
            _tones.Add(c);
            return m;
        }

        void Clear()
        {
            if (_layers.Count == 0) return;
            foreach (Transform t in _layers)
                if (t != null) Destroy(t.gameObject);
            _layers.Clear();
            _rest.Clear();
            _tones.Clear();
            foreach (Material m in _skins)
                if (m != null) Destroy(m);
            _skins.Clear();
        }

        void OnDisable() => Clear();
    }
}
