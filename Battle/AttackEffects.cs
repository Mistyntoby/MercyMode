using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace MercyMode.Battle
{
	/// <summary>Small square sparks that fly out, slow down and fade: hits, breaks, landings.</summary>
	public sealed class Sparks : BattleEffect
	{
		public override bool WithBullets => true;
		private Vector2 pos, vel;
		private readonly Color color;
		private readonly float gravity;
		private float life = 1f;
		private readonly float fade;
		private readonly float size;

		public Sparks(Vector2 pos, Vector2 vel, Color color, float size = 3f, float gravity = 0f, float fade = 0.06f)
		{
			this.pos = pos;
			this.vel = vel;
			this.color = color;
			this.size = size;
			this.gravity = gravity;
			this.fade = fade;
		}

		/// <summary>A burst of sparks in every direction.</summary>
		public static void Burst(BattleSystem battle, Vector2 at, int count, Color color, float speed = 2f, float gravity = 0f)
		{
			for (int i = 0; i < count; i++)
			{
				Vector2 v = Main.rand.NextFloat(MathHelper.TwoPi).ToRotationVector2() * speed * Main.rand.NextFloat(0.4f, 1f);
				battle.AddEffect(new Sparks(at, v, color, Main.rand.NextFloat(2f, 4f), gravity));
			}
		}

		/// <summary>Dust kicked up along the floor of the box on both sides of a landing.</summary>
		public static void FloorDust(BattleSystem battle, Vector2 at, Color color, int count = 8)
		{
			for (int i = 0; i < count; i++)
			{
				float side = i % 2 == 0 ? 1f : -1f;
				var v = new Vector2(side * Main.rand.NextFloat(1f, 3.2f), -Main.rand.NextFloat(0.3f, 1.8f));
				battle.AddEffect(new Sparks(at, v, color, Main.rand.NextFloat(2f, 4f), 0.08f, 0.04f));
			}
		}

		public override void Step(float dt)
		{
			vel.Y += gravity * dt * 2f;
			vel *= (float)Math.Pow(0.92f, dt * 2f);
			pos += vel * dt * 2f;
			life -= fade * dt * 2f;
			if (life <= 0f)
				Done = true;
		}

		public override void Draw()
		{
			float s = size * (0.5f + life * 0.5f);
			DrDraw.Rect(pos.X - s / 2f, pos.Y - s / 2f, s, s, color * MathHelper.Clamp(life, 0f, 1f));
		}
	}

	/// <summary>An expanding ring: a slam's shockwave, an explosion, a SOUL mode change.</summary>
	public sealed class Shockwave : BattleEffect
	{
		public override bool WithBullets => true;
		private readonly Vector2 pos;
		private readonly Color color;
		private readonly float maxRadius;
		private float t;

		public Shockwave(Vector2 pos, Color color, float maxRadius = 40f)
		{
			this.pos = pos;
			this.color = color;
			this.maxRadius = maxRadius;
		}

		public override void Step(float dt)
		{
			t += dt / 10f;
			if (t >= 1f)
				Done = true;
		}

		public override void Draw()
		{
			float r = maxRadius * (1f - (1f - t) * (1f - t));
			float alpha = 1f - t;
			const int segments = 28;
			for (int i = 0; i < segments; i++)
			{
				float a0 = MathHelper.TwoPi * i / segments, a1 = MathHelper.TwoPi * (i + 1) / segments;
				DrDraw.Line(pos + a0.ToRotationVector2() * r, pos + a1.ToRotationVector2() * r, 2f + 2f * (1f - t), color * alpha);
			}
		}
	}

	/// <summary>A soft round puff that grows, drifts and fades: smoke behind rockets, steam, dust clouds.</summary>
	public sealed class Puff : BattleEffect
	{
		public override bool WithBullets => true;
		private Vector2 pos;
		private readonly Vector2 vel;
		private readonly Color color;
		private float radius;
		private readonly float grow;
		private float life = 1f;
		private readonly float fade;

		public Puff(Vector2 pos, Vector2 vel, Color color, float radius = 3f, float grow = 0.12f, float fade = 0.035f)
		{
			this.pos = pos;
			this.vel = vel;
			this.color = color;
			this.radius = radius;
			this.grow = grow;
			this.fade = fade;
		}

		public override void Step(float dt)
		{
			pos += vel * dt * 2f;
			radius += grow * dt * 2f;
			life -= fade * dt * 2f;
			if (life <= 0f)
				Done = true;
		}

		public override void Draw()
		{
			// A filled circle out of 2 px rows
			Color c = color * (MathHelper.Clamp(life, 0f, 1f) * 0.7f);
			for (float dy = -radius; dy < radius; dy += 2f)
			{
				float half = (float)Math.Sqrt(Math.Max(0f, radius * radius - (dy + 1f) * (dy + 1f)));
				DrDraw.Rect(pos.X - half, pos.Y + dy, half * 2f, 2f, c);
			}
		}
	}

	/// <summary>Bullets that leave something behind as they fly: smoke, embers, sparkles.</summary>
	public static class Emitters
	{
		/// <summary>A grey smoke trail (rockets, cannonballs, bombs), with a flicker of flame at the back.</summary>
		public static Bullet Smoking(this Bullet b, int every = 3)
		{
			b.OnUpdate += x =>
			{
				if (x.Age % every != 0 || x.Velocity.LengthSquared() < 0.2f || BattleSystem.Instance == null)
					return;
				Vector2 back = x.Position - Vector2.Normalize(x.Velocity) * (x.HitSize.X / 2f + 2f);
				var drift = new Vector2(Main.rand.NextFloat(-0.15f, 0.15f), -Main.rand.NextFloat(0.05f, 0.25f));
				float grey = Main.rand.NextFloat(0.45f, 0.7f);
				BattleSystem.Instance.AddEffect(new Puff(back, drift, new Color(grey, grey, grey), 2.5f, 0.14f, 0.03f));
				if (Main.rand.NextBool(2))
					BattleSystem.Instance.AddEffect(new Sparks(back, -x.Velocity * 0.15f, new Color(255, 170, 60), 2.5f, 0f, 0.12f));
			};
			return b;
		}

		/// <summary>Embers that fall off it (fireballs, flames).</summary>
		public static Bullet Fiery(this Bullet b, int every = 4)
		{
			b.OnUpdate += x =>
			{
				if (x.Age % every != 0 || BattleSystem.Instance == null)
					return;
				Color c = Main.rand.NextBool() ? new Color(255, 200, 60) : new Color(255, 100, 30);
				var v = new Vector2(Main.rand.NextFloat(-0.4f, 0.4f), -Main.rand.NextFloat(0.2f, 0.7f)) - x.Velocity * 0.1f;
				BattleSystem.Instance.AddEffect(new Sparks(x.Position + Main.rand.NextVector2Circular(3f, 3f), v, c, 2.5f, -0.02f, 0.07f));
			};
			return b;
		}

		/// <summary>Twinkling sparkles (magic, crystals).</summary>
		public static Bullet Sparkly(this Bullet b, Color color, int every = 5)
		{
			b.OnUpdate += x =>
			{
				if (x.Age % every != 0 || BattleSystem.Instance == null)
					return;
				BattleSystem.Instance.AddEffect(new Sparks(x.Position + Main.rand.NextVector2Circular(5f, 5f), Main.rand.NextVector2Circular(0.4f, 0.4f), Color.Lerp(color, Color.White, 0.4f), 2f, 0f, 0.08f));
			};
			return b;
		}

		/// <summary>Drips that fall away from it (gel, poison, blood).</summary>
		public static Bullet Dripping(this Bullet b, Color color, int every = 9)
		{
			b.OnUpdate += x =>
			{
				if (x.Age % every != 0 || BattleSystem.Instance == null)
					return;
				BattleSystem.Instance.AddEffect(new Sparks(x.Position + new Vector2(Main.rand.NextFloat(-3f, 3f), x.HitSize.Y / 2f), new Vector2(0f, 0.3f), color, 2.5f, 0.12f, 0.05f));
			};
			return b;
		}
	}

	/// <summary>Plays a Deltarune sprite's frames once at a spot (obj_animation), or a few sparks without the sprite.</summary>
	public sealed class SpriteAnim : BattleEffect
	{
		public override bool WithBullets => true;
		private readonly string sprite;
		private readonly Vector2 pos;
		private readonly float speed, scale;
		private float frame;
		private bool started;

		/// <param name="speed">Frames per Deltarune frame (image_speed).</param>
		public SpriteAnim(string sprite, Vector2 pos, float speed, float scale = 1f)
		{
			this.sprite = sprite;
			this.pos = pos;
			this.speed = speed;
			this.scale = scale;
		}

		public override void Step(float dt)
		{
			var s = Deltarune.DeltaruneAssets.Sprite(sprite);
			if (s == null)
			{
				if (!started && BattleSystem.Instance != null)
					Sparks.Burst(BattleSystem.Instance, pos, 5, new Color(255, 255, 120), 2f);
				Done = true;
				return;
			}
			started = true;
			frame += speed * dt;
			if (frame >= s.Frames.Length)
				Done = true;
		}

		public override void Draw()
		{
			if (Deltarune.DeltaruneAssets.Sprite(sprite) is Deltarune.DrSprite s && frame < s.Frames.Length)
				DrDraw.Sb.Draw(s.Frame((int)frame), pos, null, Color.White, 0f, s.Origin, scale, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0f);
		}
	}

	/// <summary>
	/// A beam from a weapon (the Last Prism, the Charged Blaster Cannon): it charges thin, fires wide, then fades. The
	/// Last Prism's is six rainbow rays converging a little way out into one beam.
	/// </summary>
	public sealed class BeamEffect : BattleEffect
	{
		public override bool WithBullets => true;
		private readonly Vector2 from, to;
		private readonly bool prism;
		private readonly float length;
		private float t;

		public BeamEffect(Vector2 from, Vector2 to, bool prism, float length = 30f)
		{
			this.from = from;
			this.to = to;
			this.prism = prism;
			this.length = length;
		}

		public override void Step(float dt)
		{
			t += dt;
			if (t >= length)
				Done = true;
		}

		public override void Draw() => WeaponBeam.Draw(from, to, t / length, prism, 1f);
	}

	/// <summary>Drawing for beams, shared by the effect and the duel's beam bullets.</summary>
	public static class WeaponBeam
	{
		/// <summary>A rainbow colour that cycles with time (and along the beam).</summary>
		public static Color Rainbow(float offset) =>
			Main.hslToRgb(((float)(Main.GlobalTimeWrappedHourly * 0.8f) + offset) % 1f, 1f, 0.6f);

		/// <param name="progress">0..1 through the beam's life: thin while it charges, full, then fading.</param>
		public static void Draw(Vector2 from, Vector2 to, float progress, bool prism, float alpha)
		{
			float width = progress < 0.2f ? MathHelper.Lerp(1f, 10f, progress / 0.2f) : progress > 0.75f ? 10f * (1f - (progress - 0.75f) / 0.25f) : 10f;
			float a = alpha * (progress > 0.85f ? 1f - (progress - 0.85f) / 0.15f : 1f);
			Vector2 dir = to - from;
			if (dir.LengthSquared() < 1f)
				return;
			Vector2 n = Vector2.Normalize(dir);
			Vector2 side = new(-n.Y, n.X);
			Vector2 start = from;
			if (prism)
			{
				// Six thin rays fan out of the prism and meet a little way along
				Vector2 meet = from + n * Math.Min(60f, dir.Length() * 0.3f);
				for (int i = 0; i < 6; i++)
				{
					float off = (i - 2.5f) * 7f;
					DrDraw.Line(from + side * off, meet, 2f, Rainbow(i / 6f) * a);
				}
				start = meet;
			}
			Color outer = prism ? Rainbow(0f) : new Color(90, 220, 255);
			DrDraw.Line(start, to, width * 1.8f, outer * (0.35f * a));
			DrDraw.Line(start, to, width, outer * (0.8f * a));
			DrDraw.Line(start, to, Math.Max(1f, width * 0.4f), Color.White * a);
		}

		/// <summary>Whether an area touches the beam (a line of the given half-width).</summary>
		public static bool Touches(Vector2 from, Vector2 to, float halfWidth, Rectangle area)
		{
			Vector2 c = area.Center.ToVector2();
			Vector2 d = to - from;
			float len2 = d.LengthSquared();
			float k = len2 < 0.01f ? 0f : MathHelper.Clamp(Vector2.Dot(c - from, d) / len2, 0f, 1f);
			Vector2 near = from + d * k;
			return Vector2.Distance(near, c) <= halfWidth + Math.Min(area.Width, area.Height) / 2f;
		}
	}
}
