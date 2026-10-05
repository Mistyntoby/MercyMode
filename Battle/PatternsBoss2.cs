using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace MercyMode.Battle
{
	// ====================================================================== more bosses' signature attacks

	/// <summary>Small drawing helpers for the patterns below.</summary>
	internal static class BossDraw
	{
		/// <summary>A filled circle, in thin rows.</summary>
		public static void Circle(Vector2 c, float r, Color color)
		{
			for (float y = -r; y < r; y += 2f)
			{
				float w = (float)Math.Sqrt(Math.Max(0f, r * r - (y + 1f) * (y + 1f)));
				DrDraw.Rect(c.X - w, c.Y + y, w * 2f, 2f, color);
			}
		}

		/// <summary>A jagged bolt from a to b (it re-jags every couple of ticks).</summary>
		public static void Lightning(Vector2 a, Vector2 b, int seed, float width, Color color)
		{
			var rand = new Random(seed);
			Vector2 d = b - a;
			Vector2 n = new Vector2(-d.Y, d.X).SafeNormalize(Vector2.Zero);
			const int segs = 10;
			Vector2 prev = a;
			for (int i = 1; i <= segs; i++)
			{
				float t = i / (float)segs;
				Vector2 next = i == segs ? b : a + d * t + n * (float)(rand.NextDouble() * 2 - 1) * 9f;
				DrDraw.Line(prev, next, width + 3f, color * 0.4f);
				DrDraw.Line(prev, next, width, Color.White);
				prev = next;
			}
		}
	}

	/// <summary>
	/// The Eater of Worlds burrows: a rumbling mound travels under the box toward the SOUL, stops, shakes, and the worm
	/// erupts there in a burst of dirt.
	/// </summary>
	public class BurrowTrail : RepeatingAttack
	{
		public float Speed = 1.5f, Turn = 0.06f;
		public int Travel = 54, Shake = 18;
		public int Shards = 8;
		public Func<Vector2, Vector2, Bullet> Dirt;

		public BurrowTrail(Func<Vector2, Vector2, Bullet> dirt, int every = 80)
		{
			Dirt = dirt;
			Every = every;
			StopBeforeEnd = 90;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			Vector2 pos = new(index % 2 == 0 ? box.Left + 10 : box.Right - 10, Main.rand.NextFloat(box.Top + 20, box.Bottom - 20));
			Vector2 vel = Vector2.Normalize(battle.SoulCenter - pos) * Speed;
			int travel = Travel, shake = Shake, shards = Shards;
			float turn = Turn;
			var dirt = Dirt;
			var brown = new Color(150, 100, 60);
			battle.Spawn(new Bullet
			{
				Position = pos,
				Harmful = false,
				DestroyOnHit = false,
				Lifetime = travel + shake + 16,
				OffscreenMargin = 2000f,
				DamageMult = 0.9f,
				OnUpdate = x =>
				{
					if (x.Age < travel)
					{
						// Steers toward the SOUL, a little at a time
						float want = (battle.SoulCenter - x.Position).ToRotation();
						float now = x.Velocity.ToRotation();
						x.Velocity = (now + MathHelper.Clamp(MathHelper.WrapAngle(want - now), -turn, turn)).ToRotationVector2() * x.Velocity.Length();
						Rectangle b = battle.Box;
						x.Position.X = MathHelper.Clamp(x.Position.X, b.Left + 8, b.Right - 8);
						x.Position.Y = MathHelper.Clamp(x.Position.Y, b.Top + 8, b.Bottom - 8);
					}
					else if (x.Age == travel)
					{
						x.Velocity = Vector2.Zero;
						AttackSfx.Vanilla(SoundID.WormDig, 0.8f);
					}
					else if (x.Age == travel + shake)
					{
						x.Harmful = true;
						AttackSfx.Vanilla(SoundID.Roar, 0.6f, 0.3f);
						battle.ShakeScreen(3);
						for (int i = 0; i < shards; i++)
						{
							float a = MathHelper.TwoPi * i / shards + Main.rand.NextFloat(-0.15f, 0.15f);
							battle.Spawn(dirt(x.Position, a.ToRotationVector2() * Main.rand.NextFloat(1.8f, 2.6f)));
						}
					}
				},
				HitTest = (x, r) => x.Harmful && Vector2.Distance(r.Center.ToVector2(), x.Position) < 16f,
				OnDraw = x =>
				{
					if (x.Age < travel + shake)
					{
						// The mound: dirt specks, shaking harder once it stops
						float jitter = x.Age >= travel ? 2f : 0.8f;
						for (int i = 0; i < 6; i++)
						{
							Vector2 o = new((float)Math.Sin(x.Age * 0.9f + i * 2.1f) * 7f, (float)Math.Cos(x.Age * 0.7f + i * 1.7f) * 4f);
							o += new Vector2(Main.rand.NextFloat(-jitter, jitter), Main.rand.NextFloat(-jitter, jitter));
							DrDraw.Rect(x.Position.X + o.X - 2, x.Position.Y + o.Y - 2, 4, 4, brown);
						}
						return;
					}
					float life = (x.Age - travel - shake) / 16f;
					BossDraw.Circle(x.Position, 16f * (1f - life * 0.5f), brown * (1f - life));
				},
			});
		}
	}

	/// <summary>
	/// The Destroyer's probes scan the box: a line sweeps across; where it finds the SOUL it marks the spot, and a
	/// cross of lasers strikes there a moment later.
	/// </summary>
	public class ProbeScan : RepeatingAttack
	{
		public int SweepTicks = 56, StrikeWarn = 22, StrikeActive = 12;
		public Color ScanColor = new(120, 255, 140), StrikeColor = new(255, 60, 60);

		public ProbeScan(int every = 96)
		{
			Every = every;
			StopBeforeEnd = 100;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			bool vertical = index % 2 == 1;
			int sweep = SweepTicks, warn = StrikeWarn, active = StrikeActive;
			Color scan = ScanColor, strike = StrikeColor;
			bool found = false;
			AttackSfx.Vanilla(SoundID.Item93, 0.5f);
			battle.Spawn(new Bullet
			{
				Position = battle.Box.Center.ToVector2(),
				Harmful = false,
				DestroyOnHit = false,
				Lifetime = sweep,
				OffscreenMargin = 2000f,
				OnUpdate = x =>
				{
					Rectangle box = battle.Box;
					float t = x.Age / (float)sweep;
					float line = vertical ? box.Left + box.Width * t : box.Top + box.Height * t;
					float soul = vertical ? battle.SoulCenter.X : battle.SoulCenter.Y;
					if (!found && Math.Abs(line - soul) < 3f)
					{
						// Found it: lock on and strike that spot
						found = true;
						Vector2 at = battle.SoulCenter;
						AttackSfx.Vanilla(SoundID.Item92, 0.7f);
						void fire()
						{
							AttackSfx.Vanilla(SoundID.Item33, 0.8f);
							battle.ShakeScreen(2);
						}
						battle.Spawn(Beam.Make(battle, at, 0f, warn, active, 9f, strike, 0.8f, fire));
						battle.Spawn(Beam.Make(battle, at, MathHelper.PiOver2, warn, active, 9f, strike, 0.8f, null));
					}
				},
				OnDraw = x =>
				{
					Rectangle box = battle.Box;
					float t = x.Age / (float)sweep;
					if (vertical)
					{
						float lx = box.Left + box.Width * t;
						DrDraw.Rect(lx - 1, box.Top, 2, box.Height, scan);
						DrDraw.Rect(lx - 6, box.Top, 5, box.Height, scan * 0.2f);
					}
					else
					{
						float ly = box.Top + box.Height * t;
						DrDraw.Rect(box.Left, ly - 1, box.Width, 2, scan);
						DrDraw.Rect(box.Left, ly - 6, box.Width, 5, scan * 0.2f);
					}
				},
			});
		}
	}

	/// <summary>
	/// Skeletron Prime's guillotine: the box is split into lanes; a couple flash, then saws drop straight down them.
	/// Never every lane at once, and the next drop always leaves a lane open next to the last safe one.
	/// </summary>
	public class Guillotine : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Saw;
		public int Lanes = 5, Dropping = 2, Warn = 34;
		public float FallSpeed = 8f;
		public Color Warning = new(255, 80, 60);

		public Guillotine(Func<Vector2, Vector2, Bullet> saw, int every = 46)
		{
			Saw = saw;
			Every = every;
			StopBeforeEnd = 60;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			float laneW = box.Width / (float)Lanes;
			var picks = Enumerable.Range(0, Lanes).OrderBy(_ => Main.rand.Next()).Take(Math.Min(Dropping, Lanes - 1)).ToList();
			int warn = Warn;
			Color warning = Warning;
			AttackSfx.Appear();
			foreach (int lane in picks)
			{
				float cx = box.Left + laneW * (lane + 0.5f);
				var b = new Bullet
				{
					Position = new Vector2(cx, box.Top),
					Harmful = false,
					Lifetime = warn,
					OnDraw = x =>
					{
						float a = (x.Age / 3) % 2 == 0 ? 0.35f : 0.15f;
						DrDraw.Rect(cx - laneW / 2f + 2, battle.Box.Top, laneW - 4, battle.Box.Height, warning * a);
					},
				};
				var saw = Saw;
				float fall = FallSpeed;
				b.OnUpdate = x =>
				{
					if (x.Age == warn - 1)
					{
						Bullet s = saw(new Vector2(cx, battle.Box.Top - 20), new Vector2(0f, fall));
						s.Lifetime = (int)((battle.Box.Height + 60) / fall);
						s.OffscreenMargin = 200f;
						battle.Spawn(s);
						AttackSfx.Vanilla(SoundID.Item22, 0.5f);
					}
				};
				battle.Spawn(b);
			}
		}
	}

	/// <summary>
	/// Plantera sows seeds: they land around the box (harmless while they settle), pulse, then sprout thorns in a
	/// cross that hold for a moment and wither.
	/// </summary>
	public class SeedBombs : RepeatingAttack
	{
		public int Seeds = 3, Settle = 50, Grow = 10, Hold = 26;
		public float ThornLength = 38f;
		public Color Seed = new(140, 200, 60), Thorn = new(230, 120, 200);

		public SeedBombs(int every = 54)
		{
			Every = every;
			StopBeforeEnd = 100;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			int settle = Settle, grow = Grow, hold = Hold;
			float len = ThornLength;
			Color seed = Seed, thorn = Thorn;
			for (int k = 0; k < Seeds; k++)
			{
				Vector2 at = new(Main.rand.NextFloat(box.Left + 14, box.Right - 14), Main.rand.NextFloat(box.Top + 14, box.Bottom - 14));
				// Not right on top of the SOUL
				if (Vector2.Distance(at, battle.SoulCenter) < 26f)
					at += Vector2.Normalize(at - battle.SoulCenter + new Vector2(0.1f, 0f)) * 30f;
				float spin = Main.rand.NextBool() ? 0f : MathHelper.PiOver4;
				bool first = k == 0;
				battle.Spawn(new Bullet
				{
					Position = at,
					Harmful = false,
					DestroyOnHit = false,
					Lifetime = settle + grow + hold + 10,
					DamageMult = 0.8f,
					OnUpdate = x =>
					{
						if (x.Age == settle)
						{
							x.Harmful = true;
							if (first)
								AttackSfx.Vanilla(SoundID.Grass, 1f, -0.3f);
						}
						if (x.Age == settle + grow + hold)
							x.Harmful = false;
					},
					HitTest = (x, r) =>
					{
						if (!x.Harmful)
							return false;
						float l = len * Math.Min(1f, (x.Age - settle) / (float)grow);
						for (int i = 0; i < 4; i++)
						{
							Vector2 d = (spin + i * MathHelper.PiOver2).ToRotationVector2();
							if (Beam.SegmentNear(at, at + d * l, r, 3f))
								return true;
						}
						return false;
					},
					OnDraw = x =>
					{
						if (x.Age < settle)
						{
							float pulse = 3f + (float)Math.Sin(x.Age * 0.4f) * (x.Age > settle - 16 ? 1.5f : 0.6f);
							BossDraw.Circle(at, pulse, seed);
							return;
						}
						float wither = x.Age > settle + grow + hold ? 1f - (x.Age - settle - grow - hold) / 10f : 1f;
						float l = len * Math.Min(1f, (x.Age - settle) / (float)grow);
						for (int i = 0; i < 4; i++)
						{
							Vector2 d = (spin + i * MathHelper.PiOver2).ToRotationVector2();
							DrDraw.Line(at, at + d * l, 4f, thorn * wither);
							DrDraw.Line(at + d * (l - 6f), at + d * l, 2f, Color.White * wither);
						}
						BossDraw.Circle(at, 4f, seed * wither);
					},
				});
			}
		}
	}

	/// <summary>
	/// Golem's temple traps: spiked balls on chains swing across the box from above, each on its own rhythm.
	/// </summary>
	public class Pendulums : EnemyAttack
	{
		public int Count = 2;
		public float Swing = 0.95f, Speed = 0.045f, BallRadius = 11f;
		public Color Chain = new(150, 140, 120), Ball = new(120, 110, 90);
		private bool spawned;

		public Pendulums() => Duration = BattleConstants.DefaultEnemyTurnTicks;

		public override void Update(BattleSystem battle, int tick)
		{
			if (spawned)
				return;
			spawned = true;
			for (int k = 0; k < Count; k++)
			{
				float fx = (k + 1f) / (Count + 1f);
				float phase = k * MathHelper.Pi * 0.9f;
				float swing = Swing, speed = Speed * (1f + k * 0.18f), radius = BallRadius;
				Color chain = Chain, ball = Ball;
				int fadeIn = 30;
				battle.Spawn(new Bullet
				{
					Harmful = false,
					DestroyOnHit = false,
					Lifetime = Duration - 10,
					OffscreenMargin = 2000f,
					DamageMult = 0.9f,
					OnUpdate = x =>
					{
						Rectangle box = battle.Box;
						Vector2 anchor = new(box.Left + box.Width * fx, box.Top - 20f);
						float length = box.Height * 0.85f;
						float angle = (float)Math.Sin(x.Age * speed + phase) * swing;
						x.Position = anchor + new Vector2((float)Math.Sin(angle), (float)Math.Cos(angle)) * length;
						if (x.Age == fadeIn)
							x.Harmful = true;
						if (x.Age > fadeIn && Math.Abs(angle) < 0.05f && x.Age % 30 < 2)
							AttackSfx.Vanilla(SoundID.Item7, 0.4f);
					},
					HitTest = (x, r) => x.Harmful && Vector2.Distance(r.Center.ToVector2(), x.Position) < radius + 3f,
					OnDraw = x =>
					{
						Rectangle box = battle.Box;
						Vector2 anchor = new(box.Left + box.Width * fx, box.Top - 20f);
						float a = Math.Min(1f, x.Age / (float)fadeIn);
						DrDraw.Line(anchor, x.Position, 2f, chain * a);
						BossDraw.Circle(x.Position, radius, ball * a);
						for (int s = 0; s < 8; s++)
						{
							Vector2 d = (s * MathHelper.PiOver4 + x.Age * 0.05f).ToRotationVector2();
							DrDraw.Line(x.Position + d * radius, x.Position + d * (radius + 5f), 2f, Color.White * a);
						}
					},
				});
			}
		}
	}

	/// <summary>
	/// Duke Fishron's tsunami: a wall of water sweeps across the box with one or two openings in it, then another from
	/// the other side.
	/// </summary>
	public class Tsunami : RepeatingAttack
	{
		public float Speed = 1.5f, Thickness = 26f, GapSize = 46f;
		public int Gaps = 1;
		public Color Water = new(60, 140, 255);

		public Tsunami(int every = 110)
		{
			Every = every;
			StopBeforeEnd = 100;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			int side = index % 2 == 0 ? -1 : 1;
			float x0 = side < 0 ? box.Left - Thickness : box.Right + Thickness;
			float speed = Speed, thick = Thickness, gapSize = GapSize;
			// Openings: one near the SOUL's height (a short walk), any others anywhere
			var gaps = new List<float>();
			float limit = box.Height / 2f - gapSize / 2f - 4f;
			gaps.Add(box.Center.Y + MathHelper.Clamp(battle.SoulCenter.Y - box.Center.Y + Main.rand.NextFloat(-40f, 40f), -limit, limit));
			for (int g = 1; g < Gaps; g++)
				gaps.Add(box.Center.Y + Main.rand.NextFloat(-limit, limit));
			Color water = Water;
			AttackSfx.Vanilla(SoundID.Splash, 1f);
			battle.Spawn(new Bullet
			{
				Position = new Vector2(x0, box.Center.Y),
				Velocity = new Vector2(-side * speed, 0f),
				DestroyOnHit = false,
				Lifetime = (int)((box.Width + thick * 3f) / speed),
				OffscreenMargin = 2000f,
				DamageMult = 0.9f,
				HitTest = (x, r) =>
				{
					Rectangle b = battle.Box;
					if (r.Right < x.Position.X - thick / 2f || r.Left > x.Position.X + thick / 2f)
						return false;
					float cy = r.Center.Y;
					return !gaps.Any(g => Math.Abs(cy - g) < gapSize / 2f - r.Height / 2f);
				},
				OnDraw = x =>
				{
					Rectangle b = battle.Box;
					for (float y = b.Top; y < b.Bottom; y += 4f)
					{
						if (gaps.Any(g => Math.Abs(y + 2f - g) < gapSize / 2f))
							continue;
						float wave = (float)Math.Sin(y * 0.15f + x.Age * 0.3f) * 3f;
						DrDraw.Rect(x.Position.X - thick / 2f + wave, y, thick, 4f, water);
						// Foam on the leading edge
						float lead = side < 0 ? x.Position.X + thick / 2f + wave - 3f : x.Position.X - thick / 2f + wave;
						DrDraw.Rect(lead, y, 3f, 4f, Color.White * 0.8f);
					}
				},
			});
		}
	}

	/// <summary>
	/// The Lunatic Cultist's lightning rods: pairs of rods appear around the box, crackle, then lightning arcs between
	/// each pair for a moment.
	/// </summary>
	public class LightningRods : RepeatingAttack
	{
		public int Pairs = 2, Warn = 40, Active = 16;
		public Color Color = new(140, 220, 255);

		public LightningRods(int every = 64)
		{
			Every = every;
			StopBeforeEnd = 70;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			int warn = Warn, active = Active;
			Color color = Color;
			AttackSfx.Appear();
			for (int k = 0; k < Pairs; k++)
			{
				// Two rods on opposite edges, so the bolt crosses the box
				bool across = Main.rand.NextBool();
				Vector2 a = across
					? new Vector2(box.Left + 4, Main.rand.NextFloat(box.Top + 10, box.Bottom - 10))
					: new Vector2(Main.rand.NextFloat(box.Left + 10, box.Right - 10), box.Top + 4);
				Vector2 b = across
					? new Vector2(box.Right - 4, Main.rand.NextFloat(box.Top + 10, box.Bottom - 10))
					: new Vector2(Main.rand.NextFloat(box.Left + 10, box.Right - 10), box.Bottom - 4);
				bool first = k == 0;
				battle.Spawn(new Bullet
				{
					Position = (a + b) / 2f,
					Harmful = false,
					DestroyOnHit = false,
					Lifetime = warn + active,
					OffscreenMargin = 2000f,
					DamageMult = 0.8f,
					OnUpdate = x =>
					{
						if (x.Age == warn)
						{
							x.Harmful = true;
							if (first)
							{
								AttackSfx.Vanilla(SoundID.Item122, 0.8f);
								battle.ShakeScreen(2);
							}
						}
					},
					HitTest = (x, r) => x.Harmful && Beam.SegmentNear(a, b, r, 4f),
					OnDraw = x =>
					{
						// The rods, sparking more as they charge
						foreach (Vector2 rod in new[] { a, b })
						{
							DrDraw.Rect(rod.X - 3, rod.Y - 3, 6, 6, Color.White);
							if (x.Age < warn && Main.rand.Next(warn) < x.Age)
								DrDraw.Line(rod, rod + Main.rand.NextVector2Circular(8f, 8f), 1f, color);
						}
						if (x.Age < warn)
						{
							// A faint guide line between them
							float t = x.Age / (float)warn;
							DrDraw.Line(a, b, 1f, color * (0.15f + 0.25f * t));
							return;
						}
						BossDraw.Lightning(a, b, x.Age / 2 * 7919 + (int)a.X, 3f, color);
					},
				});
			}
		}
	}

	/// <summary>
	/// The Moon Lord's black hole: it drifts slowly round the box, pulling the SOUL toward it (never harder than the
	/// SOUL can walk away from) and drawing debris in from the edges; touching its core hurts.
	/// </summary>
	public class BlackHole : EnemyAttack
	{
		public float Pull = 0.9f, Core = 10f, Orbit = 34f, DriftSpeed = 0.012f;
		public int DebrisEvery = 14;
		public Func<Vector2, Vector2, Bullet> Debris;
		private Bullet hole;

		public BlackHole(Func<Vector2, Vector2, Bullet> debris)
		{
			Debris = debris;
			Duration = BattleConstants.DefaultEnemyTurnTicks;
		}

		private Vector2 Centre(BattleSystem battle, int tick)
		{
			Vector2 c = battle.Box.Center.ToVector2();
			return c + new Vector2((float)Math.Cos(tick * DriftSpeed), (float)Math.Sin(tick * DriftSpeed * 1.6f)) * Orbit;
		}

		public override void Update(BattleSystem battle, int tick)
		{
			Vector2 at = Centre(battle, tick);
			// Fades in over the first second, so its pull starts gently
			float strength = Math.Min(1f, tick / 60f) * (tick < Duration - 40 ? 1f : Math.Max(0f, (Duration - tick) / 40f));
			if (hole == null)
			{
				float core = Core;
				hole = new Bullet
				{
					Position = at,
					DestroyOnHit = false,
					Lifetime = Duration,
					OffscreenMargin = 2000f,
					DamageMult = 1f,
					Harmful = false,
					HitTest = (x, r) => x.Harmful && Vector2.Distance(r.Center.ToVector2(), x.Position) < core,
					OnDraw = x =>
					{
						float a = Math.Min(1f, x.Age / 30f);
						for (int ring = 4; ring >= 1; ring--)
							BossDraw.Circle(x.Position, core + ring * 5f + (float)Math.Sin(x.Age * 0.2f + ring) * 1.5f, new Color(90, 40, 160) * (0.12f * a));
						BossDraw.Circle(x.Position, core, Color.Black * a);
						for (int s = 0; s < 3; s++)
						{
							float ang = x.Age * 0.12f + s * MathHelper.TwoPi / 3f;
							DrDraw.Line(x.Position + ang.ToRotationVector2() * (core + 2f), x.Position + (ang + 0.6f).ToRotationVector2() * (core + 12f), 2f, new Color(200, 160, 255) * a);
						}
					},
				};
				battle.Spawn(hole);
				AttackSfx.Vanilla(SoundID.Item117, 0.8f);
			}
			hole.Position = at;
			hole.Harmful = tick > 30;
			// The pull: stronger close in
			Vector2 to = at - battle.SoulCenter;
			float dist = Math.Max(12f, to.Length());
			battle.PullSoul(to.SafeNormalize(Vector2.Zero) * Pull * strength * Math.Min(1f, 60f / dist));
			// Debris drawn in from the edges, swallowed at the core
			if (tick % DebrisEvery == 0 && tick < Duration - 60)
			{
				Rectangle box = battle.Box;
				float ang = Main.rand.NextFloat(MathHelper.TwoPi);
				Vector2 from = box.Center.ToVector2() + ang.ToRotationVector2() * 110f;
				Vector2 side = new Vector2(-(at - from).Y, (at - from).X).SafeNormalize(Vector2.Zero) * 1.4f;
				Bullet d = Debris(from, side);
				d.Lifetime = 240;
				d.OffscreenMargin = 200f;
				d.OnUpdate += x =>
				{
					Vector2 pull = Centre(battle, tick + x.Age) - x.Position;
					if (pull.Length() < 8f)
					{
						x.Dead = true;
						return;
					}
					x.Velocity += Vector2.Normalize(pull) * 0.09f;
					if (x.Velocity.Length() > 3.2f)
						x.Velocity = Vector2.Normalize(x.Velocity) * 3.2f;
				};
				battle.Spawn(d);
			}
		}
	}

	/// <summary>
	/// Deltarune's sword throwers (chapter 1's obj_dknight_slasher, from the decompiled code in the mod kit): throwers
	/// hover off the right of the box, bobbing (40 px, ystart + sin(siner / 16) * 40, easing in and out with the
	/// turn), and each throws at the SOUL on its own rhythm: a 6-frame wind-up, the throw on frame 4, then a pause of
	/// 3 frames per thrower. A throw flies at 9 + sin(siner / 10) * 4 px a frame, 5 degrees either way, slower with
	/// more throwers (x0.85 for two, x0.7 for three). Frames are Deltarune's 30 a second, so per tick it's half.
	/// </summary>
	public class Slashers : EnemyAttack
	{
		public Func<Vector2, Vector2, Bullet> Thrown;
		public Func<Vector2, Bullet> Thrower;
		public int Count = 2;
		private readonly List<(Bullet body, float ystart, float siner, int timer, int con, float frame, bool thrown)> throwers = new();
		private float factor;

		public Slashers(Func<Vector2, Vector2, Bullet> thrown, Func<Vector2, Bullet> thrower)
		{
			Thrown = thrown;
			Thrower = thrower;
			Duration = BattleConstants.DefaultEnemyTurnTicks;
		}

		public override void Update(BattleSystem battle, int tick)
		{
			Rectangle box = battle.Box;
			if (throwers.Count == 0)
			{
				for (int i = 0; i < Count; i++)
				{
					float y = box.Top + box.Height * (i + 1f) / (Count + 1f);
					Bullet body = Thrower(new Vector2(box.Right + 36f + (i % 2) * 22f, y));
					body.Harmful = false;
					body.DestroyOnHit = false;
					body.Lifetime = Duration;
					body.OffscreenMargin = 2000f;
					battle.Spawn(body);
					// Staggered, so they don't all throw together
					throwers.Add((body, y, i * 20f, -i * 8, 10, 0f, false));
				}
			}
			// movefactor: eases in while the turn has time left, out in its last 30 frames
			int left = Duration - tick;
			factor = left >= 60 ? Math.Min(1f, factor + 0.05f) : Math.Max(0f, factor - 0.05f);
			for (int i = 0; i < throwers.Count; i++)
			{
				var t = throwers[i];
				t.siner += 0.5f;
				t.body.Position.Y = t.ystart + (float)Math.Sin(t.siner / 16f) * 40f * factor;
				if (left > 30)
				{
					if (t.con == 10)
					{
						t.frame = 0f;
						t.thrown = false;
						t.con = 11;
					}
					else if (t.con == 11)
					{
						t.frame += 0.334f / 2f;
						if (t.frame >= 4f && !t.thrown)
						{
							t.thrown = true;
							Vector2 from = t.body.Position + new Vector2(-6f, 0f);
							float speed = (9f + (float)Math.Sin(t.siner / 10f) * 4f) / 2f;
							if (Count == 2)
								speed *= 0.85f;
							else if (Count >= 3)
								speed *= 0.7f;
							float angle = (battle.SoulCenter - from).ToRotation() + MathHelper.ToRadians(Main.rand.NextFloat(-5f, 5f));
							battle.Spawn(Thrown(from, angle.ToRotationVector2() * speed));
							AttackSfx.Vanilla(SoundID.Item1, 0.4f, 0.3f);
						}
						if (t.frame >= 6f)
						{
							t.con = 12;
							t.timer = 0;
						}
					}
					else if (t.con == 12 && ++t.timer >= Count * 3 * 2)
						t.con = 10;
				}
				throwers[i] = t;
			}
		}
	}

	/// <summary>
	/// A ring of bullets forms around the SOUL, turning, then fires in at it one at a time (like Hathy's hearts):
	/// each aims where the SOUL is when its turn comes.
	/// </summary>
	public class RingVolley : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public int Count = 10, Form = 40, Gap = 6;
		public float Radius = 72f, Spin = 0.03f, Speed = 3.6f;
		/// <summary>The ring only hurts once a bullet fires: you can't be caught by it just while it circles.</summary>
		public bool SafeWhileCircling;

		public RingVolley(Func<Vector2, Vector2, Bullet> make, int every = 120)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 100;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Vector2 centre = battle.SoulCenter;
			float start = Main.rand.NextFloat(MathHelper.TwoPi);
			float spin = index % 2 == 0 ? Spin : -Spin;
			int form = Form;
			float radius = Radius, speed = Speed;
			bool safe = SafeWhileCircling;
			AttackSfx.Appear();
			for (int i = 0; i < Count; i++)
			{
				float a0 = start + MathHelper.TwoPi * i / Count;
				int fireAt = form + i * Gap;
				Bullet b = Make(centre + a0.ToRotationVector2() * radius, Vector2.Zero);
				b.Harmful = false;
				b.Alpha = 0f;
				b.RotateWithVelocity = false;
				b.Lifetime = fireAt + 160;
				b.OnUpdate += x =>
				{
					if (x.Age < fireAt)
					{
						float a = a0 + spin * x.Age;
						x.Position = centre + a.ToRotationVector2() * radius;
						x.Velocity = Vector2.Zero;
						x.Alpha = Math.Min(1f, x.Age / (form * 0.6f));
						x.Harmful = !safe && x.Age > form * 0.6f;
						if (fireAt - x.Age < 6)
							x.Flash = 2;
					}
					else if (x.Age == fireAt)
					{
						x.Velocity = (battle.SoulCenter - x.Position).SafeNormalize(Vector2.UnitX) * speed;
						x.Harmful = true;
						x.Trail = 3;
						AttackSfx.Vanilla(SoundID.Item17, 0.35f);
					}
				};
				battle.Spawn(b);
			}
		}
	}
}
