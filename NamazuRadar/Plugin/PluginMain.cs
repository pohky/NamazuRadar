using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Data;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;

namespace NamazuRadar.Plugin; 

public sealed unsafe class PluginMain : IDalamudPlugin {
	public string Name => "NamazuRadar";

	public readonly PluginConfig Config;
	public readonly DalamudPluginInterface PluginInterface;
	public readonly DataManager Data;
	public readonly CommandManager Command;

	private static GameObject* LocalPlayer => (GameObject*)Control.Instance()->LocalPlayer;

	public readonly MapMarkerDraw MapMarker;
	public readonly MiniMapMarkerDraw MiniMapMarker;

	public PluginMain(DalamudPluginInterface pi, DataManager data, CommandManager cmd) {
		PluginInterface = pi;
		Data = data;
		Command = cmd;
		Config = pi.GetPluginConfig() as PluginConfig ?? new PluginConfig();
		MapMarker = new MapMarkerDraw(data);
		MiniMapMarker = new MiniMapMarkerDraw();
		if (cmd.Commands.ContainsKey("/namazu"))
			cmd.RemoveHandler("/namazu");
		cmd.AddHandler("/namazu", new CommandInfo(CommandHandler) { HelpMessage = $"Toggle {Name} Window", ShowInHelp = true });
		pi.UiBuilder.Draw += OnDraw;
		pi.UiBuilder.OpenConfigUi += OnOpenConfigUi;
	}
	
	public void Dispose() {
		Command.RemoveHandler("/namazu");
		PluginInterface.SavePluginConfig(Config);
		PluginInterface.UiBuilder.Draw -= OnDraw;
		PluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi;
	}

	
	private void CommandHandler(string command, string arguments) {
		Config.WindowVisible = !Config.WindowVisible;
	}

	private void OnOpenConfigUi() {
		Config.WindowVisible = !Config.WindowVisible;
	}

