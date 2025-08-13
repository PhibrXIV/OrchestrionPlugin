using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NAudio.Wave;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace Orchestrion.Audio;

public static class LocalAudioPlayer
{
    private static readonly object Gate = new();

    private static IWavePlayer _output;
    private static AudioFileReader _reader;
    private static bool _stopping;
    private static bool _isPaused;

    public static bool IsPlaying { get; private set; }
    public static string CurrentPath { get; private set; } = string.Empty;

    /// <summary>Total duration of the loaded track, or zero if none.</summary>
    public static TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    /// <summary>Current playback position (get/set). No-op if nothing is loaded.</summary>
    public static TimeSpan Position
    {
        get => _reader?.CurrentTime ?? TimeSpan.Zero;
        set { if (_reader != null) _reader.CurrentTime = value; }
    }

    /// <summary>Convenience alias kept for callers expecting Play(string).</summary>
    public static void Play(string path) => PlayFile(path);

    /// <summary>Load and start playing a local audio file.</summary>
    public static void PlayFile(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                DalamudApi.ChatGui.PrintError($"[Orchestrion] File not found: {path}");
                return;
            }

            lock (Gate)
            {
                StopInternal();

                _reader = new AudioFileReader(path)
                {
                    Volume = 1.0f, // actual level is applied by ApplyGameVolume()
                };

                _output = new WaveOutEvent();
                _output.Init(_reader);
                _output.PlaybackStopped += OutputOnPlaybackStopped;

                _output.Play();
                _isPaused = false;
                IsPlaying = true;
                CurrentPath = path;

                // Immediately align to in-game volume.
                ApplyGameVolume();
            }
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, "[LocalAudioPlayer] Failed to play file.");
            Stop();
        }
    }

    /// <summary>Resume if paused (does nothing if not loaded).</summary>
    public static void Play()
    {
        lock (Gate)
        {
            if (_output == null || IsPlaying && !_isPaused) return;

            _output.Play();
            _isPaused = false;
            IsPlaying = true;

            // ensure volume reflects current game setting on resume
            ApplyGameVolume();
        }
    }

    /// <summary>Pause playback (does nothing if not loaded).</summary>
    public static void Pause()
    {
        lock (Gate)
        {
            if (_output == null || _isPaused) return;
            try
            {
                _output.Pause();
                _isPaused = true;
                IsPlaying = false;
            }
            catch { /* ignore */ }
        }
    }

    /// <summary>Stop playback.</summary>
    public static void Stop()
    {
        lock (Gate)
        {
            StopInternal();
        }
    }

    private static void StopInternal()
    {
        if (_output != null)
        {
            try
            {
                _stopping = true;
                _output.Stop();
            }
            catch { /* ignored */ }
            finally
            {
                _output.PlaybackStopped -= OutputOnPlaybackStopped;
                _output.Dispose();
                _output = null;
                _stopping = false;
            }
        }

        if (_reader != null)
        {
            _reader.Dispose();
            _reader = null;
        }

        IsPlaying = false;
        _isPaused = false;
        CurrentPath = string.Empty;
    }

    private static void OutputOnPlaybackStopped(object sender, StoppedEventArgs e)
    {
        lock (Gate)
        {
            if (_stopping || _reader == null || _output == null)
                return;

            // Loop by default: restart the same track seamlessly.
            try
            {
                _reader.Position = 0;
                _output.Play();
                IsPlaying = true;
                _isPaused = false;
                ApplyGameVolume();
                return;
            }
            catch
            {
                // Fall through to cleanup if loop restart fails.
            }

            // Cleanup fallback
            _output?.Dispose();
            _reader?.Dispose();
            _output = null;
            _reader = null;
            IsPlaying = false;
            _isPaused = false;
            CurrentPath = string.Empty;
        }
    }

    // =============================
    // In-game volume integration
    // =============================

    /// <summary>
    /// Read the game's current Master + BGM sliders (and their muted flags) and
    /// apply the combined scalar to the local player's volume.
    /// </summary>
    public static void ApplyGameVolume()
    {
        lock (Gate)
        {
            if (_reader == null) return;

            try
            {
                var scalar = ReadGameBgmScalar();
                _reader.Volume = scalar;
            }
            catch (Exception ex)
            {
                DalamudApi.PluginLog.Error(ex, "[LocalAudioPlayer] ApplyGameVolume failed; falling back to 1.0");
                _reader.Volume = 1.0f;
            }
        }
    }

    // Backward-compat overloads: ignore the argument and use live game settings.
    public static void ApplyGameVolume(float _) => ApplyGameVolume();
    public static void ApplyGameVolume(double _) => ApplyGameVolume();

    /// <summary>
    /// Returns 0..1: (Master/100) * (BGM/100) unless either is muted.
    /// </summary>
    private static unsafe float ReadGameBgmScalar()
    {
        var cfg = ConfigModule.Instance();
        if (cfg == null) return 1.0f;

        ref var masterVal = ref RawOptionValue.From(ref cfg->Values[(int)ConfigEnum.Master]);
        ref var bgmVal = ref RawOptionValue.From(ref cfg->Values[(int)ConfigEnum.Bgm]);
        ref var masterMuted = ref RawOptionValue.From(ref cfg->Values[(int)ConfigEnum.MasterMuted]);
        ref var bgmMuted = ref RawOptionValue.From(ref cfg->Values[(int)ConfigEnum.BgmMuted]);

        var muted = (byte)masterMuted.Value1 != 0 || (byte)bgmMuted.Value1 != 0;
        if (muted) return 0.0f;

        var m = Math.Clamp((byte)masterVal.Value1 / 100f, 0f, 1f);
        var b = Math.Clamp((byte)bgmVal.Value1 / 100f, 0f, 1f);
        return Math.Clamp(m * b, 0f, 1f);
    }

    // Minimal local copies so we don't pull in extra files just to read values.
    private enum ConfigEnum
    {
        // Matches SoundSetter's ConfigOptionKind.ConfigEnum
        Master = 86,
        Bgm = 87,

        MasterMuted = 96,
        BgmMuted = 97,
    }

    [StructLayout(LayoutKind.Sequential, Size = 0x10)]
    private struct RawOptionValue
    {
        public ulong Value1;
        public ulong Value2;

        public static unsafe ref RawOptionValue From(ref ConfigModule.OptionValue optionValue)
            => ref Unsafe.As<ConfigModule.OptionValue, RawOptionValue>(ref optionValue);
    }
}
