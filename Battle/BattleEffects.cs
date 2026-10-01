using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using MercyMode.Deltarune;

namespace MercyMode.Battle
{
	/// <summary>
	/// A short animation on the battle screen. <see cref="Step"/> runs every tick with <c>dt</c> in Deltarune frames
	/// (0.5 at 60 fps): the motion follows the original's per-frame numbers, drawn at Terraria's frame rate.
	/// </summary>
	public abstract class BattleEffect
	{
		public bool Done;
		/// <summary>Drawn with the bullets, over the box (attack sparks, smoke), instead of under it.</summary>
		public virtual bool WithBullets => false;
		public abstract void Step(float dt);
		public abstract void Draw();
	}

	/// <summary>White silhouettes of textures, for Deltarune's d3d_set_fog(c_white) flashes.</summary>
	public static class WhiteMask
	{
		private static readonly Dictionary<Texture2D, Texture2D> cache = new();

		public static Texture2D Of(Texture2D tex)
		{
			if (tex == null)
				return null;
			if (cache.TryGetValue(tex, out var mask) && !mask.IsDisposed)
				return mask;
			var data = new Color[tex.Width * tex.Height];
			tex.GetData(data);
			for (int i = 0; i < data.Length; i++)
			{
				byte a = data[i].A;
				data[i] = new Color(a, a, a, a); // premultiplied white
			}
			mask = new Texture2D(Main.graphics.GraphicsDevice, tex.Width, tex.Height);
			mask.SetData(data);
			cache[tex] = mask;
			return mask;
		}

		public static void Clear()
		{
			foreach (var m in cache.Values)
				m.Dispose();
			cache.Clear();
		}
	}

	/// <summary>How the enemy looked on its last drawn frame, so spare/death animations can play after it's gone.</summary>
	public struct EnemySnapshot
	{
		public Texture2D Texture;
		public Rectangle Frame;
		public Vector2 Position;
		public float Rotation;
		public float Scale;
		public Color Color;
		public bool Valid;

		public void Draw(Vector2 offset, Color color) =>
			DrDraw.Sb.Draw(Texture, Position + offset, Frame, color, Rotation, Frame.Size() / 2f, Scale, SpriteEffects.None, 0f);

		public void DrawWhite(Vector2 offset, float alpha) =>
			DrDraw.Sb.Draw(WhiteMask.Of(Texture), Position + offset, Frame, Color.White * MathHelper.Clamp(alpha, 0f, 1f), Rotation, Frame.Size() / 2f, Scale, SpriteEffects.None, 0f);
	}

	/// <summary>
	/// obj_dmgwriter: the number pops up, slides right, bounces twice, un-squashes, then rises and fades after 35
	/// frames. Damage digits use spr_numbersfontbig; MISS/MAX use spr_battlemsg.
	/// </summary>
	public class DamageNumber : BattleEffect
	{
		// spr_battlemsg frames: 0 MISS, 1 DOWN, 2 MAX, 3 UP (obj_dmgwriter messages 1-4)
		public const int MissFrame = 0, MaxFrame = 2;

		private readonly int number;
		private readonly int message; // spr_battlemsg frame, or -1 for a number
		private readonly Color color;
		private float x, y;
		private readonly float ystart;
		private float vspeed, hspeed, vstart;
		private int bounces;
		private float stretch = 0.2f;
		private bool stretchGo = true;
		private float kill;
		private float killTimer;
		private bool killActive;
		private readonly int delay;
		private float delayTimer;
		private bool launched;

		/// <param name="x">Left of the writer: text is right-aligned at x + 30, like Deltarune's.</param>
		public DamageNumber(float x, float y, int number, Color color, int message = -1, int delay = 2)
		{
			this.x = x;
			this.y = ystart = y;
			this.number = number;
			this.color = color;
			this.message = message;
			this.delay = delay;
		}

		public override void Step(float dt)
		{
			delayTimer += dt;
			if (!launched && delayTimer >= delay)
			{
				launched = true;
				vspeed = -5 - Main.rand.NextFloat(2f);
				hspeed = 10;
				vstart = vspeed;
			}
			if (!launched)
				return;

			if (hspeed > 0) hspeed -= dt;
			if (Math.Abs(hspeed) < 1) hspeed = 0;
			if (bounces < 2)
				vspeed += dt;
			if (y > ystart && bounces < 2 && !killActive)
			{
				y = ystart;
				vspeed = vstart / 2;
				bounces++;
			}
			if (bounces >= 2 && !killActive)
			{
				vspeed = 0;
				y = ystart;
			}
			if (stretchGo)
				stretch += 0.4f * dt;
			if (stretch >= 1.2f)
			{
				stretch = 1f;
				stretchGo = false;
			}
			killTimer += dt;
			if (killTimer > 35)
				killActive = true;
			if (killActive)
			{
				kill += 0.08f * dt;
				y -= 4 * dt;
			}
			if (kill > 1)
				Done = true;
			x += hspeed * dt;
			y += vspeed * dt;
		}

