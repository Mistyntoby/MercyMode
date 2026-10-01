using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;
using MercyMode.Deltarune;

namespace MercyMode.Battle
{
	/// <summary>Sounds for attack patterns: Deltarune's when they're loaded, Terraria's otherwise.</summary>
	public static class AttackSfx
	{
		/// <summary>Bullets materialising (a telegraph, a ring forming).</summary>
		public static void Appear() => DeltaruneAssets.Play("bulletappear", SoundID.Item8 with { Volume = 0.6f, Pitch = 0.3f });
		/// <summary>A sword slash (the FIGHT slash sound).</summary>
		public static void Slash() => DeltaruneAssets.Play("slash", SoundID.Item71 with { Volume = 0.8f });
		/// <summary>Bullets launching after a telegraph.</summary>
		public static void Fire() => DeltaruneAssets.Play("bulletfire", SoundID.Item5 with { Volume = 0.8f });
		/// <summary>Something heavy hitting the floor of the box.</summary>
		public static void Impact() => DeltaruneAssets.Play("impact", SoundID.Item14 with { Volume = 0.7f, Pitch = -0.2f });
		/// <summary>A shell bursting into bullets.</summary>
		public static void Explosion() => DeltaruneAssets.Play("explosion", SoundID.Item14 with { Volume = 0.6f });

		/// <summary>A Terraria sound (roars, lasers, stingers) at the battle's sound volume.</summary>
		public static void Vanilla(SoundStyle style, float volume = 1f, float pitch = 0f)
		{
			float battleVolume = ModContent.GetInstance<MercyConfig>()?.BattleSoundVolume ?? 1f;
			SoundEngine.PlaySound(style with { Volume = style.Volume * volume * battleVolume, Pitch = style.Pitch + pitch });
		}
	}

	/// <summary>
	/// Bullets fade in on a circle around the SOUL, pointing at it, then all fire at the spot where it was
	/// (Deltarune's spear rings). Move once they flash.
	/// </summary>
	public class Converge : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public int Count = 8;
		public float Radius = 78f;
		/// <summary>Ticks the ring takes to appear before it fires.</summary>
		public int Warn = 40;
		public float Speed = 4f;
		/// <summary>The ring turns while it forms (radians per tick).</summary>
		public float Spin = 0.012f;
		/// <summary>Sprites that don't point right get this added to their aim.</summary>
		public float RotationOffset;

