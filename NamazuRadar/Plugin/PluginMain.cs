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

	private bool m_ModelCharaConfig;
	private string m_ModelCharaTemp = "0";

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

		DrawModelCharaFilter();

		if (!Config.WindowVisible) return;
		try {
			ImGui.SetNextWindowSize(new Vector2(600, 350), ImGuiCond.FirstUseEver);
			if (!ImGui.Begin(Name, ref Config.WindowVisible))
				return;

			ImGui.SetNextItemWidth(250);
			ImGui.InputText("Filter##NamazuFilter", ref Config.FilterString, 512);
			ImGui.SameLine();
			ImGui.Checkbox("IncludeNameless", ref Config.IncludeNameless);
			ImGui.SameLine();
			if (ImGui.Button("Config##ModelCharaConfigButton"))
				m_ModelCharaConfig = !m_ModelCharaConfig;

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
				ImGui.SameLine();
				ImGui.Checkbox("Name", ref Config.MapIconText);
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

	private void DrawModelCharaFilter() {
		if (!m_ModelCharaConfig) return;
		try {
			ImGui.SetNextWindowSize(new Vector2(400, 300), ImGuiCond.FirstUseEver);
			if (!ImGui.Begin("Edit ModelChara Filter", ref m_ModelCharaConfig))
				return;

			ImGui.SetNextItemWidth(150);
			ImGui.InputText("ModelChara Id##ModelCharaInput", ref m_ModelCharaTemp, 16);
			ImGui.SameLine();
			if (ImGui.Button("Add##AddModelChara") && int.TryParse(m_ModelCharaTemp, out var customId))
				Config.ModelCharaFilter.Add(customId);

			var targetId = -1;
			var target = TargetSystem.Instance()->GetCurrentTarget();
			if (target != null && target->IsCharacter()) {
				var chara = (Character*)target;
				targetId = chara->ModelCharaId_2 == -1 ? chara->ModelCharaId : chara->ModelCharaId_2;
			}
			ImGui.TextUnformatted($"Current Target: {targetId}");
			ImGui.SameLine();
			if (ImGui.Button("Add Target") && targetId >= 0)
				Config.ModelCharaFilter.Add(targetId);

			ImGui.Separator();
			if (!ImGui.BeginTable("##ModelCharaIdTable", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
				return;
			ImGui.TableSetupScrollFreeze(0, 1);
			ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthFixed);
			ImGui.TableSetupColumn("##Buttons", ImGuiTableColumnFlags.WidthFixed);
			ImGui.TableHeadersRow();

			foreach (var id in Config.ModelCharaFilter.ToList()) {
				ImGui.TableNextColumn();
				ImGui.TextUnformatted($"{id}");
				
				ImGui.TableNextColumn();
				if (ImGui.Button($"Delete##delete{id}"))
					Config.ModelCharaFilter.Remove(id);
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
				MapMarker.DrawObject((GameObject*)address, (uint)Config.MapIconId, Config.MapIconScale, 0, (byte)(Config.MapIconText ? 3 : 0));
			MapMarker.End();
		}
		
		if (MiniMapMarker.Begin()) {
			foreach (var address in namazuList)
				MiniMapMarker.DrawObject((GameObject*)address, (uint)Config.MapIconId, Config.MapIconScale, 0, (byte)(Config.MapIconText ? 3 : 0));
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

	private bool IsNamazu(nint address) {
		if (address == 0) return false;
		var obj = (GameObject*)address;
		if (!obj->IsCharacter()) return false;
		var chara = (Character*)address;
		
		var id = chara->ModelCharaId_2 == -1 ? chara->ModelCharaId : chara->ModelCharaId_2;
		return Config.ModelCharaFilter.Contains(id);
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
