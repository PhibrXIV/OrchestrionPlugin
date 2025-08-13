using System.IO;
using System.Threading.Tasks;
using CheapLoc;
using Dalamud.Plugin.Services;
using Orchestrion.BGMSystem;
using Orchestrion.Ipc;
using Orchestrion.Persistence;
using Orchestrion.Types;

namespace Orchestrion.Audio;

public static class BGMManager
{
    private static readonly BGMController _bgmController;
    private static readonly OrchestrionIpcManager _ipcManager;

    private static bool _isPlayingReplacement;
    private static string _ddPlaylist;

    // External/local playback silencing state
    private static int _externalSilenceDepth;
    private static bool _silencedViaExternal;
    // Which in-game BGM ID is currently being replaced by a local file (0 = none)
    private static int _activeLocalTargetId;
    public static int ActiveLocalTargetId => _activeLocalTargetId;

    // Fade timings (tweak as desired)
    private const int FadeOutMs = 2000;
    private const int FadeInMs = 2000;

    public delegate void SongChanged(int oldSong, int currentSong, int oldSecondSong, int oldCurrentSong, bool oldPlayedByOrch, bool playedByOrchestrion);
    public static event SongChanged OnSongChanged;

    public static int CurrentSongId => _bgmController.CurrentSongId;
    public static int PlayingSongId => _bgmController.PlayingSongId;
    public static int CurrentAudibleSong => _bgmController.CurrentAudibleSong;
    public static int PlayingScene => _bgmController.PlayingScene;

    static BGMManager()
    {
        _bgmController = new BGMController();
        _ipcManager = new OrchestrionIpcManager();

        DalamudApi.Framework.Update += Update;
        _bgmController.OnSongChanged += HandleSongChanged;
        OnSongChanged += IpcUpdate;
    }

    public static void Dispose()
    {
        DalamudApi.Framework.Update -= Update;
        Stop();
        _bgmController.Dispose();
    }

    private static void IpcUpdate(int oldSong, int newSong, int oldSecondSong, int oldCurrentSong, bool oldPlayedByOrch, bool playedByOrch)
    {
        _ipcManager.InvokeSongChanged(newSong);
        if (playedByOrch) _ipcManager.InvokeOrchSongChanged(newSong);
    }

    private static long _lastLocalAudibleTick;

    public static void Update(IFramework _)
    {
        _bgmController.Update();

        // Keep local player's volume synced to in-game settings.
        LocalAudioPlayer.ApplyGameVolume();

        // Track when local audio was last definitely audible.
        if (LocalAudioPlayer.IsPlaying)
            _lastLocalAudibleTick = Environment.TickCount64;

        // Safety: only release silence if local audio has been stopped for a short while,
        // to avoid dropping silence during crossfades or loop boundaries.
        const int GraceMs = 750;
        if (_silencedViaExternal && !LocalAudioPlayer.IsPlaying)
        {
            if (Environment.TickCount64 - _lastLocalAudibleTick > GraceMs)
                EndExternalSilence();
        }
    }

