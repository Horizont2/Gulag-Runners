using System;
using UnityEngine;
using UnityEngine.Events;

namespace GulagRunners.Game
{
    /// <summary>
    /// What the player owns, as the menu sees it.
    ///
    /// Typed in the inspector for now, because there is no save file yet. When there is
    /// one, this is the shape it fills: nothing else in the menu reads anything else, so
    /// the save slots in here and no screen has to change.
    ///
    /// Gold is a NUMBER and not a fraction on purpose. docs/09 gives it no ceiling, and a
    /// bar implies one — the first mockup drew a bar beside the coin, which is the one
    /// place it lied about a mechanic.
    ///
    /// The season fields are gone for the same reason: there is no pass to fill them.
    /// </summary>
    [Serializable]
    public class MenuProfile
    {
        public string playerName = "Runner";

        [Tooltip("No cap. docs/09: gold only ever buys cosmetics.")]
        public int gold = 0;

        [Header("League")]
        public string league = "Unranked";
        [Range(0f, 1f)] public float leagueProgress;

        [Tooltip("docs/09 protects the first 20 matches. While this is above zero the menu " +
                 "can say so instead of showing a rank nobody has earned yet.")]
        public int protectedMatchesLeft = 20;

        [Header("Dailies")]
        [Tooltip("docs/09 gives three a day at 40 gold each, and they are there to TEACH " +
                 "the mechanics — a counter nobody sees teaches nobody.")]
        public int dailiesDone;
        public int dailiesTotal = 3;
    }

    /// <summary>
    /// The menu's buttons and readouts.
    ///
    /// Wire a Button's OnClick straight to the public methods here: Play, Profile, Shop,
    /// Dailies, Settings, Friends, Back. That is the whole contract.
    ///
    /// No season and no battle pass. docs/09 describes a six-week pass and the menu was
    /// carrying a slot for it -- a number, a bar, a panel and a button -- none of which
    /// the game ships. A readout of something that does not exist is worse than a missing
    /// one: it is a promise, and the first screen is where a player decides what this
    /// game is. It comes back as seasonNumber, seasonProgress, seasonPanel, Season() and
    /// two UnityEvents on the day there is a pass behind them.
    ///
    /// It does not reference Button, Text or any other uGUI type, and that is deliberate.
    /// Everything else in this project talks to the interface through
    /// UnityEngine.EventSystems and nothing else (see VirtualButton), which keeps the game
    /// code free to be drawn by uGUI today and by something else later. So the readouts go
    /// out as UnityEvents: hook GoldChanged to a TMP_Text.SetText or a Text.text setter,
    /// and LeagueFilled to an Image.fillAmount or a Slider.value, in the inspector, where
    /// you can see what is connected to what.
    ///
    /// Nothing is created at play time. Every panel is already in the scene, switched off.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuUI : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Runs the gate and loads the match. Found in the scene when empty.")]
        public MenuDirector director;

        [Tooltip("The fighter on show, so the shop can dress him. Optional.")]
        public MenuFighter fighter;

        [Header("What the player owns")]
        public MenuProfile profile = new MenuProfile();

        [Tooltip("Optional. The saved ladder and purse (Meta/PlayerProgress). Set it and " +
                 "the profile above stops being typed-in numbers and becomes a READOUT of " +
                 "what the player has actually done; leave it empty and the menu is a mock " +
                 "again, which is what it has to stay for laying the screen out.\n\n" +
                 "Found through PlayerProgress.Instance when empty.")]
        public GulagRunners.Meta.PlayerProgress progress;

        [Header("Panels — already in the scene, switched off")]
        [Tooltip("Optional, every one of them. One at a time: opening any closes whatever " +
                 "was open, and a panel that does not exist yet simply cannot be opened. " +
                 "The first screen works on its own and the depth behind it arrives panel " +
                 "by panel.")]
        public GameObject profilePanel;

        [Tooltip("Optional. Cosmetics, priced in gold. docs/09: it never sells power.")]
        public GameObject shopPanel;

        [Tooltip("Optional. The three a day, and what each is asking for.")]
        public GameObject dailiesPanel;

        [Tooltip("Optional. Sound, language, stick sensitivity, controls, account.")]
        public GameObject settingsPanel;

        [Tooltip("Optional. Friends, and the private match docs/09's saved seed lives in.")]
        public GameObject friendsPanel;

        [Tooltip("Optional. Switched off while a panel is open, so the fighter and PLAY " +
                 "are not sitting behind it.")]
        public GameObject homePanel;

        [Header("Readouts")]
        [Tooltip("The gold figure, as text. Wire to a TMP_Text.SetText or a Text.text setter.")]
        public UnityEvent<string> goldChanged;

        [Tooltip("The league's name, or how many protected matches are left.")]
        public UnityEvent<string> leagueChanged;

        [Tooltip("0 to 1. Wire to an Image.fillAmount or a Slider.value.")]
        public UnityEvent<float> leagueFilled;

        [Tooltip("\"1/3\". The badge on the dailies icon.")]
        public UnityEvent<string> dailiesChanged;

        public UnityEvent<string> playerNameChanged;

        [Tooltip("Which league badge to show: the League enum's value, or -1 while the " +
                 "player is still in their first five matches and has none.\n\n" +
                 "An int and not the name, because the name would have to be parsed back " +
                 "out of \"Platinum II\" and the parse is where the bug would live.")]
        public UnityEvent<int> leagueBadgeChanged;

