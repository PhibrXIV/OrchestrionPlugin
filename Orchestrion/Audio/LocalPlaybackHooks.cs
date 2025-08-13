using System;
using Orchestrion.BGMSystem;

namespace Orchestrion.Audio;

/// <summary>
/// Tiny bridge you can call from your external/local player lifecycle.
/// Hook these from wherever you already start/stop local song playback.
/// </summary>
public static class LocalPlaybackHooks
{
    /// <summary>Call right after your local player begins playback.</summary>
    public static void OnLocalPlaybackStarted()
        => BGMManager.BeginExternalSilence();

    /// <summary>Call right after your local player fully stops/pauses.</summary>
    public static void OnLocalPlaybackStopped()
        => BGMManager.EndExternalSilence();

    /// <summary>
    /// Acquire a temporary "hold" that keeps the game silenced for the duration of a critical section
    /// (e.g., crossfading A → B). Never lets the ref-count drop to 0 mid-transition.
    /// </summary>
    public static IDisposable AcquireSilenceHold() => new SilenceHold();

    private sealed class SilenceHold : IDisposable
    {
        public SilenceHold()  { BGMManager.BeginExternalSilence(); }
        public void Dispose() { BGMManager.EndExternalSilence(); }
    }
}
