using UnityEngine;
using UnityEngine.UI;

namespace GulagRunners.Game
{
    /// <summary>
    /// One line of text, behind a METHOD.
    ///
    /// <see cref="MenuUI"/> sends its readouts out as UnityEvents and references no uGUI
    /// type at all, which is what keeps the menu free to be drawn by something else later.
    /// A UnityEvent needs something to call, though, and a C# property setter is not a
    /// method a persistent listener can be given from an editor script without reflection.
    ///
    /// So: a method. Three lines of component, and the wiring in the inspector then reads
    /// "goldChanged -> Gold label -> Set", which is a sentence rather than a riddle.
    ///
    /// Its own file, like every MonoBehaviour here. Unity resolves a behaviour by matching
    /// the class to the FILE NAME, and three of these once shared one called MenuWidgets.cs
    /// -- which the scene then deserialised onto whichever object it liked, putting a rank
    /// badge on the gold label and leaving the real one unwired.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuLabel : MonoBehaviour
    {
        [Tooltip("The text to write into. Left empty, one is looked for on this object.")]
        public Text target;

        [Tooltip("What it says while the game is not running, so the screen can be laid " +
                 "out against something that looks like real content.")]
        public string placeholder = "";

        void Reset() => target = GetComponent<Text>();

        void Awake()
        {
            if (target == null) target = GetComponent<Text>();
        }

        public void Set(string value)
        {
            if (target == null) target = GetComponent<Text>();
            if (target != null && target.text != value) target.text = value;
        }
    }
}
