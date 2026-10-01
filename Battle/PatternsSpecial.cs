using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace MercyMode.Battle
{
	// ====================================================================== SOUL-mode attacks

	/// <summary>
	/// Green SOUL: spears fly at the middle of the box from the four sides, for the shield to block. Some are
	/// tricksters (yellow) that jump to the opposite side just before they arrive.
	/// </summary>
	public class ShieldSpears : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public float Speed = 2.4f;
		public float Distance = 120f;
		/// <summary>One in this many is a trickster (0 = none).</summary>
		public int TricksterEvery = 5;
		private int lastDir = -1;

		public ShieldSpears(Func<Vector2, Vector2, Bullet> make, int every = 22)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 60;
			Soul = SoulMode.Green;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Vector2 centre = battle.BoxCentre;
			int dir = Main.rand.Next(4);
			if (dir == lastDir && Main.rand.NextBool())
				dir = (dir + 1 + Main.rand.Next(3)) % 4;
			lastDir = dir;
			Vector2 from = (dir * MathHelper.PiOver2 - MathHelper.PiOver2).ToRotationVector2();
			float speed = Speed * Main.rand.NextFloat(0.85f, 1.25f);
			Bullet b = Make(centre + from * Distance, -from * speed);
			b.RotateWithVelocity = true;
			b.Lifetime = (int)(Distance * 2f / speed) + 20;
			b.OffscreenMargin = 300f;
			b.Trail = 3;
			bool trickster = TricksterEvery > 0 && index > 1 && index % TricksterEvery == 0;
			if (trickster)
			{
				b.Color = new Color(255, 230, 80);
				bool flipped = false;
				b.OnUpdate += x =>
				{
					if (!flipped && Vector2.Distance(x.Position, centre) < 52f)
					{
						// Jumps round to come in from the other side
						flipped = true;
						x.Position = centre - (x.Position - centre);
						x.Velocity = -x.Velocity;
						Sparks.Burst(battle, x.Position, 4, new Color(255, 230, 80), 1.5f);
					}
				};
			}
			battle.Spawn(b);
		}
	}

	/// <summary>
	/// Purple SOUL: things run along the strings from the sides (spiders on their webs). Sometimes two strings at
	/// once, never all three.
	/// </summary>
	public class StringRunners : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public float Speed = 2.6f;

		public StringRunners(Func<Vector2, Vector2, Bullet> make, int every = 26)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 70;
			Soul = SoulMode.Purple;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			int first = index % 3 == 0 ? NearestString(battle) : Main.rand.Next(BattleSystem.PurpleStrings);
			bool two = index % 4 == 3;
			for (int k = 0; k < (two ? 2 : 1); k++)
			{
				int s = (first + k) % BattleSystem.PurpleStrings;
				bool fromRight = Main.rand.NextBool();
				float speed = Speed * Main.rand.NextFloat(0.85f, 1.2f);
				var pos = new Vector2(fromRight ? box.Right + 24 : box.Left - 24, battle.PurpleStringY(s));
				Bullet b = Make(pos, new Vector2(fromRight ? -speed : speed, 0f));
				b.OnUpdate += x => x.Position.Y = battle.PurpleStringY(s);
				battle.Spawn(b);
			}
		}

		private static int NearestString(BattleSystem battle)
		{
			int best = 0;
			for (int i = 1; i < BattleSystem.PurpleStrings; i++)
				if (Math.Abs(battle.PurpleStringY(i) - battle.SoulCenter.Y) < Math.Abs(battle.PurpleStringY(best) - battle.SoulCenter.Y))
					best = i;
			return best;
		}
	}

	/// <summary>
	/// Blue SOUL: bones slide across the box (Papyrus style): short ones standing on the floor to jump over, and
	/// long ones hanging from the ceiling to stay under.
	/// </summary>
	public class BoneWalls : RepeatingAttack
	{
		public float Speed = 2.6f;
		public Color Color = Color.White;
		/// <summary>Played as the attack starts: the SOUL is thrown to the floor.</summary>
		public bool SlamFirst = true;

		public BoneWalls(int every = 34)
		{
			Every = every;
			StopBeforeEnd = 60;
			FirstAt = 20;
			Soul = SoulMode.Blue;
		}

		public override void Update(BattleSystem battle, int tick)
		{
			if (tick == 1 && SlamFirst)
				battle.SlamSoul();
			base.Update(battle, tick);
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			bool fromRight = index % 4 != 3;
			// Mostly low bones to hop; every third a ceiling bone that leaves a gap at the bottom
			bool ceiling = index % 3 == 2;
			float height = ceiling ? box.Height - 34f : Main.rand.NextFloat(18f, 40f);
			float top = ceiling ? box.Top : box.Bottom - height;
			float x = fromRight ? box.Right + 10 : box.Left - 10;
			battle.Spawn(Bone(new Vector2(x, top + height / 2f), new Vector2(fromRight ? -Speed : Speed, 0f), height, Color));
		}

		/// <summary>A vertical bone of a given height centred on a point.</summary>
		public static Bullet Bone(Vector2 centre, Vector2 vel, float height, Color color) => new()
		{
			Position = centre,
			Velocity = vel,
			HitSize = new Vector2(8f, height),
			Lifetime = 400,
			DamageMult = 0.8f,
			OnDraw = b =>
			{
				float top = b.Position.Y - b.HitSize.Y / 2f, x = b.Position.X;
				DrDraw.Rect(x - 3f, top + 3f, 6f, b.HitSize.Y - 6f, color);
				DrDraw.Rect(x - 6f, top, 5f, 5f, color);
				DrDraw.Rect(x + 1f, top, 5f, 5f, color);
				DrDraw.Rect(x - 6f, top + b.HitSize.Y - 5f, 5f, 5f, color);
				DrDraw.Rect(x + 1f, top + b.HitSize.Y - 5f, 5f, 5f, color);
			},
		};
	}

	/// <summary>
	/// Yellow SOUL: tough targets fly in from the right and hang there shooting at the SOUL until they're shot
	/// down (or the turn ends). Its shots can be broken too.
	/// </summary>
	public class Gunships : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public Func<Vector2, Vector2, Bullet> MakeShot;
		public int Toughness = 4;
		public int FireEvery = 48;
		public float ShotSpeed = 2.4f;

		public Gunships(Func<Vector2, Vector2, Bullet> make, Func<Vector2, Vector2, Bullet> shot, int every = 70)
		{
			Make = make;
			MakeShot = shot;
			Every = every;
			StopBeforeEnd = 80;
			Soul = SoulMode.Yellow;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			float y = Main.rand.NextFloat(box.Top + 18, box.Bottom - 18);
			float stopX = box.Right - Main.rand.NextFloat(14f, 40f);
			Bullet ship = Make(new Vector2(box.Right + 40f, y), new Vector2(-2.4f, 0f));
			ship.Toughness = Toughness;
			ship.Lifetime = 600;
			ship.Trail = 3;
			int fireEvery = FireEvery, phase = Main.rand.Next(20);
			float shotSpeed = ShotSpeed;
			float bob = Main.rand.NextFloat(MathHelper.TwoPi);
			ship.OnUpdate += x =>
			{
				if (x.Position.X <= stopX && x.Velocity.X < 0f)
					x.Velocity = Vector2.Zero;
				if (x.Velocity == Vector2.Zero)
				{
					x.Position.Y = y + (float)Math.Sin(x.Age / 25f + bob) * 10f;
					if ((x.Age + phase) % fireEvery == 0)
					{
						Bullet s = MakeShot(x.Position, Vector2.Normalize(battle.SoulCenter - x.Position) * shotSpeed);
						battle.Spawn(s);
						AttackSfx.Fire();
					}
				}
			};
			battle.Spawn(ship);
		}
	}

	// ====================================================================== new patterns

	/// <summary>Bullets that bounce off the walls of the box a few times before leaving.</summary>
	public class Ricochet : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public float Speed = 2.4f;
		public int Bounces = 2;

		public Ricochet(Func<Vector2, Vector2, Bullet> make, int every = 34)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 70;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			Vector2 from = Shots.AroundBox(box, 130f);
			Vector2 aim = new(Main.rand.NextFloat(box.Left + 20, box.Right - 20), Main.rand.NextFloat(box.Top + 20, box.Bottom - 20));
			Bullet b = Make(from, Vector2.Normalize(aim - from) * Speed);
			b.Lifetime = 600;
			b.Trail = 3;
			int bounces = Bounces;
			bool inside = false;
			b.OnUpdate += x =>
			{
				Rectangle r = battle.Box;
				if (!inside)
				{
					inside = r.Contains(x.Position.ToPoint());
					return;
				}
				if (bounces <= 0)
					return;
				Vector2 next = x.Position + x.Velocity;
				bool bounced = false;
				if (next.X < r.Left + 4 || next.X > r.Right - 4) { x.Velocity.X = -x.Velocity.X; bounced = true; }
				if (next.Y < r.Top + 4 || next.Y > r.Bottom - 4) { x.Velocity.Y = -x.Velocity.Y; bounced = true; }
				if (bounced)
				{
					bounces--;
					Sparks.Burst(battle, x.Position, 4, x.Color, 1.6f);
					AttackSfx.Vanilla(SoundID.Item10, 0.3f, 0.5f);
				}
			};
			battle.Spawn(b);
		}
	}

	/// <summary>A big shot flies at the SOUL, then splits into a fan of small ones partway.</summary>
	public class Splitter : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> MakeShell;
		public Func<Vector2, Vector2, Bullet> MakeShard;
		public int Fuse = 34;
		public int Count = 5;
		public float Spread = 1.1f;
		public float Speed = 2.6f, ShardSpeed = 2.2f;
		/// <summary>Split into a full ring instead of a forward fan.</summary>
		public bool Ring;

		public Splitter(Func<Vector2, Vector2, Bullet> shell, Func<Vector2, Vector2, Bullet> shard, int every = 46)
		{
			MakeShell = shell;
			MakeShard = shard;
			Every = every;
			StopBeforeEnd = 70;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Vector2 from = Shots.AroundBox(battle.Box, 140f);
			Vector2 dir = Vector2.Normalize(battle.SoulCenter - from);
			Bullet shell = MakeShell(from, dir * Speed);
			shell.Trail = 4;
			shell.Lifetime = 300;
			Color baseColor = shell.Color;
			int fuse = Fuse + 30, count = Count;
			float spread = Spread, shardSpeed = ShardSpeed;
			bool ring = Ring;
			shell.OnUpdate += x =>
			{
				if (x.Age > fuse - 10)
					x.Color = (x.Age / 2) % 2 == 0 ? Color.White : baseColor;
				if (x.Age < fuse)
					return;
				x.Dead = true;
				AttackSfx.Explosion();
				battle.AddEffect(new Shockwave(x.Position, baseColor, 26f));
				float a0 = x.Velocity.ToRotation();
				for (int i = 0; i < count; i++)
				{
					float a = ring ? a0 + MathHelper.TwoPi * i / count : a0 + MathHelper.Lerp(-spread, spread, count == 1 ? 0.5f : i / (float)(count - 1));
					battle.Spawn(MakeShard(x.Position, a.ToRotationVector2() * shardSpeed));
				}
			};
			battle.Spawn(shell);
		}
	}

	/// <summary>
	/// Shots lobbed high from the sides that come down where the SOUL is: a marker flashes on the floor, then the
	/// shot lands there (and can splash along the floor).
	/// </summary>
	public class Lobs : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public Func<Vector2, Vector2, Bullet> MakeSplash;
		public int Flight = 56;
		public float Gravity = 0.16f;
		public int Splash = 2;
		/// <summary>Lob a few at once, spread out around the SOUL.</summary>
		public int Volley = 1;

		public Lobs(Func<Vector2, Vector2, Bullet> make, int every = 32)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 80;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			for (int v = 0; v < Volley; v++)
			{
				float targetX = MathHelper.Clamp(battle.SoulCenter.X + (v - (Volley - 1) / 2f) * 34f + Main.rand.NextFloat(-6f, 6f), box.Left + 8, box.Right - 8);
				float floorY = box.Bottom - 8f;
				bool fromRight = (index + v) % 2 == 0;
				var from = new Vector2(fromRight ? box.Right + 30 : box.Left - 30, box.Top - 10);
				int t = Flight;
				float g = Gravity;
				// Ballistic: x moves evenly; y = from + vy t + g t^2 / 2 lands on the floor at t
				var vel = new Vector2((targetX - from.X) / t, (floorY - from.Y - g * t * t / 2f) / t);
				battle.Spawn(Shots.Warning(new Rectangle((int)targetX - 9, (int)floorY - 6, 18, 8), t, new Color(255, 200, 60)));
				Bullet b = Make(from, vel);
				b.Acceleration = new Vector2(0f, g);
				b.Trail = 3;
				b.OffscreenMargin = 200f;
				b.Lifetime = t + 4;
				var splash = MakeSplash;
				int splashCount = Splash;
				b.OnUpdate += x =>
				{
					if (x.Age != t)
						return;
					x.Dead = true;
					Sparks.FloorDust(battle, new Vector2(x.Position.X, floorY), x.Color, 6);
					if (splash == null)
						return;
					AttackSfx.Impact();
					for (int i = 0; i < splashCount; i++)
						for (int side = -1; side <= 1; side += 2)
							battle.Spawn(splash(new Vector2(x.Position.X, floorY - 2f), new Vector2(side * (1.6f + i * 0.8f), 0f)));
				};
				battle.Spawn(b);
			}
		}
	}

	/// <summary>
	/// Something appears above the box, follows the SOUL sideways for a moment (a line shows where it'll go),
	/// then dives straight down.
	/// </summary>
	public class Diver : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public int Track = 34;
		public float DiveSpeed = 7.5f;

		public Diver(Func<Vector2, Vector2, Bullet> make, int every = 40)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 50;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			float x0 = Main.rand.NextFloat(box.Left + 10, box.Right - 10);
			Bullet b = Make(new Vector2(x0, box.Top - 26f), Vector2.Zero);
			b.Harmful = false;
			b.Alpha = 0f;
			b.Lifetime = Track + 120;
			b.OffscreenMargin = 200f;
			b.Trail = 5;
			int track = Track;
			float dive = DiveSpeed;
			b.OnUpdate += x =>
			{
				if (x.Age < track)
				{
					x.Alpha = Math.Min(1f, x.Age / 12f);
					float want = MathHelper.Clamp(battle.SoulCenter.X, box.Left + 8, box.Right - 8);
					x.Position.X += (want - x.Position.X) * 0.12f;
					x.Velocity = Vector2.Zero;
				}
				else if (x.Age == track)
				{
					x.Harmful = true;
					x.Velocity = new Vector2(0f, dive);
					AttackSfx.Fire();
				}
			};
			var line = new Bullet
			{
				Harmful = false,
				Lifetime = Track,
				OnDraw = l =>
				{
					float a = (l.Age / 3) % 2 == 0 ? 0.45f : 0.2f;
					DrDraw.Line(new Vector2(b.Position.X, box.Top), new Vector2(b.Position.X, box.Bottom), 2f, new Color(255, 80, 80) * a);
				},
			};
			battle.Spawn(line);
			battle.Spawn(b);
		}
	}

	/// <summary>A caster blinks in somewhere around the box, fires a short spread at the SOUL, and vanishes.</summary>
	public class Blinker : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> MakeCaster;
		public Func<Vector2, Vector2, Bullet> MakeShot;
		public int Windup = 30;
		public int Shots = 3;
		public float Spread = 0.3f, ShotSpeed = 2.6f;
		public Color Glow = new(200, 90, 255);

		public Blinker(Func<Vector2, Vector2, Bullet> caster, Func<Vector2, Vector2, Bullet> shot, int every = 50)
		{
			MakeCaster = caster;
			MakeShot = shot;
			Every = every;
			StopBeforeEnd = 60;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Vector2 at = Battle.Shots.AroundBox(battle.Box, 105f);
			Bullet c = MakeCaster(at, Vector2.Zero);
			c.Harmful = false;
			c.Alpha = 0f;
			c.Lifetime = Windup + 26;
			int windup = Windup, shots = Shots;
			float spread = Spread, speed = ShotSpeed;
			Color glow = Glow;
			Sparks.Burst(battle, at, 8, glow, 2f);
			AttackSfx.Appear();
			c.OnUpdate += x =>
			{
				x.Alpha = x.Age < 10 ? x.Age / 10f : x.Age > windup + 10 ? Math.Max(0f, 1f - (x.Age - windup - 10) / 14f) : 1f;
				if (x.Age == windup)
				{
					float a0 = (battle.SoulCenter - x.Position).ToRotation();
					for (int i = 0; i < shots; i++)
					{
						float a = a0 + (shots == 1 ? 0f : MathHelper.Lerp(-spread, spread, i / (float)(shots - 1)));
						battle.Spawn(MakeShot(x.Position, a.ToRotationVector2() * speed));
					}
					AttackSfx.Fire();
					battle.AddEffect(new Shockwave(x.Position, glow, 18f));
				}
			};
			battle.Spawn(c);
		}
	}

	/// <summary>A crowd walks in along the floor of the box from the sides, now and then hopping (zombies).</summary>
	public class Walkers : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public float Speed = 1.4f;
		public float HopSpeed = 3.2f;
		public float Gravity = 0.14f;
		/// <summary>-1 from the left, 1 from the right, 0 both.</summary>
		public int Side;

		public Walkers(Func<Vector2, Vector2, Bullet> make, int every = 30)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 60;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			bool fromRight = Side != 0 ? Side > 0 : index % 2 == 0;
			float speed = Speed * Main.rand.NextFloat(0.8f, 1.25f);
			Bullet b = Make(new Vector2(fromRight ? box.Right + 20 : box.Left - 20, box.Bottom), new Vector2(fromRight ? -speed : speed, 0f));
			float floor = box.Bottom - b.HitSize.Y / 2f - 2f;
			b.Position.Y = floor;
			b.Lifetime = 600;
			float hop = HopSpeed, g = Gravity;
			int hopAt = Main.rand.Next(40, 90);
			b.OnUpdate += x =>
			{
				if (x.Age % hopAt == 0 && x.Position.Y >= floor - 0.1f)
					x.Velocity.Y = -hop;
				if (x.Position.Y < floor || x.Velocity.Y < 0f)
					x.Velocity.Y += g;
				if (x.Position.Y + x.Velocity.Y > floor)
				{
					x.Position.Y = floor;
					x.Velocity.Y = 0f;
				}
			};
			battle.Spawn(b);
		}
	}

	/// <summary>
	/// Jaws: rows of teeth appear at the top and bottom of the box, then snap shut, leaving one gap to stand in.
	/// </summary>
	public class Jaws : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public int Warn = 36;
		public int Hold = 16;
		public float Spacing = 16f;
		public float GapSize = 38f;

		public Jaws(Func<Vector2, Vector2, Bullet> make, int every = 90)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 70;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			float gap = Main.rand.NextFloat(box.Left + GapSize / 2f + 6f, box.Right - GapSize / 2f - 6f);
			// Not right where the SOUL already is: it has to move
			if (Math.Abs(gap - battle.SoulCenter.X) < GapSize && box.Width > GapSize * 3f)
				gap = gap < box.Center.X ? gap + box.Width / 2f - GapSize / 2f : gap - box.Width / 2f + GapSize / 2f;
			gap = MathHelper.Clamp(gap, box.Left + GapSize / 2f + 6f, box.Right - GapSize / 2f - 6f);
			float mid = box.Center.Y;
			int warn = Warn, hold = Hold;
			AttackSfx.Appear();
			bool leader = true;
			for (float x = box.Left + Spacing / 2f; x < box.Right; x += Spacing)
			{
				if (Math.Abs(x - gap) < GapSize / 2f)
					continue;
				for (int side = -1; side <= 1; side += 2)
				{
					float edge = side < 0 ? box.Top + 7f : box.Bottom - 7f;
					float shut = mid + side * 7f;
					Bullet t = Make(new Vector2(x, edge), Vector2.Zero);
					t.Harmful = false;
					t.Alpha = 0.5f;
					t.RotateWithVelocity = false;
					t.Rotation = side < 0 ? MathHelper.Pi : 0f;
					t.Lifetime = warn + 12 + hold + 20;
					bool first = leader;
					leader = false;
					t.OnUpdate += b =>
					{
						if (b.Age < warn)
						{
							b.Alpha = 0.35f + ((b.Age / 4) % 2) * 0.25f;
							return;
						}
						b.Alpha = 1f;
						b.Harmful = true;
						float k = b.Age - warn;
						float y = k < 12 ? MathHelper.Lerp(edge, shut, k / 12f)
							: k < 12 + hold ? shut
							: MathHelper.Lerp(shut, edge, Math.Min(1f, (k - 12 - hold) / 20f));
						b.Position.Y = y;
						if (first && k == 12)
						{
							AttackSfx.Impact();
							battle.ShakeScreen(3);
						}
					};
					battle.Spawn(t);
				}
			}
		}
	}

	public static class BulletModifiers
	{
		/// <summary>Fades in and out on a cycle; can't hurt while it's mostly invisible (spirits).</summary>
		public static Bullet Phasing(this Bullet b, int period = 50)
		{
			int offset = Main.rand.Next(period);
			b.OnUpdate += x =>
			{
				float p = (float)Math.Sin((x.Age + offset) * MathHelper.TwoPi / period);
				x.Alpha = 0.15f + 0.85f * Math.Max(0f, p);
				x.Harmful = x.Alpha > 0.4f;
			};
			return b;
		}

		/// <summary>Leaves afterimages while it moves.</summary>
		public static Bullet Trailing(this Bullet b, int count = 4)
		{
			b.Trail = count;
			return b;
		}
	}
}