    private static void HandleSongChanged(int oldSong, int newSong, int oldSecondSong, int newSecondSong)
    {
        var currentChanged = oldSong != newSong;
        var secondChanged = oldSecondSong != newSecondSong;

        var newHasReplacement = Configuration.Instance.SongReplacements.TryGetValue(newSong, out var newSongReplacement);
        var newSecondHasReplacement = Configuration.Instance.SongReplacements.TryGetValue(newSecondSong, out var newSecondSongReplacement);

        if (currentChanged)
            DalamudApi.PluginLog.Debug($"[HandleSongChanged] Current Song ID changed from {_bgmController.OldSongId} to {_bgmController.CurrentSongId}");

        if (secondChanged)
            DalamudApi.PluginLog.Debug($"[HandleSongChanged] Second song ID changed from {_bgmController.OldSecondSongId} to {_bgmController.SecondSongId}");

        // Deep Dungeon mode overrides everything.
        if (PlayingSongId != 0 && DeepDungeonModeActive())
        {
            if (!string.IsNullOrEmpty(_ddPlaylist) && !Configuration.Instance.Playlists.ContainsKey(_ddPlaylist))
                Stop();
            else
                PlayRandomSong(_ddPlaylist);
            return;
        }

        // If the user manually started something and it wasn't a replacement, do nothing.
        if (PlayingSongId != 0 && !_isPlayingReplacement) return;
        // Don't care about behind song unless we were playing a replacement.
        if (secondChanged && !currentChanged && !_isPlayingReplacement) return;

        // =========================================================
        // While external/local playback is active
        // =========================================================
        if (_silencedViaExternal)
        {
            if (newHasReplacement && newSongReplacement.IsLocal)
            {
                var path = newSongReplacement.LocalPath?.Trim() ?? string.Empty;
                if (File.Exists(path))
                {
                    _activeLocalTargetId = newSong;
                    _ = LocalAudioPlayer.CrossfadeToFile(path, FadeOutMs, FadeInMs);
                }
                else
                {
                    DalamudApi.ChatGui.PrintError($"[Orchestrion] Local replacement missing: {path}");
                }

                // Keep the game silenced underneath (force silent track if needed).
                if (PlayingSongId != 1 || !_isPlayingReplacement)
                    Play(1, isReplacement: true);

                // NEW: let the UI (DTR/chat/ipc) know the *game* changed even though we’re still forcing silence.
                InvokeSongChanged(oldSong, newSong, oldSecondSong, newSecondSong, oldPlayedByOrch: _isPlayingReplacement, playedByOrch: true);

                return;
            }

            // Otherwise (NO local replacement for the new game BGM):
            //  - fade out and stop local playback,
            //  - end external silence,
            //  - optionally play an in-game replacement if configured.
            int toPlayAfter = ResolveInGameReplacementToPlay(
                newSong, newSecondSong,
                newHasReplacement, newSongReplacement,
                newSecondHasReplacement, newSecondSongReplacement,
                oldSong, PlayingSongId);

            _ = Task.Run(async () =>
            {
                await LocalAudioPlayer.StopAsync(FadeOutMs).ConfigureAwait(false);
                _activeLocalTargetId = 0;
                EndExternalSilence();
                if (toPlayAfter > 0)
                    Play(toPlayAfter, isReplacement: true);
            });

            return;
        }

        // =========================================================
        // Local-file replacement handling (when local is NOT currently active)
        // =========================================================
        if (newHasReplacement && newSongReplacement.IsLocal)
        {
            var path = newSongReplacement.LocalPath?.Trim() ?? string.Empty;
            if (!File.Exists(path))
            {
                DalamudApi.ChatGui.PrintError($"[Orchestrion] Local replacement missing: {path}");
                // fall back to normal replacement rules
            }
            else
            {
                _activeLocalTargetId = newSong;
                // Start local playback with a fade and silence the game audio beneath it.
                BeginExternalSilence();
                LocalAudioPlayer.PlayFile(path, 0, FadeInMs);
                return;
            }
        }

        // =========================================================
        // Normal in-game replacement handling (existing behavior)
        // =========================================================

        if (!newHasReplacement) // user isn't playing and no replacement at all
        {
            if (PlayingSongId != 0 || LocalAudioPlayer.IsPlaying)
                Stop();
            else
                InvokeSongChanged(oldSong, newSong, oldSecondSong, newSecondSong, oldPlayedByOrch: false, playedByOrch: false);
            return;
        }

        var toPlay = 0;

        DalamudApi.PluginLog.Debug($"[HandleSongChanged] Song ID {newSong} has a replacement of {newSongReplacement.ReplacementId}");

        // Handle 2nd changing when 1st has replacement of NoChangeId
        if (newSongReplacement.ReplacementId == SongReplacementEntry.NoChangeId)
        {
            if (secondChanged)
            {
                toPlay = newSecondSong;
                if (newSecondHasReplacement && !newSecondSongReplacement.IsLocal)
                    toPlay = newSecondSongReplacement.ReplacementId;
                if (toPlay == SongReplacementEntry.NoChangeId) return; // give up
            }
        }

        if (newSongReplacement.ReplacementId == SongReplacementEntry.NoChangeId && PlayingSongId == 0)
        {
            toPlay = oldSong; // no net BGM change
        }
        else if (newSongReplacement.ReplacementId != SongReplacementEntry.NoChangeId)
        {
            toPlay = newSongReplacement.ReplacementId;
        }

        // Ensure any lingering local playback is stopped when switching to an in-game replacement.
        if (LocalAudioPlayer.IsPlaying) _ = LocalAudioPlayer.StopAsync(FadeOutMs);

        // We might have been silencing the game earlier; make sure it's released.
        if (_silencedViaExternal) EndExternalSilence();

        Play(toPlay, isReplacement: true);
    }

    private static int ResolveInGameReplacementToPlay(
        int newSong, int newSecondSong,
        bool newHasReplacement, SongReplacementEntry newRep,
        bool newSecondHasReplacement, SongReplacementEntry newSecondRep,
        int oldSong, int playingSongId)
    {
        if (!newHasReplacement || newRep.IsLocal)
            return 0;

        if (newRep.ReplacementId == SongReplacementEntry.NoChangeId)
        {
            // Prefer a concrete replacement for the second track if one exists.
            if (newSecondHasReplacement && !newSecondRep.IsLocal && newSecondRep.ReplacementId != SongReplacementEntry.NoChangeId)
                return newSecondRep.ReplacementId;

            // If nothing is currently forced by us, keep the old audible song (game's own crossfade).
            if (playingSongId == 0)
                return oldSong;

            return 0;
        }

        return newRep.ReplacementId;
    }

