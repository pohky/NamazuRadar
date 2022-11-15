using System.Numerics;
using Dalamud.Data;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.GeneratedSheets;

namespace NamazuRadar;

public unsafe class MiniMapMarkerDraw : MapMarkerDraw {
	public override int MaxMarkers => 100;
	public override int MarkerCount {
		get => Agent->MiniMapMarkerCount;
		set => Agent->MiniMapMarkerCount = (byte)value;
	}

	public MiniMapMarkerDraw() : base(null) { }

	public override bool Begin() {
		if (Control.Instance()->LocalPlayer == null) return false;
		var addon = AtkStage.GetSingleton()->RaptureAtkUnitManager->GetAddonByName("_NaviMap");
		if (addon == null || !addon->IsVisible) return false;
		Agent->UpdateFlags |= 1 << 17;
		return true;
	}

	public override void End() => Agent->UpdateFlags |= 1 << 21;
	public override void Reset() => Agent->UpdateFlags |= 1 << 17;

	protected override void WriteMarker(MapMarkerInfo* info, int index) {
		var m = stackalloc MiniMapMarker[1];
		m->MapMarker = info->MapMarker;
		*(MiniMapMarker*)(Agent->MiniMapMarkerArray + sizeof(MiniMapMarker) * index) = *m;
	}
}

public unsafe class MapMarkerDraw {
	private readonly Dictionary<uint, byte> _defaultMarkerCounts = new();
	protected readonly AgentMap* Agent;
	private uint _lastMap;
	public int AvailableMarkers => MaxMarkers - MarkerCount;
	public virtual int MaxMarkers => 132;
	public virtual int MarkerCount {
		get => Agent->MapMarkerCount;
		set => Agent->MapMarkerCount = (byte)value;
	}

	public MapMarkerDraw(DataManager? data) {
		if (data != null) {
			var markers = data.GetExcelSheet<MapMarker>()!;
			foreach (var map in data.GetExcelSheet<Map>()!)
				_defaultMarkerCounts[map.RowId] = (byte)markers.Count(m => m.RowId == map.MapMarkerRange);
		}
		Agent = AgentMap.Instance();
	}

	public virtual bool Begin() {
		if (Control.Instance()->LocalPlayer == null || !Agent->AgentInterface.IsAgentActive())
			return false;
		if (!_defaultMarkerCounts.ContainsKey(Agent->CurrentMapId))
			return false;
		Agent->MapMarkerCount = _defaultMarkerCounts[Agent->CurrentMapId];
		if (_lastMap != Agent->CurrentMapId) {
			Agent->UpdateFlags |= 1 << 1;
			_lastMap = Agent->CurrentMapId;
		}
		return true;
	}

	public virtual void End() => Agent->UpdateFlags |= 1 << 6;
	public virtual void Reset() {
		if (_defaultMarkerCounts.TryGetValue(Agent->CurrentMapId, out var cnt))
			Agent->MapMarkerCount = cnt;
		Agent->UpdateFlags |= 1 << 1;
	}

	public bool DrawObject(GameObject* obj, uint iconId = 60421, int scale = 0, byte style = 0, byte textPosition = 3) {
		if (MarkerCount >= MaxMarkers)
			return false;

		Vector3 objPosition = obj->Position;
		//if (textPosition is > 0 and < 12 && GetType() == typeof(MapMarkerDraw))
		//	objPosition *= 2;

		var info = stackalloc MapMarkerInfo[1];
		info->MapMarker.X = (short)(objPosition.X * 16);
		info->MapMarker.Y = (short)(objPosition.Z * 16);
		info->MapMarker.IconId = iconId;
		info->MapMarker.Index = (byte)MarkerCount;
		info->MapMarker.Scale = scale;
		info->MapMarker.Subtext = obj->GetName();
		info->MapMarker.SubtextStyle = style;
		info->MapMarker.SubtextOrientation = textPosition;
		WriteMarker(info, MarkerCount++);
		return true;
	}

	protected virtual void WriteMarker(MapMarkerInfo* info, int index) {
		*(MapMarkerInfo*)(Agent->MapMarkerInfoArray + sizeof(MapMarkerInfo) * index) = *info;
	}
}