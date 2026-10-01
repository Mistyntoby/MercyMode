using Terraria;
using Terraria.ModLoader;

namespace MercyMode.Battle
{
	/// <summary>
	/// Silences Terraria's ambience (owls, birds, frogs, wind, rain, waterfalls...) during a battle. All of it is
	/// SoundType.Ambient, scaled by Main.ambientVolume, so that is set to 0 while a battle runs and put back after.
	/// The player's real setting is what gets saved, even if Terraria saves its settings mid-battle.
	/// </summary>
	public class AmbienceMute : ModSystem
	{
		/// <summary>The player's ambient volume while it is muted, or null when nothing is muted.</summary>
		private static float? saved;

		public static void Mute()
		{
			if (saved != null)
				return;
			saved = Main.ambientVolume;
			Main.ambientVolume = 0f;
		}

		public static void Restore()
		{
			if (saved is not float volume)
				return;
			saved = null;
			// If the player moved the slider during the battle, keep their new value
			if (Main.ambientVolume == 0f)
				Main.ambientVolume = volume;
		}

		/// <summary>The value the player actually chose, for saving.</summary>
		private static float RealVolume => saved is float volume && Main.ambientVolume == 0f ? volume : Main.ambientVolume;

		public override void Load()
		{
			On_Main.SaveSettings += SaveWithRealVolume;
		}

		public override void Unload() => Restore();

		public override void OnWorldUnload() => Restore();

		private static bool SaveWithRealVolume(On_Main.orig_SaveSettings orig)
		{
			if (saved == null)
				return orig();
			float muted = Main.ambientVolume;
			Main.ambientVolume = RealVolume;
			try
			{
				return orig();
			}
			finally
			{
				Main.ambientVolume = muted;
			}
		}
	}
}
