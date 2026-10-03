using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using MercyMode.Battle.Encounters;
using MercyMode.Deltarune;
using static MercyMode.Battle.BattleConstants;

namespace MercyMode.Battle
{
	/// <summary>
	/// Deltarune's extras on top of the battle: enemies talk in speech bubbles as their turn starts, TIRED enemies (blue
	/// names) can be put to sleep with PACIFY, and regular enemies spared enough times become RECRUITS, friendlier
	/// from then on.
	/// </summary>
	public partial class BattleSystem
	{
		/// <summary>Deltarune's blue for a TIRED enemy's name.</summary>
		private static readonly Color TiredBlue = new(60, 160, 255);

		/// <summary>The spare star after a spareable enemy's name (spr_sparestar, or a drawn star).</summary>
		/// <returns>How wide it is.</returns>
		private float DrawSpareMark(float x, float y)
		{
			var spr = DeltaruneAssets.Sprite("spr_sparestar");
			if (spr != null && DrDraw.Sprite("spr_sparestar", (int)(time / 8), x + spr.Origin.X, y + spr.Origin.Y, Color.White, 1f))
				return spr.Frame(0).Width;
			Color yellow = new(255, 255, 0);
			DrDraw.Rect(x + 6, y + 1, 4, 14, yellow);
			DrDraw.Rect(x + 1, y + 6, 14, 4, yellow);
			DrDraw.Rect(x + 4, y + 4, 8, 8, yellow);
			return 16f;
		}

		/// <summary>The TIRED mark: spr_tiredmark, or little blue z's.</summary>
		/// <returns>How wide it is.</returns>
		private float DrawTiredMark(float x, float y)
		{
			var spr = DeltaruneAssets.Sprite("spr_tiredmark");
			if (spr != null && DrDraw.Sprite("spr_tiredmark", (int)(time / 10), x + spr.Origin.X, y + spr.Origin.Y, Color.White, 1f))
				return spr.Frame(0).Width;
			// Two little z's, one up and one down, in Deltarune's cyan
			Color zc = new(110, 220, 230);
			DrDraw.Text("z", x, y + 1, zc, DrDraw.SmallFont, 0.7f);
			DrDraw.Text("z", x + 7, y + 6, zc, DrDraw.SmallFont, 0.7f);
			return 16f;
		}

		/// <summary>
		/// A command button pulsing white: Deltarune's own white frame if the sprite has one, otherwise its selected
		/// frame (the one with the name under it) turned white.
		/// </summary>
		private void DrawButtonGlow(string sprite, string label, float x, float y)
		{
			float a = 0.45f + 0.45f * (float)Math.Sin(time / 12f);
			var spr = DeltaruneAssets.Sprite(sprite);
			if (spr == null)
			{
				DrDraw.Outline(x, y, 31, 32, Color.White * a, 2);
				DrDraw.Text(label, x + 15 - DrDraw.Measure(label, DrDraw.SmallFont) * 0.35f, y + 33, Color.White * a, DrDraw.SmallFont, 0.7f);
				return;
			}
			// Only the button's coloured parts light up (outline, icon, name), not its black inside
			Texture2D tex = spr.Frames.Length >= 3 ? spr.Frame(2) : WhiteMask.OfBright(spr.Frame(1));
			DrDraw.Sb.Draw(tex, new Vector2(x, y), null, Color.White * a, 0f, spr.Origin, 1f, SpriteEffects.None, 0f);
		}

		// ---- PACIFY ----

		/// <summary>PACIFY on a TIRED target: it falls asleep and is spared. Returns the lines to show.</summary>
		internal List<string> PacifyTarget()
		{
			BattleEnemy who = targetEnemy;
			string name = encounter.Name;
			who.Out = true;
			// The spell: sparkles off the caster with the cast sound, then the target nods off and fades away
			DeltaruneAssets.Play("spellcast", Terraria.ID.SoundID.Item4);
			Sparks.Burst(this, HeroFeetNow + new Vector2(0f, -40f), 12, new Color(160, 200, 255), 2.4f);
			if (duelWith < 0)
				enemyOverride = new PacifyAnimation(enemySnap);
			if (Net.BattleNet.Online)
				Net.BattleNet.SendSpare(encounter.Npc);
			else
				encounter.Spare();
			var lines = new List<string> { $"* You cast PACIFY!\n* {name} fell asleep!" };
			if (RecordRecruit(who.E) is string recruit)
				lines.Add(recruit);
			if (LivingEnemies.Count == 0)
			{
				battleOver = true;
				return lines;
			}
			if (OnEnemySpared(who) is string squad)
				lines.Add(squad);
			RetargetIfNeeded();
			return lines;
		}

