using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
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
		private void DrawSpareMark(float x, float y)
		{
			if (DrDraw.Sprite("spr_sparestar", (int)(time / 8), x, y, Color.White, 1f))
				return;
			Color yellow = new(255, 255, 0);
			DrDraw.Rect(x + 6, y + 1, 4, 14, yellow);
			DrDraw.Rect(x + 1, y + 6, 14, 4, yellow);
			DrDraw.Rect(x + 4, y + 4, 8, 8, yellow);
		}

		/// <summary>The TIRED mark: spr_tiredmark, or little blue z's.</summary>
		private void DrawTiredMark(float x, float y)
		{
			if (DrDraw.Sprite("spr_tiredmark", (int)(time / 10), x, y, Color.White, 1f))
				return;
			DrDraw.Text("z", x, y + 6, TiredBlue, DrDraw.SmallFont, 0.7f);
			DrDraw.Text("Z", x + 7, y, TiredBlue, DrDraw.SmallFont, 0.9f);
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
		/// <summary>A recruit starts its battles with this much MERCY.</summary>
		private const float RecruitMercy = 35f;

		/// <summary>One kind of enemy for recruiting: its banner groups the variants (every zombie is one recruit).</summary>
		private static int RecruitKey(NPC npc)
		{
			int banner = npc.BannerID();
			return banner > 0 ? banner : npc.type;
		}

		private bool IsRecruited(NPC npc) =>
			Player.GetModPlayer<RecruitPlayer>().Count(RecruitKey(npc)) >= RecruitNeeded;

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
				return $"* {e.Name} became your RECRUIT!\n* (It'll go easier on you from now on.)";
			}
			return null;
		}

		// ---- speech bubbles ----

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
				int chars = Math.Min(en.Bubble.Length, age / 2 + 1);

				// Wrapped to the bubble's width (Deltarune's bubbles: the dialogue font at full size, black on white)
				const float scale = 1f, lineH = 17f, pad = 8f, tail = 10f;
				float half = 24f;
				WithEnemy(en, () =>
				{
					float sc = EnemyScaleNow(out _, out Rectangle frame);
					if (frame.Width > 0)
						half = MathHelper.Clamp(frame.Width * sc / 2f, 16f, 70f);
				});
				// As wide as fits between the bullet box and the enemy (narrower, taller bubbles when it's tight)
				float room = at.X - half - 8f - tail - (Box.Right + 8f);
				float maxW = MathHelper.Clamp(room - pad * 2f, 90f, 190f);
				var lines = new List<string>();
				string line = "";
				foreach (string word in en.Bubble.Split(' '))
				{
					string tryLine = line.Length == 0 ? word : line + " " + word;
					if (DrDraw.Measure(tryLine, DrDraw.SmallFont) * scale > maxW && line.Length > 0)
					{
						lines.Add(line);
						line = word;
					}
					else
						line = tryLine;
				}
				lines.Add(line);
				float w = Math.Min(maxW, lines.Max(l => DrDraw.Measure(l, DrDraw.SmallFont) * scale)) + pad * 2f;
				float h = lines.Count * lineH + pad * 1.5f;
				// Beside the enemy at its own height, its tail pointing at it (its left edge, from how big it's drawn)
				// On whole pixels, so the corners stay sharp
				w = (float)Math.Round(w);
				h = (float)Math.Round(h);
				float bx = (float)Math.Round(Math.Max(4f, at.X - half - 8f - tail - w)), by = (float)Math.Round(Math.Clamp(at.Y - h / 2f, 4f, ScreenHeight - PanelHeight - h - 4f));
				// A square notch cut from each corner, like Deltarune's
				const float notch = 2f;
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
				// The text, typed out so far (whole lines at a time once a line is done)
				int left = chars;
				float ty = by + pad * 0.75f;
				foreach (string l in lines)
				{
					if (left <= 0)
						break;
					string part = l.Length <= left ? l : l.Substring(0, left);
					DrDraw.Text(part, bx + pad, ty, Color.Black * alpha, DrDraw.SmallFont, scale);
					left -= l.Length + 1;
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
