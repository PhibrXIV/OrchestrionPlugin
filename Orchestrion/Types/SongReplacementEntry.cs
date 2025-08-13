namespace Orchestrion.Types;

public struct SongReplacementEntry
{
    public const int NoChangeId = -1;

    /// <summary>
    /// The ID of the song to replace (the original, in-game song).
    /// </summary>
    public int TargetSongId;

    /// <summary>
    /// The ID of the replacement in-game track to play.
    /// Use -1 (NoChangeId) for "ignore this song".
    /// Ignored when <see cref="LocalPath"/> is set.
    /// </summary>
    public int ReplacementId;

    /// <summary>
    /// Optional absolute path to a local audio file (mp3, wav, etc.).
    /// When set (non-empty), this entry is treated as a local-file replacement.
    /// </summary>
    public string LocalPath;

    /// <summary>
    /// True if this replacement uses a local file.
    /// </summary>
    public bool IsLocal => !string.IsNullOrWhiteSpace(LocalPath);
}
