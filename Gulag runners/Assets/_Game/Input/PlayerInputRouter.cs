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

        public InputFlags Read()
        {
            InputFlags flags = InputFlags.None;
            for (int i = 0; i < _sources.Count; i++) flags |= _sources[i].Read();
            return flags;
        }
    }
}
