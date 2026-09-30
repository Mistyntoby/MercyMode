using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace MercyMode
{
	public class MercyConfig : ModConfig
	{
		public override ConfigScope Mode => ConfigScope.ClientSide;

		[DefaultValue(true)]
		public bool UseDeltaruneAssets;

		[DefaultValue("")]
		public string DeltaruneFolder;

		[Range(0, 10)]
		[DefaultValue(0)]
		public int Chapter;

		public override void OnChanged()
		{
			// Reload assets when the config changes after startup
			if (Deltarune.DeltaruneAssets.State != Deltarune.DeltaruneAssets.LoadState.NotStarted)
			{
				Deltarune.DeltaruneAssets.State = Deltarune.DeltaruneAssets.LoadState.NotStarted;
				Deltarune.DeltaruneAssets.StartLoading();
			}
		}
	}
}
