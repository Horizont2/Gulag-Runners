using UnityEngine;
using UnityEngine.Events;

namespace GulagRunners.Net
{
    /// <summary>
    /// Two taps to get two phones into one match.
    ///
    /// One device taps HOST and the other taps JOIN; the host shouts on the local network
    /// once a second and the guest joins the first address it hears. Nobody types an IP
    /// on a phone keyboard, which is the whole reason the beacon exists -- and one tap is
    /// the whole budget, so JOIN keeps looking and connects by itself the moment it hears
    /// a host rather than asking for a second tap.
    ///
    /// Written the way every other screen here is: no uGUI type is named, the readouts
    /// leave as UnityEvents, and the two things a person can do are public methods a
    /// Button's OnClick points at. The panel is in the scene, switched off by the match
    /// starting, and nothing is made at play time.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetConnectView : MonoBehaviour
    {
        [Header("Wiring")]
        public LanLink link;

        [Tooltip("Switched off once the two devices are talking, so the fight is not " +
                 "played through a menu.")]
        public GameObject panel;

        [Header("Readouts")]
        [Tooltip("What is happening, in a sentence. Wire to a label.")]
        public UnityEvent<string> StatusChanged;

        [Tooltip("This device's address, so it can be read out loud if the beacon is " +
                 "blocked — some phone hotspots do not pass broadcast.")]
        public UnityEvent<string> AddressChanged;

        [Tooltip("Fires once both devices are talking. Hide whatever else is up.")]
        public UnityEvent Connected;

        [Header("Debug")]
        public string state = "idle";

        bool _announced;
        bool _looking;
        string _was;

        void Awake()
        {
            if (link == null) link = FindFirstObjectByType<LanLink>();
        }

        void OnEnable()
        {
            AddressChanged?.Invoke(LanLink.LocalAddress());
            Say("Tap HOST on one phone and JOIN on the other.");
        }

        /// <summary>Wire a Button here. This device waits to be joined, and is player 1.</summary>
        public void Host()
        {
            if (link == null) return;
            _looking = false;
            link.Host();
            Say($"Hosting. Tell the other phone to tap JOIN.\n{LanLink.LocalAddress()}");
        }

        /// <summary>Wire a Button here. Looks for a host and joins the first one heard.</summary>
        public void JoinFound()
        {
            if (link == null) return;

            if (link.TryFound(out string address))
            {
                _looking = false;
                link.Join(address);
                Say($"Joining {address}…");
                return;
            }

            // Nothing heard yet. Keep listening and join by itself the moment one shouts:
            // the beacon goes out once a second, so a second tap would usually be the
            // player doing the waiting by hand.
            _looking = true;
            link.Discover();
            Say("Looking for a host on this network…");
        }

        /// <summary>Wire a Button here, or a text field's end-edit, for a typed address.</summary>
        public void JoinAt(string address)
        {
            if (link == null) return;
            _looking = false;
            link.Join(address);
            Say($"Joining {address}…");
        }

        public void Disconnect()
        {
            if (link == null) return;
            link.Stop();
            _announced = false;
            _looking = false;
            if (panel != null) panel.SetActive(true);
            Say("Disconnected.");
        }

        void Update()
        {
            if (link == null) return;

            if (link.Connected)
            {
                if (_announced) return;
                _announced = true;
                _looking = false;
                if (panel != null) panel.SetActive(false);
                Connected?.Invoke();
                Say($"Connected as player {link.LocalPlayer + 1}.");
                return;
            }

            _announced = false;

            if (_looking && link.TryFound(out string address))
            {
                _looking = false;
                link.Join(address);
                Say($"Found a host at {address} — joining…");
                return;
            }

            if (link.Role == LinkRole.Host)
            {
                Say($"Hosting as player 1 — {LanLink.LocalAddress()}\n"
                    + "Tap JOIN on the other phone.");
                return;
            }

            if (_looking) return;        // the sentence JoinFound left is still the truth

            // Whatever the link is doing, it says so itself: cannot open the port, no
            // answer, a bad address. Pass it on rather than leaving the player looking at
            // a sentence that stopped being true.
            if (link.state != "off") Say(link.state);
        }

        void Say(string what)
        {
            if (string.IsNullOrEmpty(what)) return;
            state = what;
            if (what == _was) return;
            _was = what;
            StatusChanged?.Invoke(what);
        }
    }
}
