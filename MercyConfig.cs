using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace MercyMode
{
	public class MercyConfig : ModConfig
	{
		public override ConfigScope Mode => ConfigScope.ClientSide;

		[DefaultValue(true)]
		public bool TurnBasedBattles;

		[Range(0.1f, 10f)]
		[Increment(0.1f)]
		[DefaultValue(1f)]
		public float FightDamageMultiplier;

		[DefaultValue(true)]
		public bool UseDeltaruneAssets;

		[DefaultValue("")]
		public string DeltaruneFolder;

		[Range(0, 10)]
		[DefaultValue(0)]
		public int Chapter;

		private (bool, string, int)? loadedWith;

		public override void OnChanged()
		{
			// Reload assets when an asset setting changes after startup
			var now = (UseDeltaruneAssets, DeltaruneFolder, Chapter);
			bool changed = loadedWith != null && loadedWith != now;
			loadedWith = now;
			if (changed && Deltarune.DeltaruneAssets.State != Deltarune.DeltaruneAssets.LoadState.NotStarted)
			{
				Deltarune.DeltaruneAssets.State = Deltarune.DeltaruneAssets.LoadState.NotStarted;
				Deltarune.DeltaruneAssets.StartLoading();
			}
		}
	}
}
