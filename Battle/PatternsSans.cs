using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace MercyMode.Battle
{
	// ====================================================================== Skeletron's skeleton tricks
	// Undertale's skeleton fight, done with Skeletron's bones and skull: blue bones that only hurt if you move, orange
	// ones that only hurt if you don't, the SOUL slammed into the floor, and skulls that charge up and fire beams.

	public static class SansBones
	{
		public static readonly Color Blue = new(20, 170, 255), Orange = new(255, 160, 40), White = new(235, 225, 200);

		/// <summary>A bone of a colour: 1 blue (stand still), 2 orange (keep moving), 0 white (just dodge).</summary>
		public static Bullet Make(Vector2 centre, Vector2 vel, float height, int kind)
		{
			Bullet b = BoneWalls.Bone(centre, vel, height, kind == 1 ? Blue : kind == 2 ? Orange : White);
			b.Sans = kind;
			return b;
		}
	}

	/// <summary>
	/// Blue SOUL: the SOUL is slammed into the floor, then bones burst up along the whole floor (a warning strip
	/// first): jump, and keep jumping.
	/// </summary>
	public class FloorBoneWave : RepeatingAttack
	{
		public int Warn = 26, Active = 26;
		public float Height = 28f;

		public FloorBoneWave(int every = 72)
		{
			Every = every;
			StopBeforeEnd = 60;
			FirstAt = 24;
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
			int warn = Warn, active = Active;
			float height = Height;
			// The warning: the floor flashes where the bones come up
			battle.Spawn(Shots.Warning(new Rectangle(box.Left, (int)(box.Bottom - height), box.Width, (int)height), warn, SansBones.White));
			for (float x = box.Left + 6; x < box.Right - 2; x += 12f)
			{
				float bx = x;
				Bullet b = SansBones.Make(new Vector2(bx, box.Bottom + height / 2f), Vector2.Zero, height, 0);
				b.StartDelay = warn;
				b.Lifetime = active + 24;
				b.OnUpdate += y =>
				{
					// Up fast, hold, back down
					float k = y.Age < 6 ? y.Age / 6f : y.Age < active ? 1f : Math.Max(0f, 1f - (y.Age - active) / 8f);
					y.Position.Y = box.Bottom + height / 2f - height * k;
					y.Harmful = k > 0.5f;
				};
				battle.Spawn(b);
			}
			AttackSfx.Vanilla(SoundID.Item71, 0.4f, 0.5f);
		}
	}

	/// <summary>
	/// Blue SOUL: tall bones sweep across the box, blue (stand still and they pass through you) and orange (keep moving
	/// and they pass), with a short white one to hop now and then.
	/// </summary>
	public class ColoredBones : RepeatingAttack
	{
		public float Speed = 2.6f;

		public ColoredBones(int every = 40)
		{
			Every = every;
			StopBeforeEnd = 70;
			FirstAt = 20;
			Soul = SoulMode.Blue;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			bool fromLeft = index % 2 == 0;
			float x = fromLeft ? box.Left - 10 : box.Right + 10;
			var vel = new Vector2(fromLeft ? Speed : -Speed, 0f);
			if (index % 3 == 2)
			{
				// A short white one: jump it
				float h = Main.rand.NextFloat(18f, 30f);
				battle.Spawn(SansBones.Make(new Vector2(x, box.Bottom - h / 2f), vel, h, 0));
				return;
			}
			int kind = Main.rand.NextBool() ? 1 : 2;
			float height = box.Height - 4f;
			battle.Spawn(SansBones.Make(new Vector2(x, box.Center.Y), vel, height, kind));
		}
	}

	/// <summary>
	/// Skulls slide in round the box, turn to the SOUL, glow while they charge, then fire a wide beam straight
	/// through it, and leave.
	/// </summary>
	public class SkullBlasters : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public int Charge = 34, Fire = 22;
		public float Width = 22f;

		public SkullBlasters(Func<Vector2, Vector2, Bullet> make, int every = 50)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 70;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			Vector2 c = box.Center.ToVector2();
			float ang = Main.rand.NextFloat(MathHelper.TwoPi);
			float reach = Math.Max(box.Width, box.Height) / 2f + 34f;
			Vector2 spot = c + ang.ToRotationVector2() * reach;
			Vector2 from = c + ang.ToRotationVector2() * (reach + 90f);
			Vector2 target = battle.SoulCenter;
			Vector2 dir = Vector2.Normalize(target - spot);
			int charge = Charge, fire = Fire;
			float width = Width;
			Bullet skull = Make(from, Vector2.Zero);
			skull.Harmful = false;
			skull.Lifetime = 20 + charge + fire + 20;
			skull.OffscreenMargin = 9999f;
			skull.DestroyOnHit = false;
			skull.RotateWithVelocity = false;
			skull.Rotation = dir.ToRotation() - MathHelper.PiOver2;
			skull.Alpha = 0f;
			Vector2 end = spot + dir * 900f;
			skull.OnUpdate += x =>
			{
				int t = x.Age;
				x.Velocity = Vector2.Zero;
				if (t < 20)
				{
					float k = t / 20f;
					x.Position = Vector2.Lerp(from, spot, 1f - (1f - k) * (1f - k));
					x.Alpha = k;
					return;
				}
				if (t < 20 + charge)
				{
					x.Position = spot + Main.rand.NextVector2Circular(1f, 1f);
					if (t == 20)
						AttackSfx.Vanilla(SoundID.Item15, 0.5f, -0.4f);
					return;
				}
				if (t == 20 + charge)
				{
					x.Harmful = true;
					battle.ShakeScreen(3f);
					AttackSfx.Vanilla(SoundID.Item33 with { Pitch = -0.6f }, 0.7f);
				}
				if (t >= 20 + charge + fire)
				{
					// Away it goes
					x.Harmful = false;
					x.Position = Vector2.Lerp(x.Position, from, 0.12f);
					x.Alpha = Math.Max(0f, x.Alpha - 0.06f);
				}
			};
			// Only the beam hurts, not the skull
			skull.HitTest = (x, soul) =>
			{
				if (!x.Harmful)
					return false;
				Vector2 p = soul.Center.ToVector2();
				Vector2 ab = end - spot;
				float tt = MathHelper.Clamp(Vector2.Dot(p - spot, ab) / ab.LengthSquared(), 0f, 1f);
				return Vector2.Distance(p, spot + ab * tt) < width / 2f + soul.Width / 2f - 2f;
			};
			skull.OnDraw = x =>
			{
				int t = x.Age;
				if (t >= 20 && t < 20 + charge)
				{
					// Charging: a thin aim line and a growing glow in its mouth
					float k = (t - 20) / (float)charge;
					DrDraw.Line(spot, end, 1f, Color.White * (0.25f + 0.25f * k));
					DrDraw.Glow(spot + dir * 10f, 6f + 10f * k, Color.White * (0.4f + 0.4f * k));
				}
				else if (t >= 20 + charge && t < 20 + charge + fire)
				{
					float k = 1f - (t - 20 - charge) / (float)fire;
					DrDraw.Line(spot, end, width + 6f, Color.White * (0.35f * k + 0.2f));
					DrDraw.Line(spot, end, width, Color.White * (0.6f * k + 0.4f));
				}
				x.DrawSprite();
			};
			battle.Spawn(skull);
		}
	}
}
