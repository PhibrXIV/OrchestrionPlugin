using System.Collections.Generic;
using System.IO;
using System.Numerics;
using CheapLoc;
using Dalamud.Bindings.ImGui;
using Orchestrion.Persistence;
using Orchestrion.Types;
using Orchestrion.UI.Components;

namespace Orchestrion.UI.Windows.MainWindow;

public partial class MainWindow
{
    private SongReplacementEntry _tmpReplacement;
    private readonly List<int> _removalList = new();

    // UI state for local-file replacement
    private bool _useLocalFile = false;
    private string _localFilePath = string.Empty;

    private void DrawReplacementsTab()
    {
        ImGui.BeginChild("##replacementlist");
        DrawCurrentReplacement();
        DrawReplacementList();
        ImGui.EndChild();
    }

    private void DrawReplacementList()
    {
        foreach (var replacement in Configuration.Instance.SongReplacements.Values)
        {
            if (!SongList.Instance.TryGetSong(replacement.TargetSongId, out var targetSong)) continue;

            var isLocal = replacement.IsLocal;
            var hasInGameRepl = !isLocal && replacement.ReplacementId != SongReplacementEntry.NoChangeId;

            Song replacementSong = default;
            if (hasInGameRepl)
            {
                if (!SongList.Instance.TryGetSong(replacement.ReplacementId, out replacementSong)) continue;
            }

            // Filtering/search
            var displayMatch =
                Util.SearchMatches(_searchText, targetSong) ||
                (hasInGameRepl && Util.SearchMatches(_searchText, replacementSong)) ||
                (isLocal && (!string.IsNullOrEmpty(replacement.LocalPath) &&
                             Path.GetFileName(replacement.LocalPath).ToLower().Contains(_searchText.ToLower())));

            if (!displayMatch) continue;

            ImGui.Spacing();

            var targetText = $"{replacement.TargetSongId} - {targetSong.Name}";
            var replText =
                isLocal
                    ? $"[Local] {Path.GetFileName(replacement.LocalPath)}"
                    : replacement.ReplacementId == SongReplacementEntry.NoChangeId
                        ? _noChange
                        : $"{replacement.ReplacementId} - {replacementSong.Name}";

            ImGui.TextWrapped($"{targetText}");
            if (ImGui.IsItemHovered())
                BgmTooltip.DrawBgmTooltip(targetSong);

            ImGui.Text(Loc.Localize("ReplaceWith", "will be replaced with"));
            ImGui.TextWrapped($"{replText}");
            if (ImGui.IsItemHovered() && hasInGameRepl)
                BgmTooltip.DrawBgmTooltip(replacementSong);

            // Buttons in bottom right of area
            var editText = Loc.Localize("Edit", "Edit");
            var deleteText = Loc.Localize("Delete", "Delete");
            RightAlignButtons(ImGui.GetCursorPosY(), new[] { editText, deleteText });
            if (ImGui.Button($"{editText}##{replacement.TargetSongId}"))
            {
                _removalList.Add(replacement.TargetSongId);
                _tmpReplacement.TargetSongId = replacement.TargetSongId;
                _tmpReplacement.ReplacementId = replacement.ReplacementId;

                _useLocalFile = replacement.IsLocal;
                _localFilePath = replacement.LocalPath ?? string.Empty;
            }
            ImGui.SameLine();
            if (ImGui.Button($"{deleteText}##{replacement.TargetSongId}"))
                _removalList.Add(replacement.TargetSongId);

            ImGui.Separator();
        }

        if (_removalList.Count > 0)
        {
            foreach (var toRemove in _removalList)
                Configuration.Instance.SongReplacements.Remove(toRemove);
            _removalList.Clear();
            Configuration.Instance.Save();
        }
    }

