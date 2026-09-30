using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.UI;

namespace MercyMode
{
	public class MercyUI : ModSystem
	{
		// 7x6 pixel heart, drawn with rectangles so no texture files are needed
		private static readonly string[] Heart = {
			".XX.XX.",
			"XXXXXXX",
			"XXXXXXX",
			".XXXXX.",
			"..XXX..",
			"...X...",
		};

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			int resourceIndex = layers.FindIndex(l => l.Name == "Vanilla: Resource Bars");
			if (resourceIndex < 0)
				return;

			layers.Insert(resourceIndex + 1, new LegacyGameInterfaceLayer(
				"MercyMode: TP Gauge",
				() => { DrawTPGauge(Main.spriteBatch); return true; },
				InterfaceScaleType.UI));

			layers.Insert(resourceIndex + 1, new LegacyGameInterfaceLayer(
				"MercyMode: Soul",
				() => { DrawSoul(Main.spriteBatch); return true; },
				InterfaceScaleType.Game));
		}

		private static void DrawTPGauge(SpriteBatch sb)
		{
			Player player = Main.LocalPlayer;
			var mp = player.GetModPlayer<MercyPlayer>();
			if (!MercyMode.AnyBossAlive() && mp.TP <= 0f)
				return;

			Texture2D px = TextureAssets.MagicPixel.Value;
			const int width = 22;
			const int height = 180;
			int x = 24;
			int y = Main.screenHeight / 2 - height / 2;

			var outline = new Rectangle(x - 2, y - 2, width + 4, height + 4);
			var back = new Rectangle(x, y, width, height);
			int fillHeight = (int)(height * (mp.TP / 100f));
			var fill = new Rectangle(x, y + height - fillHeight, width, fillHeight);

			sb.Draw(px, outline, Color.White);
			sb.Draw(px, back, new Color(60, 20, 0));
			sb.Draw(px, fill, MercyMode.TPOrange);

			// Marker showing how much a Heal Prayer costs
			int costY = y + height - (int)(height * (MercyPlayer.HealPrayerCost / 100f));
			sb.Draw(px, new Rectangle(x - 4, costY, width + 8, 2), Color.White * 0.8f);

			Utils.DrawBorderString(sb, "TP", new Vector2(x + width / 2f, y - 8), MercyMode.TPOrange, 1f, 0.5f, 1f);
			string pct = mp.TP >= 100f ? "MAX" : $"{(int)mp.TP}%";
			Utils.DrawBorderString(sb, pct, new Vector2(x + width / 2f, y + height + 6), Color.White, 0.9f, 0.5f, 0f);
		}

		private static void DrawSoul(SpriteBatch sb)
		{
			Player player = Main.LocalPlayer;
			if (player.dead || !MercyMode.AnyBossAlive())
				return;

			var mp = player.GetModPlayer<MercyPlayer>();

			// Real SOUL from the player's Deltarune install
			Texture2D soul = Deltarune.DeltaruneAssets.Soul;
			if (soul != null)
			{
				float scale = mp.GrazeFlash > 0 ? 1.25f : 1f;
				sb.Draw(soul, player.Center - Main.screenPosition, null, Color.White * 0.95f, 0f,
					new Vector2(soul.Width / 2f, soul.Height / 2f), scale, SpriteEffects.None, 0f);
				return;
			}

			// Fallback: heart drawn out of pixels
			Texture2D px = TextureAssets.MagicPixel.Value;
			const int pixel = 2;
			Vector2 origin = player.Center - Main.screenPosition - new Vector2(Heart[0].Length * pixel / 2f, Heart.Length * pixel / 2f);

			// Flashes white when you graze, like the soul lighting up
			Color color = mp.GrazeFlash > 0 ? Color.White : new Color(255, 0, 0);
			color *= 0.9f;

			for (int row = 0; row < Heart.Length; row++)
			{
				for (int col = 0; col < Heart[row].Length; col++)
				{
					if (Heart[row][col] != 'X')
						continue;
					var r = new Rectangle((int)origin.X + col * pixel, (int)origin.Y + row * pixel, pixel, pixel);
					sb.Draw(px, r, color);
				}
			}
		}
	}
}
