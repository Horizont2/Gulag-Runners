using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// Everything the game needs from an audio system, and nothing more.
    ///
    /// <see cref="AudioManager"/> decides WHAT is heard and HOW LOUDLY — that is game design and
    /// it lives in this project. A backend only has to make a noise when told to. Swapping Unity
    /// audio for FMOD is therefore one new class implementing this and one reference changed in
    /// the inspector; no call site moves, and the hearing model, which is balance, is untouched.
    /// </summary>
    public interface IAudioBackend
    {
        /// <summary>
        /// Plays one sound. Volume and pan are already decided — a backend must not apply its own
        /// distance rolloff on top, or the hearing model stops matching what the on-screen
        /// indicator shows.
        /// </summary>
        /// <param name="sound">What to play.</param>
        /// <param name="worldPosition">Where it happened. For FMOD, the event's 3D attributes.</param>
        /// <param name="volume">0..1, already attenuated.</param>
        /// <param name="pan">-1 left, +1 right.</param>
        /// <param name="muffled">True when it came through a floor; a backend may filter it.</param>
        void Play(SoundId sound, Vector3 worldPosition, float volume, float pan, bool muffled);

        /// <summary>True when the backend is ready to be called. A backend still loading is silent.</summary>
        bool Ready { get; }
    }
}
