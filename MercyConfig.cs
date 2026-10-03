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

	/// <summary>Your colour in multiplayer battles (your battle UI, and your box and SOUL on the others' screens).</summary>
	public enum PartyColorChoice
	{
		Automatic,
		Cyan,
		Magenta,
		Green,
		Yellow,
		Orange,
		Blue,
		White,
	}

	public class MercyConfig : ModConfig
	{
		public override ConfigScope Mode => ConfigScope.ClientSide;

		/// <summary>
		/// Which default changes this saved config has been through (not shown in the menu). 1: the battle volumes'
		/// defaults went from 0.45 / 0.25 to 1, so a config still on the old defaults moves up with them.
		/// </summary>
		[Newtonsoft.Json.JsonProperty]
		private int defaultsVersion;

		public override void OnLoaded()
		{
			if (defaultsVersion < 1)
			{
				// Still exactly the old defaults: never touched, so they follow the new ones (a chosen value stays)
				if (System.Math.Abs(BattleSoundVolume - 0.45f) < 0.001f)
					BattleSoundVolume = 1f;
				if (System.Math.Abs(BattleMusicVolume - 0.25f) < 0.001f)
					BattleMusicVolume = 1f;
				defaultsVersion = 1;
			}
		}

		[DefaultValue(true)]
		public bool TurnBasedBattles;

		[DefaultValue(true)]
		public bool BattlesWithEnemies;

		/// <summary>Automatic: by the order players joined the server (the first is Kris cyan).</summary>
		[DefaultValue(PartyColorChoice.Automatic)]
		public PartyColorChoice PartyColor;

		/// <summary>
		/// Enemies start battles during invasions, moon events, eclipses and the Old One's Army: a squad of that army at a
		/// time. (Renamed from BattlesDuringEvents, which defaulted to off, so saved configs pick up the new default.)
		/// </summary>
		[DefaultValue(true)]
		public bool EventBattles;

		/// <summary>Boss battles play the boss's own Terraria music; off = Rude Buster for every battle.</summary>
		[DefaultValue(true)]
		public bool BossBattleMusic;

		/// <summary>How much louder the boss's music plays during its battle (capped at full volume).</summary>
		[Range(1f, 3f)]
		[Increment(0.1f)]
		[DefaultValue(1.6f)]
		public float BossMusicBoost;

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
		[DefaultValue(1f)]
		public float BattleSoundVolume;

		[Range(0f, 1.5f)]
		[Increment(0.05f)]
		[DefaultValue(1f)]
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
