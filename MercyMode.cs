using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace MercyMode
{
	public class MercyMode : Mod
	{
		public static ModKeybind ActKey;
		public static ModKeybind SpareKey;
		public static ModKeybind HealPrayerKey;

		public override void Load()
		{
			ActKey = KeybindLoader.RegisterKeybind(this, "Act", "F");
			SpareKey = KeybindLoader.RegisterKeybind(this, "Spare", "G");
			HealPrayerKey = KeybindLoader.RegisterKeybind(this, "HealPrayer", "V");
		}

		public override void Unload()
		{
			ActKey = null;
			SpareKey = null;
			HealPrayerKey = null;
		}

		// Chat colors, Deltarune-ish
		public static readonly Color TextWhite = new Color(255, 255, 255);
		public static readonly Color MercyYellow = new Color(255, 255, 64);
		public static readonly Color TPOrange = new Color(255, 160, 64);
		public static readonly Color Gray = new Color(160, 160, 160);

		public static void Say(string text, Color color) => Main.NewText(text, color);

		/// <summary>Worm segments and similar point at a "head" through realLife. Mercy lives on the head.</summary>
		public static NPC Root(NPC npc)
		{
			if (npc.realLife >= 0 && npc.realLife < Main.maxNPCs && Main.npc[npc.realLife].active)
				return Main.npc[npc.realLife];
			return npc;
		}

		/// <summary>Closest active boss (by its root NPC) within range of the player.</summary>
		public static NPC FindTargetBoss(Player player, float range = 2000f)
		{
			NPC best = null;
			float bestDist = range;
			foreach (NPC npc in Main.ActiveNPCs)
			{
				if (!npc.boss || npc.friendly || npc.realLife >= 0 && npc.realLife != npc.whoAmI)
					continue;
				float d = Vector2.Distance(player.Center, npc.Center);
				if (d < bestDist)
				{
					bestDist = d;
					best = npc;
				}
			}
			return best;
		}

		public static bool AnyBossAlive()
		{
			foreach (NPC npc in Main.ActiveNPCs)
				if (npc.boss)
					return true;
			return false;
		}

		public static bool IsSingleplayer => Main.netMode == NetmodeID.SinglePlayer;
	}
}
