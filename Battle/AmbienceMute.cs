using System;
using Terraria;
using Terraria.ModLoader;

namespace MercyMode.Battle
{
	/// <summary>
	/// Audio settings the battle changes for a while and puts back: Terraria's ambience (owls, birds, frogs, wind,
	/// rain, waterfalls: all SoundType.Ambient, scaled by Main.ambientVolume) is silenced, and in boss battles the
	/// music (the boss's own track) is turned up to sit level with the battle's sounds. The player's real settings
	/// are what get saved, even if Terraria saves its settings mid-battle, and a slider moved during the battle wins.
	/// </summary>
	public class AmbienceMute : ModSystem
	{
		/// <summary>The player's ambient volume while it is muted, or null when nothing is muted.</summary>
		private static float? saved;
		/// <summary>The player's music volume while it is boosted, and the boosted value we set.</summary>
		private static float? savedMusic;
		private static float boostedMusic;

		public static void Mute()
		{
			if (saved != null)
				return;
			saved = Main.ambientVolume;
			Main.ambientVolume = 0f;
		}

		/// <summary>Turns the music up by a factor (capped at full volume) until <see cref="Restore"/>.</summary>
		public static void BoostMusic(float factor)
		{
			if (savedMusic != null || factor <= 1f)
				return;
			savedMusic = Main.musicVolume;
			boostedMusic = Math.Min(1f, Main.musicVolume * factor);
			Main.musicVolume = boostedMusic;
		}

		public static void Restore()
		{
			if (saved is float volume)
			{
				saved = null;
				// If the player moved the slider during the battle, keep their new value
				if (Main.ambientVolume == 0f)
					Main.ambientVolume = volume;
			}
			if (savedMusic is float music)
			{
				savedMusic = null;
				if (Main.musicVolume == boostedMusic)
					Main.musicVolume = music;
			}
		}

		/// <summary>The values the player actually chose, for saving.</summary>
		private static float RealAmbient => saved is float volume && Main.ambientVolume == 0f ? volume : Main.ambientVolume;
		private static float RealMusic => savedMusic is float music && Main.musicVolume == boostedMusic ? music : Main.musicVolume;

		public override void Load()
		{
			On_Main.SaveSettings += SaveWithRealVolume;
		}

		public override void Unload() => Restore();

		public override void OnWorldUnload() => Restore();

		private static bool SaveWithRealVolume(On_Main.orig_SaveSettings orig)
		{
			if (saved == null && savedMusic == null)
				return orig();
			float ambient = Main.ambientVolume, music = Main.musicVolume;
			Main.ambientVolume = RealAmbient;
			Main.musicVolume = RealMusic;
			try
			{
				return orig();
			}
			finally
			{
				Main.ambientVolume = ambient;
				Main.musicVolume = music;
			}
		}
	}
}
