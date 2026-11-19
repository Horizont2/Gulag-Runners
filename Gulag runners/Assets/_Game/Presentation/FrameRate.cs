using UnityEngine;

namespace GulagRunners.Game
{
    /// <summary>
    /// Asks the phone for sixty frames a second.
    ///
    /// NOBODY HAD ASKED. Application.targetFrameRate was not set anywhere in the project
    /// and QualitySettings has vSyncCount at 0, which means the platform default applies --
    /// and on Android and iOS that default is THIRTY. So every piece of smoothing in this
    /// game, the camera's easing, the bar's catch-up, the fall, the smoke, has been running
    /// at half the rate it was tuned at, and the one-line fix is worth more than any of
    /// them.
    ///
    /// It matters more here than in most games because the simulation ticks at 60 Hz
    /// (docs/06). At 30 frames the presentation draws every second tick, so the
    /// interpolation that exists to hide the tick boundary is instead being asked to hide
    /// half of them.
    ///
    /// RUNTIMEINITIALIZEONLOAD, so it needs no object, no scene and nothing to remember to
    /// wire. It runs before the first scene loads, in a build and in the editor alike.
    ///
    /// SIXTY AND NOT MORE. A 120 Hz phone will happily give 120, and the battery pays for
    /// it twice over while the simulation still decides everything at 60 -- so the extra
    /// frames are interpolation of interpolation. A device that cannot hold 60 is not
    /// forced to: this is a ceiling, not a floor.
    /// </summary>
    public static class FrameRate
    {
        /// <summary>What the game asks for. One place, so a profiler run can move it.</summary>
        public const int Wanted = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Ask()
        {
            // vSync wins over targetFrameRate wherever both exist, so the request is only
            // honoured with it off. It is already off in QualitySettings; this makes the
            // pair explicit rather than leaving one of them to be changed by somebody who
            // does not know it silently disables the other.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Wanted;
        }
    }
}
