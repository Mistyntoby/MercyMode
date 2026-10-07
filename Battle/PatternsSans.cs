using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace MercyMode.Battle
{
	// ====================================================================== Skeletron's skeleton tricks
	// Undertale's skeleton fight, done with Skeletron's bones and skull: blue bones that only hurt if you move, orange
	// ones that only hurt if you don't, the SOUL slammed into the floor, and skulls that charge up and fire beams.

	public static class SansBones
	{
		public static readonly Color Blue = new(20, 170, 255), Orange = new(255, 160, 40), White = new(235, 225, 200);

		/// <summary>A bone of a colour: 1 blue (stand still), 2 orange (keep moving), 0 white (just dodge).</summary>
		public static Bullet Make(Vector2 centre, Vector2 vel, float height, int kind)
		{
			Bullet b = BoneWalls.Bone(centre, vel, height, kind == 1 ? Blue : kind == 2 ? Orange : White);
			b.Sans = kind;
			return b;
		}

		/// <summary>A sideways bone (lying along x) of a length, centred on a point.</summary>
		public static Bullet Lying(Vector2 centre, Vector2 vel, float length, Color? color = null)
		{
			Color c = color ?? White;
			return new Bullet
			{
				Position = centre,
				Velocity = vel,
				HitSize = new Vector2(length, 8f),
				Lifetime = 400,
				DamageMult = 0.8f,
				OnDraw = b =>
				{
					float half = b.HitSize.X / 2f - 4f;
					Vector2 l = b.Position - new Vector2(half, 0f), r = b.Position + new Vector2(half, 0f);
					DrDraw.Line(l, r, 6f, c * b.Alpha);
					DrDraw.Ball(l + new Vector2(0f, -3f), 3.5f, c * b.Alpha);
					DrDraw.Ball(l + new Vector2(0f, 3f), 3.5f, c * b.Alpha);
					DrDraw.Ball(r + new Vector2(0f, -3f), 3.5f, c * b.Alpha);
					DrDraw.Ball(r + new Vector2(0f, 3f), 3.5f, c * b.Alpha);
				},
			};
		}

		/// <summary>
		/// A bone sticking out of whichever side is the floor for this gravity (0 down, 1 left, 2 up, 3 right), sliding
		/// along it from one end: hop it.
		/// </summary>
		public static Bullet AlongFloor(Rectangle box, int gravity, bool fromStart, float speed, float height)
		{
			float s = fromStart ? speed : -speed;
			switch (gravity)
			{
				case 1: return Lying(new Vector2(box.Left + height / 2f, fromStart ? box.Top - 10 : box.Bottom + 10), new Vector2(0f, s), height);
				case 3: return Lying(new Vector2(box.Right - height / 2f, fromStart ? box.Top - 10 : box.Bottom + 10), new Vector2(0f, s), height);
				case 2: return Make(new Vector2(fromStart ? box.Left - 10 : box.Right + 10, box.Top + height / 2f), new Vector2(s, 0f), height, 0);
				default: return Make(new Vector2(fromStart ? box.Left - 10 : box.Right + 10, box.Bottom - height / 2f), new Vector2(s, 0f), height, 0);
			}
		}

		/// <summary>A flashing arrow in the middle of the box pointing the way gravity is about to go.</summary>
		public static Bullet GravityArrow(BattleSystem battle, int gravity, int ticks) => new()
		{
			Harmful = false,
			Lifetime = ticks,
			Position = battle.Box.Center.ToVector2(),
			OnDraw = x =>
			{
				float a = x.Age / 4 % 2 == 0 ? 0.8f : 0.35f;
				Vector2 d = (MathHelper.PiOver2 + gravity * MathHelper.PiOver2).ToRotationVector2();
				Vector2 side = new(-d.Y, d.X);
				Vector2 c = battle.Box.Center.ToVector2();
				DrDraw.Line(c - d * 18f, c + d * 18f, 3f, Blue * a);
				DrDraw.Line(c + d * 18f, c + d * 8f + side * 9f, 3f, Blue * a);
				DrDraw.Line(c + d * 18f, c + d * 8f - side * 9f, 3f, Blue * a);
			},
		};
	}

	/// <summary>
	/// Blue SOUL: the SOUL is slammed into the floor, then bones burst up along the whole floor (a warning strip
	/// first): jump, and keep jumping.
	/// </summary>
	public class FloorBoneWave : RepeatingAttack
	{
		public int Warn = 26, Active = 26;
		public float Height = 28f;

		public FloorBoneWave(int every = 72)
		{
			Every = every;
			StopBeforeEnd = 60;
			FirstAt = 24;
			Soul = SoulMode.Blue;
		}

		public override void Update(BattleSystem battle, int tick)
		{
			if (tick == 1)
				battle.SlamSoul();
			base.Update(battle, tick);
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			int warn = Warn, active = Active;
			float height = Height;
			// The warning: the floor flashes where the bones come up
			battle.Spawn(Shots.Warning(new Rectangle(box.Left, (int)(box.Bottom - height), box.Width, (int)height), warn, SansBones.White));
			for (float x = box.Left + 6; x < box.Right - 2; x += 12f)
			{
				float bx = x;
				Bullet b = SansBones.Make(new Vector2(bx, box.Bottom + height / 2f), Vector2.Zero, height, 0);
				b.StartDelay = warn;
				b.Lifetime = active + 24;
				b.OnUpdate += y =>
				{
					// Up fast, hold, back down
					float k = y.Age < 6 ? y.Age / 6f : y.Age < active ? 1f : Math.Max(0f, 1f - (y.Age - active) / 8f);
					y.Position.Y = box.Bottom + height / 2f - height * k;
					y.Harmful = k > 0.5f;
				};
				battle.Spawn(b);
			}
			AttackSfx.Vanilla(SoundID.Item71, 0.4f, 0.5f);
		}
	}

	/// <summary>
	/// Blue SOUL: tall bones sweep across the box, blue (stand still and they pass through you) and orange (keep moving
	/// and they pass), with a short white one to hop now and then.
	/// </summary>
	public class ColoredBones : RepeatingAttack
	{
		public float Speed = 2.6f;

		public ColoredBones(int every = 40)
		{
			Every = every;
			StopBeforeEnd = 70;
			FirstAt = 20;
			Soul = SoulMode.Blue;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			bool fromLeft = index % 2 == 0;
			float x = fromLeft ? box.Left - 10 : box.Right + 10;
			var vel = new Vector2(fromLeft ? Speed : -Speed, 0f);
			if (index % 3 == 2)
			{
				// A short white one: jump it
				float h = Main.rand.NextFloat(18f, 30f);
				battle.Spawn(SansBones.Make(new Vector2(x, box.Bottom - h / 2f), vel, h, 0));
				return;
			}
			int kind = Main.rand.NextBool() ? 1 : 2;
			float height = box.Height - 4f;
			battle.Spawn(SansBones.Make(new Vector2(x, box.Center.Y), vel, height, kind));
		}
	}

	/// <summary>
	/// Skulls slide in round the box, turn to the SOUL, glow while they charge, then fire a wide beam straight
	/// through it, and leave.
	/// </summary>
	public class SkullBlasters : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public int Charge = 34, Fire = 22;
		public float Width = 22f;
		/// <summary>Round the box in turn instead of at random: each one this many radians on from the last (0 = random).</summary>
		public float Sweep;
		private float sweepStart = float.NaN;

		public SkullBlasters(Func<Vector2, Vector2, Bullet> make, int every = 50)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 70;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			Vector2 c = box.Center.ToVector2();
			if (float.IsNaN(sweepStart))
				sweepStart = Main.rand.NextFloat(MathHelper.TwoPi);
			float ang = Sweep != 0f ? sweepStart + index * Sweep : Main.rand.NextFloat(MathHelper.TwoPi);
			float reach = Math.Max(box.Width, box.Height) / 2f + 34f;
			Vector2 spot = c + ang.ToRotationVector2() * reach;
			Vector2 from = c + ang.ToRotationVector2() * (reach + 90f);
			Vector2 target = battle.SoulCenter;
			Vector2 dir = Vector2.Normalize(target - spot);
			int charge = Charge, fire = Fire;
			float width = Width;
			Bullet skull = Make(from, Vector2.Zero);
			skull.Harmful = false;
			skull.Lifetime = 20 + charge + fire + 20;
			skull.OffscreenMargin = 9999f;
			skull.DestroyOnHit = false;
			skull.RotateWithVelocity = false;
			skull.Rotation = dir.ToRotation() - MathHelper.PiOver2;
			skull.Alpha = 0f;
			Vector2 end = spot + dir * 900f;
			skull.OnUpdate += x =>
			{
				int t = x.Age;
				x.Velocity = Vector2.Zero;
				if (t < 20)
				{
					float k = t / 20f;
					x.Position = Vector2.Lerp(from, spot, 1f - (1f - k) * (1f - k));
					x.Alpha = k;
					return;
				}
				if (t < 20 + charge)
				{
					x.Position = spot + Main.rand.NextVector2Circular(1f, 1f);
					if (t == 20)
						AttackSfx.Vanilla(SoundID.Item15, 0.5f, -0.4f);
					return;
				}
				if (t == 20 + charge)
				{
					x.Harmful = true;
					battle.ShakeScreen(3f);
					AttackSfx.Vanilla(SoundID.Item33 with { Pitch = -0.6f }, 0.7f);
				}
				if (t >= 20 + charge + fire)
				{
					// Away it goes
					x.Harmful = false;
					x.Position = Vector2.Lerp(x.Position, from, 0.12f);
					x.Alpha = Math.Max(0f, x.Alpha - 0.06f);
				}
			};
			// Only the beam hurts, not the skull
			skull.HitTest = (x, soul) =>
			{
				if (!x.Harmful)
					return false;
				Vector2 p = soul.Center.ToVector2();
				Vector2 ab = end - spot;
				float tt = MathHelper.Clamp(Vector2.Dot(p - spot, ab) / ab.LengthSquared(), 0f, 1f);
				return Vector2.Distance(p, spot + ab * tt) < width / 2f + soul.Width / 2f - 2f;
			};
			skull.OnDraw = x =>
			{
				int t = x.Age;
				if (t >= 20 && t < 20 + charge)
				{
					// Charging: a thin aim line and a growing glow in its mouth
					float k = (t - 20) / (float)charge;
					DrDraw.Line(spot, end, 1f, Color.White * (0.25f + 0.25f * k));
					DrDraw.Glow(spot + dir * 10f, 6f + 10f * k, Color.White * (0.4f + 0.4f * k));
				}
				else if (t >= 20 + charge && t < 20 + charge + fire)
				{
					float k = 1f - (t - 20 - charge) / (float)fire;
					DrDraw.Line(spot, end, width + 6f, Color.White * (0.35f * k + 0.2f));
					DrDraw.Line(spot, end, width, Color.White * (0.6f * k + 0.4f));
				}
				x.DrawSprite();
			};
			battle.Spawn(skull);
		}
	}

	/// <summary>
	/// Blue SOUL: gravity flips between the floor and the ceiling (an arrow flashes first), slamming the SOUL into its
	/// new floor, and bones sweep along whichever side is down.
	/// </summary>
	public class GravityFlip : EnemyAttack
	{
		public int FlipEvery = 100, Warn = 30, BoneEvery = 30;
		public float Speed = 2.6f;
		/// <summary>Gravity can go any of the four ways, not just floor and ceiling.</summary>
		public bool AllWays;
		private int next = -1;

		public GravityFlip() => Soul = SoulMode.Blue;

		public override void Update(BattleSystem battle, int tick)
		{
			Rectangle box = battle.Box;
			if (tick == 1)
				battle.SlamSoul();
			int t = tick % FlipEvery;
			if (tick > 20 && t == FlipEvery - Warn && tick < Duration - 60)
			{
				// Where it goes next: the other way, or (all ways) any other side
				next = AllWays ? (battle.Gravity + Main.rand.Next(1, 4)) % 4 : battle.Gravity == 2 ? 0 : 2;
				battle.Spawn(SansBones.GravityArrow(battle, next, Warn));
			}
			if (tick > 20 && t == 0 && tick < Duration - 60 && next >= 0)
			{
				battle.SetGravity(next);
				next = -1;
				AttackSfx.Impact();
			}
			// Low bones along the side that's down, to hop
			if (tick > 30 && tick % BoneEvery == 0 && tick < Duration - 50)
			{
				bool fromStart = tick / BoneEvery % 2 == 0;
				battle.Spawn(SansBones.AlongFloor(box, battle.Gravity, fromStart, Speed, Main.rand.NextFloat(16f, 28f)));
			}
		}
	}

	/// <summary>Both hands rush in from the sides at the SOUL's height and clap together (its lane flashes first).</summary>
	public class HandClap : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> MakeHand;
		public int Warn = 30;
		public float Speed = 6f;

		public HandClap(Func<Vector2, Vector2, Bullet> hand, int every = 60)
		{
			MakeHand = hand;
			Every = every;
			StopBeforeEnd = 60;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			float y = MathHelper.Clamp(battle.SoulCenter.Y, box.Top + 14, box.Bottom - 14);
			const int lane = 30;
			battle.Spawn(Shots.Warning(new Rectangle(box.Left, (int)(y - lane / 2f), box.Width, lane), Warn));
			float speed = Speed;
			int warn = Warn;
			foreach (int side in new[] { -1, 1 })
			{
				Bullet h = MakeHand(new Vector2(side < 0 ? box.Left - 30 : box.Right + 30, y), Vector2.Zero);
				h.StartDelay = warn;
				h.Lifetime = 120;
				h.DestroyOnHit = false;
				h.FlipX = side > 0;
				float centre = box.Center.X;
				h.OnUpdate += x =>
				{
					// In to the middle, a clap, then back out
					bool coming = side < 0 ? x.Position.X < centre - 10 : x.Position.X > centre + 10;
					if (x.Age < 2)
						x.Velocity = new Vector2(-side * speed, 0f);
					else if (!coming && x.Velocity.X * -side > 0)
					{
						x.Velocity = new Vector2(side * speed * 0.6f, 0f);
						if (side < 0)
						{
							AttackSfx.Vanilla(SoundID.NPCHit2, 0.8f, -0.3f);
							battle.ShakeScreen(2f);
						}
					}
				};
				battle.Spawn(h);
			}
		}
	}

	/// <summary>
	/// The Dungeon Guardian drifts after the SOUL for the whole turn, slower than you but relentless, while bones fall:
	/// it can't be outrun forever, only kept away from.
	/// </summary>
	public class GuardianChase : EnemyAttack
	{
		public Func<Vector2, Vector2, Bullet> MakeGuardian;
		public float Speed = 1.1f;
		private Bullet guardian;

		public override void Update(BattleSystem battle, int tick)
		{
			Rectangle box = battle.Box;
			if (guardian == null)
			{
				Vector2 c = box.Center.ToVector2();
				Vector2 at = new(battle.SoulCenter.X < c.X ? box.Right - 20 : box.Left + 20, battle.SoulCenter.Y < c.Y ? box.Bottom - 20 : box.Top + 20);
				guardian = MakeGuardian(at, Vector2.Zero);
				guardian.Harmful = false;
				guardian.Alpha = 0f;
				guardian.Lifetime = Duration + 10;
				guardian.DestroyOnHit = false;
				guardian.OffscreenMargin = 9999f;
				guardian.Trail = 4;
				float speed = Speed;
				guardian.OnUpdate += x =>
				{
					x.Alpha = Math.Min(1f, x.Age / 40f);
					x.Harmful = x.Age > 40;
					if (x.Age < 40)
						return;
					// Speeding up very slowly: it never gives up
					x.Velocity = Vector2.Normalize(battle.SoulCenter - x.Position + new Vector2(0.01f, 0f)) * (speed + x.Age * 0.0015f);
				};
				battle.Spawn(guardian);
				AttackSfx.Vanilla(SoundID.Roar, 0.6f, -0.4f);
			}
		}
	}

	/// <summary>Two long bones turn round the middle of the box like propeller blades; stay ahead of them.</summary>
	public class BoneWheel : EnemyAttack
	{
		public int Arms = 2;
		public float Turn = 0.014f;
		private float angle;
		private bool started;

		public override void Update(BattleSystem battle, int tick)
		{
			if (started)
			{
				angle += Turn * Math.Min(1f, tick / 60f);
				return;
			}
			started = true;
			angle = (battle.SoulCenter - battle.Box.Center.ToVector2()).ToRotation() + MathHelper.PiOver2;
			int arms = Arms, duration = Duration;
			for (int i = 0; i < arms; i++)
			{
				float offset = MathHelper.TwoPi * i / arms;
				battle.Spawn(new Bullet
				{
					Position = battle.Box.Center.ToVector2(),
					Lifetime = duration - 10,
					DestroyOnHit = false,
					Harmful = false,
					OnUpdate = x => x.Harmful = x.Age > 40,
					HitTest = (x, soul) =>
					{
						Rectangle b = battle.Box;
						Vector2 c = b.Center.ToVector2(), tip = c + (angle + offset).ToRotationVector2() * b.Width;
						Vector2 p = soul.Center.ToVector2(), ab = tip - c;
						float tt = MathHelper.Clamp(Vector2.Dot(p - c, ab) / ab.LengthSquared(), 0.12f, 1f);
						return Vector2.Distance(p, c + ab * tt) < 4f + soul.Width / 2f - 2f;
					},
					OnDraw = x =>
					{
						Rectangle b = battle.Box;
						Vector2 c = b.Center.ToVector2(), dir = (angle + offset).ToRotationVector2();
						float a = Math.Min(1f, x.Age / 40f) * (x.Age < 40 && x.Age / 4 % 2 == 0 ? 0.5f : 1f);
						Vector2 from = c + dir * (b.Width * 0.12f), to = c + dir * b.Width * 0.75f;
						DrDraw.Line(from, to, 6f, SansBones.White * a);
						DrDraw.Ball(from, 4f, SansBones.White * a);
						DrDraw.Ball(to, 4f, SansBones.White * a);
					},
				});
			}
			// The hub
			battle.Spawn(new Bullet { Position = battle.Box.Center.ToVector2(), Harmful = false, Lifetime = duration - 10, OnDraw = x => DrDraw.Ball(battle.Box.Center.ToVector2(), 6f, SansBones.White * Math.Min(1f, x.Age / 40f)) });
		}
	}

	/// <summary>A square of bones closes in round the SOUL with one side missing: get out through the gap in time.</summary>
	public class BoneCage : RepeatingAttack
	{
		public int Close = 80;
		public float Start = 70f;

		public BoneCage(int every = 120)
		{
			Every = every;
			StopBeforeEnd = 100;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Vector2 c = battle.SoulCenter;
			int open = Main.rand.Next(4), close = Close;
			float start = Start;
			AttackSfx.Appear();
			for (int side = 0; side < 4; side++)
			{
				if (side == open)
					continue;
				int s = side;
				battle.Spawn(new Bullet
				{
					Position = c,
					Lifetime = close + 30,
					DestroyOnHit = false,
					Harmful = false,
					OnUpdate = x => x.Harmful = x.Age > 12,
					HitTest = (x, soul) => Wall(c, s, Half(x.Age, close, start)).Intersects(soul),
					OnDraw = x =>
					{
						Rectangle w = Wall(c, s, Half(x.Age, close, start));
						float a = Math.Min(1f, x.Age / 12f) * Math.Min(1f, (close + 30 - x.Age) / 10f);
						DrDraw.Rect(w.X, w.Y, w.Width, w.Height, SansBones.White * a);
					},
				});
			}
		}

		/// <summary>Half the cage's size: closing in from start to 8 over the time it takes.</summary>
		private static float Half(int age, int close, float start) => MathHelper.Lerp(start, 8f, MathHelper.Clamp(age / (float)close, 0f, 1f));

		private static Rectangle Wall(Vector2 c, int side, float half) => side switch
		{
			0 => new Rectangle((int)(c.X - half), (int)(c.Y - half - 3), (int)(half * 2), 6),
			1 => new Rectangle((int)(c.X + half - 3), (int)(c.Y - half), 6, (int)(half * 2)),
			2 => new Rectangle((int)(c.X - half), (int)(c.Y + half - 3), (int)(half * 2), 6),
			_ => new Rectangle((int)(c.X - half - 3), (int)(c.Y - half), 6, (int)(half * 2)),
		};
	}

	/// <summary>Bones shoot out of the left and right walls at different heights (each lane flashes first), then pull back.</summary>
	public class SpikeWalls : RepeatingAttack
	{
		public int Warn = 24, Hold = 20;
		public float Reach = 0.62f;

		public SpikeWalls(int every = 26)
		{
			Every = every;
			StopBeforeEnd = 60;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			bool left = index % 2 == 0;
			float y = Main.rand.NextFloat(box.Top + 10, box.Bottom - 10);
			int warn = Warn, hold = Hold;
			float reach = box.Width * Reach;
			const int thick = 10;
			battle.Spawn(Shots.Warning(new Rectangle(left ? box.Left : (int)(box.Right - reach), (int)(y - thick / 2f), (int)reach, thick), warn));
			battle.Spawn(new Bullet
			{
				Position = new Vector2(left ? box.Left : box.Right, y),
				StartDelay = warn,
				Lifetime = hold + 16,
				DestroyOnHit = false,
				HitTest = (x, soul) => Spike(box, left, y, Len(x.Age, hold, reach), thick).Intersects(soul),
				OnDraw = x =>
				{
					Rectangle r = Spike(box, left, y, Len(x.Age, hold, reach), thick);
					DrDraw.Rect(r.X, r.Y + 2, r.Width, r.Height - 4, SansBones.White);
					float tipX = left ? r.Right : r.Left;
					DrDraw.Ball(new Vector2(tipX, y), 5f, SansBones.White);
				},
			});
		}

		private static float Len(int age, int hold, float reach) =>
			age < 6 ? reach * age / 6f : age < 6 + hold ? reach : Math.Max(0f, reach * (1f - (age - 6 - hold) / 10f));

		private static Rectangle Spike(Rectangle box, bool left, float y, float len, int thick) =>
			new(left ? box.Left : (int)(box.Right - len), (int)(y - thick / 2f), (int)len, thick);
	}

	/// <summary>
	/// Blue SOUL, Undertale's platform bit: rows of platforms drift across the box, the floor fills with bones, and
	/// you have to hop from platform to platform while bones skim along the lower row.
	/// </summary>
	public class BonePlatforms : EnemyAttack
	{
		public float LowSpeed = 1.2f, HighSpeed = 1.4f, SkimSpeed = 2.2f;
		public int LowEvery = 70, HighEvery = 80, SkimEvery = 55, FloorAt = 70;
		public const float Width = 52f;

		public BonePlatforms() => Soul = SoulMode.Blue;

		public static Bullet Make(Vector2 centre, Vector2 vel, float width) => new()
		{
			Position = centre,
			Velocity = vel,
			HitSize = new Vector2(width, 6f),
			Harmful = false,
			Platform = true,
			DestroyOnHit = false,
			Lifetime = 600,
			OnDraw = b =>
			{
				float l = b.Position.X - b.HitSize.X / 2f, t = b.Position.Y - 3f;
				DrDraw.Rect(l, t, b.HitSize.X, 6f, new Color(20, 60, 20) * b.Alpha);
				DrDraw.Rect(l, t, b.HitSize.X, 2f, Color.White * b.Alpha);
				DrDraw.Rect(l, t + 4f, b.HitSize.X, 2f, new Color(60, 200, 60) * b.Alpha);
				DrDraw.Rect(l, t, 2f, 6f, Color.White * b.Alpha);
				DrDraw.Rect(l + b.HitSize.X - 2f, t, 2f, 6f, Color.White * b.Alpha);
			},
		};

		private static float LowTop(Rectangle box) => box.Bottom - 42f;
		private static float HighTop(Rectangle box) => box.Bottom - 84f;

		public override void Update(BattleSystem battle, int tick)
		{
			Rectangle box = battle.Box;
			float low = LowTop(box) + 3f, high = HighTop(box) + 3f;
			if (tick == 1)
			{
				battle.SlamSoul();
				// Already a row going by, so there's somewhere to jump to before the floor fills
				for (float x = box.Left + Width / 2f; x < box.Right; x += LowSpeed * LowEvery)
					battle.Spawn(Make(new Vector2(x, low), new Vector2(LowSpeed, 0f), Width));
			}
			if (tick > 1 && tick % LowEvery == 0 && tick < Duration - 40)
				battle.Spawn(Make(new Vector2(box.Left - Width / 2f, low), new Vector2(LowSpeed, 0f), Width));
			if (tick % HighEvery == 20 && tick < Duration - 40)
				battle.Spawn(Make(new Vector2(box.Right + Width / 2f, high), new Vector2(-HighSpeed, 0f), Width - 6f));
			// The floor flashes, then fills with bones for the rest of the turn
			if (tick == FloorAt - 30)
				battle.Spawn(Shots.Warning(new Rectangle(box.Left, box.Bottom - 16, box.Width, 16), 30, SansBones.White));
			if (tick == FloorAt)
			{
				int left = Duration - FloorAt;
				for (float x = box.Left + 6; x < box.Right - 2; x += 12f)
				{
					Bullet b = SansBones.Make(new Vector2(x, box.Bottom - 8f), Vector2.Zero, 16f, 0);
					b.Lifetime = left;
					b.DestroyOnHit = false;
					battle.Spawn(b);
				}
				AttackSfx.Vanilla(SoundID.Item71, 0.4f, 0.5f);
			}
			// Bones skimming along the lower row: hop them, or get up to the high row
			if (tick > FloorAt + 20 && tick % SkimEvery == 0 && tick < Duration - 50)
			{
				float h = 20f;
				battle.Spawn(SansBones.Make(new Vector2(box.Right + 10, LowTop(box) - h / 2f), new Vector2(-SkimSpeed, 0f), h, 0));
			}
		}
	}

	/// <summary>
	/// Blue SOUL, the big finish. Bones burst out of the floor, then gravity swings right and the SOUL falls: the box
	/// stretches out past both edges of the screen, the camera follows the SOUL (it stays put, only moving up and
	/// down) and everything else rushes left: the hero and Skeletron slide away, bone walls with a gap fly at you,
	/// skulls cross your lane, and Skeletron keeps teleporting in behind the box to watch you go by. At last the
	/// box's right wall comes into view, the SOUL slams into it, and everything comes back.
	/// Every position the attack plays by is fixed (not the screen's width), so a party all sees the same thing.
	/// </summary>
	public class SideFall : EnemyAttack
	{
		public Func<Vector2, Vector2, Bullet> MakeSkull;
		public int FloorFor = 120, FallFor = 620, WallEvery = 56, SkullEvery = 80, Watch = 150;
		public float TopSpeed = 6f, Gap = 48f;
		/// <summary>Where things in the fall start: past the right edge of even a very wide screen.</summary>
		private const float FarRight = 1000f;
		/// <summary>The stretched box's left and right: off the screen either side however wide it is.</summary>
		private const int WideLeft = -1000, WideRight = 1700;
		private readonly FloorBoneWave floor = new(50) { Height = 26f };
		private float speed, scroll, gapY = -1f, wallX = FarRight + 40f, hold;
		private int fallStart = -1, hitAt = -1;

		public SideFall()
		{
			Soul = SoulMode.Blue;
			Duration = FloorFor + 20 + FallFor + 150;
		}

		/// <summary>The normal box (the stretched one keeps its top and bottom).</summary>
		private static Rectangle Normal => new((int)(BattleConstants.BoxCenterX - BattleConstants.BoxSize / 2f), (int)(BattleConstants.BoxCenterY - BattleConstants.BoxSize / 2f), BattleConstants.BoxSize, BattleConstants.BoxSize);

		public override void Update(BattleSystem battle, int tick)
		{
			Rectangle box = Normal;
			if (tick < FloorFor)
			{
				floor.Duration = FloorFor + 30;
				floor.Update(battle, tick);
			}
			if (tick == FloorFor - 30)
				battle.Spawn(SansBones.GravityArrow(battle, 3, 30));
			if (tick == FloorFor)
			{
				battle.SetGravity(3);
				battle.ShakeScreen(5f);
				AttackSfx.Impact();
			}
			if (tick == FloorFor + 20)
			{
				// It's falling now: hold it where it is and let the camera follow
				fallStart = tick;
				hold = battle.SoulCenter.X - BattleConstants.SoulSize / 2f;
				AttackSfx.Vanilla(Terraria.ID.SoundID.Item24, 0.6f, -0.4f);
			}
			if (fallStart < 0)
				return;
			int f = tick - fallStart;

			if (hitAt < 0)
			{
				// Speeding up, the camera drifting to keep the SOUL in the middle
				speed = TopSpeed * Math.Min(1f, f / 50f);
				hold = MathHelper.Lerp(hold, BattleConstants.BoxCenterX - BattleConstants.SoulSize / 2f, 0.03f);
				battle.HoldSoulX = hold;
				// The wall at the end comes into view and rushes up to the SOUL
				if (f >= FallFor - 120)
					wallX -= speed;
				float stop = hold + BattleConstants.BoxClampHigh;
				if (wallX <= stop)
				{
					wallX = stop;
					hitAt = tick;
					speed = 0f;
					battle.HoldSoulX = null;
					battle.ShakeScreen(7f);
					AttackSfx.Impact();
					AttackSfx.Vanilla(Terraria.ID.SoundID.Item70, 0.7f, -0.2f);
				}
				battle.BoxOverride = new Rectangle(WideLeft, box.Top, (int)Math.Min(WideRight, wallX) - WideLeft, box.Height);
			}
			scroll += speed;
			battle.SceneScroll = scroll;

			if (hitAt < 0)
			{
				// Everything that stays behind slides off to the left
				battle.HeroShift = new Vector2(-Math.Min(scroll, 1500f), 0f);
				UpdateWatcher(battle, f);
				if (f < FallFor - 150)
					SpawnObstacles(battle, box, f);
				return;
			}

			// Landed: the box closes back in, and the camera finds everyone again
			int after = tick - hitAt;
			if (after == 30)
			{
				battle.BoxOverride = null;
				// They come in from the right, as the scene's last bit of motion
				battle.HeroShift = new Vector2(700f, 0f);
				battle.EnemyShift = new Vector2(700f, 0f);
				battle.EnemyFade = 1f;
			}
			if (after > 30)
			{
				battle.HeroShift *= 0.9f;
				battle.EnemyShift *= 0.9f;
				if (battle.HeroShift.Length() < 0.5f)
					battle.HeroShift = battle.EnemyShift = Vector2.Zero;
			}
			if (after == 50)
				battle.SetGravity(0);
		}

		/// <summary>
		/// Skeletron: first it slides away with everything else, then it keeps teleporting in behind the box, drifting
		/// along (slower than the fall, so it falls behind) and blinking out again.
		/// </summary>
		private void UpdateWatcher(BattleSystem battle, int f)
		{
			const int leave = 110;
			if (f < leave)
			{
				battle.EnemyShift = new Vector2(-Math.Min(scroll, 1500f), 0f);
				return;
			}
			int c = (f - leave) % Watch, k = (f - leave) / Watch;
			Vector2 home = battle.Encounter?.ScreenCenter ?? new Vector2(500f, 150f);
			if (c == 0)
			{
				// A new spot ahead of the SOUL, a little different each time
				float x = 380f + k % 3 * 70f;
				battle.EnemyShift = new Vector2(x - home.X, (k % 2 == 0 ? -20f : -34f));
				battle.AddEffect(new Shockwave(battle.EnemyScreenNow, new Color(220, 220, 255), 70f));
				AttackSfx.Vanilla(Terraria.ID.SoundID.Item8, 0.6f, -0.3f);
			}
			battle.EnemyShift -= new Vector2(speed * 0.35f, 0f);
			battle.EnemyFade = c < 14 ? c / 14f : c < Watch - 30 ? 1f : c < Watch - 16 ? 1f - (c - (Watch - 30)) / 14f : 0f;
			if (c == Watch - 30)
				battle.AddEffect(new Shockwave(battle.EnemyScreenNow, new Color(220, 220, 255), 50f));
		}

		private void SpawnObstacles(BattleSystem battle, Rectangle box, int f)
		{
			// Walls of bones with a gap that wanders from one wall to the next
			if (f >= 50 && f % WallEvery == 0)
			{
				float half = Gap / 2f;
				gapY = gapY < 0f ? box.Center.Y : gapY + Main.rand.NextFloat(-45f, 45f);
				gapY = MathHelper.Clamp(gapY, box.Top + half + 8f, box.Bottom - half - 8f);
				float top = gapY - half, bottom = gapY + half;
				for (int col = 0; col < 2; col++)
				{
					float x = FarRight + col * 12f;
					Wall(battle, SansBones.Make(new Vector2(x, (box.Top + top) / 2f), Vector2.Zero, top - box.Top, 0));
					Wall(battle, SansBones.Make(new Vector2(x, (bottom + box.Bottom) / 2f), Vector2.Zero, box.Bottom - bottom, 0));
				}
			}
			// A skull flies across a lane, faster than the fall (the lane flashes first)
			if (f >= 90 && f % SkullEvery == 0 && MakeSkull != null)
			{
				float y = Main.rand.NextFloat(box.Top + 16, box.Bottom - 16);
				const int warn = 30;
				battle.Spawn(Shots.Warning(new Rectangle(WideLeft, (int)y - 13, WideRight - WideLeft, 26), warn + 40));
				Bullet sk = MakeSkull(new Vector2(FarRight - 100f, y), new Vector2(-14f, 0f));
				sk.StartDelay = warn;
				sk.Lifetime = 120;
				sk.OffscreenMargin = 800f;
				sk.DestroyOnHit = false;
				battle.Spawn(sk);
				AttackSfx.Appear();
			}
		}

		/// <summary>Something standing still in the world: on screen it moves left as fast as the fall.</summary>
		private void Wall(BattleSystem battle, Bullet b)
		{
			b.Lifetime = 700;
			b.OffscreenMargin = 800f;
			b.DestroyOnHit = false;
			b.OnUpdate += x => x.Velocity.X = -speed;
			battle.Spawn(b);
		}
	}
}
