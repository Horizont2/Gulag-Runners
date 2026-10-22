using System.Collections.Generic;
using UnityEngine;
using GulagRunners.Sim;

namespace GulagRunners.Game
{
    /// <summary>
    /// Merges every input source on this object. Keyboard and touch can both be live at once,
    /// which is what makes testing on a device next to the editor painless.
    /// </summary>
    public sealed class PlayerInputRouter : MonoBehaviour, IPlayerInputSource
    {
        readonly List<IPlayerInputSource> _sources = new List<IPlayerInputSource>();

        void Awake()
        {
            foreach (MonoBehaviour mb in GetComponentsInChildren<MonoBehaviour>(true))
                if (mb is IPlayerInputSource src && !ReferenceEquals(src, this))
                    _sources.Add(src);
        }

        public void Register(IPlayerInputSource source)
        {
            if (source != null && !_sources.Contains(source)) _sources.Add(source);
        }

        public TickInput Read()
        {
            // One source may take the fighter over -- a bot, a replay. Merging it with a
            // keyboard would give a fighter two drivers, and the symptom of that is a
            // character who will not stop walking because something is always pressing.
            for (int i = 0; i < _sources.Count; i++)
                if (_sources[i] is IInputTakeover takeover && takeover.TakingOver)
                    return _sources[i].Read();

            // Buttons from everything at once; the aim from whichever source is actually
            // pushing its stick hardest. Summing two aims would point the arm at the
            // average of a touch stick resting at zero and a pad being used, which is
            // half as far as asked -- so they compete rather than add.
            InputFlags flags = InputFlags.None;
            int bestX = 0, bestY = 0, bestPush = 0;
            for (int i = 0; i < _sources.Count; i++)
            {
                TickInput t = _sources[i].Read();
                flags |= t.Flags;

                int push = t.AimX * t.AimX + t.AimY * t.AimY;
                if (push > bestPush) { bestPush = push; bestX = t.AimX; bestY = t.AimY; }
            }
            return new TickInput(flags, bestX, bestY);
        }
    }
}
