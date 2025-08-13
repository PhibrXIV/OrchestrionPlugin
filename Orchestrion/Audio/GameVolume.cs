using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace Orchestrion.Audio;

/// <summary>
/// Reads FFXIV sound options directly from ConfigModule (like SoundSetter),
/// computes the effective BGM loudness, and forwards it to the external player.
/// - Uses Master * BGM sliders and respects the Master/BGM mute flags.
/// - Optionally mutes when the game window is not active and "Play sounds while
///   window is not active" (global or BGM) is off.
/// </summary>
public static unsafe class GameVolume
{
    // These indices mirror SoundSetter's mapping of ConfigModule.Values[…]
    private enum ConfigIndex
    {
        PlaySoundsWhileWindowIsNotActive = 80,

        Master = 86,
        Bgm,
        SoundEffects,
        Voice,
        SystemSounds,
        AmbientSounds,
        Performance,

        Self,
        Party,
        OtherPCs,

        MasterMuted,
        BgmMuted,
        SoundEffectsMuted,
        VoiceMuted,
        SystemSoundsMuted,
        AmbientSoundsMuted,
        PerformanceMuted,

        PlaySoundsWhileWindowIsNotActiveBGM = 106,
        PlaySoundsWhileWindowIsNotActiveSoundEffects,
        PlaySoundsWhileWindowIsNotActiveVoice,
        PlaySoundsWhileWindowIsNotActiveSystemSounds,
        PlaySoundsWhileWindowIsNotActiveAmbientSounds,
        PlaySoundsWhileWindowIsNotActivePerformance,
    }

    [StructLayout(LayoutKind.Sequential, Size = 0x10)]
    private struct OptionValueMirror
    {
        public ulong Value1;
        public ulong Value2;
    }

    private static IFramework _framework = null!;
    private static float _lastLinear = -1f;
    private static bool _respectInactiveWindowSetting = true;

    public static void Initialize(IFramework framework)
    {
        _framework = framework;
        _framework.Update += OnUpdate;

        // Push an initial value immediately
        ApplyIfChanged(ReadEffectiveLinear() ?? 1f);
    }

    public static void Dispose()
    {
        if (_framework != null)
            _framework.Update -= OnUpdate;
    }

    private static void OnUpdate(IFramework _)
    {
        var v = ReadEffectiveLinear();
        if (v.HasValue)
            ApplyIfChanged(v.Value);
    }

    private static void ApplyIfChanged(float linear)
    {
        linear = Math.Clamp(linear, 0f, 1f);
        if (Math.Abs(linear - _lastLinear) < 0.001f) return;
        _lastLinear = linear;
        LocalAudioPlayer.ApplyGameVolume(linear);
    }

    /// <summary>
    /// Compute the effective BGM volume as the game would: Master * BGM, respecting mute flags.
    /// Optionally zeroes when the game window is unfocused and the relevant settings are off.
    /// </summary>
    private static float? ReadEffectiveLinear()
    {
        var cm = ConfigModule.Instance();
        if (cm == null) return null;

        try
        {
            byte master = ReadByte(cm, ConfigIndex.Master);
            byte bgm = ReadByte(cm, ConfigIndex.Bgm);

            bool masterMuted = ReadBool(cm, ConfigIndex.MasterMuted);
            bool bgmMuted = ReadBool(cm, ConfigIndex.BgmMuted);

            if (_respectInactiveWindowSetting && !IsGameWindowActive())
            {
                bool playWhenInactiveGlobal = ReadBool(cm, ConfigIndex.PlaySoundsWhileWindowIsNotActive);
                bool playWhenInactiveBgm = ReadBool(cm, ConfigIndex.PlaySoundsWhileWindowIsNotActiveBGM);
                if (!playWhenInactiveGlobal || !playWhenInactiveBgm)
                    return 0f;
            }

            if (masterMuted || bgmMuted) return 0f;

            var linear = (master / 100f) * (bgm / 100f);
            return Math.Clamp(linear, 0f, 1f);
        }
        catch
        {
            return null;
        }
    }

    // --- raw readers (fixed ref usage) --------------------------------------

    private static byte ReadByte(ConfigModule* cm, ConfigIndex idx)
    {
        ref var raw = ref cm->Values[(int)idx];
        ref var ov = ref UnsafeRef(ref raw);
        return (byte)ov.Value1;
    }

    private static bool ReadBool(ConfigModule* cm, ConfigIndex idx)
    {
        ref var raw = ref cm->Values[(int)idx];
        ref var ov = ref UnsafeRef(ref raw);
        return ((byte)ov.Value1) != 0;
    }

    private static ref OptionValueMirror UnsafeRef(ref ConfigModule.OptionValue v)
    {
        // reinterpret cast: ConfigModule.OptionValue -> OptionValueMirror
        return ref System.Runtime.CompilerServices.Unsafe.As<ConfigModule.OptionValue, OptionValueMirror>(ref v);
    }

    // --- window focus -------------------------------------------------------

    private static bool IsGameWindowActive()
    {
        try
        {
            var current = Process.GetCurrentProcess();
            var hwnd = current?.MainWindowHandle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero) return true; // if unknown, treat as active
            var fg = GetForegroundWindow();
            return fg == hwnd;
        }
        catch
        {
            return true;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
