using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// A chest, placed by hand in the scene.
    ///
    /// Like <see cref="SimCollider"/>, this is only an authoring surface: the box and what is
    /// inside it are read once into the <see cref="SimWorld"/> and the simulation takes it from
    /// there. Nothing about a chest is created at runtime, so every one of them is visible and
    /// editable in the inspector before the game starts.
    ///
    /// Size comes from a BoxCollider if there is one, otherwise from the transform scale. The
    /// baker never turns that collider into a wall — you walk into a chest, not against it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Chest : MonoBehaviour
    {
        [Header("Type")]
        [Tooltip("Crate is quick and quiet, locker is slow and loud, safe is the locked one. " +
                 "Each type is a different answer to the same question: how much information " +
                 "about yourself will you trade for this loot? (docs/03)")]
        public ChestKind kind = ChestKind.Crate;

        [Tooltip("What comes out. One item: there is no inventory, so it goes straight into its " +
                 "own slot and replaces whatever was there (docs/02).")]
        public ItemId contents = ItemId.Club;

        [Tooltip("This safe can be picked with a puzzle instead of forced. Nothing reads it yet — " +
                 "docs/04 is its own piece of work — but placing chests with it set now means the " +
                 "arena does not have to be re-authored when the puzzles land.")]
        public bool hasPuzzle;

        [Header("Look")]
        [Tooltip("Tint the chest by the tier of what is inside. docs/03 asks that the tier be " +
                 "readable from across the room while the exact item is not: that is what makes " +
                 "\"run four seconds to the gold one or open the white one under your nose\" a " +
                 "decision rather than a lottery.")]
        public bool tintByTier = true;

        public Renderer tintTarget;

        [Tooltip("Tier colours: none, white, blue, gold.")]
        public Color[] tierColours =
        {
            new Color(0.55f, 0.55f, 0.58f),
            new Color(0.90f, 0.90f, 0.92f),
            new Color(0.35f, 0.62f, 1.00f),
            new Color(1.00f, 0.78f, 0.25f)
        };

        MaterialPropertyBlock _block;

        public ItemDef ContentsDef => ItemTable.Get(contents);
        public int Tier => ContentsDef.Tier;

        public Color TierColour
        {
            get
            {
                if (tierColours == null || tierColours.Length == 0) return Color.white;
                int t = Mathf.Clamp(Tier, 0, tierColours.Length - 1);
                return tierColours[t];
            }
        }

        public Rect ToRect() => SimCollider.WorldRect(this, GetComponent<Collider>());

        public ChestDef ToDef() => new ChestDef
        {
            Box = SimCollider.RectToAabb(ToRect()),
            Kind = kind,
            Contents = contents,
            HasPuzzle = hasPuzzle
        };

        void OnEnable() => ApplyTint();
        void OnValidate() => ApplyTint();

        /// <summary>
        /// Tints through a property block rather than a material instance: instantiating a
        /// material per chest would leak one material per chest and break batching, and this
        /// project does not create anything at runtime.
        /// </summary>
        public void ApplyTint()
        {
            if (!tintByTier) return;

            Renderer r = tintTarget != null ? tintTarget : GetComponentInChildren<Renderer>();
            if (r == null) return;

            _block ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(_block);
            _block.SetColor("_BaseColor", TierColour);
            _block.SetColor("_Color", TierColour);
            r.SetPropertyBlock(_block);
        }

        void OnDrawGizmos()
        {
            Rect r = ToRect();
            Color c = TierColour;

            Gizmos.color = new Color(c.r, c.g, c.b, 0.9f);
            Gizmos.DrawWireCube(new Vector3(r.center.x, r.center.y, transform.position.z),
                                new Vector3(r.width, r.height, 0.05f));

            // A second, inset box for the ones that cost you something to open, so a glance at
            // the scene view says which chests are the expensive ones.
            if (kind == ChestKind.Crate) return;
            float inset = kind == ChestKind.Safe ? 0.82f : 0.9f;
            Gizmos.DrawWireCube(new Vector3(r.center.x, r.center.y, transform.position.z),
                                new Vector3(r.width * inset, r.height * inset, 0.05f));
        }
    }
}