        [Header("Debug")]
        public string openPanel = "";

        void Reset()
        {
            director = FindFirstObjectByType<MenuDirector>();
            fighter = FindFirstObjectByType<MenuFighter>();
        }

        void Awake()
        {
            if (director == null) director = FindFirstObjectByType<MenuDirector>();
            if (fighter == null) fighter = FindFirstObjectByType<MenuFighter>();
        }

        void Start()
        {
            CloseAll();
            Refresh();
        }

        // ------------------------------------------------------------------ the buttons

        /// <summary>PLAY. Opens the gate and leaves.</summary>
        public void Play()
        {
            CloseAll();
            if (director != null) director.Play();
            else Debug.LogWarning($"{name}: no MenuDirector, so PLAY has nothing to open.", this);
        }

        /// <summary>The stone plaque: match history, statistics, saved arena seeds.</summary>
        public void Profile() => Open(profilePanel, nameof(Profile));

        /// <summary>The forge: skins, weapon skins, emotes, graffiti, frames (docs/09).</summary>
        public void Shop() => Open(shopPanel, nameof(Shop));

        /// <summary>The three a day, and what each one is asking for.</summary>
        public void Dailies() => Open(dailiesPanel, nameof(Dailies));

        /// <summary>Sound, language, stick sensitivity, controls, account.</summary>
        public void Settings() => Open(settingsPanel, nameof(Settings));

        /// <summary>Friends, invites, and the saved seed for a private match.</summary>
        public void Friends() => Open(friendsPanel, nameof(Friends));

        /// <summary>Back to the cell. Wire this to every panel's close button.</summary>
        public void Back() => CloseAll();

        // ------------------------------------------------------------------ the wardrobe

        /// <summary>Puts a skin on the fighter. Wire a shop entry to this.</summary>
        public void Wear(string skinId)
        {
            if (fighter != null) fighter.Wear(skinId);
        }

        // ------------------------------------------------------------------ the readouts

        /// <summary>
        /// Pushes every number out to whatever is drawing it. Call it after changing the
        /// profile — buying something, finishing a daily, coming back from a match.
        /// </summary>
        [ContextMenu("Refresh")]
        public void Refresh()
        {
            if (profile == null) return;

            // The ladder is the authority when there is one. Copied into the profile rather
            // than read straight into the events, so the rest of this class has exactly one
            // thing to read and the screen can still be laid out with the ladder absent.
            if (progress == null) progress = GulagRunners.Meta.PlayerProgress.Instance;
            if (progress != null)
            {
                profile.playerName = progress.playerName;
                profile.gold = progress.gold;
                profile.league = progress.LeagueName;
                profile.leagueProgress = progress.LeagueProgress;
                profile.protectedMatchesLeft = progress.PlacementLeft;
                profile.dailiesDone = progress.dailiesDone;
                profile.dailiesTotal = GulagRunners.Meta.Purse.DailiesPerDay;
            }

            goldChanged?.Invoke(profile.gold.ToString());
            playerNameChanged?.Invoke(profile.playerName);

            // While the first twenty matches are protected there is no rank to show, and
            // showing one anyway is the fastest way to make a new player feel ranked badly.
            leagueChanged?.Invoke(profile.protectedMatchesLeft > 0
                ? $"{profile.protectedMatchesLeft} placement matches left"
                : profile.league);
            leagueFilled?.Invoke(Mathf.Clamp01(profile.leagueProgress));
            leagueBadgeChanged?.Invoke(progress != null && progress.matchesPlayed < 5
                ? -1
                : (int)GulagRunners.Meta.Ladder.LeagueOf(
                      progress != null ? progress.rating : 1500.0));

            dailiesChanged?.Invoke($"{profile.dailiesDone}/{Mathf.Max(1, profile.dailiesTotal)}");
        }

        /// <summary>Spends gold if there is enough. True when it went through.</summary>
        public bool Spend(int cost)
        {
            if (profile == null || cost < 0 || profile.gold < cost) return false;
            profile.gold -= cost;
            Refresh();
            return true;
        }

        /// <summary>Adds gold — a match reward, a daily, a streak.</summary>
        public void Earn(int amount)
        {
            if (profile == null || amount <= 0) return;
            profile.gold += amount;
            Refresh();
        }

        // ------------------------------------------------------------------ panels

        void Open(GameObject panel, string which)
        {
            if (panel == null)
            {
                Debug.LogWarning($"{name}: no panel assigned for {which}.", this);
                return;
            }

            // Already open: the button is also the way back out, which is what a player
            // tries first and what saves a close button on every screen.
            if (openPanel == which) { CloseAll(); return; }

            CloseAll();
            panel.SetActive(true);
            if (homePanel != null) homePanel.SetActive(false);
            openPanel = which;
        }

        /// <summary>Everything shut, the cell showing.</summary>
        public void CloseAll()
        {
            Shut(profilePanel);
            Shut(shopPanel);
            Shut(dailiesPanel);
            Shut(settingsPanel);
            Shut(friendsPanel);

            if (homePanel != null && !homePanel.activeSelf) homePanel.SetActive(true);
            openPanel = "";
        }

        static void Shut(GameObject panel)
        {
            if (panel != null && panel.activeSelf) panel.SetActive(false);
        }
    }
}
