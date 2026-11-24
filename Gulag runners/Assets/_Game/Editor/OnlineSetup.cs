using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using GulagRunners.Game;
using GulagRunners.Meta;
using GulagRunners.Net;

using UnityEventTools = UnityEditor.Events.UnityEventTools;

namespace GulagRunners.GameEditor
{
    /// <summary>
    /// Puts the live pool behind the search screen: the queue, the relay, and the one event
    /// that connects them.
    ///
    /// IT WAS WIRED BY HAND, ONCE, AND NOTHING KNEW. Menu.unity carries a UgsQueue and an
    /// OnlineMatch on the search canvas, with MatchSearch's Queue slot, Opponent Gate slot
    /// and Paired event pointing at them -- five references somebody set in the inspector
    /// and no tool could restore. <see cref="SearchBuilder"/> begins by destroying the
    /// canvas those five live on and building a fresh one, so rebuilding the search screen
    /// SILENTLY TOOK ONLINE PLAY OUT OF THE GAME: the slot came up empty, MatchSearch fell
    /// back to the practice opponent exactly as it is designed to, every match worked, and
    /// nothing anywhere said that the only thing left in the queue was a bot.
    ///
    /// That is the same shape as every other screen in this project and the reason they are
    /// all built rather than placed: a scene somebody has to assemble again is a scene that
    /// gets assembled once and then forgotten. So this is the tool, and SearchBuilder calls
    /// it at the end of its own run.
    ///
    /// It is additive and safe to run twice. Nothing is destroyed, components already there
    /// are kept with their settings, and the Paired listener is cleared before it is added
    /// so a second run does not open two relays for one pairing.
    /// </summary>
    public static class OnlineSetup
    {
        [MenuItem("Tools/Gulag Runners/Setup/Set up online matchmaking")]
        public static void Setup()
        {
            MatchSearch search = Object.FindFirstObjectByType<MatchSearch>();
            if (search == null)
            {
                Debug.LogWarning(
                    "No MatchSearch in this scene, so there is no search screen to put a "
                    + "queue behind. Open Menu.unity, or run Tools > Gulag Runners > Build "
                    + "the search screen first.");
                return;
            }

            List<string> wired = new List<string>();
            GameObject go = search.gameObject;

            // On the search screen's own object, which is the one that outlives the panel.
            // MatchSearch's shownChanged switches a CHILD off rather than this -- deliberately,
            // so its own Update keeps running -- and these two have to keep running with it:
            // UgsQueue is polling a service and OnlineMatch is pumping a relay, and a
            // component on a deactivated object does neither.
            UgsQueue queue = go.GetComponent<UgsQueue>();
            if (queue == null)
            {
                queue = Undo.AddComponent<UgsQueue>(go);
                wired.Add("a UgsQueue");
            }

            OnlineMatch online = go.GetComponent<OnlineMatch>();
            if (online == null)
            {
                online = Undo.AddComponent<OnlineMatch>(go);
                wired.Add("an OnlineMatch");
            }

            Undo.RecordObject(search, "Set up online matchmaking");
            Undo.RecordObject(online, "Set up online matchmaking");

            if (search.queueSource != queue)
            {
                search.queueSource = queue;
                wired.Add("the queue slot");
            }

            if (search.opponentGate != online)
            {
                search.opponentGate = online;
                wired.Add("the opponent gate");
            }

            if (online.search != search)
            {
                online.search = search;
                wired.Add("the relay's search screen");
            }

            // The director too, because the one thing worse than no online is online that
            // pairs you and then loads nothing. MatchSearch finds one at runtime when this
            // is empty, and it was empty in Menu.unity for long enough to be worth setting
            // rather than relying on.
            if (search.director == null)
            {
                search.director = Object.FindFirstObjectByType<MenuDirector>();
                if (search.director != null) wired.Add("the director");
            }

            if (search.progress == null)
            {
                search.progress = Object.FindFirstObjectByType<PlayerProgress>();
                if (search.progress != null) wired.Add("saved progress");
            }

            // Paired -> Begin, and cleared first. Two listeners means two handshakes for one
            // pairing: the second calls Drop() on the first before it has finished, so the
            // relay that gets carried into the arena is whichever one happened to win.
            for (int i = search.Paired.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEventTools.RemovePersistentListener(search.Paired, i);

            // Void, although Paired carries a bool: Begin takes no argument and reads the
            // pairing off the search screen instead, which is how the scene has it wired
            // (m_Mode 1). AddVoidPersistentListener is the call for exactly that shape.
            UnityEventTools.AddVoidPersistentListener(
                search.Paired, new UnityAction(online.Begin));
            wired.Add("Paired -> Begin");

            EditorUtility.SetDirty(search);
            EditorUtility.SetDirty(online);
            EditorUtility.SetDirty(queue);

            Debug.Log(
                $"Online matchmaking: {string.Join(", ", wired)}.\n"
                + $"Queue name \"{queue.queueName}\", rating attribute "
                + $"\"{queue.ratingAttribute}\" -- both have to match the queue and the "
                + "match rule on the Unity dashboard exactly, or the rule compares nothing "
                + "and everybody matches everybody. Matchmaker AND Relay both have to be "
                + "enabled there.\n"
                + "Nothing here can fail in a way that stops the game: MatchPool keeps the "
                + "practice opponent under this queue, so an empty pool, a dashboard with "
                + "the service switched off and a phone with no signal all end in a match "
                + "rather than on a dead search screen. Watch UgsQueue's own State field "
                + "while searching to see which of the two you got.",
                go);
        }
    }
}