		public override void Draw()
		{
			if (!launched)
				return;
			var scale = new Vector2(2 - stretch, stretch + kill);
			Color c = color * (1 - kill);
			float right = x + 30;
			if (message >= 0 && DeltaruneAssets.Sprite("spr_battlemsg") is DrSprite msg)
			{
				// origin (74, 0): drawn from its right edge
				DrDraw.Sb.Draw(msg.Frame(message), new Vector2(right, y), null, c, 0f, msg.Origin, scale, SpriteEffects.None, 0f);
				return;
			}
			string s = message == MissFrame ? "MISS" : message == MaxFrame ? "MAX" : number.ToString();
			DrSprite digits = DeltaruneAssets.Sprite("spr_numbersfontbig");
			if (message < 0 && digits != null && digits.Frames.Length >= 10)
			{
				float w = digits.Width * scale.X;
				float px = right - w * s.Length;
				foreach (char ch in s)
				{
					DrDraw.Sb.Draw(digits.Frame(ch - '0'), new Vector2(px, y), null, c, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
					px += w;
				}
				return;
			}
			DrDraw.Text(s, right - DrDraw.Measure(s, DrDraw.BigFont) * scale.X, y, c, DrDraw.BigFont, 1f);
		}
	}

	/// <summary>A gold +X% popup shown when an ACT raises MERCY.</summary>
	public sealed class MercyGainPopup : BattleEffect
	{
		private readonly string amount;
		private float x, y;
		private readonly float ystart;
		private float vspeed, hspeed, vstart;
		private int bounces;
		private float stretch = 0.2f;
		private bool stretchGo = true;
		private float kill;
		private float killTimer;
		private bool killActive;
		private float delayTimer;
		private bool launched;

		public MercyGainPopup(Vector2 center, float amount)
		{
			x = center.X - 30f;
			y = ystart = center.Y;
			this.amount = Math.Round(amount).ToString(System.Globalization.CultureInfo.InvariantCulture);
		}

		public override void Step(float dt)
		{
			delayTimer += dt;
			if (!launched && delayTimer >= 2f)
			{
				launched = true;
				vspeed = -5f - Main.rand.NextFloat(2f);
				hspeed = 10f;
				vstart = vspeed;
			}
			if (!launched)
				return;

			if (hspeed > 0f)
				hspeed -= dt;
			if (Math.Abs(hspeed) < 1f)
				hspeed = 0f;
			if (bounces < 2)
				vspeed += dt;
			if (y > ystart && bounces < 2 && !killActive)
			{
				y = ystart;
				vspeed = vstart / 2f;
				bounces++;
			}
			if (bounces >= 2 && !killActive)
			{
				vspeed = 0f;
				y = ystart;
			}
			if (stretchGo)
				stretch += 0.4f * dt;
			if (stretch >= 1.2f)
			{
				stretch = 1f;
				stretchGo = false;
			}
			killTimer += dt;
			if (killTimer > 35)
				killActive = true;
			if (killActive)
			{
				kill += 0.08f * dt;
				y -= 4f * dt;
			}
			if (kill > 1f)
				Done = true;
			x += hspeed * dt;
			y += vspeed * dt;
		}

		public override void Draw()
		{
			if (!launched)
				return;
			Vector2 scale = new(2f - stretch, stretch + kill);
			float alpha = 1f - kill;
			DrSprite digits = DeltaruneAssets.Sprite("spr_numbersfontbig_gold");
			const string goldCharacters = "0123456789+-%";
			if (digits != null && digits.Frames.Length >= goldCharacters.Length)
			{
				string text = "+" + amount + "%";
				float right = x + 30f;
				float px = right - text.Length * digits.Width * scale.X;
				foreach (char ch in text)
				{
					int frame = goldCharacters.IndexOf(ch);
					if (frame >= 0)
						DrDraw.Sb.Draw(digits.Frame(frame), new Vector2(px, y), null,
							Color.White * alpha, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
					px += digits.Width * scale.X;
				}
				return;
			}
			string fallback = "+" + amount + "%";
			DrDraw.Text(fallback, x + 30f - DrDraw.Measure(fallback, DrDraw.BigFont) * scale.X,
				y, MercyMode.MercyYellow * alpha, DrDraw.BigFont, scale.X);
		}
	}

	/// <summary>obj_heartburst: three heart outlines stretch out and fade over 10 frames.</summary>
	public class HeartBurst : BattleEffect
	{
		private readonly Vector2 center;
		private float burst;

