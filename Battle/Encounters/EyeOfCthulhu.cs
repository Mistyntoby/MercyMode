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

		// The sprite looks down; turn it to look left at the party. While it tears open it spins, faster and faster,
		// then slows to a stop (Terraria's phase change)
		public override float DrawRotation(int time)
		{
			float look = MathHelper.PiOver2 + (float)Math.Sin(time / 30f) * 0.08f;
			if (ripProgress is float p && p < 1f)
			{
				// Speeds up, then winds down: smoothstep of the turns made, five whole turns in all
				float s = p * p * (3f - 2f * p);
				look += s * MathHelper.TwoPi * 5f;
			}
			return look;
		}

		// ---- tearing open (phase 2) ----

		/// <summary>0..1 through the turn it tears open; 1 once it has; null before.</summary>
		private float? ripProgress;
		/// <summary>Torn open: the mouth frames (3-5 of the sheet) from the moment it bursts.</summary>
		private bool Ripped => ripProgress >= RipBurstAt;
		private const float RipBurstAt = 0.5f;
		private const int RipTicks = 150;

		public override int? FrameOverride(int time, int frameCount)
		{
			if (frameCount < 6)
				return null;
			// Its eye frames (0-2) or mouth frames (3-5), cycling slowly
			int cycle = time / 8 % 3;
			return Ripped ? 3 + cycle : cycle;
		}

		/// <summary>The turn it tears open: no bullets, just the spin, the roar and the burst.</summary>
		private class RipOpen : EnemyAttack
		{
			private readonly EyeOfCthulhu eye;
			private bool burst;

			public RipOpen(EyeOfCthulhu eye)
			{
				this.eye = eye;
				Duration = RipTicks;
			}

			public override void Update(BattleSystem battle, int tick)
			{
				eye.ripProgress = Math.Min(1f, tick / (float)(RipTicks - 20));
				if (tick == 1)
					AttackSfx.Vanilla(SoundID.ForceRoar, 0.9f);
				if (tick % 12 == 0 && !burst)
					battle.ShakeScreen(1);
				if (!burst && eye.Ripped)
				{
					burst = true;
					AttackSfx.Vanilla(SoundID.NPCHit1, 1f, -0.3f);
					AttackSfx.Vanilla(SoundID.Roar, 0.9f);
					battle.ShakeScreen(6);
					Vector2 at = eye.ScreenCenter;
					// Blood and bits of eye fly off it (harmless: it's a show, not an attack)
					for (int i = 0; i < 28; i++)
					{
						Vector2 v = Main.rand.NextVector2Unit() * Main.rand.NextFloat(1.5f, 5f);
						bool chunk = i % 4 == 0;
						Bullet b = Shots.Ball(at, v, chunk ? new Color(235, 225, 225) : new Color(200, 20, 30), 0f, chunk ? 1.1f : Main.rand.NextFloat(0.5f, 0.9f));
						b.Harmful = false;
						b.GrazePoints = 0f;
						b.Acceleration = new Vector2(0f, 0.18f);
						b.Lifetime = 70;
						b.OffscreenMargin = 2000f;
						battle.Spawn(b);
					}
				}
			}
		}
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
			// Halfway down it tears its eye open instead of attacking, once
			if (hard && ripProgress == null)
			{
				ripProgress = 0f;
				return new RipOpen(this);
			}
			Bullet servant(Vector2 p, Vector2 v) => Shots.Npc(NPCID.ServantofCthulhu, p, v, 1f, 0.9f, new Vector2(14, 14), rotate: false);
			var moves = new Func<EnemyAttack>[]
			{
				() => new TearRain(hard),
				// Tears well up along the path you just took
				() => new EchoTrail(EyeTear) { Delay = hard ? 30 : 38, RecordEvery = hard ? 5 : 6 },
				() => new ServantSwarm(hard),
				// Deltarune's sword throwers: servants bob off to the side and hurl tears at you
				() => new Slashers(EyeTear, p => { Bullet b = servant(p, Vector2.Zero); b.Rotation = MathHelper.PiOver2; return b; }) { Count = hard ? 3 : 2 },
				() => new EyeDash(hard),
				// Its stare: beams aimed at the SOUL, quicker and wider; in phase 2 it keeps crying while it stares
				() => hard
					? new Combo(BattleConstants.DefaultEnemyTurnTicks, new Beam(38) { Width = 18f, Warn = 30 }, new TearRain(false))
					: new Beam(46) { Width = 16f, Warn = 34 },
				// Tears gather around the SOUL, then fall in on it
				() => new Converge(EyeTear, hard ? 50 : 66) { Count = hard ? 10 : 8, Speed = hard ? 4.6f : 3.8f },
				// A ring of tears forms around you and fires in one at a time
				() => new RingVolley(EyeTear, hard ? 100 : 120) { Count = hard ? 12 : 10, Gap = hard ? 5 : 6, Speed = hard ? 4f : 3.6f },
				// Rings of blood close in, each with one way out: three of them (with servants once it's torn open)
				() => hard ? new Combo(EyeRing.Ticks, new EyeRing { Duration = EyeRing.Ticks }, new ServantSwarm(false))
					: new EyeRing { Duration = EyeRing.Ticks },
				// Servants line up over the SOUL and dive at it, trailing blood
				() => new Diver((p, v) => servant(p, v).Dripping(new Color(200, 30, 40)), hard ? 22 : 32) { DiveSpeed = hard ? 8.5f : 7f },
				// Phase 2's full-screen frenzy: its gaze slashes across everything while it cries blood
				() => hard
					? new Combo(BattleConstants.FullScreenTurnTicks,
						new Slashes(64) { Color = new Color(255, 70, 70), PerBurst = 3 },
						new TearRain(false)) { FullScreen = true }
					: new ServantSwarm(false),
			};
			return moves[Turn % moves.Length]();
		}

		/// <summary>A tear of blood (drawn by hand: dark red, a tail along its path, dripping).</summary>
		private static Bullet EyeTear(Vector2 p, Vector2 v) => Shots.Blood(p, v, 1.2f, 0.9f);

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
					Bullet drop = Shots.Blood(new Vector2(x, box.Top - 24), new Vector2(0, Main.rand.NextFloat(1.8f, 2.6f) * (hard ? 1.2f : 1f)), 1f, 0.6f);
					drop.OnUpdate += b => b.Position.X += (float)Math.Sin(b.Age / 12f + sway) * 0.4f;
					battle.Spawn(drop);
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
			/// <summary>Long enough for three rings to form, close in and fade.</summary>
			public const int Ticks = 380;
			/// <summary>A ring holds still (harmless, its opening glowing) this long before it closes in.</summary>
			private const int Form = 30;
			private const float Radius = 120f, Speed = 1.2f, FadeFrom = 34f, GoneAt = 16f;

			public override void Update(BattleSystem battle, int tick)
			{
				// A ring every 100 ticks from the start: three in a turn, with time to read each one
				if (tick < 10 || (tick - 10) % 100 != 0 || tick > Duration - 110)
					return;
				Vector2 center = battle.Box.Center.ToVector2();
				// 18 drops with four missing in a row: an opening you can see and fit through, its edges glowing
				const int count = 18, gapSize = 4;
				int gap = Main.rand.Next(count);
				AttackSfx.Appear();
				for (int i = 0; i < count; i++)
				{
					int fromGap = (i - gap + count) % count;
					if (fromGap < gapSize)
						continue;
					float a = MathHelper.TwoPi * i / count;
					Vector2 dir = a.ToRotationVector2();
					// No drips here: they cluttered the ring and hid the opening. Softer than an ordinary tear
					Bullet drop = Shots.Blood(center + dir * Radius, Vector2.Zero, 1.3f, 0.8f, drips: false);
					drop.Harmful = false;
					drop.Alpha = 0f;
					drop.Lifetime = Form + (int)((Radius - GoneAt) / Speed) + 2;
					drop.OnUpdate += x =>
					{
						if (x.Age < Form)
						{
							// Fading in where it stands, so the opening can be read before anything moves
							x.Alpha = Math.Min(1f, x.Age / (Form * 0.6f));
							return;
						}
						if (x.Age == Form)
						{
							x.Harmful = true;
							x.Velocity = -dir * Speed;
						}
						// Fades out as it nears the middle instead of piling up there for the next ring
						float d = Vector2.Distance(x.Position, center);
						if (d < FadeFrom)
						{
							x.Alpha = MathHelper.Clamp((d - GoneAt) / (FadeFrom - GoneAt), 0f, 1f);
							x.Harmful = d > GoneAt + 6f;
						}
						if (d <= GoneAt)
							x.Dead = true;
					};
					if (fromGap == gapSize || fromGap == count - 1)
					{
						var draw = drop.OnDraw;
						drop.OnDraw = x =>
						{
							DrDraw.Glow(x.Position, 16f, Color.White * (0.55f * x.Alpha));
							draw(x);
						};
					}
					battle.Spawn(drop);
				}
			}
		}
	}
}
