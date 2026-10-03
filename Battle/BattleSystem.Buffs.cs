using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using static MercyMode.Battle.BattleConstants;

namespace MercyMode.Battle
{
	public partial class BattleSystem
	{
		/// <summary>The longest each buff has had left (by type) this battle: the full length of its bar.</summary>
		private readonly Dictionary<int, int> buffFull = new();

		/// <summary>
		/// The player's buffs and debuffs, bottom right of the battle screen above the panel: icon, name, a bar running
		/// down and the time left. Buffs are edged green, debuffs red.
		/// </summary>
		private void DrawBuffs()
		{
			if (Main.dedServ || panel <= 0)
				return;
			float alpha = MathHelper.Clamp(panel / (float)PanelHeight, 0f, 1f);
			var shown = new List<(int type, int time)>();
			var seen = new HashSet<int>();
			for (int i = 0; i < Player.MaxBuffs; i++)
			{
				int type = Player.buffType[i];
				int left = Player.buffTime[i];
				if (type <= 0 || left <= 0 || type >= TextureAssets.Buff.Length || Main.buffNoTimeDisplay[type] && left > 2)
				{
					// (Timeless ones still show, without a bar)
					if (type > 0 && type < TextureAssets.Buff.Length && left > 0)
						shown.Add((type, -1));
					continue;
				}
				shown.Add((type, left));
				seen.Add(type);
				// A fresh or topped-up buff: its bar starts full again
				if (!buffFull.TryGetValue(type, out int full) || left > full)
					buffFull[type] = left;
			}
			// Gone ones start over next time
			foreach (int type in new List<int>(buffFull.Keys))
				if (!seen.Contains(type))
					buffFull.Remove(type);
			if (shown.Count == 0)
				return;

			const float rowH = 22f, w = 150f;
			const int maxRows = 6;
			float right = ScreenWidth - 8f;
			float bottom = ScreenHeight - panel - 6f;
			int rows = Math.Min(maxRows, shown.Count);
			for (int r = 0; r < rows; r++)
			{
				var (type, left) = shown[r];
				float y = bottom - (r + 1) * rowH;
				float x = right - w;
				bool debuff = Main.debuff[type];
				Color edge = debuff ? new Color(255, 70, 70) : new Color(80, 230, 110);
				DrDraw.Rect(x, y, w, rowH - 3, Color.Black * (0.7f * alpha));
				DrDraw.Outline(x, y, w, rowH - 3, edge * alpha, 1);

				// Icon
				Texture2D icon = TextureAssets.Buff[type].Value;
				float scale = 16f / Math.Max(icon.Width, icon.Height);
				DrDraw.Sb.Draw(icon, new Vector2(x + 3, y + 2), null, Color.White * alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

				// Name, and the time left
				string name = Lang.GetBuffName(type);
				string timer = left < 0 ? "" : left >= 3600 * 60 ? "" : FormatBuffTime(left);
				float nameScale = 0.62f;
				float timerW = DrDraw.Measure(timer, DrDraw.SmallFont) * 0.62f;
				float maxName = w - 26 - timerW - 6;
				if (DrDraw.Measure(name, DrDraw.SmallFont) * nameScale > maxName)
					nameScale = Math.Max(0.4f, maxName / Math.Max(1f, DrDraw.Measure(name, DrDraw.SmallFont)));
				DrDraw.Text(name, x + 22, y + 1, Color.White * alpha, DrDraw.SmallFont, nameScale);
				if (timer.Length > 0)
				{
					// The last few seconds blink
					bool ending = left < 5 * 60 && (time / 10) % 2 == 0;
					DrDraw.Text(timer, right - timerW - 4, y + 1, (ending ? edge : new Color(200, 200, 200)) * alpha, DrDraw.SmallFont, 0.62f);
				}

				// The bar running down, in the buff's colour
				if (left >= 0 && buffFull.TryGetValue(type, out int full) && full > 0)
				{
					float frac = MathHelper.Clamp(left / (float)full, 0f, 1f);
					DrDraw.Rect(x + 22, y + rowH - 7, w - 26, 2, Color.White * (0.15f * alpha));
					DrDraw.Rect(x + 22, y + rowH - 7, (w - 26) * frac, 2, edge * alpha);
				}
			}
			if (shown.Count > maxRows)
				DrDraw.Text($"+{shown.Count - maxRows} more", right - 60, bottom - (maxRows + 1) * rowH + 6, new Color(180, 180, 180) * alpha, DrDraw.SmallFont, 0.6f);
		}

		private static string FormatBuffTime(int ticks)
		{
			int s = (ticks + 59) / 60;
			if (s >= 3600)
				return $"{s / 3600}h";
			if (s >= 60)
				return $"{s / 60}:{s % 60:00}";
			return $"{s}s";
		}
	}
}