		/// <param name="heartTopLeft">The heart's x/y (top-left); the burst is centred 9 px in.</param>
		public HeartBurst(Vector2 heartTopLeft)
		{
			center = heartTopLeft + new Vector2(9, 9);
		}

		public override void Step(float dt)
		{
			burst += dt;
			if (burst > 10)
				Done = true;
		}

		public override void Draw()
		{
			float b = burst;
			void outline(string spr, float sx, float sy, float a)
			{
				if (!DrDraw.Sprite(spr, 0, center.X, center.Y, Color.White, new Vector2(sx, sy), 0f, MathHelper.Clamp(a, 0f, 1f)))
					DrDraw.Outline(center.X - 10 * sx, center.Y - 10 * sy, 20 * sx, 20 * sy, Color.White * MathHelper.Clamp(a, 0f, 1f));
			}
			outline("spr_heartoutline2", 0.25f + b, 0.25f + b / 2f, 0.8f - b / 6f);
			outline("spr_heartoutline", 0.25f + b / 1.5f, 0.25f + b / 3f, 1f - b / 6f);
			outline("spr_heartoutline", 0.2f + b / 2.5f, 0.2f + b / 5f, 1.2f - b / 6f);
		}
	}

	/// <summary>Little spinning stars (spr_sparestar_anim) used by the spare and heal animations.</summary>
	public class StarParticle : BattleEffect
	{
		private Vector2 pos, vel;
		private readonly Vector2 accel;
		private readonly float friction;
		private readonly float spin;
		private readonly Color color;
		private float alpha = 2f;
		private float angle;
		private float frame;
		private float t;
		private readonly int fadeFrom;

		public StarParticle(Vector2 pos, Vector2 vel, Vector2 accel, float friction, float spin, Color color, int fadeFrom)
		{
			this.pos = pos;
			this.vel = vel;
			this.accel = accel;
			this.friction = friction;
			this.spin = spin;
			this.color = color;
			this.fadeFrom = fadeFrom;
			angle = Main.rand.NextFloat(360f);
		}

		public override void Step(float dt)
		{
			t += dt;
			vel += accel * dt;
			float f = friction * dt;
			if (f > 0 && vel != Vector2.Zero)
				vel = vel.Length() <= f ? Vector2.Zero : vel - Vector2.Normalize(vel) * f;
			pos += vel * dt;
			frame += 0.25f * dt;
			if (t >= fadeFrom)
			{
				angle += spin * dt;
				alpha -= 0.1f * dt;
			}
			if (alpha <= 0)
				Done = true;
		}

		public override void Draw()
		{
			if (!DrDraw.Sprite("spr_sparestar_anim", (int)frame, pos.X, pos.Y, color, 2f, MathHelper.ToRadians(-angle), MathHelper.Clamp(alpha, 0f, 1f)))
				DrDraw.Rect(pos.X - 3, pos.Y - 3, 6, 6, color * MathHelper.Clamp(alpha, 0f, 1f));
		}
	}

	/// <summary>spr_lightfairy sparkles that fly off the hero on a perfect hit (friction -0.25: they speed up).</summary>
	public class CritSparkle : BattleEffect
	{
		private Vector2 pos;
		private float hspeed;
		private float frame;

		public CritSparkle(Vector2 pos)
		{
			this.pos = pos;
			hspeed = 2 + Main.rand.NextFloat(4f);
		}

		public override void Step(float dt)
		{
			hspeed += 0.25f * dt;
			pos.X += hspeed * dt;
			frame += 0.25f * dt;
			if (frame >= 5 || pos.X > BattleConstants.ScreenWidth + 20)
				Done = true;
		}

		public override void Draw()
		{
			if (!DrDraw.Sprite("spr_lightfairy", (int)frame, pos.X, pos.Y, Color.White, 2f))
				DrDraw.Rect(pos.X - 2, pos.Y - 2, 4, 4, Color.White);
		}
	}

	/// <summary>
	/// obj_spareanim: the enemy goes white over 5 frames while stars burst out, then white afterimages streak to
	/// the right and fade.
	/// </summary>
	public class SpareAnimation : BattleEffect
	{
		private readonly EnemySnapshot snap;
		private readonly List<StarParticle> stars = new();
		private float t, afterimage, tone, neotone;

		public SpareAnimation(EnemySnapshot snap)
		{
			this.snap = snap;
			DeltaruneAssets.Play("spare", Terraria.ID.SoundID.Item4);
		}

