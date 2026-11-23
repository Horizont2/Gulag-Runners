using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// Moves the menu's camera to the shop's view while the shop is open.
    ///
    /// The shop is a PANEL in the menu, not a scene of its own, and that decides what this
    /// has to be. A separate scene would have had its own camera and its own load; a panel
    /// shares the cell the player is standing in, so opening the wardrobe has to feel like
    /// turning to look at it rather than cutting somewhere else.
    ///
    /// SO THE SHOP CAMERA IS A POSE, NOT A CAMERA. There were two enabled cameras in the
    /// menu scene at the same depth -- Main Camera and ShopCamera -- which is undefined:
    /// Unity renders both and which one you see is which was registered last. The second
    /// one is a place to stand, marked out by hand, and this walks the real camera to it
    /// and back. One camera, one render, and the pose keeps whatever framing it was given.
    ///
    /// Nothing here knows what a shop is. It watches the string MenuUI already keeps for
    /// which panel is open, so a second pose for a second panel is two lines.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuShopView : MonoBehaviour
    {
        [Tooltip("Leave empty to find the MenuUI in the scene.")]
        public MenuUI ui;

        [Tooltip("What moves. Leave empty to use the main camera.")]
        public Transform view;

        [Tooltip("Where to stand while the shop is open -- the ShopCamera object, used as " +
                 "a marker. Its own Camera component is switched off by the setup tool: " +
                 "two enabled cameras at the same depth is undefined.")]
        public Transform shopPose;

        [Tooltip("Which panel this pose belongs to, as MenuUI names it.")]
        public string panel = "Shop";

        [Tooltip("Seconds to get there. Long enough to read as a move and short enough " +
                 "that a player who opens the shop to change one thing is not waiting.")]
        [Range(0.05f, 2f)] public float seconds = 0.45f;

        Vector3 _homePos;
        Quaternion _homeRot;
        bool _known;

        void Awake()
        {
            if (ui == null) ui = FindFirstObjectByType<MenuUI>(FindObjectsInactive.Include);
            if (view == null && Camera.main != null) view = Camera.main.transform;
        }

        void OnEnable()
        {
            // The home pose is wherever the camera was when the menu came up, so moving
            // the camera in the scene moves the home with it. Taken once: sampling it
            // every frame would make home follow the shop and the camera never come back.
            if (view != null && !_known)
            {
                _homePos = view.position;
                _homeRot = view.rotation;
                _known = true;
            }
        }

        void LateUpdate()
        {
            if (view == null || ui == null || !_known) return;

            bool open = shopPose != null && ui.openPanel == panel;

            Vector3 wantPos = open ? shopPose.position : _homePos;
            Quaternion wantRot = open ? shopPose.rotation : _homeRot;

            // Framerate-independent easing: the share of the remaining distance covered in
            // a second is what `seconds` means, so a phone at 30 and one at 60 arrive
            // together. A plain Lerp on deltaTime does not.
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * (5f / Mathf.Max(0.05f, seconds)));
            view.position = Vector3.Lerp(view.position, wantPos, k);
            view.rotation = Quaternion.Slerp(view.rotation, wantRot, k);
        }
    }
}
