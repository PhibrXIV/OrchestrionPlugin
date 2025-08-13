using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Orchestrion.Audio;

/// <summary>
/// Minimal local audio player with smooth fade in/out and crossfade support,
/// plus a base "game volume" multiplier that mirrors the in-game Master×BGM levels.
/// </summary>
public static class LocalAudioPlayer
{
    private static readonly object _gate = new();
    private static readonly SemaphoreSlim _transitionLock = new(1, 1);

    private static WaveOutEvent? _output;
    private static AudioFileReader? _reader;

    private static float _fadeGain = 1f;       // 0..1, controlled by fades
    private static float _baseGameVolume = 1f; // 0..1, mirrors in-game (master * BGM * mutes)
    private static CancellationTokenSource? _fadeCts;

    // Gentler defaults
    private const int DefaultFadeOutMs = 4000;
    private const int DefaultFadeInMs = 2000;

    public static bool IsPlaying
        => _output is not null && _output.PlaybackState == PlaybackState.Playing;

    public static TimeSpan Duration
        => _reader?.TotalTime ?? TimeSpan.Zero;

    public static TimeSpan Position
        => _reader?.CurrentTime ?? TimeSpan.Zero;

    /// <summary>
    /// Apply the last set base/game volume. Call when you update it externally.
    /// </summary>
    public static void ApplyGameVolume()
    {
        UpdateOutputVolume();
    }

    /// <summary>
    /// Convenience overload for callers that pass the base game volume (0..1).
    /// </summary>
    public static void ApplyGameVolume(float baseVolume01)
    {
        SetBaseVolume(baseVolume01);
    }

    /// <summary>Directly set the base/game volume (0..1).</summary>
    public static void SetBaseVolume(float v01)
    {
        lock (_gate)
        {
            _baseGameVolume = Math.Clamp(v01, 0f, 1f);
            UpdateOutputVolume();
        }
    }

    /// <summary>
    /// Start playback of a file with optional fades (defaults tuned for subtle starts).
    /// </summary>
    public static void PlayFile(string path, int fadeOutMs = DefaultFadeOutMs, int fadeInMs = DefaultFadeInMs)
        => _ = TransitionToAsync(path, fadeOutMs, fadeInMs);

    /// <summary>
    /// Crossfade from the current file to a new one.
    /// </summary>
    public static Task CrossfadeToFile(string path, int fadeOutMs = DefaultFadeOutMs, int fadeInMs = DefaultFadeInMs)
        => TransitionToAsync(path, fadeOutMs, fadeInMs);

    /// <summary>
    /// Back-compat wrapper (older code called LocalAudioPlayer.Play).
    /// </summary>
    public static void Play(string path) => PlayFile(path, DefaultFadeOutMs, DefaultFadeInMs);

    /// <summary>
    /// Back-compat wrapper with explicit fade timings.
    /// </summary>
    public static void Play(string path, int fadeOutMs, int fadeInMs) => PlayFile(path, fadeOutMs, fadeInMs);

    public static void Stop()
        => _ = StopAsync(DefaultFadeOutMs);

    public static async Task StopAsync(int fadeOutMs = DefaultFadeOutMs)
    {
        await _transitionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            CancelFade_NoLock();
            if (_output is not null)
            {
                var cts = NewFadeCts();
                await FadeToAsync(0f, fadeOutMs, cts.Token).ConfigureAwait(false);
            }
            InternalStop_NoLock();
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    // -------------------------- internals --------------------------

    private static async Task TransitionToAsync(string path, int fadeOutMs, int fadeInMs)
    {
        if (!File.Exists(path))
            return;

        await _transitionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            CancelFade_NoLock();

            // Fade out current
            if (_output is not null)
            {
                var ctsOut = NewFadeCts();
                await FadeToAsync(0f, fadeOutMs, ctsOut.Token).ConfigureAwait(false);
            }

            // Swap stream
            InternalStop_NoLock();
            InternalStart_NoLock(path);

            // Fade in new
            _fadeGain = 0f;
            UpdateOutputVolume();
            var ctsIn = NewFadeCts();
            await FadeToAsync(1f, fadeInMs, ctsIn.Token).ConfigureAwait(false);
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    private static void InternalStart_NoLock(string path)
    {
        _reader = new AudioFileReader(path)
        {
            Volume = 0f // fade will raise it
        };
        _output = new WaveOutEvent();
        _output.Init(_reader);
        _output.Play();
        UpdateOutputVolume();
    }

    private static void InternalStop_NoLock()
    {
        try { _output?.Stop(); } catch { /* ignore */ }
        _output?.Dispose();
        _reader?.Dispose();
        _output = null;
        _reader = null;
        _fadeGain = 1f;
        UpdateOutputVolume();
    }

    private static async Task FadeToAsync(float target, int ms, CancellationToken ct)
    {
        if (_reader is null || _output is null || ms <= 0)
        {
            _fadeGain = target;
            UpdateOutputVolume();
            return;
        }

        var start = _fadeGain;
        var delta = target - start;
        ms = Math.Max(ms, 1);
        var steps = Math.Max(1, ms / 16); // ~60 fps

        for (var i = 1; i <= steps; i++)
        {
            ct.ThrowIfCancellationRequested();
            var t = i / (float)steps;
            // Cosine easing for a nicer curve.
            var eased = (float)((1 - Math.Cos(t * Math.PI)) / 2.0);
            _fadeGain = start + delta * eased;
            UpdateOutputVolume();
            await Task.Delay(16, ct).ConfigureAwait(false);
        }

        _fadeGain = target;
        UpdateOutputVolume();
    }

    private static void UpdateOutputVolume()
    {
        if (_reader is null) return;
        var vol = Math.Clamp(_fadeGain * _baseGameVolume, 0f, 1f);
        _reader.Volume = vol;
    }

    private static CancellationTokenSource NewFadeCts()
    {
        CancelFade_NoLock();
        _fadeCts = new CancellationTokenSource();
        return _fadeCts;
    }

    private static void CancelFade_NoLock()
    {
        try { _fadeCts?.Cancel(); } catch { /* ignore */ }
        _fadeCts?.Dispose();
        _fadeCts = null;
    }
}
