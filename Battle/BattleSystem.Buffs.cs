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

		private const float DrawerWidth = 176f, BuffRowH = 21f;

		/// <summary>The text box's line, before wrapping, and the width it's wrapped to.</summary>
		private string rawText = "";
		private float textWrap = 570f;

		/// <summary>Lines stop short of the buff drawer while it's out (it sits over the right end of the text box).</summary>
		private float TextWrapWidth => buffsWanted && BuffRows().Count > 0 ? ScreenWidth - DrawerWidth - 30f - 46f : 570f;

		/// <summary>Menus that use the right side of the panel (the drawer slides away for them).</summary>
		private bool PanelRightBusy => phase is Phase.WeaponSelect or Phase.ActSelect or Phase.ItemSelect or Phase.SummonSelect
			or Phase.EnemySelect or Phase.PartySelect or Phase.FightBar or Phase.FightResult or Phase.Build or Phase.MercyPrompt;

		/// <summary>
		/// Flagged as debuffs by Terraria only because they can't be cancelled; they're harmless (the Happy! of a nearby
		/// sunflower, a campfire's Cozy Fire...), so they aren't shown red, and go to the bottom.
		/// </summary>
		private static readonly HashSet<int> HarmlessBuffs = new()
		{
			Terraria.ID.BuffID.Sunflower, Terraria.ID.BuffID.Campfire, Terraria.ID.BuffID.HeartLamp, Terraria.ID.BuffID.StarInBottle,
			Terraria.ID.BuffID.CatBast, Terraria.ID.BuffID.MonsterBanner, Terraria.ID.BuffID.Honey, Terraria.ID.BuffID.PeaceCandle,
			Terraria.ID.BuffID.ShadowCandle,
		};

		/// <summary>When each buff showed up (it's "ongoing" once it has outlasted its own timer: kept topped up).</summary>
		private readonly Dictionary<int, uint> buffSince = new();
		/// <summary>The buff opened for a closer look (its description), or 0.</summary>
		private int buffInspect;

		private static bool BadBuff(int type) => Main.debuff[type] && !HarmlessBuffs.Contains(type);

		/// <summary>Kept going by something nearby or worn (a campfire, the sunflower): it never runs out while that lasts.</summary>
		private bool Ongoing(int type, int left) =>
			left >= 0 && buffSince.TryGetValue(type, out uint since) && buffFull.TryGetValue(type, out int full)
			&& Main.GameUpdateCount - since > full + 120;

		/// <summary>
		/// The player's buffs to list: (type, ticks left, or -1 for ones without a timer). Debuffs first, then buffs,
		/// then the ones that hardly matter (pets, lights, harmless surroundings, anything kept topped up).
		/// </summary>
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
			int Rank((int type, int left) r)
			{
				if (BadBuff(r.type))
					return 0;
				bool minor = Main.vanityPet[r.type] || Main.lightPet[r.type] || HarmlessBuffs.Contains(r.type) || r.left < 0 || Ongoing(r.type, r.left);
				return minor ? 2 : 1;
			}
			return rows.OrderBy(Rank).ToList();
		}

		private float DrawerTop => ScreenHeight - panel + 40f;
		private float DrawerX => ScreenWidth + 2f - (DrawerWidth + 6f) * EaseOut(buffDrawer);

		private static float EaseOut(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

		/// <summary>
		/// The tab the drawer hangs from: beside it while it's out, and when it's away just a small sliver tucked against
		/// the right edge, to bring it back.
		/// </summary>
		private Rectangle BuffTab
		{
			get
			{
				float k = EaseOut(buffDrawer);
				float w = MathHelper.Lerp(9f, 14f, k), h = MathHelper.Lerp(20f, 26f, k);
				return new Rectangle((int)(DrawerX - w), (int)DrawerTop + 4, (int)w, (int)h);
			}
		}

		/// <summary>The description of a buff, in lines that fit the drawer.</summary>
		private static List<string> BuffDescription(int type, float width, float scale)
		{
			string text = Lang.GetBuffDescription(type) ?? "";
			var lines = new List<string>();
			foreach (string para in text.Split('\n'))
			{
				string line = "";
				foreach (string word in para.Split(' '))
				{
					string tryLine = line.Length == 0 ? word : line + " " + word;
					if (DrDraw.Measure(tryLine, DrDraw.SmallFont) * scale > width && line.Length > 0)
					{
						lines.Add(line);
						line = word;
					}
					else
						line = tryLine;
				}
				if (line.Length > 0)
					lines.Add(line);
			}
			return lines.Take(4).ToList();
		}

		private const float DescScale = 0.5f, DescLineH = 11f;

		/// <summary>Where each shown row goes (the one being inspected is taller), from the scroll position down.</summary>
		private List<(int index, float y, float h)> BuffLayout(List<(int type, int left)> rows)
		{
			var layout = new List<(int, float, float)>();
			float y = DrawerTop + 16f, bottom = ScreenHeight - 6f;
			for (int i = buffScroll; i < rows.Count; i++)
			{
				float h = BuffRowH;
				if (rows[i].type == buffInspect)
					h += BuffDescription(rows[i].type, DrawerWidth - 30f, DescScale).Count * DescLineH + 4f;
				if (y + h > bottom && layout.Count > 0)
					break;
				layout.Add((i, y, h));
				y += h;
			}
			return layout;
		}

		private void UpdateBuffDrawer()
		{
			var rows = BuffRows();
			// Bars: the most each buff has had left (a fresh or topped-up buff fills its bar again)
			var seen = new HashSet<int>();
			foreach (var (type, left) in rows)
			{
				seen.Add(type);
				if (!buffSince.ContainsKey(type))
					buffSince[type] = Main.GameUpdateCount;
				if (left > 0 && (!buffFull.TryGetValue(type, out int full) || left > full))
					buffFull[type] = left;
			}
			foreach (int type in buffFull.Keys.ToList())
				if (!seen.Contains(type))
					buffFull.Remove(type);
			foreach (int type in buffSince.Keys.ToList())
				if (!seen.Contains(type))
					buffSince.Remove(type);
			if (buffInspect != 0 && !seen.Contains(buffInspect))
				buffInspect = 0;

			if (Main.dedServ || Main.drawingPlayerChat)
				return;
			if (BuffsKeyPressed())
			{
				buffsWanted = !buffsWanted;
				Sfx("menumove");
			}
			Vector2 mouse = MouseBattle();
			Point mp = mouse.ToPoint();
			bool clicked = Main.mouseLeft && Main.mouseLeftRelease && phase != Phase.Build && panel > 0;
			if (clicked && BuffTab.Contains(mp))
			{
				buffsWanted = !buffsWanted;
				Main.mouseLeftRelease = false;
				Sfx("menumove");
				clicked = false;
			}
			bool open = buffsWanted && !PanelRightBusy && rows.Count > 0 && panel > 0;
			// The text box wraps around the drawer: re-wrap the line when it comes or goes
			if (TextWrapWidth != textWrap)
			{
				// (Only if what's shown is still that line: the text box may have been cleared or set some other way)
				bool same = rawText.Length > 0 && text == DrDraw.Wrap(rawText, textWrap);
				textWrap = TextWrapWidth;
				if (same)
				{
					text = DrDraw.Wrap(rawText, textWrap);
					textShown = Math.Min(textShown, text.Length);
				}
			}
			// Slides out and back with an ease
			buffDrawer = MathHelper.Clamp(buffDrawer + (open ? 0.09f : -0.12f), 0f, 1f);

			// A click on a row opens its description (again closes it)
			if (clicked && buffDrawer > 0.9f)
				foreach (var (index, y, h) in BuffLayout(rows))
					if (new Rectangle((int)DrawerX, (int)y, (int)DrawerWidth, (int)h).Contains(mp))
					{
						buffInspect = buffInspect == rows[index].type ? 0 : rows[index].type;
						Main.mouseLeftRelease = false;
						Sfx("select");
						break;
					}

			// The wheel scrolls it with the mouse over it (the attack builder keeps the wheel for itself)
			var area = new Rectangle((int)DrawerX, (int)DrawerTop, (int)DrawerWidth, (int)(ScreenHeight - DrawerTop));
			if (phase != Phase.Build)
			{
				if (buffDrawer > 0.5f && area.Contains(mp) && buildScroll != 0)
					buffScroll += buildScroll < 0 ? 1 : -1;
				buildScroll = 0;
			}
			buffScroll = Math.Clamp(buffScroll, 0, Math.Max(0, rows.Count - 1));
		}

		/// <summary>
		/// The buff drawer, in the dark panel on the right: a list of the player's buffs and debuffs (icon, name, a bar
		/// running down and the time left; debuffs red) that slides out and away, scrolls, and opens a buff's
		/// description on a click. No borders: it's part of the panel.
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
			Point mouse = MouseBattle().ToPoint();

			// The tab
			Rectangle tab = BuffTab;
			bool tabHover = tab.Contains(mouse);
			DrDraw.Rect(tab.X, tab.Y, tab.Width, tab.Height, new Color(24, 24, 28) * panelAlpha);
			DrDraw.Text(buffDrawer > 0.5f ? ">" : "<", tab.X + tab.Width / 2f - 3f, tab.Y + tab.Height / 2f - 7f,
				(tabHover ? Color.White : new Color(150, 150, 150)) * panelAlpha, DrDraw.SmallFont, 0.6f);
			if (buffDrawer <= 0.01f)
				return;

			float alpha = panelAlpha * Math.Min(1f, buffDrawer * 2f);
			DrDraw.Rect(x, top, DrawerWidth + 8f, ScreenHeight - top + 6f, Color.Black * alpha);
			// Good and bad alike (and plain statuses), so not "buffs"
			DrDraw.Text("EFFECTS", x + 6, top + 2, new Color(130, 130, 130) * alpha, DrDraw.SmallFont, 0.6f);
			// The key that hides it (whatever it's bound to), as a hint
			// (Never rebound: Terraria lists no keys for it yet, but B is what works)
			string key = MercyMode.AssignedKeys(MercyMode.BuffsKey).FirstOrDefault() ?? "B";
			string hint = $"HIDE: {key.ToUpperInvariant()}";
			DrDraw.Text(hint, x + DrawerWidth - 8 - DrDraw.Measure(hint, DrDraw.SmallFont) * 0.6f, top + 2, new Color(110, 110, 110) * alpha, DrDraw.SmallFont, 0.6f);

			var layout = BuffLayout(rows);
			foreach (var (index, y, h) in layout)
			{
				var (type, ticks) = rows[index];
				float rx = x + 4f, rw = DrawerWidth - 14f;
				bool bad = BadBuff(type);
				// Pets and lights are just there: neither good nor bad
				bool status = !bad && (Main.vanityPet[type] || Main.lightPet[type]);
				bool battleSick = type == Terraria.ID.BuffID.PotionSickness && potionSickTurns > 0;
				bool ongoing = !battleSick && Ongoing(type, ticks);
				Color edge = bad ? new Color(255, 70, 70) : status ? new Color(150, 150, 160) : new Color(80, 230, 110);
				bool hover = new Rectangle((int)x, (int)y, (int)DrawerWidth, (int)h).Contains(mouse);
				bool inspected = type == buffInspect;
				DrDraw.Rect(rx, y, rw, h - 3, (inspected ? new Color(26, 26, 32) : hover ? new Color(20, 20, 24) : new Color(12, 12, 14)) * alpha);
				DrDraw.Rect(rx, y, 2, h - 3, edge * alpha);

				Texture2D icon = TextureAssets.Buff[type].Value;
				float scale = 16f / Math.Max(icon.Width, icon.Height);
				DrDraw.Sb.Draw(icon, new Vector2(rx + 4, y + 1), null, Color.White * alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

				string name = Lang.GetBuffName(type);
				// Its sign: + good, - bad, a dot for a plain status
				string sign = bad ? "-" : status ? "=" : "+";
				DrDraw.Text(sign, rx + 23, y + 1, edge * alpha, DrDraw.SmallFont, 0.6f);
				float signW = DrDraw.Measure("+ ", DrDraw.SmallFont) * 0.6f;
				// Kept topped up: it just says it keeps going. The battle's potion sickness counts turns, not time
				string timer = battleSick ? $"{potionSickTurns} TURN{(potionSickTurns == 1 ? "" : "S")}"
					: ticks < 0 ? "" : ongoing ? "ONGOING" : FormatBuffTime(ticks);
				float timerScale = ongoing ? 0.5f : 0.6f;
				float timerW = DrDraw.Measure(timer, DrDraw.SmallFont) * timerScale;
				float maxName = rw - 24 - timerW - 6 - signW;
				float nameScale = 0.6f;
				if (DrDraw.Measure(name, DrDraw.SmallFont) * nameScale > maxName)
					nameScale = Math.Max(0.38f, maxName / Math.Max(1f, DrDraw.Measure(name, DrDraw.SmallFont)));
				DrDraw.Text(name, rx + 23 + signW, y + 1, Color.White * alpha, DrDraw.SmallFont, nameScale);
				if (timer.Length > 0)
				{
					bool ending = !ongoing && ticks < 5 * 60 && (time / 10) % 2 == 0;
					Color tc = ongoing ? new Color(140, 140, 140) : ending ? edge : new Color(200, 200, 200);
					DrDraw.Text(timer, rx + rw - timerW - 3, y + (ongoing ? 3 : 1), tc * alpha, DrDraw.SmallFont, timerScale);
				}
				float barY = y + BuffRowH - 7;
				if (ongoing)
				{
					// A full bar with a glint running along it: it isn't running down
					DrDraw.Rect(rx + 23, barY, rw - 27, 2, edge * (0.45f * alpha));
					float g = (time % 90) / 90f;
					DrDraw.Rect(rx + 23 + (rw - 35) * g, barY, 8, 2, Color.White * (0.7f * alpha));
				}
				else if (battleSick)
				{
					float frac = MathHelper.Clamp(potionSickTurns / (float)Math.Max(1, potionSickFull), 0f, 1f);
					DrDraw.Rect(rx + 23, barY, rw - 27, 2, Color.White * (0.15f * alpha));
					DrDraw.Rect(rx + 23, barY, (rw - 27) * frac, 2, edge * alpha);
				}
				else if (ticks >= 0 && buffFull.TryGetValue(type, out int full) && full > 0)
				{
					float frac = MathHelper.Clamp(ticks / (float)full, 0f, 1f);
					DrDraw.Rect(rx + 23, barY, rw - 27, 2, Color.White * (0.15f * alpha));
					DrDraw.Rect(rx + 23, barY, (rw - 27) * frac, 2, edge * alpha);
				}
				// Opened: what it does
				if (inspected)
				{
					float dy = y + BuffRowH;
					foreach (string line in BuffDescription(type, rw - 16f, DescScale))
					{
						DrDraw.Text(line, rx + 8, dy, new Color(190, 190, 200) * alpha, DrDraw.SmallFont, DescScale);
						dy += DescLineH;
					}
				}
			}

			// Scrollbar, when they don't all fit
			if (buffScroll > 0 || layout.Count < rows.Count - buffScroll)
			{
				float trackTop = top + 16f, trackH = ScreenHeight - 10f - trackTop;
				float thumbH = Math.Max(8f, trackH * layout.Count / rows.Count);
				float thumbY = trackTop + (trackH - thumbH) * buffScroll / Math.Max(1, rows.Count - layout.Count);
				DrDraw.Rect(x + DrawerWidth - 7, trackTop, 2, trackH, Color.White * (0.08f * alpha));
				DrDraw.Rect(x + DrawerWidth - 7, thumbY, 2, thumbH, Color.White * (0.5f * alpha));
			}
			float lastBottom = layout.Count > 0 ? layout[^1].y + layout[^1].h : top + 16f;
			if (buffInspect == 0 && lastBottom < ScreenHeight - 18f)
				DrDraw.Text("click a buff for details", x + 6, ScreenHeight - 16f, new Color(90, 90, 90) * alpha, DrDraw.SmallFont, 0.45f);
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
