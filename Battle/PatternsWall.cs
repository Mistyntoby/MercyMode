using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace MercyMode.Battle
{
	// ====================================================================== the Wall of Flesh
	// It's always coming: a strip of flesh eats into the box from the right, a bit more every turn. Its eyes fire from
	// where they really are on the screen (break one and its lasers stop), the Hungry lunge on their tethers, its tongue
	// drags you toward it, and the Underworld joins in with demon scythes and rising lava.

	public static class Flesh
	{
		public static readonly Color Meat = new(150, 30, 50), Dark = new(80, 10, 25), Vein = new(220, 70, 90), Laser = new(255, 80, 200);

		/// <summary>A segment hits a rectangle (the SOUL) if it passes within this far of its middle.</summary>
		public static bool LineHits(Vector2 a, Vector2 b, float width, Rectangle soul)
		{
			Vector2 p = soul.Center.ToVector2(), ab = b - a;
			float t = MathHelper.Clamp(Vector2.Dot(p - a, ab) / Math.Max(1f, ab.LengthSquared()), 0f, 1f);
			return Vector2.Distance(p, a + ab * t) < width / 2f + soul.Width / 2f - 2f;
		}
	}

	/// <summary>
	/// The wall's edge: flesh filling this share of the box from the right for the whole turn, hurting on touch. The
	/// encounter makes it a little deeper every turn (it's advancing on you).
	/// </summary>
	public class FleshEdge : EnemyAttack
	{
		public float Share = 0.1f;

		public override void Update(BattleSystem battle, int tick)
		{
			if (tick != 1 || Share <= 0.01f)
				return;
			float share = Share;
			int duration = Duration;
			battle.Spawn(new Bullet
			{
				Position = battle.Box.Center.ToVector2(),
				Lifetime = duration,
				DestroyOnHit = false,
				OffscreenMargin = 2000f,
				DamageMult = 0.6f,
				OnUpdate = x => x.Harmful = x.Age > 20,
				HitTest = (x, r) => Area(battle.Box, share, x.Age).Intersects(r),
				OnDraw = x =>
				{
					Rectangle a = Area(battle.Box, share, x.Age);
					float k = Math.Min(1f, x.Age / 20f);
					DrDraw.Rect(a.X, a.Y, a.Width, a.Height, Flesh.Meat * k);
					// Its lumpy, pulsing edge and a few veins
					for (int y = a.Top; y < a.Bottom; y += 8)
					{
						float bulge = 3f + (float)Math.Sin(y * 0.3f + x.Age * 0.1f) * 3f;
						DrDraw.Ball(new Vector2(a.Left, y + 4), bulge, Flesh.Meat * k);
						DrDraw.Rect(a.Left + 6, y + 3, a.Width - 6, 1, Flesh.Dark * (0.5f * k));
					}
					DrDraw.Rect(a.Left - 1, a.Top, 2, a.Height, Flesh.Vein * (0.7f * k));
				},
			});
		}

		private static Rectangle Area(Rectangle box, float share, int age)
		{
			float grow = Math.Min(1f, age / 20f);
			int w = (int)(box.Width * share * grow);
			return new Rectangle(box.Right - w, box.Top, w, box.Height);
		}
	}

	/// <summary>Each living eye fires bursts of lasers at the SOUL from where it is on the screen, taking turns.</summary>
	public class EyeLasers : RepeatingAttack
	{
		public Func<List<Vector2>> Eyes;
		public Func<Vector2, Vector2, Bullet> Make;
		public int Burst = 3, BurstGap = 6;
		public float Speed = 4.5f, Spread = 0.12f;

		public EyeLasers(Func<List<Vector2>> eyes, Func<Vector2, Vector2, Bullet> make, int every = 40)
		{
			Eyes = eyes;
			Make = make;
			Every = every;
			StopBeforeEnd = 60;
			FirstAt = 20;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			List<Vector2> eyes = Eyes();
			if (eyes.Count == 0)
				return;
			Vector2 from = eyes[index % eyes.Count];
			int gap = BurstGap;
			float speed = Speed, spread = Spread;
			for (int i = 0; i < Burst; i++)
			{
				float off = (i - (Burst - 1) / 2f) * spread;
				Vector2 dir = (battle.SoulCenter - from).SafeNormalize(-Vector2.UnitX).RotatedBy(off);
				Bullet b = Make(from, dir * speed);
				b.StartDelay = i * gap;
				b.OffscreenMargin = 400f;
				battle.Spawn(b);
			}
			AttackSfx.Vanilla(SoundID.Item33, 0.5f);
		}
	}

	/// <summary>
	/// Each living eye draws a thin aiming line near the SOUL, then fires a wide beam that sweeps across. Two eyes
	/// sweep from opposite sides, crossing.
	/// </summary>
	public class EyeBeams : RepeatingAttack
	{
		public Func<List<Vector2>> Eyes;
		public int Aim = 36, Fire = 26;
		public float Width = 16f, Sweep = 0.35f;

		public EyeBeams(Func<List<Vector2>> eyes, int every = 80)
		{
			Eyes = eyes;
			Every = every;
			StopBeforeEnd = 80;
			FirstAt = 10;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			List<Vector2> eyes = Eyes();
			int aim = Aim, fire = Fire;
			float width = Width;
			for (int e = 0; e < eyes.Count; e++)
			{
				Vector2 from = eyes[e];
				// Starts off to one side of the SOUL and sweeps through it
				float side = e % 2 == 0 ? 1f : -1f;
				float centre = (battle.SoulCenter - from).ToRotation();
				float start = centre - Sweep * side, end = centre + Sweep * side;
				float Angle(int age) => age < aim ? start : MathHelper.Lerp(start, end, Math.Min(1f, (age - aim) / (float)fire));
				battle.Spawn(new Bullet
				{
					Position = from,
					Harmful = false,
					DestroyOnHit = false,
					Lifetime = aim + fire,
					OffscreenMargin = 2000f,
					DamageMult = 1f,
					OnUpdate = x =>
					{
						x.Harmful = x.Age >= aim;
						if (x.Age == aim)
						{
							AttackSfx.Vanilla(SoundID.Item33, 0.7f, -0.4f);
							battle.ShakeScreen(2f);
						}
					},
					HitTest = (x, r) => Flesh.LineHits(from, from + Angle(x.Age).ToRotationVector2() * 900f, width, r),
					OnDraw = x =>
					{
						Vector2 to = from + Angle(x.Age).ToRotationVector2() * 900f;
						if (x.Age < aim)
						{
							float a = x.Age / 3 % 2 == 0 ? 0.6f : 0.25f;
							DrDraw.Line(from, to, 1.5f, Flesh.Laser * a);
							DrDraw.Glow(from, 6f + 10f * x.Age / aim, Flesh.Laser * 0.6f);
						}
						else
						{
							float k = 1f - (x.Age - aim) / (float)fire * 0.4f;
							DrDraw.Line(from, to, width + 8f, Flesh.Laser * (0.35f * k));
							DrDraw.Line(from, to, width, Color.Lerp(Flesh.Laser, Color.White, 0.5f) * k);
						}
					},
				});
			}
		}
	}

	/// <summary>
	/// The Hungry lunge out of the wall on their tethers along a lane (it flashes first), snap at the end of the
	/// tether, then get yanked back in.
	/// </summary>
	public class HungryTethers : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public int Warn = 30, Out = 22, Hold = 10, Back = 24;
		/// <summary>How far across the box they reach (a share of its width).</summary>
		public float ReachMin = 0.6f, ReachMax = 0.85f;

		public HungryTethers(Func<Vector2, Vector2, Bullet> make, int every = 34)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 70;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			// Every other one goes for your row
			float y = index % 2 == 0
				? MathHelper.Clamp(battle.SoulCenter.Y, box.Top + 12, box.Bottom - 12)
				: Main.rand.NextFloat(box.Top + 12, box.Bottom - 12);
			float reach = box.Width * Main.rand.NextFloat(ReachMin, ReachMax);
			Vector2 anchor = new(box.Right + 40, y);
			int warn = Warn, outT = Out, hold = Hold, back = Back;
			battle.Spawn(Shots.Warning(new Rectangle((int)(box.Right - reach), (int)y - 12, (int)reach, 24), warn, Flesh.Vein));
			Bullet h = Make(anchor, Vector2.Zero);
			h.StartDelay = warn;
			h.Lifetime = outT + hold + back;
			h.DestroyOnHit = false;
			h.OffscreenMargin = 400f;
			h.FlipX = false;
			h.OnUpdate += x =>
			{
				float k = x.Age < outT ? 1f - (float)Math.Pow(1f - x.Age / (float)outT, 3) : x.Age < outT + hold ? 1f : 1f - (x.Age - outT - hold) / (float)back;
				Vector2 to = new(anchor.X - 40 - reach * k, y + (float)Math.Sin(x.Age * 0.5f) * 2f);
				x.Velocity = to - x.Position;
				if (x.Age == outT)
					AttackSfx.Vanilla(SoundID.NPCHit1, 0.5f, -0.3f);
			};
			Action<Bullet> draw = h.OnDraw;
			h.OnDraw = x =>
			{
				// The tether back into the wall
				DrDraw.Line(anchor + new Vector2(30, 0), x.Position, 3f, Flesh.Dark);
				DrDraw.Line(anchor + new Vector2(30, 0), x.Position, 1.5f, Flesh.Vein * 0.8f);
				if (draw != null)
					draw(x);
				else
					x.DrawSprite();
			};
			battle.Spawn(h);
		}
	}

	/// <summary>
	/// Its tongue latches onto the SOUL and drags it toward the wall the whole turn: keep pulling away while the
	/// other attacks come.
	/// </summary>
	public class Tongue : EnemyAttack
	{
		public Func<Vector2> Mouth;
		public float Pull = 0.55f;

		public override void Update(BattleSystem battle, int tick)
		{
			int start = 30;
			if (tick == 1)
			{
				int duration = Duration;
				battle.Spawn(new Bullet
				{
					Harmful = false,
					Lifetime = duration,
					OffscreenMargin = 2000f,
					Position = battle.SoulCenter,
					OnDraw = x =>
					{
						Vector2 m = Mouth?.Invoke() ?? new Vector2(battle.Box.Right + 100, battle.Box.Center.Y);
						float k = Math.Min(1f, x.Age / (float)start);
						Vector2 tip = Vector2.Lerp(m, battle.SoulCenter, k);
						// A wobbling tongue, thicker at the mouth
						const int n = 14;
						for (int i = 0; i < n; i++)
						{
							float t0 = i / (float)n, t1 = (i + 1f) / n;
							Vector2 side = Vector2.Normalize(tip - m + new Vector2(0.01f, 0f)).RotatedBy(MathHelper.PiOver2);
							Vector2 a = Vector2.Lerp(m, tip, t0) + side * (float)Math.Sin(t0 * 9f + x.Age * 0.2f) * 4f * (1f - t0);
							Vector2 b = Vector2.Lerp(m, tip, t1) + side * (float)Math.Sin(t1 * 9f + x.Age * 0.2f) * 4f * (1f - t1);
							DrDraw.Line(a, b, 7f - 4f * t0, new Color(200, 70, 110));
						}
					},
				});
				AttackSfx.Vanilla(SoundID.NPCDeath13, 0.5f, 0.2f);
			}
			if (tick > start && tick < Duration - 20)
				battle.PullSoul(new Vector2(Pull, 0f));
		}
	}

	/// <summary>Demon scythes fade in round the box, crawl at first, then speed up through it, spinning.</summary>
	public class Scythes : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public int Count = 3;
		public float Start = 0.3f, Accel = 1.06f, Top = 7f;

		public Scythes(Func<Vector2, Vector2, Bullet> make, int every = 46)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 70;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			float accel = Accel, top = Top;
			float baseAngle = Main.rand.NextFloat(MathHelper.TwoPi);
			for (int i = 0; i < Count; i++)
			{
				Vector2 at = box.Center.ToVector2() + (baseAngle + MathHelper.TwoPi * i / Count).ToRotationVector2() * (box.Width * 0.62f);
				Vector2 dir = (battle.SoulCenter - at).SafeNormalize(Vector2.UnitX);
				Bullet b = Make(at, dir * Start);
				b.Alpha = 0f;
				b.Lifetime = 240;
				b.OffscreenMargin = 200f;
				b.OnUpdate += x =>
				{
					x.Alpha = Math.Min(1f, x.Age / 15f);
					x.Harmful = x.Age > 12;
					if (x.Velocity.Length() < top)
						x.Velocity *= accel;
					x.Rotation += 0.3f;
				};
				battle.Spawn(b);
			}
			AttackSfx.Vanilla(SoundID.Item8, 0.5f, -0.2f);
		}
	}
}