		// ---- RECRUITS ----

		/// <summary>Spares of the same kind of enemy (variants together) that make it a recruit.</summary>
		public const int RecruitNeeded = 4;

		/// <summary>One kind of enemy for recruiting: its banner groups the variants (every zombie is one recruit).</summary>
		private static int RecruitKey(NPC npc)
		{
			int banner = npc.BannerID();
			return banner > 0 ? banner : npc.type;
		}

		/// <summary>A regular enemy was spared: one more toward recruiting its kind. The line to show, or null.</summary>
		private string RecordRecruit(Encounter e)
		{
			if (e is not EnemyEncounter || duelWith >= 0 || e.Npc == null)
				return null;
			var rp = Player.GetModPlayer<RecruitPlayer>();
			int key = RecruitKey(e.Npc);
			int now = rp.Add(key);
			if (now < RecruitNeeded)
				return $"* {e.Name}: RECRUIT {now}/{RecruitNeeded}";
			if (now == RecruitNeeded)
			{
				Sfx("mercyadd");
				return $"* {e.Name} became your RECRUIT!";
			}
			return null;
		}

		// ---- speech bubbles ----

		/// <summary>
		/// How a letter moves, like Deltarune's text writer: still, shaking in place (a jitter every couple of frames),
		/// trembling (a bigger shake: scared, freezing, furious) or waving (a sine down the line: ghostly, sleepy, sing-song).
		/// </summary>
		internal enum TextFx : byte { None, Shake, Tremble, Wave }

		/// <summary>
		/// A bubble line with its effects marked inline, [shake]...[/shake], [tremble]...[/tremble] and [wave]...[/wave]:
		/// the plain text, and each letter's effect.
		/// </summary>
		internal static (string plain, TextFx[] fx) ParseBubble(string text)
		{
			var plain = new System.Text.StringBuilder();
			var fx = new List<TextFx>();
			TextFx now = TextFx.None;
			int i = 0;
			while (i < text.Length)
			{
				if (text[i] == '[')
				{
					int end = text.IndexOf(']', i);
					if (end > i)
					{
						string tag = text.Substring(i + 1, end - i - 1).ToLowerInvariant();
						TextFx? set = tag switch
						{
							"shake" => TextFx.Shake,
							"tremble" => TextFx.Tremble,
							"wave" => TextFx.Wave,
							"/shake" or "/tremble" or "/wave" => TextFx.None,
							_ => null,
						};
						if (set is TextFx f)
						{
							now = f;
							i = end + 1;
							continue;
						}
					}
				}
				plain.Append(text[i]);
				fx.Add(now);
				i++;
			}
			return (plain.ToString(), fx.ToArray());
		}

		internal static string BubblePlain(string text) => text == null ? "" : ParseBubble(text).plain;

		/// <summary>Lines up to this long ("Blorp!", "Braaains...") get the small bubble.</summary>
		private const int ShortBubbleChars = 14;

		/// <summary>Where a letter sits off its place right now for its effect (in battle pixels).</summary>
		private Vector2 LetterOffset(TextFx fx, int index)
		{
			switch (fx)
			{
				case TextFx.Shake:
				case TextFx.Tremble:
				{
					// A new random spot every 2 frames, the same for every screen and draw of that moment
					uint h = (uint)(index * 73856093) ^ (uint)((time / (2 * TicksPerFrame)) * 19349663);
					h ^= h >> 13;
					h *= 0x5bd1e995;
					float amp = fx == TextFx.Tremble ? 2f : 1f;
					float dx = ((h & 0xff) / 255f * 2f - 1f) * amp;
					float dy = (((h >> 8) & 0xff) / 255f * 2f - 1f) * amp;
					return new Vector2((float)Math.Round(dx), (float)Math.Round(dy));
				}
				case TextFx.Wave:
					return new Vector2(0f, (float)Math.Round(Math.Sin(time * 0.12f + index * 0.55f) * 2f));
				default:
					return Vector2.Zero;
			}
		}