		public Converge(Func<Vector2, Vector2, Bullet> make, int every = 60)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 70;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Vector2 target = battle.SoulCenter;
			float start = Main.rand.NextFloat(MathHelper.TwoPi);
			float spin = index % 2 == 0 ? Spin : -Spin;
			int warn = Warn;
			float speed = Speed, radius = Radius, offset = RotationOffset;
			AttackSfx.Appear();
			for (int i = 0; i < Count; i++)
			{
				float a0 = start + MathHelper.TwoPi * i / Count;
				bool leader = i == 0;
				Bullet b = Make(target + a0.ToRotationVector2() * radius, Vector2.Zero);
				Color baseColor = b.Color;
				b.Harmful = false;
				b.Alpha = 0f;
				b.RotateWithVelocity = false;
				b.Lifetime = warn + 140;
				b.OnUpdate += x =>
				{
					if (x.Age < warn)
					{
						float a = a0 + spin * x.Age;
						x.Position = target + a.ToRotationVector2() * radius;
						x.Velocity = Vector2.Zero;
						x.Alpha = Math.Min(1f, x.Age / (warn * 0.6f));
						x.Rotation = (a + MathHelper.Pi) + offset;
						// a quick flash right before they fire
						x.Color = warn - x.Age < 8 && (x.Age / 2) % 2 == 0 ? Color.White : baseColor;
					}
					else if (x.Age == warn)
					{
						x.Harmful = true;
						x.Alpha = 1f;
						x.Color = baseColor;
						x.Velocity = Vector2.Normalize(target - x.Position) * speed;
						if (leader)
							AttackSfx.Fire();
					}
				};
				battle.Spawn(b);
			}
		}
	}

	/// <summary>
	/// A thin line flashes across the box through the SOUL, then a beam fires along it for a moment. Beams can be
	/// any angle, or alternate horizontal/vertical.
	/// </summary>
	public class Beam : RepeatingAttack
	{
		public int Warn = 42;
		public int Active = 26;
		public float Width = 16f;
		public Color Color = new(255, 60, 60);
		/// <summary>Only horizontal and vertical beams (alternating) instead of any angle.</summary>
		public bool AxisAligned;
		/// <summary>Through the SOUL, or through a random point of the box.</summary>
		public bool Aimed = true;
		/// <summary>Always this direction (radians; 0 = horizontal) give or take <see cref="Tilt"/>; null = any.</summary>
		public float? FixedAngle;
		public float Tilt;
		/// <summary>Played when the beam fires.</summary>
		public SoundStyle FireSound = SoundID.Item12;
		public float Damage = 1f;

		public Beam(int every = 60)
		{
			Every = every;
			StopBeforeEnd = 70;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			Vector2 through = Aimed ? battle.SoulCenter
				: new Vector2(Main.rand.NextFloat(box.Left + 15, box.Right - 15), Main.rand.NextFloat(box.Top + 15, box.Bottom - 15));
			float angle = FixedAngle is float fixedAngle ? fixedAngle + Main.rand.NextFloat(-Tilt, Tilt)
				: AxisAligned ? (index % 2 == 0 ? 0f : MathHelper.PiOver2)
				: Main.rand.NextFloat(MathHelper.Pi);
			SoundStyle fireSound = FireSound;
			AttackSfx.Appear();
			battle.Spawn(Make(battle, through, angle, Warn, Active, Width, Color, Damage, () =>
			{
				AttackSfx.Vanilla(fireSound, 0.8f);
				battle.ShakeScreen(2);
			}));
		}

		/// <summary>
		/// One beam through a point: a flickering telegraph line for <paramref name="warn"/> ticks, then the beam for
		/// <paramref name="active"/> ticks (hurts along its whole length). <paramref name="onFire"/> runs as it fires.
		/// </summary>
		public static Bullet Make(BattleSystem battle, Vector2 through, float angle, int warn, int active, float width,
			Color color, float damage, Action onFire, bool sharp = false)
		{
			Vector2 dir = angle.ToRotationVector2();
			Vector2 a = through - dir * 520f, b2 = through + dir * 520f;
			return new Bullet
			{
				Position = through,
				Harmful = false,
				Lifetime = warn + active,
				DamageMult = damage,
				GrazePoints = 3f,
				DestroyOnHit = false,
				OnUpdate = x =>
				{
					if (x.Age == warn)
					{
						x.Harmful = true;
						onFire?.Invoke();
					}
					// A slash only cuts in its first moments; the rest is the afterglow fading
					if (sharp && x.Age > warn + active / 2)
						x.Harmful = false;
				},
				HitTest = (x, r) => x.Harmful && SegmentNear(a, b2, r, width / 2f),
				OnDraw = x =>
				{
					if (x.Age < warn)
					{
						// The telegraph: a thin flickering line that thickens as the shot gets close
						float t = x.Age / (float)warn;
						float alpha = (x.Age / 3) % 2 == 0 ? 0.8f : 0.4f;
						DrDraw.Line(a, b2, 1f + t * 2f, color * alpha);
						return;
					}
					float life = (x.Age - warn) / (float)active;
					if (sharp)
					{
						// A slash: full width at once, then thins out to nothing
						float s = 1f - life;
						DrDraw.Line(a, b2, width * s + 10f * s, color * (0.3f * s));
						DrDraw.Line(a, b2, width * s * s, color);
						DrDraw.Line(a, b2, width * 0.4f * s * s, Color.White);
						return;
					}
					float w = width * (life < 0.15f ? life / 0.15f : life > 0.7f ? (1f - life) / 0.3f : 1f);
					w *= 1f + (float)Math.Sin(x.Age * 1.7f) * 0.08f;
					DrDraw.Line(a, b2, w + 6f, color * 0.35f);
					DrDraw.Line(a, b2, w, color);
					DrDraw.Line(a, b2, w * 0.45f, Color.White);
				},
			};
		}

		/// <summary>Whether a segment passes within <paramref name="radius"/> of a rectangle.</summary>
		public static bool SegmentNear(Vector2 a, Vector2 b, Rectangle r, float radius)
		{
			Vector2 c = r.Center.ToVector2();
			Vector2 ab = b - a;
			float t = MathHelper.Clamp(Vector2.Dot(c - a, ab) / ab.LengthSquared(), 0f, 1f);
			Vector2 closest = a + ab * t;
			Vector2 d = c - closest;
			// distance from the rectangle (not just its centre) to the line
			float dx = Math.Max(0f, Math.Abs(d.X) - r.Width / 2f);
			float dy = Math.Max(0f, Math.Abs(d.Y) - r.Height / 2f);
			return dx * dx + dy * dy <= radius * radius;
		}
	}

	/// <summary>
	/// The Roaring Knight's way: bursts of huge slashes across the whole arena. Each slash flickers as a thin line,
	/// then cuts in one stroke; a burst's slashes land one after another, the first aimed at the SOUL. Meant for
	/// full-screen attacks (set <see cref="EnemyAttack.FullScreen"/>), so it waits for the arena to open.
	/// </summary>
	public class Slashes : RepeatingAttack
	{
		public int PerBurst = 3;
		/// <summary>Ticks between slashes in a burst.</summary>
		public int Stagger = 9;
		public int Warn = 34;
		public int Active = 14;
		public float Width = 22f;
		public Color Color = Color.White;
		public float Damage = 1.1f;

		public Slashes(int every = 70)
		{
			Every = every;
			FirstAt = 36; // the arena opens first
			StopBeforeEnd = 60;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			AttackSfx.Appear();
			float baseAngle = Main.rand.NextFloat(MathHelper.Pi);
			for (int i = 0; i < PerBurst; i++)
			{
				// The first goes through the SOUL; the rest cut across the arena at spread-out angles
				Vector2 through = i == 0 ? battle.SoulCenter
					: new Vector2(Main.rand.NextFloat(box.Left + 30, box.Right - 30), Main.rand.NextFloat(box.Top + 30, box.Bottom - 30));
				float angle = baseAngle + i * (MathHelper.Pi / PerBurst) + Main.rand.NextFloat(-0.2f, 0.2f);
				battle.Spawn(Beam.Make(battle, through, angle, Warn + i * Stagger, Active, Width, Color, Damage, () =>
				{
					AttackSfx.Slash();
					battle.ShakeScreen(3);
				}, sharp: true));
			}
		}
	}

	/// <summary>
	/// Something big flashes a column over the SOUL, drops, and hits the floor of the box: the screen shakes and
	/// shockwave bullets slide out along the floor both ways (and some debris pops up).
	/// </summary>
	public class Slam : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public Func<Vector2, Vector2, Bullet> MakeShard;
		public int Warn = 34;
		public float FallSpeed = 9f;
		public float Width = 40f;
		public int Shards = 2;
		public float ShardSpeed = 2.4f;
		public int Debris = 3;

		public Slam(Func<Vector2, Vector2, Bullet> make, Func<Vector2, Vector2, Bullet> shard, int every = 80)
		{
			Make = make;
			MakeShard = shard;
			Every = every;
			StopBeforeEnd = 90;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			float x = MathHelper.Clamp(battle.SoulCenter.X, box.Left + Width / 2f, box.Right - Width / 2f);
			battle.Spawn(Shots.Warning(new Rectangle((int)(x - Width / 2f), box.Top, (int)Width, box.Height), Warn));
			AttackSfx.Appear();

			Bullet body = Make(new Vector2(x, box.Top - 70f), Vector2.Zero);
			body.Harmful = false;
			body.RotateWithVelocity = false;
			body.Lifetime = Warn + 90;
			body.OffscreenMargin = 300f;
			float floor = box.Bottom - body.HitSize.Y / 2f - 2f;
			int warn = Warn;
			float fall = FallSpeed, shardSpeed = ShardSpeed;
			int shards = Shards, debris = Debris;
			bool landed = false;
			body.OnUpdate += b =>
			{
				if (b.Age == warn)
				{
					b.Harmful = true;
					b.Velocity = new Vector2(0f, fall);
				}
				if (!landed && b.Age > warn && b.Position.Y + b.Velocity.Y >= floor)
				{
					landed = true;
					b.Position.Y = floor;
					b.Velocity = Vector2.Zero;
					b.Lifetime = b.Age + 14; // rests a moment, then goes
					AttackSfx.Impact();
					battle.ShakeScreen(5);
					for (int i = 0; i < shards; i++)
					{
						float y = box.Bottom - 8f;
						for (int side = -1; side <= 1; side += 2)
						{
							Bullet s = MakeShard(new Vector2(x + side * 10f, y), new Vector2(side * shardSpeed * (1f + i * 0.45f), 0f));
							s.StartDelay = i * 6;
							battle.Spawn(s);
						}
					}
					for (int i = 0; i < debris; i++)
					{
						Bullet d = MakeShard(new Vector2(x, floor - 10f), new Vector2(Main.rand.NextFloat(-1.6f, 1.6f), Main.rand.NextFloat(-4.5f, -3f)));
						d.Acceleration = new Vector2(0f, 0.12f);
						d.Scale *= 0.8f;
						battle.Spawn(d);
					}
				}
				// Fade out after landing
				if (landed && b.Age > b.Lifetime - 8)
				{
					b.Harmful = false;
					b.Alpha = Math.Max(0f, (b.Lifetime - b.Age) / 8f);
				}
			};
			battle.Spawn(body);
		}
	}

	/// <summary>
	/// A stream of bullets from one point that sweeps back and forth (a fan) or turns all the way round (a
	/// spiral). Bullets fired inside the box stay harmless for a moment so they can't appear on the SOUL.
	/// </summary>
	public class Sprinkler : EnemyAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		/// <summary>Where the stream comes from; null = just above the box.</summary>
		public Vector2? Origin;
		public int Every = 5;
		public int Arms = 1;
		public float Speed = 2.4f;
		/// <summary>Spiral: radians per tick the arms turn. Fan: how fast it sweeps.</summary>
		public float TurnSpeed = 0.05f;
		/// <summary>True to spin all the way round; false to sweep a fan.</summary>
		public bool Spiral;
		/// <summary>Fan only: centre direction and half-width (radians).</summary>
		public float FanCenter = MathHelper.PiOver2, FanSpread = 0.9f;
		public int ArmTicks = 18;
		public int StopBeforeEnd = 40;
		public int FirstAt;

		public Sprinkler(Func<Vector2, Vector2, Bullet> make)
		{
			Make = make;
		}

		public override void Update(BattleSystem battle, int tick)
		{
			if (tick < FirstAt || tick > Duration - StopBeforeEnd || (tick - FirstAt) % Math.Max(1, Every) != 0)
				return;
			Rectangle box = battle.Box;
			Vector2 origin = Origin ?? new Vector2(box.Center.X, box.Top - 30f);
			bool inside = box.Contains(origin.ToPoint());
			float baseAngle = Spiral ? tick * TurnSpeed : FanCenter + (float)Math.Sin(tick * TurnSpeed) * FanSpread;
			int armTicks = ArmTicks;
			for (int i = 0; i < Arms; i++)
			{
				// Spiral arms share the full circle; a fan's arms sit side by side within it
				float a = Spiral ? baseAngle + MathHelper.TwoPi * i / Arms : baseAngle + (i - (Arms - 1) / 2f) * 0.35f;
				Bullet b = Make(origin, a.ToRotationVector2() * Speed);
				if (inside)
				{
					b.Harmful = false;
					b.Alpha = 0f;
					b.OnUpdate += x =>
					{
						x.Alpha = Math.Min(1f, x.Age / (float)armTicks);
						if (x.Age >= armTicks)
							x.Harmful = true;
					};
				}
				battle.Spawn(b);
			}
		}
	}

	/// <summary>
	/// Rows of bullets fall through the box, each with a gap; the gap drifts from row to row, so the SOUL has to
	/// weave down a zigzag path.
	/// </summary>
	public class GapRows : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public float Speed = 1.6f;
		public float Spacing = 14f;
		public float GapSize = 44f;
		/// <summary>How far the gap moves between rows.</summary>
		public float Drift = 26f;
		private float gapCenter = -1f;
		private int drift = 1;

		public GapRows(Func<Vector2, Vector2, Bullet> make, int every = 36)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 100;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			float min = box.Left + GapSize / 2f + 4f, max = box.Right - GapSize / 2f - 4f;
			if (gapCenter < 0f)
				gapCenter = MathHelper.Clamp(battle.SoulCenter.X, min, max);
			else
			{
				gapCenter += drift * Drift * Main.rand.NextFloat(0.6f, 1.2f);
				if (gapCenter <= min || gapCenter >= max || Main.rand.NextBool(5))
					drift = -drift;
				gapCenter = MathHelper.Clamp(gapCenter, min, max);
			}
			for (float x = box.Left + 4f; x <= box.Right - 4f; x += Spacing)
			{
				if (Math.Abs(x - gapCenter) < GapSize / 2f)
					continue;
				battle.Spawn(Make(new Vector2(x, box.Top - 20f), new Vector2(0f, Speed)));
			}
		}
	}

	/// <summary>Shells fly into the box, stop, flash, and burst into a ring of bullets.</summary>
	public class Fireworks : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> MakeShell;
		public Func<Vector2, Vector2, Bullet> MakeShard;
		public int Count = 8;
		public int Fuse = 50;
		public float ShardSpeed = 1.8f;

		public Fireworks(Func<Vector2, Vector2, Bullet> shell, Func<Vector2, Vector2, Bullet> shard, int every = 45)
		{
			MakeShell = shell;
			MakeShard = shard;
			Every = every;
			StopBeforeEnd = 90;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			// Burst somewhere away from the SOUL so the ring has room
			Vector2 soul = battle.SoulCenter;
			Vector2 target = new(Main.rand.NextFloat(box.Left + 20, box.Right - 20), Main.rand.NextFloat(box.Top + 20, box.Bottom - 20));
			for (int tries = 0; tries < 6 && Vector2.Distance(target, soul) < 50f; tries++)
				target = new(Main.rand.NextFloat(box.Left + 20, box.Right - 20), Main.rand.NextFloat(box.Top + 20, box.Bottom - 20));
			Vector2 from = Shots.AroundBox(box, 150f);
			int fuse = Fuse, travel = Math.Max(10, Fuse - 18), count = Count;
			float shardSpeed = ShardSpeed;
			float spin = Main.rand.NextFloat(MathHelper.TwoPi);

			Bullet shell = MakeShell(from, (target - from) / travel);
			Color shellColor = shell.Color;
			shell.RotateWithVelocity = false;
			// Harmless while it's lobbed in (it flies fast); it's the burst that hurts
			shell.Harmful = false;
			shell.Lifetime = fuse + 2;
			shell.OffscreenMargin = 300f;
			shell.OnUpdate += x =>
			{
				if (x.Age == travel)
					x.Velocity = Vector2.Zero;
				if (x.Age >= travel)
				{
					// About to pop: pulse white
					x.Color = (x.Age / 3) % 2 == 0 ? Color.White : shellColor;
					x.Scale *= 1.01f;
				}
				if (x.Age == fuse)
				{
					x.Dead = true;
					AttackSfx.Explosion();
					for (int i = 0; i < count; i++)
						battle.Spawn(MakeShard(x.Position, (spin + MathHelper.TwoPi * i / count).ToRotationVector2() * shardSpeed));
				}
			};
			battle.Spawn(shell);
		}
	}
}
