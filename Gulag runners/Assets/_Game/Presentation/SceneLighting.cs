using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// The arena's lighting, as numbers you can see rather than settings in four menus.
    ///
    /// Scene lighting in Unity lives in Window > Rendering > Lighting, the render pipeline
    /// asset, each light's own inspector and the camera's — four places, none of which says
    /// WHY a number is what it is. For a game whose whole readability rests on a fixed camera
    /// looking at a flat lane through a 3D set, the why is most of the work, so it lives here
    /// next to the numbers.
    ///
    /// The one that matters most is the fog. docs/02 builds the picture out of a foreground the
    /// fight happens on and a background that is only there to be looked at, and nothing
    /// separates those two for the eye on its own: at the same contrast and the same colour, a
    /// handrail nine metres back reads as something a fighter could be standing on. Aerial
    /// perspective is how every side-on game has solved that since parallax scrolling, and it
    /// costs one draw-state change.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]            // before the cameras read anything
    public sealed class SceneLighting : MonoBehaviour
    {
        [Header("Depth fog (docs/02: the fight is the front slice)")]
        public bool fog = true;

        [Tooltip("Haze colour, and it belongs to the arena rather than to this default.\n\n" +
                 "Match it to WHAT IS BEHIND THE SET, which in this game is not the skybox: " +
                 "it is the nearest parallax layer, 26 m back, drawn by a shader that takes " +
                 "no fog. Measured off those images where an arena's back wall lands, the " +
                 "near layer runs 0.08-0.13 in the forest and 0.10-0.14 in the temple -- and " +
                 "three arenas were carrying the same 0.74 pale grey, so the back of the set " +
                 "faded to something four to seven times brighter than the country it was " +
                 "supposed to be melting into. A cave and a night temple had the daylight " +
                 "value of a snow fortress.\n\n" +
                 "Each arena's is its own near layer lifted about a third of the way towards " +
                 "that theme's haze, because above the layer's ridge line you are seeing the " +
                 "sky behind it. The mine lifts towards its INK instead: it has no sky, so " +
                 "distance there falls away into black rather than up into light, which is " +
                 "the same inversion make_backdrops.py applies to its layer order.")]
        public Color fogColour = new Color(0.18f, 0.243f, 0.246f, 1f);

        [Tooltip("Take the fog's near edge from the camera instead of the figure below.\n\n" +
                 "On, and the haze starts a set distance BEHIND the gameplay plane, measured " +
                 "from the camera that is actually looking at it. That is the only way this " +
                 "number stays right: the fight moved a floor down once already, and a fog " +
                 "start typed in metres would have begun in front of the fighters.")]
        public bool fogFromCamera = true;

        [Tooltip("The camera to measure from. Empty finds the first SideViewCamera in the scene.")]
        public SideViewCamera measureFrom;

        [Tooltip("How far behind the gameplay plane the haze begins, in metres.\n\n" +
                 "This was three, on the reasoning that nothing a fighter touches should be " +
                 "hazed. Measured, the arena sets are only about five metres deep from the " +
                 "plane to their back wall -- the mine's and the temple's floor slabs are " +
                 "4.7 to 6.0 m across and the dressing stands inside that -- so at three " +
                 "the haze began more than half way through the only thing it had to work " +
                 "on.\n\n" +
                 "A metre and a bit is a floor rather than a taste: light_check holds " +
                 "the rule as CLEAR_BEHIND = 1, because a fighter is a body with depth " +
                 "and the camera's framing shifts under them, so haze that begins " +
                 "exactly at the plane lands on their faces. This is that minimum and " +
                 "a little, which keeps them and the slab under their feet clear and " +
                 "starts on everything behind.")]
        [Range(0f, 15f)] public float fogBehindPlane = 1.2f;

        [Tooltip("Near edge of the fog, in metres from the camera. Used when Fog From Camera " +
                 "is off; overwritten by the measurement when it is on.")]
        public float fogStart = 9f;

        [Tooltip("Far edge: fully hazed beyond this, in metres from the camera. Used when " +
                 "Fog From Camera is off; written from Fog Depth when it is on.")]
        public float fogEnd = 38f;

        [Tooltip("How many metres of air take you from the near edge to fully hazed.\n\n" +
                 "THIS IS THE NUMBER THAT DECIDES WHETHER THE FOG DOES ANYTHING, and the " +
                 "arenas were all carrying a far edge of 34 to 38 m measured from a camera " +
                 "nine metres out -- about twenty-nine metres of air over a set five metres " +
                 "deep. The back wall of an arena came out at eight per cent haze, which is " +
                 "nothing, so the whole effect this component exists for was switched on " +
                 "and invisible.\n\n" +
                 "Measured from the near edge rather than from the camera, for the same " +
                 "reason the near edge is measured rather than typed: the four arenas sit " +
                 "at four different depths behind their cameras, 6.96 to 9.08 m, so one " +
                 "number in metres-from-camera cannot be right for all of them and nobody " +
                 "would notice which one had gone stale.\n\n" +
                 "At twelve the back of the deepest set reaches about 43 per cent and a " +
                 "shallow one about 16. The parallax layers are NOT touched at any value: " +
                 "Backdrop.shader applies no fog on purpose, which is what lets this be " +
                 "pulled in this far without flattening them into one sheet of fog colour.")]
        [Range(2f, 60f)] public float fogDepth = 12f;

        [Header("Ambient")]
        [Tooltip("How much the sky lights what the sun does not. The shadowed side of a " +
                 "fighter is this and nothing else, so at 0 half of every body is black.")]
        [Range(0f, 3f)] public float ambientIntensity = 1f;

        [Header("Sun")]
        [Tooltip("Optional. The directional light. Set it and the three numbers below are " +
                 "applied to it, so the whole lighting pass is one component.")]
        public Light sun;

        [Range(0f, 4f)] public float sunIntensity = 1.3f;

        [Tooltip("How dark its shadows are. 1 is black, which with ambient above reads as " +
                 "night rather than shade.")]
        [Range(0f, 1f)] public float shadowStrength = 0.75f;

        [Tooltip("Pulls shadows away from what casts them, to stop a surface shadowing " +
                 "itself into stripes. Raise it until the stripes go, and no further: too " +
                 "much and a fighter's shadow detaches from their feet.")]
        [Range(0f, 2f)] public float shadowBias = 0.05f;

        [Range(0f, 3f)] public float shadowNormalBias = 0.4f;

        [Header("Debug")]
        public bool logOnApply;

        void Awake() => Apply();

        void OnValidate()
        {
            if (!Application.isPlaying) Apply();
        }

        /// <summary>Writes every number above into the scene. Safe to call any time.</summary>
        [ContextMenu("Apply lighting")]
        public void Apply()
        {
            float start = fogStart;
            float end = fogEnd;

            if (fogFromCamera)
            {
                if (measureFrom == null) measureFrom = FindFirstObjectByType<SideViewCamera>();
                if (measureFrom != null)
                {
                    // The camera looks along +Z at the plane, so the distance to the plane is
                    // the difference in Z. Measured rather than assumed, because the plane is
                    // taken from the fighter at run time and can move between sessions.
                    float toPlane = Mathf.Abs(measureFrom.planeZ -
                                              measureFrom.transform.position.z);
                    start = toPlane + fogBehindPlane;
                    fogStart = start;

                    // And the far edge from the near one, so the two move together.
                    end = start + fogDepth;
                    fogEnd = end;
                }
            }

            RenderSettings.fog = fog;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColour;
            RenderSettings.fogStartDistance = start;
            RenderSettings.fogEndDistance = Mathf.Max(start + 1f, end);
            RenderSettings.ambientIntensity = ambientIntensity;

            if (sun != null)
            {
                sun.intensity = sunIntensity;
                sun.shadowStrength = shadowStrength;
                sun.shadowBias = shadowBias;
                sun.shadowNormalBias = shadowNormalBias;
            }

            if (logOnApply)
                Debug.Log($"{name}: fog {start:0.0}..{RenderSettings.fogEndDistance:0.0} m" +
                          (sun != null ? $", sun {sunIntensity:0.00}" : ", no sun wired"), this);
        }
    }
}
