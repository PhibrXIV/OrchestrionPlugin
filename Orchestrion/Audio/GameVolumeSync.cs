using System;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace Orchestrion.Audio;

/// <summary>
/// Polls the game's audio config every frame and feeds Master×BGM×mutes to LocalAudioPlayer.
/// Lightweight (no hooks), uses ConfigModule.Values indices.
/// </summary>
public static class GameVolumeSync
{
    // Indices into ConfigModule.Values (match SoundSetter's ConfigOptionKind.ConfigEnum)
    private const int MasterIndex = 86; // byte 0..100
    private const int BgmIndex = 87; // byte 0..100
    private const int MasterMutedIndex = 96; // bool
    private const int BgmMutedIndex = 97; // bool

    private static float _lastSent = -1f;

    public static void Initialize()
    {
        DalamudApi.Framework.Update += OnUpdate;
        // Push an initial value in case we start playing immediately.
        TryPushVolume();
    }

    public static void Dispose()
    {
        DalamudApi.Framework.Update -= OnUpdate;
    }

    private static void OnUpdate(IFramework _)
    {
        TryPushVolume();
    }

    private static unsafe void TryPushVolume()
    {
        try
        {
            var cfg = ConfigModule.Instance();
            if (cfg == null) return;

            var master = ReadByte(cfg, MasterIndex);
            var bgm = ReadByte(cfg, BgmIndex);
            var masterMuted = ReadBool(cfg, MasterMutedIndex);
            var bgmMuted = ReadBool(cfg, BgmMutedIndex);

            var v = (masterMuted || bgmMuted) ? 0f : (master / 100f) * (bgm / 100f);
            if (Math.Abs(v - _lastSent) > 0.0025f)
            {
                _lastSent = v;
                LocalAudioPlayer.ApplyGameVolume(v);
            }
        }
        catch
        {
            // Best-effort; don't spam logs if ConfigModule layout changes.
        }
    }

    private static unsafe byte ReadByte(ConfigModule* module, int idx)
    {
        ref var ov = ref OptionValueProxy.FromOptionValue(ref module->Values[idx]);
        return (byte)ov.Value1;
    }

    private static unsafe bool ReadBool(ConfigModule* module, int idx)
    {
        ref var ov = ref OptionValueProxy.FromOptionValue(ref module->Values[idx]);
        return ov.Value1 != 0;
    }

    // Mirror of the game's opaque OptionValue so we can read the fields.
    private struct OptionValueProxy
    {
        public ulong Value1;
        public ulong Value2;

        public static unsafe ref OptionValueProxy FromOptionValue(ref ConfigModule.OptionValue ov)
            => ref System.Runtime.CompilerServices.Unsafe.As<ConfigModule.OptionValue, OptionValueProxy>(ref ov);
    }
}