		/// <summary>
		/// Each enemy's speech bubble while its turn opens (Deltarune's): white, to its left, typing out, with a tail
		/// pointing at it. Sleepy z's float off TIRED enemies.
		/// </summary>
		private void DrawBubbles()
		{
			foreach (BattleEnemy en in enemies)
			{
				if (!en.Living)
					continue;
				Vector2 at = en.E.ScreenCenter;
				// Only while they talk, before the box opens
				if (string.IsNullOrEmpty(en.Bubble) || phase != Phase.EnemyTalk)
					continue;
				int age = time - en.BubbleAt;
				if (age < 0)
					continue;
				float alpha = 1f;
				int chars = Math.Min(BubblePlain(en.Bubble).Length, age / 2 + 1);

				// Wrapped to the bubble's width (Deltarune's bubbles: the dialogue font at full size, black on white)
				// Deltarune's bubbles: big text, roomy, up to about half the screen wide for long lines
				// Short lines get Deltarune's small bubble (small text, snug); longer ones the big one
				bool small = BubblePlain(en.Bubble).Length <= ShortBubbleChars;
				string font = small ? DrDraw.SmallFont : DrDraw.BigFont;
				float scale = small ? 1f : 0.9f, pad = small ? 8f : 12f, tail = small ? 10f : 12f;
				float lineH = DrDraw.LineHeight(font) * scale;
				float half = 24f;
				WithEnemy(en, () =>
				{
					float sc = EnemyScaleNow(out _, out Rectangle frame);
					if (frame.Width > 0)
						half = MathHelper.Clamp(frame.Width * sc / 2f, 16f, 70f);
				});
				// (The box isn't open yet while they talk, so a bubble can reach across the middle of the screen)
				float room = at.X - half - 8f - tail - 8f;
				float maxW = MathHelper.Clamp(room - pad * 2f, 100f, 290f);
				(string plain, TextFx[] fx) = ParseBubble(en.Bubble);
				var lines = new List<string>();
				string line = "";
				foreach (string word in plain.Split(' '))
				{
					string tryLine = line.Length == 0 ? word : line + " " + word;
					if (DrDraw.Measure(tryLine, font) * scale > maxW && line.Length > 0)
					{
						lines.Add(line);
						line = word;
					}
					else
						line = tryLine;
				}
				lines.Add(line);
				float w = Math.Min(maxW, lines.Max(l => DrDraw.Measure(l, font) * scale)) + pad * 2f;
				float h = lines.Count * lineH + pad * 1.6f;
				// Beside the enemy at its own height, its tail pointing at it (its left edge, from how big it's drawn)
				// On whole pixels, so the corners stay sharp
				w = (float)Math.Round(w);
				h = (float)Math.Round(h);
				float bx = (float)Math.Round(Math.Max(4f, at.X - half - 8f - tail - w)), by = (float)Math.Round(Math.Clamp(at.Y - h / 2f, 4f, ScreenHeight - PanelHeight - h - 4f));
				// A square notch cut from each corner, like Deltarune's
				float notch = small ? 2f : 3f;
				DrDraw.Rect(bx + notch, by, w - notch * 2f, h, Color.White * alpha);
				DrDraw.Rect(bx, by + notch, notch, h - notch * 2f, Color.White * alpha);
				DrDraw.Rect(bx + w - notch, by + notch, notch, h - notch * 2f, Color.White * alpha);
				// The tail: a white triangle from the bubble's right side toward the enemy
				float cy = Math.Clamp(at.Y, by + 8f, by + h - 8f);
				for (int i = 0; i < (int)tail; i++)
				{
					float half2 = 6f * (1f - i / tail);
					DrDraw.Rect(bx + w + i, cy - half2, 1, half2 * 2f, Color.White * alpha);
				}
				// The text, typed out so far, letter by letter with each letter's effect (shaking, waving...)
				int index = 0;
				float ty = by + pad * 0.6f;
				foreach (string l in lines)
				{
					for (int k = 0; k < l.Length && index + k < chars; k++)
					{
						float lx = bx + pad + DrDraw.Measure(l.Substring(0, k), font) * scale;
						Vector2 off = LetterOffset(index + k < fx.Length ? fx[index + k] : TextFx.None, index + k);
						DrDraw.Text(l[k].ToString(), lx + off.X, ty + off.Y, Color.Black * alpha, font, scale);
					}
					index += l.Length + 1;
					if (index > chars)
						break;
					ty += lineH;
				}
			}
		}
	}

	/// <summary>How many of each kind of enemy this character has spared (for RECRUITS), saved with the character.</summary>
	public class RecruitPlayer : ModPlayer
	{
		private Dictionary<int, int> spared = new();

		public int Count(int key) => spared.TryGetValue(key, out int n) ? n : 0;

		public int Add(int key)
		{
			spared[key] = Count(key) + 1;
			return spared[key];
		}

		public override void SaveData(TagCompound tag)
		{
			tag["recruitKeys"] = spared.Keys.ToList();
			tag["recruitCounts"] = spared.Values.ToList();
		}

		public override void LoadData(TagCompound tag)
		{
			spared = new Dictionary<int, int>();
			var keys = tag.GetList<int>("recruitKeys");
			var counts = tag.GetList<int>("recruitCounts");
			for (int i = 0; i < Math.Min(keys.Count, counts.Count); i++)
				spared[keys[i]] = counts[i];
		}
	}
}