    public static void Play(int songId, bool isReplacement = false)
    {
        var wasPlaying = PlayingSongId != 0;
        var oldSongId = CurrentAudibleSong;
        var secondSongId = _bgmController.SecondSongId;

        DalamudApi.PluginLog.Debug($"[Play] Playing {songId}");
        InvokeSongChanged(oldSongId, songId, secondSongId, oldSongId, oldPlayedByOrch: wasPlaying, playedByOrch: true);
        _bgmController.SetSong((ushort)songId);
        _isPlayingReplacement = isReplacement;
    }

    public static void Stop()
    {
        // Stop playlist if running.
        if (PlaylistManager.IsPlaying)
        {
            DalamudApi.PluginLog.Debug("[Stop] Stopping playlist...");
            PlaylistManager.Reset();
        }

        _ddPlaylist = null;

        // Fade out local playback if active.
        if (LocalAudioPlayer.IsPlaying)
        {
            DalamudApi.PluginLog.Debug("[Stop] Stopping local playback (fade)...");
            _ = LocalAudioPlayer.StopAsync(FadeOutMs);
        }

        // Release any explicit silence.
        _externalSilenceDepth = 0;
        _silencedViaExternal = false;
        _activeLocalTargetId = 0;
        if (PlayingSongId == 0) return;
        DalamudApi.PluginLog.Debug($"[Stop] Stopping playing {_bgmController.PlayingSongId}...");

        if (Configuration.Instance.SongReplacements.TryGetValue(_bgmController.CurrentSongId, out var replacement) && !replacement.IsLocal)
        {
            DalamudApi.PluginLog.Debug($"[Stop] Song ID {_bgmController.CurrentSongId} has a replacement of {replacement.ReplacementId}...");

            var toPlay = replacement.ReplacementId;

            if (toPlay == SongReplacementEntry.NoChangeId)
                toPlay = _bgmController.OldSongId;

            Play(toPlay, isReplacement: true);
            return;
        }

        var second = _bgmController.SecondSongId;

        InvokeSongChanged(PlayingSongId, CurrentSongId, second, second, oldPlayedByOrch: true, playedByOrch: false);
        _bgmController.SetSong(0);
        _isPlayingReplacement = false;
    }

    private static void InvokeSongChanged(int oldSongId, int newSongId, int oldSecondSongId, int newSecondSongId, bool oldPlayedByOrch, bool playedByOrch)
    {
        DalamudApi.PluginLog.Debug($"[InvokeSongChanged] {oldSongId} -> {newSongId}, {oldSecondSongId} -> {newSecondSongId} | {oldPlayedByOrch} {playedByOrch}");
        OnSongChanged?.Invoke(oldSongId, newSongId, oldSecondSongId, newSecondSongId, oldPlayedByOrch, playedByOrch);
    }

    public static void PlayRandomSong(string playlistName = "")
    {
        if (SongList.Instance.TryGetRandomSong(playlistName, out var randomFavoriteSong))
            Play(randomFavoriteSong);
        else
            DalamudApi.ChatGui.PrintError(Loc.Localize("NoPossibleSongs", "No possible songs found."));
    }

    public static void StartDeepDungeonMode(string playlistName = "")
    {
        _ddPlaylist = playlistName;
        PlayRandomSong(_ddPlaylist);
    }

    public static void StopDeepDungeonMode()
    {
        _ddPlaylist = null;
        Stop();
    }

    public static bool DeepDungeonModeActive() => _ddPlaylist != null;

    // =========================================================
    // External/local playback silencing (used by LocalPlaybackHooks)
    // =========================================================

    /// <summary>
    /// Force the in-game BGM to the silent track (ID 1) while an external/local player is active.
    /// Safe to call multiple times; requires matching EndExternalSilence calls.
    /// </summary>
    public static void BeginExternalSilence()
    {
        _externalSilenceDepth++;
        if (_silencedViaExternal) return;

        _silencedViaExternal = true;

        // If we're not already forcing silence, do so now.
        if (PlayingSongId != 1 || !_isPlayingReplacement)
            Play(1, isReplacement: true);
    }

    /// <summary>
    /// Release the forced silence started by <see cref="BeginExternalSilence"/>.
    /// When the depth reaches zero, Orchestrion stops forcing a song so the game’s current BGM is audible again.
    /// </summary>
    public static void EndExternalSilence()
    {
        if (_externalSilenceDepth > 0)
            _externalSilenceDepth--;

        if (_externalSilenceDepth > 0) return;
        if (!_silencedViaExternal) return;

        _silencedViaExternal = false;
        _activeLocalTargetId = 0;
        // If we were enforcing silence via replacement, stop forcing any song.
        if (PlayingSongId == 1 && _isPlayingReplacement)
        {
            var oldSongId = CurrentAudibleSong;
            var second = _bgmController.SecondSongId;

            // Notify and release control so the game's own BGM comes through.
            InvokeSongChanged(oldSongId, _bgmController.CurrentSongId, second, second, oldPlayedByOrch: true, playedByOrch: false);
            _bgmController.SetSong(0);
            _isPlayingReplacement = false;
        }
    }
}