	private void OnDraw() {
		if (Config.DrawOnMap)
			DrawMapMarker();

		if (!Config.WindowVisible) return;
		try {
			ImGui.SetNextWindowSize(new Vector2(550, 350), ImGuiCond.FirstUseEver);
			if (!ImGui.Begin(Name, ref Config.WindowVisible))
				return;

			ImGui.SetNextItemWidth(250);
			ImGui.InputText("Filter##NamazuFilter", ref Config.FilterString, 512);
			ImGui.SameLine();
			ImGui.Checkbox("IncludeNameless", ref Config.IncludeNameless);

			ImGui.Checkbox("HideInvisible", ref Config.HideInvisible);
			ImGui.SameLine();
			ImGui.Checkbox("SortByDistance", ref Config.SortByDistance);
			ImGui.SameLine();
			if (ImGui.Checkbox("DrawOnMap", ref Config.DrawOnMap)) {
				MapMarker.Reset();
				MiniMapMarker.Reset();
			}

			if (Config.DrawOnMap) {
				ImGui.SetNextItemWidth(100);
				ImGui.InputInt("Icon", ref Config.MapIconId, 1, 1);
				ImGui.SameLine();
				ImGui.SetNextItemWidth(100);
				ImGui.DragInt("Scale", ref Config.MapIconScale, 1, 0, 1000);
				if (ImGui.IsItemHovered()) ImGui.SetTooltip("0 = Default Scale");
			}
			ImGui.Separator();

			if (!ImGui.BeginTable("##NamazuTable", Config.HideInvisible ? 3 : 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
				return;

			ImGui.TableSetupScrollFreeze(0, 1);
			ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed);
			ImGui.TableSetupColumn("Position", ImGuiTableColumnFlags.WidthFixed);
			ImGui.TableSetupColumn("Distance", ImGuiTableColumnFlags.WidthFixed);
			if (!Config.HideInvisible)
				ImGui.TableSetupColumn("Visible", ImGuiTableColumnFlags.WidthFixed);
			ImGui.TableHeadersRow();

			var objSpan = new Span<nint>(GameObjectManager.Instance()->ObjectList, 596);
			var list = LocalPlayer == null ? Enumerable.Empty<nint>() : objSpan.ToArray().Where(IsNamazu);

			if (Config.HideInvisible)
				list = list.Where(IsVisible);
			list = list.Where(MatchFilter);
			if (Config.SortByDistance)
				list = list.OrderBy(addr => Vector3.Distance(LocalPlayer->Position, ((GameObject*)addr)->Position));

			foreach (var address in list) {
				var obj = (GameObject*)address;
				ImGui.TableNextColumn(); //Name
				ImGui.TextUnformatted($"{Marshal.PtrToStringUTF8((nint)obj->GetName())}");

				ImGui.TableNextColumn(); //Position
				Vector3 pos = obj->Position;
				var pos2d = WorldToDisplay(pos);
				ImGui.TextUnformatted($"{pos2d.X} {pos2d.Y}");

				ImGui.TableNextColumn(); //Distance
				ImGui.TextUnformatted($"{Vector3.Distance(LocalPlayer->Position, pos):N2}");

				if (!Config.HideInvisible) {
					ImGui.TableNextColumn(); //Visible
					ImGui.TextUnformatted(IsVisible(address) ? "Yes" : "No");
				}
			}

			ImGui.EndTable();
		} finally {
			ImGui.End();
		}
	}

	private void DrawMapMarker() {
		if (LocalPlayer == null) return;
		var objSpan = new Span<nint>(GameObjectManager.Instance()->ObjectList, 596);
		var list = objSpan.ToArray().Where(IsNamazu);

		if (Config.HideInvisible)
			list = list.Where(IsVisible);
		list = list.Where(MatchFilter);
		var namazuList = list.ToList();

		if (MapMarker.Begin()) {
			foreach (var address in namazuList)
				MapMarker.DrawObject((GameObject*)address, (uint)Config.MapIconId, Config.MapIconScale);
			MapMarker.End();
		}
		
		if (MiniMapMarker.Begin()) {
			foreach (var address in namazuList)
				MiniMapMarker.DrawObject((GameObject*)address, (uint)Config.MapIconId, Config.MapIconScale);
			MiniMapMarker.End();
		}
	}

	private bool MatchFilter(nint address) {
		if (address == 0) return false;
		var obj = (GameObject*)address;
		var name = Marshal.PtrToStringUTF8((nint)obj->GetName());
		if (string.IsNullOrWhiteSpace(name))
			return Config.IncludeNameless;
		if (name.Contains(Config.FilterString, StringComparison.OrdinalIgnoreCase))
			return true;
		return false;
	}

	private static bool IsVisible(nint address) {
		if (address == 0) return false;
		var obj = (GameObject*)address;
		return obj->RenderFlags == 0;
	}

	private static bool IsNamazu(nint address) {
		if (address == 0) return false;
		var obj = (GameObject*)address;
		if (!obj->IsCharacter()) return false;
		var chara = (Character*)address;
		return chara->ModelCharaId == 1793 || chara->ModelCharaId_2 == 1793;
	}

	private static Vector2 WorldToDisplay(Vector3 pos) {
		var map = AgentMap.Instance();
		return WorldToMapDisplay(pos, map->CurrentMapSizeFactor, map->CurrentOffsetX, map->CurrentOffsetY);
	}

	public static Vector2 WorldToMapDisplay(Vector3 pos, short sizeFactor, short offsetX, short offsetY, bool round = true) {
		var scale = sizeFactor / 100f;
		var x = (10 - ((pos.X + offsetX) * scale + 1024f) * -0.2f / scale) / 10f;
		var y = (10 - ((pos.Z + offsetY) * scale + 1024f) * -0.2f / scale) / 10f;
		if (round) {
			x = MathF.Round(x, 1, MidpointRounding.ToZero);
			y = MathF.Round(y, 1, MidpointRounding.ToZero);
		}
		return new Vector2(x, y);
	}
}
