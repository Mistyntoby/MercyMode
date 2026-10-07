using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using MercyMode.Deltarune;

namespace MercyMode.Battle
{
	/// <summary>
	/// One enemy bullet inside the battle, in Deltarune pixels. Mirrors obj_regularbullet: it hurts the SOUL
	/// once and disappears, and grazing it gives TP and shortens the turn.
	/// </summary>
	public class Bullet
	{
		public Vector2 Position;
		/// <summary>Pixels per tick.</summary>
		public Vector2 Velocity;
		public Vector2 Acceleration;

		/// <summary>Hitbox size, centred on Position.</summary>
		public Vector2 HitSize = new(8, 8);

		/// <summary>Deltarune sprite name, or null to use <see cref="Texture"/> or a plain shape.</summary>
		public string Sprite;
		public int Frame;
		/// <summary>A Terraria texture (for bosses' own minions).</summary>
		public Texture2D Texture;
		public Rectangle? Source;
		public float Scale = 1f;
		public float Rotation;
		public bool RotateWithVelocity;
		/// <summary>Added to the velocity angle when rotating with velocity, for sprites that don't face right.</summary>
		public float RotationOffset;
		public bool FlipX;
		public Color Color = Color.White;
		/// <summary>Drawn as a flat silhouette in <see cref="Color"/> (a tint on a dark sprite barely shows).</summary>
		public bool Solid;
		public float Alpha = 1f;

		/// <summary>Multiplies the boss's damage for this bullet.</summary>
		public float DamageMult = 1f;
		/// <summary>The enemy whose attack this is (its damage and name), when known.</summary>
		public Encounter Owner;
		public float GrazePoints = BattleConstants.DefaultGrazePoints;
		public float TimePoints = BattleConstants.DefaultTimePoints;
		public bool Grazed;

		/// <summary>Yellow SOUL shots it takes to break (0 = 1 for small bullets, 3 for big ones).</summary>
		public int Toughness;
		/// <summary>The most one yellow shot takes off it (0 = no cap): the charged shot can't clear armoured bullets in one go.</summary>
		public int MaxShotDamage;
		/// <summary>Light it gives off in the dark (radius in pixels, 0 = none): see <see cref="Underground"/>.</summary>
		public float Light;
		/// <summary>
		/// Hazards (rain, icicles, lava, falling rocks): a hit takes at least this share of the player's max HP, after
		/// defense, however weak the enemy. 0 = no floor (the enemy's contact damage, as usual).
		/// </summary>
		public float MinLifeShare;
		/// <summary>Undertale's coloured bones: 1 blue (only hurts a SOUL that's moving), 2 orange (only one that's still).</summary>
		public int Sans;
		/// <summary>Ticks left of a white flash (it was shot).</summary>
		public int Flash;
		/// <summary>Afterimages left behind while it moves (0 = none).</summary>
		public int Trail;
		private Vector2[] trailPos;
		private float[] trailRot;
		private int trailCount, trailHead;

		/// <summary>False for warnings and effects that can't hurt or be grazed.</summary>
		public bool Harmful = true;
		/// <summary>The blue SOUL can stand on its top (and ride along with it): a Sans-style platform.</summary>
		public bool Platform;
		/// <summary>Disappears when it hits the SOUL (false for beams, which keep firing).</summary>
		public bool DestroyOnHit = true;
		/// <summary>Ticks before the bullet appears and starts moving (for chains like worm segments).</summary>
		public int StartDelay;
		public bool Waiting => StartDelay > 0;
		public bool Dead;
		public int Age;
		/// <summary>Removed after this many ticks.</summary>
		public int Lifetime = 60 * 10;
		/// <summary>Removed once it leaves the screen by this much.</summary>
		public float OffscreenMargin = 80f;

