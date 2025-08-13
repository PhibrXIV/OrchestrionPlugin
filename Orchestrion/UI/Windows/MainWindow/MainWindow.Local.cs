using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;
using Orchestrion.Audio;
using Orchestrion.Persistence;

namespace Orchestrion.UI.Windows.MainWindow;

public partial class MainWindow
{
    private string _localNewPlaylistName = "";
    private string _localAddPath = "";
    private string _localSelectedPlaylist = "";

    private void DrawLocalTab()
    {
        ImGui.BeginChild("##local_top", ImGuiHelpers.ScaledVector2(-1f, 85f));

        ImGui.Text("Playlist:");
        ImGui.SameLine();
        var names = Configuration.Instance.LocalPlaylists.Keys.OrderBy(s => s).ToList();
        var currentDisplay = string.IsNullOrEmpty(_localSelectedPlaylist) ? "(none)" : _localSelectedPlaylist;
        if (ImGui.BeginCombo("##local_pls", currentDisplay))
        {
            if (ImGui.Selectable("(none)", string.IsNullOrEmpty(_localSelectedPlaylist)))
                _localSelectedPlaylist = "";
            foreach (var n in names)
                if (ImGui.Selectable(n, n == _localSelectedPlaylist))
                    _localSelectedPlaylist = n;
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button("New...")) ImGui.OpenPopup("##local_new_playlist");

        if (ImGui.BeginPopup("##local_new_playlist"))
        {
            ImGui.Text("Create Local Playlist");
            ImGui.InputText("##local_new_name", ref _localNewPlaylistName, 64);
            ImGui.BeginDisabled(string.IsNullOrWhiteSpace(_localNewPlaylistName) ||
                                Configuration.Instance.LocalPlaylists.ContainsKey(_localNewPlaylistName));
            if (ImGui.Button("Create"))
            {
                Configuration.Instance.LocalPlaylists.Add(_localNewPlaylistName, new LocalPlaylist(_localNewPlaylistName));
                Configuration.Instance.Save();
                _localSelectedPlaylist = _localNewPlaylistName;
                _localNewPlaylistName = "";
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton("##local_prev", FontAwesomeIcon.StepBackward)) LocalPlaylistManager.Previous();
        ImGui.SameLine();
        if (ImGuiComponents.IconButton("##local_play", LocalPlaylistManager.IsPlaying ? FontAwesomeIcon.Stop : FontAwesomeIcon.Play))
        {
            if (LocalPlaylistManager.IsPlaying) LocalPlaylistManager.Stop();
            else if (!string.IsNullOrEmpty(_localSelectedPlaylist)) LocalPlaylistManager.Play(_localSelectedPlaylist);
        }
        ImGui.SameLine();
        if (ImGuiComponents.IconButton("##local_next", FontAwesomeIcon.StepForward)) LocalPlaylistManager.Next();

        var pos = LocalAudioPlayer.Position;
        var dur = LocalAudioPlayer.Duration;
        var frac = dur.TotalSeconds <= 0.1 ? 0f : (float)(pos.TotalSeconds / dur.TotalSeconds);
        ImGui.ProgressBar(frac, new Vector2(-1, 8), string.Empty);
        ImGui.Text($"{pos:mm\\:ss} / {dur:mm\\:ss}");

        ImGui.EndChild();

        ImGui.BeginChild("##local_mid", ImGuiHelpers.ScaledVector2(-1f, -1f));

        if (!string.IsNullOrEmpty(_localSelectedPlaylist) &&
            Configuration.Instance.LocalPlaylists.TryGetValue(_localSelectedPlaylist, out var pl))
        {
            ImGui.InputTextWithHint("##local_add", "Add file path (paste here)", ref _localAddPath, 512);
            ImGui.SameLine();
            if (ImGui.Button("Add") && File.Exists(_localAddPath))
            {
                pl.Add(_localAddPath);
                _localAddPath = "";
                Configuration.Instance.Save();
            }

            ImGui.Separator();

            if (ImGui.BeginTable("##local_tbl", 3, ImGuiTableFlags.SizingStretchSame))
            {
                ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Path", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed);

                for (int i = 0; i < pl.Files.Count; i++)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text((i + 1).ToString());

                    ImGui.TableNextColumn();
                    var p = pl.Files[i];
                    var isCurrent = LocalPlaylistManager.Current == pl && LocalPlaylistManager.CurrentIndex == i;
                    if (isCurrent) ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.TextUnformatted(p);
                    if (isCurrent) ImGui.PopFont();

                    ImGui.TableNextColumn();
                    if (ImGuiComponents.IconButton($"##local_row_play_{i}", FontAwesomeIcon.Play))
                        LocalPlaylistManager.Play(pl.Name, i);
                    ImGui.SameLine();
                    if (ImGuiComponents.IconButton($"##local_row_del_{i}", FontAwesomeIcon.Trash))
                    {
                        pl.RemoveAt(i);
                        Configuration.Instance.Save();
                    }
                }

                ImGui.EndTable();
            }
        }
        else
        {
            ImGui.Text("Create or select a Local playlist to begin.");
        }

        ImGui.EndChild();
    }
}
