﻿using Dalamud.Configuration;

namespace NamazuRadar.Plugin {
	public class PluginConfig : IPluginConfiguration {
		public int Version { get; set; } = 0;

		public bool WindowVisible = true;
		public bool HideInvisible = true;
		public bool SortByDistance = true;
		public bool DrawOnMap;
		public int MapIconId = 60421;
		public int MapIconScale = 0;
		public string FilterString = string.Empty;
	}
}