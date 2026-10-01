using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace MercyMode
{
	public enum MercySoundRedirect
	{
		Deltarune,
		MenuTick,
		MenuOpen,
		MenuClose,
		PlayerHit,
		NpcHit,
		NpcDeath,
		Roar,
		Item1,
		Item4,
		Silent,
	}

	public class MercyConfig : ModConfig
	{
		public override ConfigScope Mode => ConfigScope.ClientSide;

		[DefaultValue(true)]
		public bool TurnBasedBattles;

		[DefaultValue(true)]
		public bool BattlesWithEnemies;

		/// <summary>Regular enemies start battles during invasions, moon events, eclipses, the Old One's Army and the pillars.</summary>
		[DefaultValue(false)]
		public bool BattlesDuringEvents;

		/// <summary>Boss battles play the boss's own Terraria music; off = Rude Buster for every battle.</summary>
		[DefaultValue(true)]
		public bool BossBattleMusic;

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

		[Range(0f, 1.5f)]
		[Increment(0.05f)]
		[DefaultValue(0.65f)]
		public float BattleSoundVolume;

		[Range(0f, 1.5f)]
		[Increment(0.05f)]
		[DefaultValue(0.45f)]
		public float BattleMusicVolume;

		[DefaultValue(MercySoundRedirect.Deltarune)]
		public MercySoundRedirect MenuSoundRedirect;

		[DefaultValue(MercySoundRedirect.Deltarune)]
		public MercySoundRedirect BattleSoundRedirect;

		[DefaultValue(MercySoundRedirect.Deltarune)]
		public MercySoundRedirect ActionSoundRedirect;

		[DefaultValue(MercySoundRedirect.Deltarune)]
		public MercySoundRedirect MercyGainSoundRedirect;

		[DefaultValue(MercySoundRedirect.Deltarune)]
		public MercySoundRedirect GrazeSoundRedirect;

		[DefaultValue(MercySoundRedirect.Deltarune)]
		public MercySoundRedirect BattleStartSoundRedirect;

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
