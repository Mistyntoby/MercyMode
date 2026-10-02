using Terraria.ModLoader;

namespace MercyMode
{
	/// <summary>The player's TP (0-100), built and spent in battles.</summary>
	public class MercyPlayer : ModPlayer
	{
		/// <summary>TP the battle's Heal Prayer ACT costs.</summary>
		public const float HealPrayerCost = 32f;

		/// <summary>0 to 100.</summary>
		public float TP;

		public override void OnRespawn()
		{
			TP = 0;
		}
	}
}
