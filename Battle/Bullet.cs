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
		public float Alpha = 1f;

		/// <summary>Multiplies the boss's damage for this bullet.</summary>
		public float DamageMult = 1f;
		public float GrazePoints = BattleConstants.DefaultGrazePoints;
		public float TimePoints = BattleConstants.DefaultTimePoints;
		public bool Grazed;

		/// <summary>False for warnings and effects that can't hurt or be grazed.</summary>
		public bool Harmful = true;
		public bool Dead;
		public int Age;
		/// <summary>Removed after this many ticks.</summary>
		public int Lifetime = 60 * 10;
		/// <summary>Removed once it leaves the screen by this much.</summary>
		public float OffscreenMargin = 80f;

		/// <summary>Extra behaviour, called every tick before moving.</summary>
		public Action<Bullet> OnUpdate;
		/// <summary>Custom drawing instead of the sprite.</summary>
		public Action<Bullet> OnDraw;

		public Rectangle Hitbox => new(
			(int)(Position.X - HitSize.X / 2f), (int)(Position.Y - HitSize.Y / 2f), (int)HitSize.X, (int)HitSize.Y);

		public void Update()
		{
			OnUpdate?.Invoke(this);
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
			if (OnDraw != null)
			{
				OnDraw(this);
				return;
			}
			var effects = FlipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
			if (Sprite != null && DeltaruneAssets.Sprite(Sprite) is DrSprite s)
			{
				DrDraw.Sb.Draw(s.Frame(Frame), Position, null, Color * Alpha, Rotation, s.Origin, Scale, effects, 0f);
				return;
			}
			if (Texture != null)
			{
				Rectangle src = Source ?? Texture.Bounds;
				DrDraw.Sb.Draw(Texture, Position, src, Color * Alpha, Rotation, src.Size() / 2f, Scale, effects, 0f);
				return;
			}
			// Fallback: a white diamond-ish square the size of the hitbox
			DrDraw.Rect(Position.X - HitSize.X / 2f, Position.Y - HitSize.Y / 2f, HitSize.X, HitSize.Y, Color * Alpha);
		}
	}
}
