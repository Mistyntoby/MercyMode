using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace MercyMode.Battle
{
	/// <summary>Bullet builders shared by every encounter.</summary>
	public static class Shots
	{
		public static readonly Color Red = new(255, 70, 70);
		public static readonly Color Green = new(120, 220, 60);
		public static readonly Color Purple = new(200, 90, 255);
		public static readonly Color Brown = new(170, 110, 60);
		public static readonly Color Ice = new(150, 220, 255);

		/// <summary>A round Deltarune bullet (spr_smallbullet) tinted any colour.</summary>
		public static Bullet Ball(Vector2 pos, Vector2 vel, Color color, float damage = 0.7f, float scale = 1f) => new()
		{
			Position = pos,
			Velocity = vel,
			Sprite = "spr_smallbullet",
			Color = color,
			Scale = scale,
			HitSize = new Vector2(8, 8) * scale,
			DamageMult = damage,
		};

		/// <summary>A bullet drawn with a Terraria NPC's sprite, animated through its frames.</summary>
		public static Bullet Npc(int type, Vector2 pos, Vector2 vel, float scale, float damage, Vector2 hitSize,
			bool rotate = true, float rotationOffset = 0f, int ticksPerFrame = 6)
		{
			Main.instance.LoadNPC(type);
			Texture2D tex = TextureAssets.Npc[type].Value;
			int frames = Math.Max(1, Main.npcFrameCount[type]);
			int fh = tex.Height / frames;
			var b = new Bullet
			{
				Position = pos,
				Velocity = vel,
				Texture = tex,
				Source = new Rectangle(0, 0, tex.Width, fh),
				Scale = scale,
				HitSize = hitSize,
				DamageMult = damage,
				RotateWithVelocity = rotate,
				RotationOffset = rotationOffset,
				OffscreenMargin = 200f,
			};
			if (frames > 1)
				b.OnUpdate += x => x.Source = new Rectangle(0, (x.Age / ticksPerFrame % frames) * fh, tex.Width, fh);
			return b;
		}

		/// <summary>A bullet drawn with a Terraria projectile's sprite.</summary>
		public static Bullet Proj(int type, Vector2 pos, Vector2 vel, float scale, float damage, Vector2 hitSize,
			bool rotate = true, float rotationOffset = 0f, float spin = 0f)
		{
			Main.instance.LoadProjectile(type);
			Texture2D tex = TextureAssets.Projectile[type].Value;
			int frames = Math.Max(1, Main.projFrames[type]);
			int fh = tex.Height / frames;
			var b = new Bullet
			{
				Position = pos,
				Velocity = vel,
				Texture = tex,
				Source = new Rectangle(0, 0, tex.Width, fh),
				Scale = scale,
				HitSize = hitSize,
				DamageMult = damage,
				RotateWithVelocity = rotate && spin == 0f,
				RotationOffset = rotationOffset,
			};
			if (spin != 0f)
				b.OnUpdate += x => x.Rotation += spin;
			if (frames > 1)
				b.OnUpdate += x => x.Source = new Rectangle(0, (x.Age / 5 % frames) * fh, tex.Width, fh);
			return b;
		}

		/// <summary>A flashing red area that can't hurt: telegraphs an attack.</summary>
		public static Bullet Warning(Rectangle area, int ticks, Color? color = null) => new()
		{
			Position = area.Center.ToVector2(),
			Harmful = false,
			Lifetime = ticks,
			OnDraw = b =>
			{
				float a = b.Age / 3 % 2 == 0 ? 0.55f : 0.25f;
				DrDraw.Rect(area.X, area.Y, area.Width, area.Height, (color ?? new Color(255, 0, 0)) * a);
			},
		};

		/// <summary>A random point on a circle around the box, outside it.</summary>
		public static Vector2 AroundBox(Rectangle box, float radius = 140f)
			=> box.Center.ToVector2() + Main.rand.NextFloat(MathHelper.TwoPi).ToRotationVector2() * radius;
	}

	/// <summary>Spawns something every few ticks for most of the turn.</summary>
	public abstract class RepeatingAttack : EnemyAttack
	{
		public int Every = 20;
		/// <summary>Stop spawning this many ticks before the turn ends so the last bullets can leave.</summary>
		public int StopBeforeEnd = 40;
		public int FirstAt;

		public override void Update(BattleSystem battle, int tick)
		{
			if (tick < FirstAt || tick > Duration - StopBeforeEnd)
				return;
			if ((tick - FirstAt) % Math.Max(1, Every) == 0)
				Spawn(battle, (tick - FirstAt) / Math.Max(1, Every));
		}

		protected abstract void Spawn(BattleSystem battle, int index);
	}

	/// <summary>Bullets fall from above the box.</summary>
	public class Rain : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public float SpeedMin = 1.8f, SpeedMax = 2.6f;
		public float Wobble = 0.4f;
		public bool FromBelow;

		public Rain(Func<Vector2, Vector2, Bullet> make, int every = 10)
		{
			Make = make;
			Every = every;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			float x = Main.rand.NextFloat(box.Left + 6, box.Right - 6);
			float speed = Main.rand.NextFloat(SpeedMin, SpeedMax);
			Vector2 pos = FromBelow ? new Vector2(x, box.Bottom + 24) : new Vector2(x, box.Top - 24);
			Bullet b = Make(pos, new Vector2(0, FromBelow ? -speed : speed));
			if (Wobble > 0)
			{
				float phase = Main.rand.NextFloat(MathHelper.TwoPi);
				float w = Wobble;
				b.OnUpdate += x2 => x2.Position.X += (float)Math.Sin(x2.Age / 12f + phase) * w;
			}
			battle.Spawn(b);
		}
	}

	/// <summary>Bullets fly straight across the box from the side, at random heights.</summary>
	public class SideShots : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public float Speed = 3f;
		/// <summary>-1 from the left, 1 from the right, 0 either.</summary>
		public int Side = 1;
		/// <summary>Aim at the SOUL's height half the time.</summary>
		public bool AimHalf = true;

		public SideShots(Func<Vector2, Vector2, Bullet> make, int every = 18)
		{
			Make = make;
			Every = every;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			int side = Side != 0 ? Side : (Main.rand.NextBool() ? 1 : -1);
			float y = AimHalf && index % 2 == 0 ? battle.SoulCenter.Y : Main.rand.NextFloat(box.Top + 8, box.Bottom - 8);
			var pos = new Vector2(side > 0 ? box.Right + 30 : box.Left - 30, y);
			battle.Spawn(Make(pos, new Vector2(-side * Speed, 0)));
		}
	}

	/// <summary>Fans of bullets aimed at the SOUL from points around the box.</summary>
	public class AimedBursts : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public int Count = 3;
		public float Spread = 0.35f;
		public float Speed = 2.2f;

		public AimedBursts(Func<Vector2, Vector2, Bullet> make, int every = 45)
		{
			Make = make;
			Every = every;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Vector2 from = Shots.AroundBox(battle.Box);
			float baseAngle = (battle.SoulCenter - from).ToRotation();
			for (int i = 0; i < Count; i++)
			{
				float a = baseAngle + (Count == 1 ? 0 : MathHelper.Lerp(-Spread, Spread, i / (float)(Count - 1)));
				battle.Spawn(Make(from, a.ToRotationVector2() * Speed));
			}
		}
	}

	/// <summary>Bullets that drop in and bounce on the bottom of the box (slime gel).</summary>
	public class Bouncers : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public float Gravity = 0.09f;
		public float Bounce = 0.92f;

		public Bouncers(Func<Vector2, Vector2, Bullet> make, int every = 28)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 80;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			bool fromLeft = Main.rand.NextBool();
			var pos = new Vector2(fromLeft ? box.Left - 20 : box.Right + 20, box.Top + Main.rand.NextFloat(0, 40));
			var vel = new Vector2((fromLeft ? 1 : -1) * Main.rand.NextFloat(0.8f, 1.6f), Main.rand.NextFloat(-1f, 0.5f));
			Bullet b = Make(pos, vel);
			b.Acceleration = new Vector2(0, Gravity);
			float floor = box.Bottom - 6 - b.HitSize.Y / 2f;
			float bounce = Bounce;
			b.OnUpdate += x =>
			{
				if (x.Position.Y > floor && x.Velocity.Y > 0)
				{
					x.Position.Y = floor;
					x.Velocity.Y = -Math.Max(2.2f, x.Velocity.Y * bounce);
				}
			};
			b.Lifetime = 420;
			battle.Spawn(b);
		}
	}

	/// <summary>A warning lane flashes over the SOUL, then something big charges through it.</summary>
	public class LaneDash : RepeatingAttack
	{
		/// <summary>Builds the charging bullet at a start position, travelling in a direction.</summary>
		public Func<Vector2, Vector2, Bullet> Make;
		public int Warn = 34;
		public float Speed = 7f;
		public bool AllowVertical;
		/// <summary>Always come straight down from above (a slime's hop).</summary>
		public bool FromTopOnly;
		public float LaneWidth = 30f;

		public LaneDash(Func<Vector2, Vector2, Bullet> make, int every = 70)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 80;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			bool vertical = FromTopOnly || AllowVertical && index % 2 == 1;
			bool fromStart = FromTopOnly || Main.rand.NextBool();
			Vector2 soul = battle.SoulCenter;
			float lane = vertical ? soul.X : soul.Y;
			Rectangle area = vertical
				? new Rectangle((int)(lane - LaneWidth / 2), box.Top, (int)LaneWidth, box.Height)
				: new Rectangle(box.Left, (int)(lane - LaneWidth / 2), box.Width, (int)LaneWidth);
			battle.Spawn(Shots.Warning(area, Warn));

			Vector2 dir = vertical ? new Vector2(0, fromStart ? 1 : -1) : new Vector2(fromStart ? 1 : -1, 0);
			Vector2 start = vertical
				? new Vector2(lane, fromStart ? box.Top - 70 : box.Bottom + 70)
				: new Vector2(fromStart ? box.Left - 70 : box.Right + 70, lane);
			Bullet b = Make(start, dir);
			b.Velocity = Vector2.Zero;
			b.Harmful = false;
			b.Lifetime = Warn + 140;
			b.OffscreenMargin = 400f;
			int warn = Warn;
			float speed = Speed;
			b.OnUpdate += x =>
			{
				if (x.Age == warn)
				{
					x.Harmful = true;
					x.Velocity = dir * speed;
				}
			};
			battle.Spawn(b);
		}
	}

	/// <summary>Rings of bullets close in on the box centre, with a gap to slip through.</summary>
	public class ClosingRing : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public int Count = 16;
		public int GapSize = 2;
		public float Radius = 120f;
		public float Speed = 1.1f;

		public ClosingRing(Func<Vector2, Vector2, Bullet> make, int every = 75)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 100;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Vector2 center = battle.Box.Center.ToVector2();
			int gap = Main.rand.Next(Count);
			for (int i = 0; i < Count; i++)
			{
				if ((i - gap + Count) % Count < GapSize)
					continue;
				Vector2 dir = (MathHelper.TwoPi * i / Count).ToRotationVector2();
				Bullet b = Make(center + dir * Radius, -dir * Speed);
				b.Lifetime = (int)(Radius / Speed) - 5;
				battle.Spawn(b);
			}
		}
	}

	/// <summary>Columns of bullets sweep across the box with one gap (a horde, a wall).</summary>
	public class Walls : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public float Speed = 1.6f;
		public float Spacing = 15f;
		public float GapSize = 40f;
		/// <summary>-1 from the left, 1 from the right, 0 alternate.</summary>
		public int Side = 1;

		public Walls(Func<Vector2, Vector2, Bullet> make, int every = 70)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 90;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			int side = Side != 0 ? Side : (index % 2 == 0 ? 1 : -1);
			float gapCenter = Main.rand.NextFloat(box.Top + GapSize / 2 + 6, box.Bottom - GapSize / 2 - 6);
			float x = side > 0 ? box.Right + 20 : box.Left - 20;
			for (float y = box.Top + 4; y <= box.Bottom - 4; y += Spacing)
			{
				if (Math.Abs(y - gapCenter) < GapSize / 2)
					continue;
				battle.Spawn(Make(new Vector2(x, y), new Vector2(-side * Speed, 0)));
			}
		}
	}

	/// <summary>A worm snakes across the box: a head and body segments following the same wavy path.</summary>
	public class Snake : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> MakeHead;
		public Func<Vector2, Vector2, Bullet> MakeBody;
		public int Segments = 8;
		public int SegmentLag = 6;
		public float Speed = 2.4f;
		public float Amplitude = 30f;
		public float Wavelength = 50f;
		/// <summary>-1 from the left, 1 from the right, 0 alternate.</summary>
		public int Side;

		public Snake(Func<Vector2, Vector2, Bullet> head, Func<Vector2, Vector2, Bullet> body, int every = 110)
		{
			MakeHead = head;
			MakeBody = body;
			Every = every;
			StopBeforeEnd = 120;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			bool fromRight = Side != 0 ? Side > 0 : index % 2 == 0;
			float baseY = Main.rand.NextFloat(box.Top + Amplitude, box.Bottom - Amplitude);
			float startX = fromRight ? box.Right + 40 : box.Left - 40;
			float dir = fromRight ? -1 : 1;
			float speed = Speed, amp = Amplitude, wl = Wavelength;
			for (int i = 0; i <= Segments; i++)
			{
				Bullet b = i == 0 ? MakeHead(new Vector2(startX, baseY), Vector2.Zero) : MakeBody(new Vector2(startX, baseY), Vector2.Zero);
				b.StartDelay = i * SegmentLag;
				b.Lifetime = 400;
				b.OffscreenMargin = 300f;
				b.OnUpdate += x =>
				{
					// Follow the path by age: every segment walks the same curve, just later
					float t = x.Age;
					var p = new Vector2(startX + dir * speed * t, baseY + (float)Math.Sin(speed * t / wl * MathHelper.TwoPi / 2f) * amp);
					x.Velocity = p - x.Position;
				};
				battle.Spawn(b);
			}
		}
	}

	/// <summary>Slow bullets that steer toward the SOUL for a while.</summary>
	public class Homing : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public float Speed = 1.6f;
		public float Turn = 0.04f;
		public int SteerTicks = 90;

		public Homing(Func<Vector2, Vector2, Bullet> make, int every = 40)
		{
			Make = make;
			Every = every;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Vector2 from = Shots.AroundBox(battle.Box, 130f);
			Vector2 vel = Vector2.Normalize(battle.SoulCenter - from) * Speed;
			Bullet b = Make(from, vel);
			float speed = Speed, turn = Turn;
			int steer = SteerTicks;
			b.Lifetime = 300;
			b.OnUpdate += x =>
			{
				if (x.Age > steer)
					return;
				float want = (battle.SoulCenter - x.Position).ToRotation();
				float have = x.Velocity.ToRotation();
				float diff = MathHelper.WrapAngle(want - have);
				x.Velocity = (have + MathHelper.Clamp(diff, -turn, turn)).ToRotationVector2() * speed;
			};
			battle.Spawn(b);
		}
	}

	/// <summary>A warning column flashes, then spikes shoot up from the floor of the box.</summary>
	public class FloorSpikes : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public int Warn = 30;
		public float Width = 22f;
		public float Speed = 6f;
		public bool FromTop;

		public FloorSpikes(Func<Vector2, Vector2, Bullet> make, int every = 35)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 70;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			float x = index % 2 == 0 ? battle.SoulCenter.X : Main.rand.NextFloat(box.Left + Width / 2, box.Right - Width / 2);
			x = MathHelper.Clamp(x, box.Left + Width / 2, box.Right - Width / 2);
			battle.Spawn(Shots.Warning(new Rectangle((int)(x - Width / 2), box.Top, (int)Width, box.Height), Warn, new Color(120, 200, 255)));
			Vector2 dir = new(0, FromTop ? 1 : -1);
			Bullet b = Make(new Vector2(x, FromTop ? box.Top - 30 : box.Bottom + 30), dir);
			b.Velocity = Vector2.Zero;
			b.Harmful = false;
			b.Lifetime = Warn + 120;
			int warn = Warn;
			float speed = Speed;
			b.OnUpdate += s =>
			{
				if (s.Age == warn)
				{
					s.Harmful = true;
					s.Velocity = dir * speed;
				}
			};
			battle.Spawn(b);
		}
	}

	/// <summary>Bullets orbit the box centre on a shrinking circle.</summary>
	public class Orbiters : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public int Count = 6;
		public float Radius = 110f;
		public float Shrink = 0.35f;
		public float AngularSpeed = 0.025f;

		public Orbiters(Func<Vector2, Vector2, Bullet> make, int every = 120)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 150;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Vector2 center = battle.Box.Center.ToVector2();
			float start = Main.rand.NextFloat(MathHelper.TwoPi);
			float dirSign = index % 2 == 0 ? 1 : -1;
			float radius0 = Radius, shrink = Shrink, w = AngularSpeed * dirSign;
			for (int i = 0; i < Count; i++)
			{
				float a0 = start + MathHelper.TwoPi * i / Count;
				Bullet b = Make(center + a0.ToRotationVector2() * radius0, Vector2.Zero);
				b.RotateWithVelocity = false;
				b.Lifetime = (int)(radius0 / shrink);
				b.OnUpdate += x =>
				{
					float r = radius0 - shrink * x.Age;
					Vector2 p = center + (a0 + w * x.Age).ToRotationVector2() * r;
					x.Velocity = p - x.Position;
				};
				battle.Spawn(b);
			}
		}
	}

	/// <summary>Flyers swoop in from the right in waves.</summary>
	public class Swoopers : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public float Speed = 2.2f;
		public float Amplitude = 35f;

		public Swoopers(Func<Vector2, Vector2, Bullet> make, int every = 30)
		{
			Make = make;
			Every = every;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			bool fromRight = index % 3 != 2;
			float baseY = Main.rand.NextFloat(box.Top + 15, box.Bottom - 15);
			float startX = fromRight ? box.Right + 40 : box.Left - 40;
			float dir = fromRight ? -1 : 1;
			float speed = Speed, amp = Amplitude, phase = Main.rand.NextFloat(MathHelper.TwoPi);
			Bullet b = Make(new Vector2(startX, baseY), new Vector2(dir * speed, 0));
			b.OnUpdate += x => x.Position.Y = baseY + (float)Math.Sin(x.Age / 18f + phase) * amp;
			battle.Spawn(b);
		}
	}

	/// <summary>Runs several attacks at once.</summary>
	public class Combo : EnemyAttack
	{
		private readonly EnemyAttack[] parts;

		public Combo(int duration, params EnemyAttack[] parts)
		{
			this.parts = parts;
			Duration = duration;
			foreach (var p in parts)
				p.Duration = duration;
		}

		public override void Update(BattleSystem battle, int tick)
		{
			foreach (var p in parts)
				p.Update(battle, tick);
		}
	}

	public static class AttackExtensions
	{
		/// <summary>Sets the turn length (and returns the attack, for building in one expression).</summary>
		public static T Lasting<T>(this T attack, int ticks) where T : EnemyAttack
		{
			attack.Duration = ticks;
			return attack;
		}
	}
}
