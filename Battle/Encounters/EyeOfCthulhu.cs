using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace MercyMode.Battle.Encounters
{
	public class EyeOfCthulhu : Encounter
	{
		public override string Name => "EYE OF CTHULHU";

		// The sprite looks down; turn it to look left at the party
		public override float DrawRotation(int time) => MathHelper.PiOver2 + (float)Math.Sin(time / 30f) * 0.08f;
		public override Vector2 DrawCenter => new(500, 180);
		public override float DrawScale(Rectangle frame) => 1f;

		public override string FlavorText()
		{
			if (Mercy >= 100f)
				return "* EYE OF CTHULHU looks tired of fighting.";
			if (LifeRatio < 0.25f)
				return "* EYE OF CTHULHU is crying blood.";
			if (LifeRatio < 0.5f)
				return "* EYE OF CTHULHU bares its teeth.";
			string[] lines = {
				"* EYE OF CTHULHU stares into your soul.",
				"* EYE OF CTHULHU's gaze follows you around the room.",
				"* Smells like the night sky.",
				"* EYE OF CTHULHU tries to blink. It can't.",
			};
			return lines[Turn % lines.Length];
		}

		public override List<ActOption> Acts(BattleSystem battle) => new()
		{
			CheckAct("* It watches you while you sleep. It wants a staring contest."),
			MercyAct("Stare", "Stare\nback", 30f, ActLines.Get(NPCID.EyeofCthulhu, 0)),
			MercyAct("EyeDrops", "Offer\neye drops", 35f, ActLines.Get(NPCID.EyeofCthulhu, 1)),
			new()
			{
				Name = "Dawn",
				Description = "Talk about\nthe sunrise",
				Run = b =>
				{
					// Worth more near the end of the night (night lasts 32400 ticks)
					bool late = !Main.dayTime && Main.time > 32400 * 0.7;
					float gained = GainMercy("Dawn", late ? 50f : 25f);
					var lines = new List<string> { ActLines.Get(NPCID.EyeofCthulhu, 2) + (gained > 0 ? "" : "\n* It didn't seem to have any effect.") };
					if (late)
						lines[0] += "\n* The sky is getting lighter...";
					if (Mercy >= 100f)
						lines.Add(SpareableLine);
					return lines;
				},
			},
			HealPrayerAct(),
		};

		public override EnemyAttack NextAttack(BattleSystem battle)
		{
			bool hard = LifeRatio < 0.5f;
			int pick = Turn % 7;
			return pick switch
			{
				0 => new TearRain(hard),
				1 => new ServantSwarm(hard),
				2 => new EyeDash(hard),
				// Its stare, as a beam aimed at the SOUL; in phase 2 it keeps crying while it stares
				3 => hard
					? new Combo(BattleConstants.DefaultEnemyTurnTicks, new Beam(50) { Width = 16f }, new TearRain(false))
					: new Beam(70) { Width = 14f },
				// Tears gather around the SOUL, then fall in on it
				4 => new Converge(EyeTear, hard ? 50 : 66) { Count = hard ? 10 : 8, Speed = hard ? 4.6f : 3.8f },
				5 => hard ? new Combo(BattleConstants.DefaultEnemyTurnTicks, new EyeRing(), new ServantSwarm(false))
					: new TearRain(false) { WithServants = true },
				// Phase 2's full-screen frenzy: its gaze slashes across everything while it cries blood
				_ => hard
					? new Combo(BattleConstants.FullScreenTurnTicks,
						new Slashes(64) { Color = new Color(255, 70, 70), PerBurst = 3 },
						new TearRain(false)) { FullScreen = true }
					: new ServantSwarm(false),
			};
		}

		private static Bullet EyeTear(Vector2 p, Vector2 v) => new()
		{
			Position = p,
			Velocity = v,
			Sprite = "spr_ponman_eyebullet",
			Color = new Color(255, 90, 90),
			Scale = 1.5f,
			HitSize = new Vector2(10, 10),
			DamageMult = 0.7f,
		};

		// ================================================================== patterns

		/// <summary>Blood tears fall from above the box, drifting a little.</summary>
		private class TearRain : EnemyAttack
		{
			private readonly bool hard;
			public bool WithServants;

			public TearRain(bool hard)
			{
				this.hard = hard;
			}

			public override void Update(BattleSystem battle, int tick)
			{
				Rectangle box = battle.Box;
				int every = hard ? 7 : 10;
				if (tick % every == 0 && tick < Duration - 40)
				{
					float x = Main.rand.NextFloat(box.Left + 6, box.Right - 6);
					float sway = Main.rand.NextFloat(MathHelper.TwoPi);
					battle.Spawn(new Bullet
					{
						Position = new Vector2(x, box.Top - 24),
						Velocity = new Vector2(0, Main.rand.NextFloat(1.8f, 2.6f) * (hard ? 1.2f : 1f)),
						Sprite = "spr_smallbullet",
						Color = new Color(255, 70, 70),
						HitSize = new Vector2(8, 8),
						DamageMult = 0.6f,
						OnUpdate = b => b.Position.X += (float)Math.Sin(b.Age / 12f + sway) * 0.4f,
					});
				}
				if (WithServants && tick % 50 == 25 && tick < Duration - 60)
					ServantSwarm.SpawnServant(battle, false);
			}
		}

		/// <summary>Servants of Cthulhu fly in from the sides, aimed at the SOUL.</summary>
		private class ServantSwarm : EnemyAttack
		{
			private readonly bool hard;

			public ServantSwarm(bool hard)
			{
				this.hard = hard;
			}

			public override void Update(BattleSystem battle, int tick)
			{
				int every = hard ? 22 : 32;
				if (tick % every == 0 && tick < Duration - 50)
					SpawnServant(battle, hard);
			}

			public static void SpawnServant(BattleSystem battle, bool hard)
			{
				Rectangle box = battle.Box;
				Vector2 center = box.Center.ToVector2();
				float angle = Main.rand.NextFloat(MathHelper.TwoPi);
				Vector2 from = center + angle.ToRotationVector2() * 150f;
				Vector2 dir = Vector2.Normalize(battle.SoulCenter - from);
				float speed = hard ? 2.8f : 2.1f;

				Main.instance.LoadNPC(NPCID.ServantofCthulhu);
				Texture2D tex = TextureAssets.Npc[NPCID.ServantofCthulhu].Value;
				int frames = Math.Max(1, Main.npcFrameCount[NPCID.ServantofCthulhu]);
				int fh = tex.Height / frames;
				battle.Spawn(new Bullet
				{
					Position = from,
					Velocity = dir * speed,
					Texture = tex,
					Source = new Rectangle(0, 0, tex.Width, fh),
					RotateWithVelocity = true,
					// Terraria's servant sprite looks down
					RotationOffset = -MathHelper.PiOver2,
					HitSize = new Vector2(14, 14),
					DamageMult = 0.8f,
					Lifetime = 400,
					OffscreenMargin = 200f,
					OnUpdate = b => b.Source = new Rectangle(0, (b.Age / 8 % frames) * fh, tex.Width, fh),
				});
			}
		}

		/// <summary>A red warning lane flashes, then a big eye charges straight through it.</summary>
		private class EyeDash : EnemyAttack
		{
			private readonly bool hard;
			private int nextDash;
			private int dashes;

			public EyeDash(bool hard)
			{
				this.hard = hard;
			}

			public override void Update(BattleSystem battle, int tick)
			{
				int warn = hard ? 26 : 36;
				int gap = hard ? 55 : 75;
				if (tick < nextDash || tick > Duration - warn - 40)
					return;
				nextDash = tick + gap;

				Rectangle box = battle.Box;
				bool vertical = hard && dashes % 2 == 1;
				bool fromLeft = Main.rand.NextBool();
				Vector2 soul = battle.SoulCenter;
				float lane = vertical ? soul.X : soul.Y;
				const float laneWidth = 30f;
				dashes++;
				AttackSfx.Appear();

				// Warning lane (not harmful)
				battle.Spawn(new Bullet
				{
					Position = vertical ? new Vector2(lane, box.Center.Y) : new Vector2(box.Center.X, lane),
					Harmful = false,
					Lifetime = warn,
					OnDraw = b =>
					{
						float a = b.Age / 3 % 2 == 0 ? 0.55f : 0.25f;
						if (vertical)
							DrDraw.Rect(lane - laneWidth / 2, box.Top, laneWidth, box.Height, new Color(255, 0, 0) * a);
						else
							DrDraw.Rect(box.Left, lane - laneWidth / 2, box.Width, laneWidth, new Color(255, 0, 0) * a);
					},
				});

				// The eye itself, launched when the warning ends
				Main.instance.LoadNPC(NPCID.EyeofCthulhu);
				Texture2D tex = TextureAssets.Npc[NPCID.EyeofCthulhu].Value;
				int fh = tex.Height / Math.Max(1, Main.npcFrameCount[NPCID.EyeofCthulhu]);
				Vector2 dir = vertical ? new Vector2(0, fromLeft ? 1 : -1) : new Vector2(fromLeft ? 1 : -1, 0);
				Vector2 start = vertical
					? new Vector2(lane, fromLeft ? box.Top - 70 : box.Bottom + 70)
					: new Vector2(fromLeft ? box.Left - 70 : box.Right + 70, lane);
				float speed = hard ? 9f : 7f;
				battle.Spawn(new Bullet
				{
					Position = start,
					Texture = tex,
					Source = new Rectangle(0, 0, tex.Width, fh),
					Scale = 0.4f,
					Rotation = dir.ToRotation() - MathHelper.PiOver2,
					HitSize = vertical ? new Vector2(26, 38) : new Vector2(38, 26),
					DamageMult = 1.2f,
					GrazePoints = 8f,
					Lifetime = warn + 120,
					OffscreenMargin = 400f,
					Harmful = false,
					OnUpdate = b =>
					{
						if (b.Age == warn)
						{
							b.Harmful = true;
							b.Velocity = dir * speed;
							// The Eye's own charge roar
							AttackSfx.Vanilla(SoundID.ForceRoar, 0.6f);
						}
					},
				});
			}
		}

		/// <summary>Phase 2: rings of tears close in on the box centre, with a gap to slip through.</summary>
		private class EyeRing : EnemyAttack
		{
			public override void Update(BattleSystem battle, int tick)
			{
				if (tick % 75 != 0 || tick > Duration - 100)
					return;
				Vector2 center = battle.Box.Center.ToVector2();
				const int count = 16;
				int gap = Main.rand.Next(count);
				for (int i = 0; i < count; i++)
				{
					if (i == gap || i == (gap + 1) % count)
						continue;
					float a = MathHelper.TwoPi * i / count;
					Vector2 dir = a.ToRotationVector2();
					battle.Spawn(new Bullet
					{
						Position = center + dir * 120f,
						Velocity = -dir * 1.1f,
						Sprite = "spr_ponman_eyebullet",
						Scale = 1.5f,
						HitSize = new Vector2(10, 10),
						DamageMult = 0.7f,
						Lifetime = 115,
					});
				}
			}
		}
	}
}