		public override void Step(float dt)
		{
			if (t >= 6 && t <= 26)
				afterimage += dt;
			// Two stars per Deltarune frame between frames 1 and 5: spawn when t reaches a whole frame
			float next = t + dt;
			bool newFrame = (int)next != (int)t;
			if (newFrame && next >= 1 && next < 6)
			{
				Rectangle area = Area();
				for (int i = 0; i < 2; i++)
				{
					var pos = new Vector2(Main.rand.NextFloat(area.Left, area.Right), Main.rand.NextFloat(area.Top, area.Bottom));
					// hspeed -3 with gravity pulling right (gravity_direction 0)
					stars.Add(new StarParticle(pos, new Vector2(-3, 0), new Vector2(0.5f, 0), 0f, 10f, Color.White, 5));
				}
			}
			foreach (var s in stars)
				s.Step(dt);
			stars.RemoveAll(s => s.Done);
			if (t >= 5 && t < 10)
				tone += dt;
			if (t >= 9)
			{
				neotone += dt;
				if (neotone >= 30)
					Done = true;
			}
			t += dt;
		}

		private Rectangle Area()
		{
			float w = snap.Frame.Width * snap.Scale, h = snap.Frame.Height * snap.Scale;
			return new Rectangle((int)(snap.Position.X - w / 2), (int)(snap.Position.Y - h / 2), (int)w, (int)h);
		}

		public override void Draw()
		{
			if (!snap.Valid)
				return;
			if (t >= 6 && t <= 26)
			{
				snap.DrawWhite(new Vector2(afterimage * 4, 0), 0.7f - afterimage / 25f);
				snap.DrawWhite(new Vector2(afterimage * 8, 0), 0.4f - afterimage / 30f);
			}
			if (t < 6)
			{
				if (t < 5)
					snap.Draw(Vector2.Zero, snap.Color * (1 - neotone / 4f));
				snap.DrawWhite(Vector2.Zero, Math.Min(1f, t / 5f) - tone / 5f);
			}
			foreach (var s in stars)
				s.Draw();
		}
	}

	/// <summary>
	/// obj_deathanim: the enemy turns red and breaks into blocks that peel off to the right, row by row.
	/// </summary>
	public class DeathAnimation : BattleEffect
	{
		private readonly EnemySnapshot snap;
		private readonly int bsize, xs, ys;
		private readonly float[,] bx, bspeed, bsin;
		private float redup;
		private float t;

		public DeathAnimation(EnemySnapshot snap)
		{
			this.snap = snap;
			int truew = snap.Frame.Width, trueh = snap.Frame.Height;
			bsize = truew >= 100 || trueh >= 100 ? 16 : truew >= 50 || trueh >= 50 ? 8 : 6;
			xs = (int)Math.Ceiling(truew / (float)bsize);
			ys = (int)Math.Ceiling(trueh / (float)bsize);
			bx = new float[xs + 1, ys + 1];
			bspeed = new float[xs + 1, ys + 1];
			bsin = new float[xs + 1, ys + 1];
			for (int i = 0; i <= xs; i++)
				for (int j = 0; j <= ys; j++)
					bsin[i, j] = 4 + j * 3 - i;
		}

		public override void Step(float dt)
		{
			t += dt;
			redup = Math.Min(10f, redup + dt);
			for (int i = 0; i <= xs; i++)
				for (int j = 0; j <= ys; j++)
				{
					if (bsin[i, j] <= 0)
						bspeed[i, j] += dt;
					bx[i, j] += bspeed[i, j] * dt;
					bsin[i, j] -= dt;
				}
			if (bspeed[0, ys] >= 12)
				Done = true;
		}

		public override void Draw()
		{
			if (!snap.Valid)
				return;
			float r = redup / 10f;
			Color tint = new(255, (int)(255 * (1 - r)), (int)(255 * (1 - r)));
			// Pieces keep the sprite's rotation; they peel off toward the right of the screen
			float s = snap.Scale;
			Vector2 half = snap.Frame.Size() * s / 2f;
			for (int i = 0; i <= xs; i++)
				for (int j = 0; j <= ys; j++)
				{
					int sx = i * bsize, sy = j * bsize;
					if (sx >= snap.Frame.Width || sy >= snap.Frame.Height)
						continue;
					var src = new Rectangle(snap.Frame.X + sx, snap.Frame.Y + sy,
						Math.Min(bsize, snap.Frame.Width - sx), Math.Min(bsize, snap.Frame.Height - sy));
					float a = 1 - bspeed[i, j] / 12f;
					if (a <= 0)
						continue;
					Vector2 local = new Vector2(sx * s, sy * s) - half;
					var pos = snap.Position + local.RotatedBy(snap.Rotation) + new Vector2(bx[i, j] * s, 0);
					DrDraw.Sb.Draw(snap.Texture, pos, src, tint * a, snap.Rotation, Vector2.Zero, s, SpriteEffects.None, 0f);
				}
		}
	}
}
