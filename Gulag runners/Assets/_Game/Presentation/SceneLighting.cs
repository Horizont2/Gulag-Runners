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

        [Tooltip("Haze colour. Match it to the skybox's horizon, or the background separates " +
                 "from the sky instead of melting into it.")]
        public Color fogColour = new Color(0.62f, 0.68f, 0.72f, 1f);

        [Tooltip("Take the fog's near edge from the camera instead of the figure below.\n\n" +
                 "On, and the haze starts a set distance BEHIND the gameplay plane, measured " +
                 "from the camera that is actually looking at it. That is the only way this " +
                 "number stays right: the fight moved a floor down once already, and a fog " +
                 "start typed in metres would have begun in front of the fighters.")]
        public bool fogFromCamera = true;

        [Tooltip("The camera to measure from. Empty finds the first SideViewCamera in the scene.")]
        public SideViewCamera measureFrom;

        [Tooltip("How far behind the gameplay plane the haze begins, in metres. Three is " +
                 "enough that nothing a fighter touches is hazed and the wall behind them is.")]
        [Range(0f, 15f)] public float fogBehindPlane = 3f;

        [Tooltip("Near edge of the fog, in metres from the camera. Used when Fog From Camera " +
                 "is off; overwritten by the measurement when it is on.")]
        public float fogStart = 9f;

        [Tooltip("Far edge: fully hazed beyond this. Short enough to bury the back of the " +
                 "set, long enough to leave it visible.")]
        public float fogEnd = 38f;

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
                }
            }

            RenderSettings.fog = fog;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColour;
            RenderSettings.fogStartDistance = start;
            RenderSettings.fogEndDistance = Mathf.Max(start + 1f, fogEnd);
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
