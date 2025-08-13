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
}
