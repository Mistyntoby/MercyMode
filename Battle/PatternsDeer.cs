using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace MercyMode.Battle
{
	// ====================================================================== Deerclops
	// Rubble tearing up out of the ground in a line, a blizzard pushing the SOUL about, a stare you can only survive by
	// keeping still, stomps that roll ice along the floor, and shadow hands that grab along a row or a column.

	public static class Frost
	{
		public static readonly Color Pale = new(200, 240, 255), Deep = new(110, 190, 240);

		/// <summary>A jagged ice block standing on a point (its base), drawn in stacked shrinking layers.</summary>
		public static void Spike(Vector2 baseCentre, float width, float height, float dir, float alpha)
		{
			int layers = Math.Max(3, (int)(height / 4f));
			for (int i = 0; i < layers; i++)
			{
				float w = width * (1f - i / (float)layers);
				float h = height / layers;
				Color c = Color.Lerp(Pale, Deep, i / (float)layers) * alpha;
				float y = dir > 0 ? baseCentre.Y - (i + 1) * h : baseCentre.Y + i * h;
				DrDraw.Rect(baseCentre.X - w / 2f, y, w, h + 0.5f, c);
			}
		}
	}

	/// <summary>
	/// Rubble tears out of the ground in a line marching across the box (each column flashes first), alternately from
	/// the floor and from the ceiling: get above (or below) it.
	/// </summary>
	public class RubbleLine : RepeatingAttack
	{
		public int Warn = 26, StepEvery = 6, Hold = 14;
		public float Reach = 0.55f, StepWidth = 20f;

		public RubbleLine(int every = 70)
		{
			Every = every;
			StopBeforeEnd = 90;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			bool fromFloor = index % 2 == 0, fromLeft = Main.rand.NextBool();
			float reach = box.Height * Reach;
			int steps = (int)(box.Width / StepWidth) + 1;
			AttackSfx.Vanilla(SoundID.DeerclopsRubbleAttack, 0.6f);
			for (int i = 0; i < steps; i++)
			{
				float x = fromLeft ? box.Left + StepWidth / 2f + i * StepWidth : box.Right - StepWidth / 2f - i * StepWidth;
				int delay = i * StepEvery;
				float top = fromFloor ? box.Bottom - reach : box.Top;
				var area = new Rectangle((int)(x - StepWidth / 2f), (int)top, (int)StepWidth, (int)reach);
				var warning = Shots.Warning(area, Warn, Frost.Pale);
				warning.StartDelay = delay;
				warning.Light = 16f; // a glowing crack in the dark
				battle.Spawn(warning);
				int hold = Hold;
				float width = StepWidth, baseY = fromFloor ? box.Bottom : box.Top, dir = fromFloor ? 1f : -1f;
				battle.Spawn(new Bullet
				{
					Position = area.Center.ToVector2(),
					HitSize = new Vector2(width - 4f, reach),
					StartDelay = delay + Warn,
					Lifetime = hold + 10,
					DestroyOnHit = false,
					OnUpdate = b => b.Harmful = b.Age < hold + 4,
					OnDraw = b =>
					{
						float k = b.Age < 4 ? b.Age / 4f : b.Age > hold ? Math.Max(0f, 1f - (b.Age - hold) / 10f) : 1f;
						Frost.Spike(new Vector2(b.Position.X, baseY), width, reach * k, dir, 1f);
					},
				});
			}
		}
	}

	/// <summary>
	/// A blizzard: the wind shoves the SOUL one way (it flips now and then, the snow showing which way) while
	/// snowballs ride the gusts.
	/// </summary>
	public class Blizzard : EnemyAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public float Push = 0.7f, Speed = 3f;
		public int FlipEvery = 120, SnowEvery = 10;
		private int wind = 1;

		public override void Update(BattleSystem battle, int tick)
		{
			Rectangle box = battle.Box;
			if (tick == 1)
				wind = Main.rand.NextBool() ? 1 : -1;
			if (tick % FlipEvery == 0)
			{
				wind = -wind;
				AttackSfx.Vanilla(SoundID.DeerclopsIceAttack, 0.4f, 0.4f);
			}
			// It builds up after a flip
			float strength = Math.Min(1f, tick % FlipEvery / 30f);
			battle.PullSoul(new Vector2(wind * Push * strength, 0f));
			// Snow streaks, just for show
			if (tick % 2 == 0)
			{
				float y = Main.rand.NextFloat(box.Top, box.Bottom);
				battle.Spawn(new Bullet
				{
					Position = new Vector2(wind > 0 ? box.Left - 10 : box.Right + 10, y),
					Velocity = new Vector2(wind * 9f, 1f),
					Harmful = false,
					Lifetime = 30,
					OnDraw = x => DrDraw.Line(x.Position, x.Position - Vector2.Normalize(x.Velocity) * 12f, 1.5f, Color.White * 0.35f),
				});
			}
			if (tick % SnowEvery == 0 && tick > 20 && tick < Duration - 50)
			{
				float y = Main.rand.NextFloat(box.Top + 8, box.Bottom - 8);
				battle.Spawn(Make(new Vector2(wind > 0 ? box.Left - 14 : box.Right + 14, y), new Vector2(wind * Speed * strength + wind * 0.8f, Main.rand.NextFloat(-0.4f, 0.4f))));
			}
		}
	}

	/// <summary>
	/// Its stare: a pale wave of frost sweeps across the box, and it only hurts if you move while it passes. Keep still.
	/// Rubble falls meanwhile, so you'll want to move between the waves.
	/// </summary>
	public class FrostStare : RepeatingAttack
	{
		public float Speed = 2.4f;

		public FrostStare(int every = 70)
		{
			Every = every;
			StopBeforeEnd = 70;
			FirstAt = 30;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			bool fromLeft = index % 2 == 0;
			float speed = Speed;
			battle.Spawn(new Bullet
			{
				Position = new Vector2(fromLeft ? box.Left - 10 : box.Right + 10, box.Center.Y),
				Velocity = new Vector2(fromLeft ? speed : -speed, 0f),
				HitSize = new Vector2(12f, box.Height),
				Sans = 1,
				DestroyOnHit = false,
				Lifetime = (int)(box.Width / speed) + 30,
				DamageMult = 0.8f,
				OnDraw = x =>
				{
					Rectangle b = battle.Box;
					DrDraw.Rect(x.Position.X - 6f, b.Top, 12f, b.Height, SansBones.Blue * 0.55f);
					DrDraw.Rect(x.Position.X - 2f, b.Top, 4f, b.Height, Color.White * 0.8f);
				},
			});
			AttackSfx.Vanilla(SoundID.DeerclopsScream, 0.35f, 0.4f);
		}
	}

	/// <summary>Blue SOUL: it stomps, and ice rolls along the floor from one side or both: jump it.</summary>
	public class StompWaves : RepeatingAttack
	{
		public float Speed = 2.8f;
		public float MinHeight = 16f, MaxHeight = 28f;

		public StompWaves(int every = 50)
		{
			Every = every;
			StopBeforeEnd = 60;
			FirstAt = 20;
			Soul = SoulMode.Blue;
		}

		public override void Update(BattleSystem battle, int tick)
		{
			if (tick == 1)
				battle.SlamSoul();
			base.Update(battle, tick);
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			battle.ShakeScreen(3f);
			AttackSfx.Vanilla(SoundID.DeerclopsStep, 0.8f);
			// Every third stomp sends one from each side (meeting in the middle)
			bool both = index % 3 == 2;
			foreach (int side in both ? new[] { -1, 1 } : new[] { index % 2 == 0 ? -1 : 1 })
			{
				float h = Main.rand.NextFloat(MinHeight, MaxHeight);
				float speed = Speed;
				battle.Spawn(new Bullet
				{
					Position = new Vector2(side < 0 ? box.Left - 10 : box.Right + 10, box.Bottom - h / 2f),
					Velocity = new Vector2(side < 0 ? speed : -speed, 0f),
					HitSize = new Vector2(14f, h),
					DestroyOnHit = false,
					Lifetime = 200,
					OnDraw = x => Frost.Spike(new Vector2(x.Position.X, x.Position.Y + h / 2f), 16f, h, 1f, 1f),
				});
			}
		}
	}

	/// <summary>Shadow hands grab along the SOUL's row or column (it flashes first), from both ends, then pull back.</summary>
	public class ShadowGrab : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public int Warn = 34, Hold = 12;
		public float Speed = 6f;

		public ShadowGrab(Func<Vector2, Vector2, Bullet> make, int every = 64)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 70;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			bool row = index % 2 == 0;
			Vector2 soul = battle.SoulCenter;
			const int lane = 26;
			Rectangle area = row
				? new Rectangle(box.Left, (int)(soul.Y - lane / 2f), box.Width, lane)
				: new Rectangle((int)(soul.X - lane / 2f), box.Top, lane, box.Height);
			battle.Spawn(Shots.Warning(area, Warn, new Color(120, 60, 160)));
			Vector2 mid = area.Center.ToVector2();
			int hold = Hold, warn = Warn;
			float speed = Speed;
			foreach (int side in new[] { -1, 1 })
			{
				Vector2 from = row ? new Vector2(side < 0 ? box.Left - 20 : box.Right + 20, mid.Y) : new Vector2(mid.X, side < 0 ? box.Top - 20 : box.Bottom + 20);
				Vector2 dir = Vector2.Normalize(mid - from);
				// Stops a hand's width short of the middle, holds, and goes back the way it came
				float reach = Vector2.Distance(from, mid) - 10f;
				Bullet h = Make(from, dir * speed);
				h.StartDelay = warn;
				h.Lifetime = 200;
				h.DestroyOnHit = false;
				float travelled = 0f;
				int held = 0;
				h.OnUpdate += x =>
				{
					travelled += x.Velocity.Length() * Math.Sign(Vector2.Dot(x.Velocity, dir));
					if (held == 0 && travelled >= reach)
					{
						x.Velocity = Vector2.Zero;
						held = 1;
					}
					else if (held > 0 && held < hold)
						held++;
					else if (held == hold)
					{
						x.Velocity = -dir * speed * 0.6f;
						held++;
					}
				};
				battle.Spawn(h);
			}
		}
	}
}