		/// <summary>Extra behaviour, called every tick before moving.</summary>
		public Action<Bullet> OnUpdate;
		/// <summary>Plays the attack sound when spawned even though it's custom-drawn (custom drawing is usually a warning or beam).</summary>
		public bool SoundOnSpawn;
		/// <summary>Custom drawing instead of the sprite.</summary>
		public Action<Bullet> OnDraw;
		/// <summary>Custom collision (beams and other shapes that aren't a box around Position).</summary>
		public Func<Bullet, Rectangle, bool> HitTest;

		public Rectangle Hitbox => new(
			(int)(Position.X - HitSize.X / 2f), (int)(Position.Y - HitSize.Y / 2f), (int)HitSize.X, (int)HitSize.Y);

		/// <summary>Whether this bullet overlaps an area (the SOUL's hitbox, or its graze box).</summary>
		public bool Touches(Rectangle area) => HitTest != null ? HitTest(this, area) : Hitbox.Intersects(area);

		public void Update()
		{
			if (StartDelay > 0)
			{
				StartDelay--;
				return;
			}
			OnUpdate?.Invoke(this);
			if (Flash > 0)
				Flash--;
			if (Trail > 0 && Age % 2 == 0 && Velocity.LengthSquared() > 1f)
			{
				trailPos ??= new Vector2[Trail];
				trailRot ??= new float[Trail];
				trailPos[trailHead] = Position;
				trailRot[trailHead] = Rotation;
				trailHead = (trailHead + 1) % Trail;
				trailCount = Math.Min(Trail, trailCount + 1);
			}
			else if (Trail > 0 && trailCount > 0 && Age % 2 == 0)
				trailCount--; // stopped: the afterimages catch up
			Velocity += Acceleration;
			Position += Velocity;
			Age++;
			if (RotateWithVelocity && Velocity != Vector2.Zero)
				Rotation = Velocity.ToRotation() + RotationOffset;
			if (Age > Lifetime)
				Dead = true;
			if (Position.X < -OffscreenMargin || Position.X > BattleConstants.ScreenWidth + OffscreenMargin ||
				Position.Y < -OffscreenMargin || Position.Y > BattleConstants.ScreenHeight + OffscreenMargin)
				Dead = true;
		}

		public void Draw()
		{
			if (Waiting)
				return;
			if (OnDraw != null)
			{
				OnDraw(this);
				return;
			}
			DrawSprite();
		}

		/// <summary>Its sprite (and afterimages), ignoring <see cref="OnDraw"/>: for custom drawing that adds to it.</summary>
		public void DrawSprite()
		{
			// Afterimages, oldest faintest
			for (int k = 0; k < trailCount; k++)
			{
				int idx = ((trailHead - 1 - k) % Trail + Trail) % Trail;
				DrawAt(trailPos[idx], trailRot[idx], 0.45f * (1f - (k + 1f) / (trailCount + 1f)), false);
			}
			DrawAt(Position, Rotation, 1f, Flash > 0);
		}

		private void DrawAt(Vector2 pos, float rotation, float alphaMul, bool white)
		{
			var effects = FlipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
			Color color = (white ? Color.Lerp(Color, Color.White, 0.75f) : Color) * Alpha * alphaMul;
			if (Sprite != null && DeltaruneAssets.Sprite(Sprite) is DrSprite s)
			{
				DrDraw.Sb.Draw(s.Frame(Frame), pos, null, color, rotation, s.Origin, Scale, effects, 0f);
				return;
			}
			if (Texture != null)
			{
				Rectangle src = Source ?? Texture.Bounds;
				DrDraw.Sb.Draw(Solid ? WhiteMask.Of(Texture) : Texture, pos, src, color, rotation, src.Size() / 2f, Scale, effects, 0f);
				return;
			}
			// Fallback (the Deltarune sprite isn't in the chapter the assets come from): a shaded round bullet
			DrDraw.Ball(pos, Math.Max(HitSize.X, HitSize.Y) / 2f + 2f * Scale, color);
		}
	}
}
