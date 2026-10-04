using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace MercyMode.Battle
{
	/// <summary>
	/// A two-person weather report, after Lanino and Elnina (Deltarune chapter 3): each front is forecast on one half
	/// of the box (a flickering tint and its name), then hits that half while the other stays clear. RAIN pours down
	/// one side (left or right); SUN scorches any half for a moment. The forecast always lands on the SOUL's half,
	/// so every front means moving, and they come quicker as the turn goes on.
	/// </summary>
	public class Forecast : EnemyAttack
	{
		/// <summary>A raindrop at a point, falling at a speed.</summary>
		public Func<Vector2, Vector2, Bullet> Drop;
		public bool Rain = true, Sun = true;
		public int Warn = 44, Active = 36;
		/// <summary>Each front's warning is this much shorter than the last (never under <see cref="MinWarn"/>).</summary>
		public int Speedup = 4, MinWarn = 30;
		public int DropEvery = 3;
		public float DropSpeed = 6f;
		public Color RainColor = new(120, 255, 60), SunColor = new(255, 70, 50);
		public int FirstAt = 10;

		private int nextAt = -1, front;

		public Forecast(Func<Vector2, Vector2, Bullet> drop) => Drop = drop;

		public override void Update(BattleSystem battle, int tick)
		{
			if (nextAt < 0)
				nextAt = FirstAt;
			if (tick < nextAt)
				return;
			int warn = Math.Max(MinWarn, Warn - front * Speedup);
			// Fronts end before the turn does; drops still falling are given room to leave
			if (tick + warn + Active > Duration - 10)
			{
				nextAt = int.MaxValue;
				return;
			}
			bool rain = Rain && (!Sun || front % 2 == 0);
			Spawn(battle, rain, warn);
			front++;
			nextAt = tick + warn + Active;
		}

		private void Spawn(BattleSystem battle, bool rain, int warn)
		{
			Rectangle box = battle.Box;
			Vector2 soul = battle.SoulCenter;
			// Rain falls straight down, so it takes a left or right half; the sun can take any of the four
			bool vertical = rain || Main.rand.NextBool();
			Rectangle zone;
			if (vertical)
			{
				bool left = soul.X < box.Center.X;
				zone = new Rectangle(left ? box.Left : box.Center.X, box.Top, box.Width / 2, box.Height);
			}
			else
			{
				bool top = soul.Y < box.Center.Y;
				zone = new Rectangle(box.Left, top ? box.Top : box.Center.Y, box.Width, box.Height / 2);
			}
			Color color = rain ? RainColor : SunColor;
			string word = rain ? "RAIN" : "SUN";
			int active = Active;
			var drop = Drop;
			int every = Math.Max(1, DropEvery);
			float speed = DropSpeed;
			AttackSfx.Appear();
			battle.Spawn(new Bullet
			{
				Position = zone.Center.ToVector2(),
				Harmful = false,
				DestroyOnHit = false,
				Lifetime = warn + active,
				GrazePoints = 2f,
				OffscreenMargin = 2000f,
				OnUpdate = x =>
				{
					if (x.Age == warn)
					{
						if (rain)
							AttackSfx.Vanilla(SoundID.Item34, 0.7f);
						else
						{
							x.Harmful = true;
							AttackSfx.Vanilla(SoundID.Item33, 0.8f);
							battle.ShakeScreen(2);
						}
					}
					if (rain && x.Age >= warn && (x.Age - warn) % every == 0)
					{
						// Inside the half with a little margin, so the line between halves is safe
						float px = Main.rand.NextFloat(zone.Left + 6, zone.Right - 6);
						battle.Spawn(drop(new Vector2(px, zone.Top - 20), new Vector2(0f, speed)));
					}
				},
				HitTest = (x, r) => x.Harmful && new Rectangle(zone.X + 2, zone.Y + 2, zone.Width - 4, zone.Height - 4).Intersects(r),
				OnDraw = x =>
				{
					if (x.Age < warn)
					{
						// The forecast: the half flickers in its weather's colour, brighter as it gets close
						float t = x.Age / (float)warn;
						float alpha = ((x.Age / 4) % 2 == 0 ? 0.22f : 0.12f) + t * 0.15f;
						DrDraw.Rect(zone.X, zone.Y, zone.Width, zone.Height, color * alpha);
						float w = DrDraw.Measure(word, DrDraw.SmallFont);
						DrDraw.Text(word, zone.Center.X - w / 2f, zone.Center.Y - 8, color * (0.6f + 0.4f * t), DrDraw.SmallFont);
						return;
					}
					float life = (x.Age - warn) / (float)active;
					if (rain)
					{
						// A dim wash while it pours
						DrDraw.Rect(zone.X, zone.Y, zone.Width, zone.Height, color * (0.1f * (1f - life)));
						return;
					}
					// The sun: a hard flash that fades, with heat lines across it
					float fade = life < 0.1f ? life / 0.1f : 1f - (life - 0.1f) / 0.9f;
					DrDraw.Rect(zone.X, zone.Y, zone.Width, zone.Height, color * (0.55f * fade));
					DrDraw.Rect(zone.X + 4, zone.Y + 4, zone.Width - 8, zone.Height - 8, Color.White * (0.35f * fade));
					for (int i = 1; i < 4; i++)
					{
						if (zone.Width > zone.Height)
						{
							float y = zone.Y + zone.Height * i / 4f + (float)Math.Sin(x.Age * 0.6f + i) * 2f;
							DrDraw.Line(new Vector2(zone.Left, y), new Vector2(zone.Right, y), 2f, Color.White * fade);
						}
						else
						{
							float xx = zone.X + zone.Width * i / 4f + (float)Math.Sin(x.Age * 0.6f + i) * 2f;
							DrDraw.Line(new Vector2(xx, zone.Top), new Vector2(xx, zone.Bottom), 2f, Color.White * fade);
						}
					}
				},
			});
		}
	}
}
