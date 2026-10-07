using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace MercyMode.Battle
{
	// ====================================================================== the Queen Bee's hive
	// Honey that slows the SOUL down, a swarm that moves as one, eggs that hatch, the Queen's dash, and the waggle
	// dance: one bee shows the way, then the swarm flies it.

	public static class Honey
	{
		public static readonly Color Amber = new(255, 185, 40), Deep = new(205, 120, 20), Shine = new(255, 235, 150);

		/// <summary>A round honey blob, drawn soft-edged with a glint.</summary>
		public static void Blob(Vector2 at, float radius, float alpha)
		{
			DrDraw.Ball(at, radius, Deep * (0.75f * alpha));
			DrDraw.Ball(at, radius * 0.82f, Amber * (0.75f * alpha));
			DrDraw.Ball(at + new Vector2(-radius * 0.35f, -radius * 0.35f), radius * 0.22f, Shine * (0.8f * alpha));
		}

		/// <summary>A bullet that steers toward the SOUL for a while, then flies straight.</summary>
		public static Bullet Homing(Bullet b, BattleSystem battle, float speed, float turn, int steer)
		{
			b.OnUpdate += x =>
			{
				if (x.Age > steer)
					return;
				Vector2 want = battle.SoulCenter - x.Position;
				if (want.LengthSquared() < 1f)
					return;
				float a = x.Velocity.LengthSquared() > 0.01f ? x.Velocity.ToRotation() : want.ToRotation();
				a += MathHelper.Clamp(MathHelper.WrapAngle(want.ToRotation() - a), -turn, turn);
				x.Velocity = a.ToRotationVector2() * speed;
			};
			return b;
		}
	}

	/// <summary>Honey puddles spread over the box: in them the SOUL wades at under half speed. Bees come for you.</summary>
	public class HoneyPuddles : EnemyAttack
	{
		public int Count = 3;
		public float Radius = 28f, Drift = 0f;
		private readonly List<Vector2> spots = new();

		public override void Update(BattleSystem battle, int tick)
		{
			Rectangle box = battle.Box;
			if (tick == 1)
			{
				for (int i = 0; i < Count; i++)
				{
					Vector2 at = new(Main.rand.NextFloat(box.Left + Radius, box.Right - Radius), Main.rand.NextFloat(box.Top + Radius, box.Bottom - Radius));
					spots.Add(at);
					int index = i, duration = Duration;
					float radius = Radius;
					battle.Spawn(new Bullet
					{
						Position = at,
						Harmful = false,
						Lifetime = duration,
						OnDraw = x =>
						{
							float grow = Math.Min(1f, x.Age / 30f) * Math.Min(1f, (duration - x.Age) / 20f);
							Vector2 c = spots[index];
							float wob = (float)Math.Sin(x.Age * 0.08f + index) * 1.5f;
							Honey.Blob(c, (radius + wob) * grow, 0.8f);
						},
					});
				}
				AttackSfx.Vanilla(SoundID.Item86, 0.5f, -0.4f);
			}
			// Slowly sliding about (below half HP)
			if (Drift > 0f)
				for (int i = 0; i < spots.Count; i++)
				{
					Vector2 p = spots[i] + new Vector2((float)Math.Sin(tick * 0.012f + i * 2f), (float)Math.Cos(tick * 0.01f + i * 3f)) * Drift;
					spots[i] = new Vector2(MathHelper.Clamp(p.X, box.Left + Radius * 0.5f, box.Right - Radius * 0.5f), MathHelper.Clamp(p.Y, box.Top + Radius * 0.5f, box.Bottom - Radius * 0.5f));
				}
			if (tick > 20)
				foreach (Vector2 c in spots)
					if (Vector2.Distance(c, battle.SoulCenter) < Radius)
					{
						battle.SlowSoul();
						break;
					}
		}
	}

	/// <summary>
	/// A swarm moving as one: a cloud of bees that drifts after the SOUL, breathing in and out, and now and then
	/// bursting wide before pulling back in. Stay out of the cloud and out of the way of the burst.
	/// </summary>
	public class Swarm : EnemyAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public int Count = 16, BurstEvery = 90;
		public float Speed = 0.6f, Radius = 30f, BurstRadius = 72f;
		private Vector2 centre;
		private float spin, radius;

		public override void Update(BattleSystem battle, int tick)
		{
			Rectangle box = battle.Box;
			if (tick == 1)
			{
				centre = new Vector2(battle.SoulCenter.X < box.Center.X ? box.Right - 30 : box.Left + 30, box.Top + 30);
				radius = Radius;
				for (int i = 0; i < Count; i++)
				{
					float offset = MathHelper.TwoPi * i / Count, wobble = Main.rand.NextFloat(MathHelper.TwoPi);
					int ring = i % 2;
					Bullet b = Make(centre, Vector2.Zero);
					b.Lifetime = Duration;
					b.Alpha = 0f;
					b.DestroyOnHit = false;
					b.OnUpdate += x =>
					{
						x.Alpha = Math.Min(1f, x.Age / 20f);
						x.Harmful = x.Age > 20;
						float r = radius * (ring == 0 ? 1f : 0.6f) + (float)Math.Sin(x.Age * 0.2f + wobble) * 3f;
						float a = offset + spin * (ring == 0 ? 1f : -1.4f);
						Vector2 to = centre + a.ToRotationVector2() * r;
						x.Velocity = to - x.Position;
					};
					battle.Spawn(b);
				}
				AttackSfx.Appear();
			}
			spin += 0.045f;
			// Breathing, with a burst every so often (it swells first, as a warning)
			int t = tick % BurstEvery;
			float rest = Radius + (float)Math.Sin(tick * 0.06f) * 6f;
			float want = t > BurstEvery - 26 ? rest + 8f : t < 14 && tick > BurstEvery ? BurstRadius : rest;
			radius = MathHelper.Lerp(radius, want, t < 14 && tick > BurstEvery ? 0.3f : 0.08f);
			if (t == 0 && tick > 1)
				AttackSfx.Vanilla(SoundID.Item97, 0.5f, 0.3f);
			Vector2 to = battle.SoulCenter - centre;
			if (to.Length() > 2f)
				centre += Vector2.Normalize(to) * Speed;
			centre.X = MathHelper.Clamp(centre.X, box.Left - 20, box.Right + 20);
			centre.Y = MathHelper.Clamp(centre.Y, box.Top - 20, box.Bottom + 20);
		}
	}

	/// <summary>
	/// The Queen charges across the SOUL's row (it flashes first), leaving stingers hanging in the air behind her;
	/// a beat later they fire up and down.
	/// </summary>
	public class RoyalDash : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> MakeQueen, MakeStinger;
		public int Warn = 30, DropEvery = 5, Hang = 22;
		public float Speed = 8f, FireSpeed = 3f;

		public RoyalDash(Func<Vector2, Vector2, Bullet> queen, Func<Vector2, Vector2, Bullet> stinger, int every = 80)
		{
			MakeQueen = queen;
			MakeStinger = stinger;
			Every = every;
			StopBeforeEnd = 70;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			float y = MathHelper.Clamp(battle.SoulCenter.Y, box.Top + 16, box.Bottom - 16);
			bool fromLeft = index % 2 == 0;
			battle.Spawn(Shots.Warning(new Rectangle(box.Left, (int)y - 16, box.Width, 32), Warn));
			Bullet q = MakeQueen(new Vector2(fromLeft ? box.Left - 40 : box.Right + 40, y), new Vector2(fromLeft ? Speed : -Speed, 0f));
			q.StartDelay = Warn;
			q.Lifetime = 140;
			q.DestroyOnHit = false;
			q.FlipX = !fromLeft;
			int dropEvery = DropEvery, hang = Hang;
			float fire = FireSpeed;
			int drops = 0;
			q.OnUpdate += x =>
			{
				if (x.Age % dropEvery != 0 || !box.Contains(x.Position.ToPoint()))
					return;
				int n = drops++;
				Bullet s = MakeStinger(x.Position, Vector2.Zero);
				s.Harmful = false;
				s.Lifetime = 200;
				s.OnUpdate += z =>
				{
					if (z.Age == hang)
					{
						z.Harmful = true;
						z.Velocity = new Vector2(0f, n % 2 == 0 ? fire : -fire);
					}
				};
				battle.Spawn(s);
			};
			battle.Spawn(q);
		}
	}

	/// <summary>Honey eggs stuck around the box pulse, then hatch into bees that come straight for you.</summary>
	public class LarvaHatch : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> MakeBee;
		public int Fuse = 60, Bees = 3;
		public float BeeSpeed = 2f;

		public LarvaHatch(Func<Vector2, Vector2, Bullet> bee, int every = 50)
		{
			MakeBee = bee;
			Every = every;
			StopBeforeEnd = 90;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			// Never right on top of the SOUL
			Vector2 at = box.Center.ToVector2();
			for (int tries = 0; tries < 8; tries++)
			{
				at = new Vector2(Main.rand.NextFloat(box.Left + 14, box.Right - 14), Main.rand.NextFloat(box.Top + 14, box.Bottom - 14));
				if (Vector2.Distance(at, battle.SoulCenter) > 55f)
					break;
			}
			int fuse = Fuse, bees = Bees;
			float speed = BeeSpeed, start = Main.rand.NextFloat(MathHelper.TwoPi);
			battle.Spawn(new Bullet
			{
				Position = at,
				HitSize = new Vector2(14, 14),
				Lifetime = fuse,
				DamageMult = 0.5f,
				DestroyOnHit = false,
				OnDraw = x =>
				{
					float k = x.Age / (float)fuse;
					float pulse = 1f + (float)Math.Sin(x.Age * (0.15f + k * 0.5f)) * (0.08f + 0.12f * k);
					Honey.Blob(x.Position, 8f * pulse * Math.Min(1f, x.Age / 10f), 1f);
					// The larva inside, getting brighter
					DrDraw.Ball(x.Position, 3f * pulse, Color.White * (0.4f + 0.5f * k));
				},
				OnUpdate = x =>
				{
					if (x.Age != fuse - 1)
						return;
					for (int i = 0; i < bees; i++)
					{
						Vector2 dir = (start + MathHelper.TwoPi * i / bees).ToRotationVector2();
						battle.Spawn(Honey.Homing(MakeBee(x.Position, dir * speed), battle, speed, 0.05f, 70));
					}
					AttackSfx.Vanilla(SoundID.NPCDeath1, 0.4f, 0.6f);
				},
			});
		}
	}

	/// <summary>
	/// The waggle dance: one glowing bee flies a shape through the box, leaving a dotted trail that fades; then the
	/// swarm flies the very same path. Remember where it went and stay off it. Below half HP the swarm flies it backwards.
	/// </summary>
	public class WaggleDance : EnemyAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public int Show = 100, Pause = 30, Stream = 26, StreamEvery = 4;
		public bool Reverse;
		public int Rounds = 2;

		public WaggleDance() => Duration = BattleConstants.DefaultEnemyTurnTicks * 4 / 3;

		private int RoundLength => Show + Pause + Show + Stream * StreamEvery / 2;

		public override void Update(BattleSystem battle, int tick)
		{
			int round = (tick - 1) / Math.Max(1, RoundLength);
			if (round >= Rounds || (tick - 1) % RoundLength != 0 || tick > Duration - Show - 40)
				return;
			Rectangle box = battle.Box;
			Vector2 c = box.Center.ToVector2();
			int shape = Main.rand.Next(3);
			float turn = Main.rand.NextFloat(MathHelper.TwoPi), size = box.Width * 0.36f;
			Func<float, Vector2> path = s => c + Shape(shape, s, size).RotatedBy(turn);
			int show = Show, pause = Pause, stream = Stream, every = StreamEvery;
			bool reverse = Reverse;

			// The guide: harmless, glowing, leaving dots
			battle.Spawn(new Bullet
			{
				Position = path(0f),
				Harmful = false,
				Lifetime = show,
				OnUpdate = x =>
				{
					x.Position = path(x.Age / (float)show);
					if (x.Age % 3 == 0)
					{
						Vector2 at = x.Position;
						int born = x.Age;
						battle.Spawn(new Bullet
						{
							Position = at,
							Harmful = false,
							Lifetime = show - born + pause + 30,
							OnDraw = d => DrDraw.Ball(d.Position, 2f, Honey.Amber * Math.Max(0f, 1f - d.Age / (float)(show - born + pause + 30))),
						});
					}
				},
				OnDraw = x =>
				{
					DrDraw.Glow(x.Position, 14f, Honey.Shine * 0.7f);
					DrDraw.Ball(x.Position, 4f, Color.White);
				},
			});
			AttackSfx.Vanilla(SoundID.Item35, 0.5f, 0.6f);
			// Then the swarm flies it
			for (int i = 0; i < stream; i++)
			{
				Bullet b = Make(path(reverse ? 1f : 0f), Vector2.Zero);
				b.StartDelay = show + pause + i * every;
				b.Lifetime = show + 2;
				b.DestroyOnHit = false;
				b.OnUpdate += x =>
				{
					float s = Math.Min(1f, x.Age / (float)show);
					Vector2 next = path(reverse ? 1f - s : s);
					x.Velocity = next - x.Position;
				};
				battle.Spawn(b);
			}
		}

		/// <summary>The dance's shapes, for s from 0 to 1: a figure of eight, a zigzag run, and a spiral in.</summary>
		private static Vector2 Shape(int shape, float s, float size)
		{
			float t = s * MathHelper.TwoPi;
			switch (shape)
			{
				case 0:
				{
					float d = 1f + (float)Math.Sin(t) * (float)Math.Sin(t);
					return new Vector2((float)Math.Cos(t) / d, (float)Math.Sin(t) * (float)Math.Cos(t) / d) * size * 1.15f;
				}
				case 1:
					// Out along a wiggling line, round, and back along another
					return s < 0.5f
						? new Vector2(MathHelper.Lerp(-1f, 1f, s * 2f), (float)Math.Sin(s * 2f * MathHelper.TwoPi * 3f) * 0.25f - 0.35f) * size
						: new Vector2(MathHelper.Lerp(1f, -1f, (s - 0.5f) * 2f), (float)Math.Sin((s - 0.5f) * 2f * MathHelper.TwoPi * 3f) * 0.25f + 0.35f) * size;
				default:
				{
					float r = 1f - s * 0.85f;
					return (t * 2f).ToRotationVector2() * r * size;
				}
			}
		}
	}

	/// <summary>Honey rises up the box (in it the SOUL wades at under half speed) while stingers rain down.</summary>
	public class HoneyFlood : EnemyAttack
	{
		/// <summary>How much of the box the honey fills at its highest.</summary>
		public float Fill = 0.5f;
		public int RiseTicks = 120;

		public override void Update(BattleSystem battle, int tick)
		{
			Rectangle box = battle.Box;
			float level = Level(box, tick);
			if (tick == 1)
			{
				int duration = Duration;
				battle.Spawn(new Bullet
				{
					Position = box.Center.ToVector2(),
					Harmful = false,
					Lifetime = duration,
					OnDraw = x =>
					{
						Rectangle b = battle.Box;
						float top = Level(b, x.Age + 1);
						if (top >= b.Bottom - 1)
							return;
						// A wavy surface over a deep amber pool
						for (float px = b.Left; px < b.Right; px += 4f)
						{
							float wave = (float)Math.Sin(px * 0.08f + x.Age * 0.07f) * 2f;
							DrDraw.Rect(px, top + wave, 4f, b.Bottom - top - wave, Honey.Deep * 0.55f);
							DrDraw.Rect(px, top + wave, 4f, 2f, Honey.Shine * 0.7f);
						}
					},
				});
				AttackSfx.Vanilla(SoundID.Item86, 0.6f, -0.6f);
			}
			if (battle.SoulCenter.Y > level)
				battle.SlowSoul();
		}

		private float Level(Rectangle box, int tick)
		{
			float k = Math.Min(1f, tick / (float)RiseTicks) * Math.Min(1f, (Duration - tick) / 30f);
			float bob = (float)Math.Sin(tick * 0.03f) * 6f;
			return box.Bottom - box.Height * Fill * k + bob * k;
		}
	}
}