    private void DrawCurrentReplacement()
    {
        ImGui.Spacing();

        var target = SongList.Instance.GetSong(_tmpReplacement.TargetSongId);
        var targetText = target.Id == 0 ? "" : $"{target.Id} - {target.Name}";

        string replacementText;
        if (_useLocalFile)
        {
            replacementText = string.IsNullOrEmpty(_localFilePath)
                ? Loc.Localize("ChooseLocalFile", "Choose a local file...")
                : $"[Local] {Path.GetFileName(_localFilePath)}";
        }
        else if (_tmpReplacement.ReplacementId == SongReplacementEntry.NoChangeId)
            replacementText = _noChange;
        else
        {
            var repl = SongList.Instance.GetSong(_tmpReplacement.ReplacementId);
            replacementText = repl.Id == 0 ? Loc.Localize("SelectSong", "Select a song...") : $"{repl.Id} - {repl.Name}";
        }

        // This fixes the ultra-wide combo boxes, I guess
        var width = ImGui.GetWindowWidth() * 0.60f;

        // Target song combo
        if (ImGui.BeginCombo(Loc.Localize("TargetSong", "Target Song"), string.IsNullOrEmpty(targetText) ? Loc.Localize("SelectSong", "Select a song...") : targetText))
        {
            foreach (var song in SongList.Instance.GetSongs().Values)
            {
                if (!Util.SearchMatches(_searchText, song)) continue;
                if (Configuration.Instance.SongReplacements.ContainsKey(song.Id)) continue;
                var tmpText = $"{song.Id} - {song.Name}";
                var tmpTextSize = ImGui.CalcTextSize(tmpText);
                var isSelected = _tmpReplacement.TargetSongId == song.Id;
                if (ImGui.Selectable(tmpText, isSelected, ImGuiSelectableFlags.None, new Vector2(width, tmpTextSize.Y)))
                    _tmpReplacement.TargetSongId = song.Id;
                if (ImGui.IsItemHovered())
                    BgmTooltip.DrawBgmTooltip(song);
            }

            ImGui.EndCombo();
        }

        ImGui.Spacing();

        // Toggle: local file vs in-game
        if (ImGui.Checkbox(Loc.Localize("UseLocalFile", "Use local file"), ref _useLocalFile))
        {
            // Reset fields when flipping modes
            if (_useLocalFile)
                _tmpReplacement.ReplacementId = 0;
            _localFilePath ??= string.Empty;
        }

        if (_useLocalFile)
        {
            // Path input
            ImGui.InputText(Loc.Localize("LocalFilePath", "Local file path"), ref _localFilePath, 512);
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), Loc.Localize("LocalHint", "Enter a full path to a .mp3/.wav/etc."));
        }
        else
        {
            // Replacement in-game song combo
            if (ImGui.BeginCombo(Loc.Localize("ReplacementSong", "Replacement Song"), replacementText))
            {
                if (ImGui.Selectable(_noChange))
                    _tmpReplacement.ReplacementId = SongReplacementEntry.NoChangeId;

                foreach (var song in SongList.Instance.GetSongs().Values)
                {
                    if (!Util.SearchMatches(_searchText, song)) continue;
                    var tmpText = $"{song.Id} - {song.Name}";
                    var tmpTextSize = ImGui.CalcTextSize(tmpText);
                    var isSelected = _tmpReplacement.ReplacementId == song.Id;
                    if (ImGui.Selectable(tmpText, isSelected, ImGuiSelectableFlags.None, new Vector2(width, tmpTextSize.Y)))
                        _tmpReplacement.ReplacementId = song.Id;
                    if (ImGui.IsItemHovered())
                        BgmTooltip.DrawBgmTooltip(song);
                }

                ImGui.EndCombo();
            }
        }

        ImGui.Spacing();
        ImGui.Spacing();

        var text = Loc.Localize("AddReplacement", "Add as song replacement");
        MainWindow.RightAlignButton(ImGui.GetCursorPosY(), text);

        // Only enable when valid target and (valid local path or valid in-game choice)
        var canAdd = _tmpReplacement.TargetSongId != 0 &&
                     ((_useLocalFile && !string.IsNullOrWhiteSpace(_localFilePath)) ||
                      (!_useLocalFile /* in-game */));

        ImGui.BeginDisabled(!canAdd);
        if (ImGui.Button(text))
        {
            var entry = new SongReplacementEntry
            {
                TargetSongId = _tmpReplacement.TargetSongId,
                ReplacementId = _useLocalFile ? 0 : _tmpReplacement.ReplacementId,
                LocalPath = _useLocalFile ? _localFilePath?.Trim() ?? string.Empty : string.Empty,
            };

            Configuration.Instance.SongReplacements[_tmpReplacement.TargetSongId] = entry;
            Configuration.Instance.Save();
            ResetReplacement();
        }
        ImGui.EndDisabled();

        ImGui.Separator();
    }
}
