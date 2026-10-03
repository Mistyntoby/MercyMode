using System;
using System.Collections.Generic;
using System.Linq;
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
		/// <summary>The player's choice (B key, or clicking the tab), and how far the drawer is out (0 hidden, 1 shown).</summary>
		private bool buffsWanted = true;
		private float buffDrawer;
		/// <summary>The first row shown (the list scrolls).</summary>
		private int buffScroll;

		private const float DrawerWidth = 170f, BuffRowH = 21f;

		/// <summary>Menus that use the right side of the panel (the drawer slides away for them).</summary>
		private bool PanelRightBusy => phase is Phase.WeaponSelect or Phase.ActSelect or Phase.ItemSelect or Phase.SummonSelect
			or Phase.EnemySelect or Phase.PartySelect or Phase.FightBar or Phase.FightResult or Phase.Build or Phase.MercyPrompt;

		/// <summary>The player's buffs to list: (type, ticks left, or -1 for ones without a timer).</summary>
		private List<(int type, int left)> BuffRows()
		{
			var rows = new List<(int, int)>();
			for (int i = 0; i < Player.MaxBuffs; i++)
			{
				int type = Player.buffType[i];
				int left = Player.buffTime[i];
				if (type <= 0 || left <= 0 || type >= TextureAssets.Buff.Length)
					continue;
				rows.Add((type, Main.buffNoTimeDisplay[type] || left >= 3600 * 60 ? -1 : left));
			}
			return rows;
		}

		private float DrawerTop => ScreenHeight - panel + 40f;
		private float DrawerX => ScreenWidth + 2f - (DrawerWidth + 6f) * EaseOut(buffDrawer);
		private int VisibleBuffRows => Math.Max(1, (int)((ScreenHeight - 6f - DrawerTop - 16f) / BuffRowH));

		private static float EaseOut(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

		/// <summary>The little tab the drawer hangs from (always on screen, to open it again).</summary>
		private Rectangle BuffTab => new((int)DrawerX - 22, (int)DrawerTop, 22, 30);

		private void UpdateBuffDrawer()
		{
			var rows = BuffRows();
			// Bars: the most each buff has had left (a fresh or topped-up buff fills its bar again)
			var seen = new HashSet<int>();
			foreach (var (type, left) in rows)
			{
				seen.Add(type);
				if (left > 0 && (!buffFull.TryGetValue(type, out int full) || left > full))
					buffFull[type] = left;
			}
			foreach (int type in buffFull.Keys.ToList())
				if (!seen.Contains(type))
					buffFull.Remove(type);

			if (Main.dedServ || Main.drawingPlayerChat)
				return;
			if (BuffsKeyPressed())
			{
				buffsWanted = !buffsWanted;
				Sfx("menumove");
			}
			Vector2 mouse = MouseBattle();
			bool clicked = Main.mouseLeft && Main.mouseLeftRelease;
			if (clicked && phase != Phase.Build && BuffTab.Contains(mouse.ToPoint()) && panel > 0)
			{
				buffsWanted = !buffsWanted;
				Main.mouseLeftRelease = false;
				Sfx("menumove");
			}
			bool open = buffsWanted && !PanelRightBusy && rows.Count > 0 && panel > 0;
			// Slides out and back with an ease
			buffDrawer = MathHelper.Clamp(buffDrawer + (open ? 0.09f : -0.12f), 0f, 1f);

			// The wheel scrolls it with the mouse over it (the attack builder keeps the wheel for itself)
			var area = new Rectangle((int)DrawerX, (int)DrawerTop, (int)DrawerWidth, (int)(ScreenHeight - DrawerTop));
			if (phase != Phase.Build)
			{
				if (buffDrawer > 0.5f && area.Contains(mouse.ToPoint()) && buildScroll != 0)
					buffScroll += buildScroll < 0 ? 1 : -1;
				buildScroll = 0;
			}
			buffScroll = Math.Clamp(buffScroll, 0, Math.Max(0, rows.Count - VisibleBuffRows));
		}

		/// <summary>
		/// The buff drawer, in the dark panel on the right: a list of the player's buffs and debuffs (icon, name, a bar
		/// running down and the time left; buffs edged green, debuffs red) that slides out and away and scrolls.
		/// </summary>
		private void DrawBuffs(float left, float width)
		{
			if (Main.dedServ || panel <= 0)
				return;
			var rows = BuffRows();
			if (rows.Count == 0)
				return;
			float panelAlpha = MathHelper.Clamp(panel / (float)PanelHeight, 0f, 1f);
			float x = DrawerX, top = DrawerTop;

			// The tab: always there while the panel is up, with how many buffs there are
			Rectangle tab = BuffTab;
			DrDraw.Rect(tab.X, tab.Y, tab.Width, tab.Height, Color.Black * panelAlpha);
			DrDraw.Outline(tab.X, tab.Y, tab.Width, tab.Height, PanelLine * panelAlpha, 2);
			DrDraw.Text(buffDrawer > 0.5f ? ">" : "<", tab.X + 6, tab.Y + 1, Color.White * panelAlpha, DrDraw.SmallFont, 0.8f);
			DrDraw.Text($"{rows.Count}", tab.X + 5, tab.Y + 15, new Color(255, 220, 64) * panelAlpha, DrDraw.SmallFont, 0.6f);
			if (buffDrawer <= 0.01f)
				return;

			float alpha = panelAlpha * Math.Min(1f, buffDrawer * 2f);
			float height = ScreenHeight - 6f - top;
			DrDraw.Rect(x, top, DrawerWidth + 8f, height + 6f, Color.Black * alpha);
			DrDraw.Outline(x, top, DrawerWidth, height, PanelLine * alpha, 2);
			DrDraw.Text("BUFFS", x + 6, top + 2, new Color(160, 160, 160) * alpha, DrDraw.SmallFont, 0.6f);
			// The key that hides it (whatever it's bound to), as a hint
			// (Never rebound: Terraria lists no keys for it yet, but B is what works)
			string key = MercyMode.AssignedKeys(MercyMode.BuffsKey).FirstOrDefault() ?? "B";
			if (key.Length > 0)
			{
				string hint = $"HIDE: {key.ToUpperInvariant()}";
				DrDraw.Text(hint, x + DrawerWidth - 8 - DrDraw.Measure(hint, DrDraw.SmallFont) * 0.6f, top + 2, new Color(140, 140, 140) * alpha, DrDraw.SmallFont, 0.6f);
			}

			int visible = VisibleBuffRows;
			float listTop = top + 16f;
			for (int r = 0; r < visible && buffScroll + r < rows.Count; r++)
			{
				var (type, ticks) = rows[buffScroll + r];
				float y = listTop + r * BuffRowH;
				float rx = x + 4f, rw = DrawerWidth - 14f;
				bool debuff = Main.debuff[type];
				Color edge = debuff ? new Color(255, 70, 70) : new Color(80, 230, 110);
				DrDraw.Rect(rx, y, rw, BuffRowH - 3, new Color(16, 16, 16) * alpha);
				DrDraw.Rect(rx, y, 2, BuffRowH - 3, edge * alpha);

				Texture2D icon = TextureAssets.Buff[type].Value;
				float scale = 16f / Math.Max(icon.Width, icon.Height);
				DrDraw.Sb.Draw(icon, new Vector2(rx + 4, y + 1), null, Color.White * alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

				string name = Lang.GetBuffName(type);
				string timer = ticks < 0 ? "" : FormatBuffTime(ticks);
				float timerW = DrDraw.Measure(timer, DrDraw.SmallFont) * 0.6f;
				float maxName = rw - 24 - timerW - 6;
				float nameScale = 0.6f;
				if (DrDraw.Measure(name, DrDraw.SmallFont) * nameScale > maxName)
					nameScale = Math.Max(0.38f, maxName / Math.Max(1f, DrDraw.Measure(name, DrDraw.SmallFont)));
				DrDraw.Text(name, rx + 23, y + 1, Color.White * alpha, DrDraw.SmallFont, nameScale);
				if (timer.Length > 0)
				{
					bool ending = ticks < 5 * 60 && (time / 10) % 2 == 0;
					DrDraw.Text(timer, rx + rw - timerW - 3, y + 1, (ending ? edge : new Color(200, 200, 200)) * alpha, DrDraw.SmallFont, 0.6f);
				}
				if (ticks >= 0 && buffFull.TryGetValue(type, out int full) && full > 0)
				{
					float frac = MathHelper.Clamp(ticks / (float)full, 0f, 1f);
					DrDraw.Rect(rx + 23, y + BuffRowH - 7, rw - 27, 2, Color.White * (0.15f * alpha));
					DrDraw.Rect(rx + 23, y + BuffRowH - 7, (rw - 27) * frac, 2, edge * alpha);
				}
			}

			// Scrollbar, when they don't all fit
			if (rows.Count > visible)
			{
				float trackTop = listTop, trackH = visible * BuffRowH - 3;
				float thumbH = Math.Max(8f, trackH * visible / rows.Count);
				float thumbY = trackTop + (trackH - thumbH) * buffScroll / Math.Max(1, rows.Count - visible);
				DrDraw.Rect(x + DrawerWidth - 7, trackTop, 3, trackH, Color.White * (0.12f * alpha));
				DrDraw.Rect(x + DrawerWidth - 7, thumbY, 3, thumbH, Color.White * (0.7f * alpha));
			}
		}

		/// <summary>
		/// The show/hide key, read straight from the keyboard (or mouse): the battle locks the player's controls, and
		/// with them Terraria's own keybind triggers.
		/// </summary>
		private static bool BuffsKeyPressed()
		{
			if (Main.editSign || Main.editChest || Main.gameMenu || !Main.hasFocus)
				return false;
			var keys = MercyMode.AssignedKeys(MercyMode.BuffsKey);
			if (keys.Count == 0)
				keys.Add("B");
			foreach (string k in keys)
			{
				if (Enum.TryParse(k, out Microsoft.Xna.Framework.Input.Keys key))
				{
					if (Main.keyState.IsKeyDown(key) && !Main.oldKeyState.IsKeyDown(key))
						return true;
				}
				else if (k.StartsWith("Mouse") && int.TryParse(k.Substring(5), out int b) && MercyMode.MouseDown(b) && !MercyMode.MouseDown(b, old: true))
					return true;
			}
			return false;
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
