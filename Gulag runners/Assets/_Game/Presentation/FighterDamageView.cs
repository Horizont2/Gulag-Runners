using System;
using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>One wound, authored on the model and switched on when it has been earned.</summary>
    [Serializable]
    public class Wound
    {
        [Tooltip("What this is, for the inspector. \"Left arm\", \"brow\", \"gut\".")]
        public string label = "";

        [Tooltip("It appears once health is at or below this. Order them down the list and the " +
                 "fighter comes apart in the order you wrote.")]
        [Range(0, 100)] public int showAtHealth = 70;

        [Tooltip("The cut, the bruise, the torn sleeve. Anything: a mesh, a decal, a quad.")]
        public GameObject root;

        [Tooltip("Played once, the moment it opens. Optional.")]
        public ParticleSystem burst;
    }

    /// <summary>
    /// What a fighter's damage looks like, on the fighter.
    ///
    /// docs/01 and docs/02 put the whole game on reading the other player rather than reading
    /// a interface: you do not see your opponent until you meet them, and when you do, what
    /// they are carrying and what state they are in has to be legible from across the room.
    /// A health bar would answer that question and kill it at the same time — so there is no
    /// bar. The fighter wears it.
    ///
    /// Three layers, each useful on its own, so this works before any wound art exists:
    ///
    ///   The drain.  Colour leaves the body as health does. Nothing until the fighter is
    ///               actually hurt, then steadily, so "nearly dead" is a silhouette you can
    ///               read at a glance rather than a number you have to be told.
    ///   The moment. A hit flashes the body, harder for a bigger hit, and a different colour
    ///               for a hit the guard ate and for one turned back as a parry. docs/02
    ///               wants those three to look nothing alike; this is where that happens.
    ///   The wounds. Cuts that appear as health falls and stay for the round. Authored on the
    ///               model, switched on — never instantiated, so every one of them is visible
    ///               in the inspector and in the scene before the game runs.
    ///
    /// Nothing here touches the simulation. It reads health and listens for the three things
    /// the fight reports, and that is all it is allowed to do (docs/06).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FighterDamageView : MonoBehaviour
    {
        [Header("Wiring")]
        public PlayerController player;

        [Tooltip("The renderers that make up the body. Filled in from the children at load " +
                 "when left empty, which is what you want for a character in one piece.")]
        public Renderer[] body;

        [Header("The drain")]
        [Tooltip("Nothing happens above this share of health: a fighter at nine tenths should " +
                 "look fine, or the tell means nothing when it matters.")]
        [Range(0f, 1f)] public float drainsBelow = 0.75f;

        [Tooltip("What the body's own colour is MULTIPLIED by once the health is gone. It is a " +
                 "filter, not a repaint: the blue fighter drains to a dark blue and the red one " +
                 "to a dark red, so neither of them stops being who they are. White switches " +
                 "the drain off.")]
        public Color drainedColour = new Color(0.62f, 0.52f, 0.52f);

        [Header("The moment it lands")]
        public Color hitFlash = new Color(1f, 0.15f, 0.1f);
        public Color blockFlash = new Color(0.85f, 0.9f, 1f);
        public Color parryFlash = new Color(1f, 0.92f, 0.5f);

        [Tooltip("How long a flash lasts. Short: it is punctuation, not a light show.")]
        [Range(0.02f, 0.5f)] public float flashSeconds = 0.12f;

        [Tooltip("How hard a flash is per point of damage, before the cap below. A chip off a " +
                 "guard and a greataxe to the head should not look the same.")]
        [Range(0f, 1f)] public float flashPerDamage = 0.12f;

        [Range(0f, 4f)] public float flashCeiling = 1.6f;

        [Header("Wounds")]
        public Wound[] wounds;

        [Header("Blood")]
        [Tooltip("Burst when a hit gets through. Optional — the drain and the flash work " +
                 "without it.")]
        public ParticleSystem bloodOnHit;

        [Tooltip("Burst when the guard eats one. Sparks off a shield, not blood.")]
        public ParticleSystem sparksOnBlock;

        [Tooltip("Particles emitted per point of damage, so a big hit sprays and a chip does not.")]
        [Range(0, 20)] public int particlesPerDamage = 3;

        [Header("Death")]
        [Tooltip("Played once, when the fighter goes down.")]
        public ParticleSystem deathBurst;

        MaterialPropertyBlock _block;
        Color[] _baseColour;
        Color _flash;
        float _flashLeft;
        bool _painted;
        bool[] _open;
        int _lastHealth = int.MinValue;

        void Reset()
        {
            player = GetComponentInParent<PlayerController>();
            body = GetComponentsInChildren<Renderer>(true);
        }

        void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (body == null || body.Length == 0)
                body = (player != null ? player.gameObject : gameObject)
                    .GetComponentsInChildren<Renderer>(true);

            RememberColours();

            _open = new bool[wounds != null ? wounds.Length : 0];
            CloseEveryWound();
        }

        void OnEnable()
        {
            if (player == null) return;
            player.Hurt += OnHurt;
            player.Blocked += OnBlocked;
            player.Parried += OnParried;
            player.Died += OnDied;
        }

        void OnDisable()
        {
            if (player == null) return;
            player.Hurt -= OnHurt;
            player.Blocked -= OnBlocked;
            player.Parried -= OnParried;
            player.Died -= OnDied;
        }

        void LateUpdate()
        {
            if (player == null || body == null) return;

            int health = player.State.Health;
            int max = Mathf.Max(1, player.MaxHealth);

            // A round reset puts the health back, and the fighter has to be whole again with it.
            if (health > _lastHealth && _lastHealth != int.MinValue) CloseEveryWound();
            _lastHealth = health;

            OpenEarnedWounds(health);

            if (_flashLeft > 0f) _flashLeft -= Time.deltaTime;
            Paint(Mathf.Clamp01(health / (float)max));
        }

        /// <summary>
        /// Each renderer's own colour, so the drain can darken it rather than replace it.
        ///
        /// Player one is blue and player two is red, and that is the first thing either of
        /// them reads about the figure across the room. Painting a flat tint over the body
        /// every frame turned them both white at full health, which is the bug this exists to
        /// not have.
        /// </summary>
        void RememberColours()
        {
            _baseColour = new Color[body.Length];
            for (int i = 0; i < body.Length; i++)
            {
                Material m = body[i] != null ? body[i].sharedMaterial : null;
                _baseColour[i] =
                    m == null ? Color.white
                    : m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                    : m.HasProperty("_Color") ? m.GetColor("_Color")
                    : Color.white;
            }
        }

        /// <summary>The tint for this frame: the drain, with whatever is left of a flash on top.</summary>
        void Paint(float health01)
        {
            float drained = drainsBelow <= 0.001f
                ? 0f
                : Mathf.Clamp01((drainsBelow - health01) / drainsBelow);

            float flash = flashSeconds <= 0.001f ? 0f : Mathf.Clamp01(_flashLeft / flashSeconds);

            // Nothing to say this frame: hand the renderers back to their own materials
            // outright rather than re-stating the colour they already had. A body at full
            // health is then EXACTLY as it was authored, down to a second material on a
            // second submesh that this could never have reproduced anyway.
            if (drained <= 0.001f && flash <= 0.001f)
            {
                if (!_painted) return;
                _painted = false;
                for (int i = 0; i < body.Length; i++)
                    if (body[i] != null) body[i].SetPropertyBlock(null);
                return;
            }

            _painted = true;
            Color emission = _flash * flash;

            if (_baseColour == null || _baseColour.Length != body.Length) RememberColours();

            for (int i = 0; i < body.Length; i++)
            {
                Renderer r = body[i];
                if (r == null) continue;

                // A filter over its own colour, not a repaint: blue drains to a dark blue.
                Color own = _baseColour[i];
                Color tint = Color.Lerp(own, own * drainedColour, drained);

                _block ??= new MaterialPropertyBlock();
                r.GetPropertyBlock(_block);
                _block.SetColor("_BaseColor", tint);
                _block.SetColor("_Color", tint);
                _block.SetColor("_EmissionColor", emission);
                r.SetPropertyBlock(_block);
            }
        }

        void OpenEarnedWounds(int health)
        {
            if (wounds == null) return;
            if (_open == null || _open.Length != wounds.Length) _open = new bool[wounds.Length];

            for (int i = 0; i < wounds.Length; i++)
            {
                Wound w = wounds[i];
                if (w == null || w.root == null) continue;
                if (_open[i] || health > w.showAtHealth) continue;

                _open[i] = true;
                w.root.SetActive(true);
                if (w.burst != null) w.burst.Play();
            }
        }

        void CloseEveryWound()
        {
            if (wounds == null) return;
            for (int i = 0; i < wounds.Length; i++)
            {
                if (wounds[i]?.root == null) continue;
                wounds[i].root.SetActive(false);
                if (_open != null && i < _open.Length) _open[i] = false;
            }
        }

        void OnHurt(int damage)
        {
            Flash(hitFlash, damage);
            Spray(bloodOnHit, damage);
        }

        void OnBlocked(int chip)
        {
            Flash(blockFlash, chip);
            Spray(sparksOnBlock, chip);
        }

        void OnParried() => Flash(parryFlash, 6);

        void OnDied()
        {
            Flash(hitFlash, 20);
            if (deathBurst != null) deathBurst.Play();
        }

        void Flash(Color colour, int damage)
        {
            float strength = Mathf.Min(flashCeiling, Mathf.Max(0, damage) * flashPerDamage);
            _flash = colour * strength;
            _flashLeft = flashSeconds;
        }

        void Spray(ParticleSystem system, int damage)
        {
            if (system == null || particlesPerDamage <= 0) return;
            int count = Mathf.Clamp(damage * particlesPerDamage, 1, 200);
            system.Emit(count);
        }
    }
}
