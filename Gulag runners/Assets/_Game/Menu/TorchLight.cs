using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// Makes a torch's light behave like fire.
    ///
    /// A flame is never still, and a torch lit by a constant lamp reads as a lamp with a
    /// particle effect stuck on it — which is exactly what six unlit torch meshes in a
    /// room look like once the fire is added and the light is not. The giveaway is not
    /// brightness, it is that nothing on the wall beside it moves.
    ///
    /// Two sine waves at unrelated speeds rather than Random.value: noise that jumps every
    /// frame reads as a broken light, while two slow waves beating against each other never
    /// quite repeat and always look like burning. The phase comes from the object's own
    /// position, so six torches in one room flicker out of step without anybody seeding
    /// anything.
    ///
    /// Presentation only, and it creates nothing: it drives a Light that is already there.
    /// </summary>
    [RequireComponent(typeof(Light))]
    [DisallowMultipleComponent]
    public sealed class TorchLight : MonoBehaviour
    {
        [Header("Flicker")]
        [Tooltip("The intensity it burns at. Taken from the Light on the first frame when " +
                 "left at zero, so a light tuned in the inspector is not overwritten.")]
        public float baseIntensity;

        [Tooltip("How far it swings, as a share of the base. A tenth is a steady torch in " +
                 "still air; a third is one in a draught, which is louder than a menu needs.")]
        [Range(0f, 0.6f)] public float swing = 0.14f;

        [Tooltip("Beats per second of the slow wave. The fast one runs at an unrelated " +
                 "multiple of it, so the two never line up and the flicker never loops.")]
        [Range(0.2f, 8f)] public float speed = 1.7f;

        [Header("Position")]
        [Tooltip("How far the light drifts with the flame, in METRES -- read as metres " +
                 "however the torch above it is scaled. Small: a light that visibly moves " +
                 "reads as a bug, but one that does not reads as a bulb.")]
        [Range(0f, 0.1f)] public float drift = 0.015f;

        Light _light;
        Vector3 _home;
        float _phase;
        float _perMetre = 1f;

        void Awake()
        {
            _light = GetComponent<Light>();
            _home = transform.localPosition;

            if (baseIntensity <= 0f) baseIntensity = _light.intensity;

            // A torch here hangs under a model instance scaled by forty, and localPosition
            // is in the PARENT's units -- so an untranslated drift of 0.015 would throw the
            // light 0.6 m across the room, which is the kind of bug that reads as a haunting.
            // One local unit is lossyScale metres, so a metre is 1/lossyScale local units.
            Transform up = transform.parent;
            float unit = up != null ? Mathf.Abs(up.lossyScale.y) : 1f;
            _perMetre = unit > 1e-4f ? 1f / unit : 1f;

            // Out of where it stands, so two torches never flicker together and nothing
            // has to be seeded by hand.
            Vector3 at = transform.position;
            _phase = (at.x * 12.9898f + at.y * 78.233f + at.z * 37.719f) % (Mathf.PI * 2f);
        }

        void LateUpdate()
        {
            if (_light == null) return;

            float t = Time.time * speed + _phase;
            float slow = Mathf.Sin(t);
            float fast = Mathf.Sin(t * 2.37f + 1.3f);     // deliberately not a whole multiple

            float k = (slow * 0.65f + fast * 0.35f);
            _light.intensity = baseIntensity * (1f + k * swing);

            if (drift > 0f)
                transform.localPosition =
                    _home + new Vector3(fast, slow, fast * 0.5f) * (drift * _perMetre);
        }
    }
}
