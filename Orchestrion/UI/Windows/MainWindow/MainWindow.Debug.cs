using CheapLoc;
using Dalamud.Bindings.ImGui;
using Orchestrion.Audio;
using Orchestrion.BGMSystem;

namespace Orchestrion.UI.Windows.MainWindow;

public partial class MainWindow
{
	private void DrawDebugTab()
	{
		var addr = BGMAddressResolver.BGMSceneManager;
		if (addr == IntPtr.Zero) return;
		var addrStr = $"{addr.ToInt64():X}";
		ImGui.Text(addrStr);
		if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
			ImGui.SetClipboardText(addrStr);
		ImGui.Text($"streaming enabled: {BGMAddressResolver.StreamingEnabled}");
		
		ImGui.Text($"OldSongId: {BGMManager.OldSongId}");
		ImGui.Text($"OldScene: {BGMManager.OldScene}");
		
		ImGui.Text($"CurrentSongId: {BGMManager.CurrentSongId}");
		ImGui.Text($"CurrentScene: {BGMManager.CurrentScene}");
		
		ImGui.Text($"SecondSongId: {BGMManager.SecondSongId}");
		ImGui.Text($"SecondScene: {BGMManager.SecondScene}");

		ImGui.Text($"PlayingSongId: {BGMManager.PlayingSongId}");
		ImGui.Text($"PlayingScene: {BGMManager.PlayingScene}");

		ImGui.Text($"Audible: {BGMManager.CurrentAudibleSong}");
		if (ImGui.Button("export loc"))
		{
			Loc.ExportLocalizable(true);
		}
		ImGui.Text($"DD: {BGMManager.DeepDungeonModeActive()}");
		// ImGui.Text($"DD playlist: {BGMManager._ddPlaylist}");
		
	}
}