using System.IO;
using Orchestrion.Persistence;

namespace Orchestrion.Audio;

public static class LocalPlaylistManager
{
    public static bool IsPlaying => LocalAudioPlayer.IsPlaying;
    public static LocalPlaylist? Current => _current;
    public static int CurrentIndex => _current?.CurrentIndex ?? -1;

    private static LocalPlaylist? _current;

    public static void Play(string name)
    {
        if (!Configuration.Instance.LocalPlaylists.TryGetValue(name, out var pl)) return;
        if (pl.Files.Count == 0) return;
        _current = pl;
        if (_current.CurrentIndex < 0) _current.CurrentIndex = 0;
        PlayIndex(_current.CurrentIndex);
    }

    public static void Play(string name, int index)
    {
        if (!Configuration.Instance.LocalPlaylists.TryGetValue(name, out var pl)) return;
        if (index < 0 || index >= pl.Files.Count) return;
        _current = pl;
        PlayIndex(index);
    }

    public static void Stop()
    {
        LocalAudioPlayer.Stop();
        _current = null;
    }

    public static void Next()
    {
        if (_current == null || _current.Files.Count == 0) { Stop(); return; }
        var next = _current.CurrentIndex + 1;
        if (next >= _current.Files.Count) next = 0;
        PlayIndex(next);
    }

    public static void Previous()
    {
        if (_current == null || _current.Files.Count == 0) { Stop(); return; }
        var prev = _current.CurrentIndex - 1;
        if (prev < 0) prev = _current.Files.Count - 1;
        PlayIndex(prev);
    }

    private static void PlayIndex(int index)
    {
        if (_current == null) return;
        _current.CurrentIndex = index;
        var path = _current.Files[index];
        if (!File.Exists(path)) { Next(); return; }
        LocalAudioPlayer.Play(path);
    }
}
