using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.ID;
using MercyMode.Deltarune;
using static MercyMode.Battle.BattleConstants;

namespace MercyMode.Battle
{
	/// <summary>
	/// How the SOUL moves during an enemy turn, like Undertale's coloured SOULs. Each attack picks one
	/// (<see cref="EnemyAttack.Soul"/>); red is the normal free movement.
	/// </summary>
	public enum SoulMode
	{
		/// <summary>Moves freely.</summary>
		Red,
		/// <summary>Gravity: falls to the floor of the box, Up jumps (hold it to jump higher).</summary>
		Blue,
		/// <summary>Stuck in the middle of the box; the arrow keys turn a shield that blocks bullets.</summary>
		Green,
		/// <summary>Trapped on three strings across the box: Left/Right slide, Up/Down hop between strings.</summary>
		Purple,
		/// <summary>Deltarune's yellow SOUL: moves freely and Z shoots to the right (hold for a big shot), breaking bullets.</summary>
		Yellow,
	}

	public static class SoulModeExtensions
	{
		/// <summary>Sets the SOUL mode for an attack (and returns it, for building in one expression).</summary>
		public static T WithSoul<T>(this T attack, SoulMode mode) where T : EnemyAttack
		{
			attack.Soul = mode;
			return attack;
		}

		public static Color Color(this SoulMode mode) => mode switch
		{
			SoulMode.Blue => new Color(0, 60, 255),
			SoulMode.Green => new Color(0, 192, 0),
			SoulMode.Purple => new Color(213, 53, 217),
			SoulMode.Yellow => new Color(255, 255, 0),
			_ => Microsoft.Xna.Framework.Color.Red,
		};
	}

	public partial class BattleSystem
	{
		// Blue: Undertale's gravity SOUL, in pixels per tick
		private const float BlueGravity = 0.18f;
		private const float BlueJumpSpeed = 4.8f;
		private const float BlueJumpCut = 1.5f;
		private const float BlueMaxFall = 7f;
		// Green: the shield sits this far from the SOUL's centre, this wide and thick
		private const float ShieldDistance = 19f;
		private const float ShieldWidth = 30f;
		private const float ShieldThickness = 6f;
		// Purple: three strings. They stretch out from the SOUL as the turn starts (with a twang), wobble where the SOUL
		// lands, and pull back into it as the turn ends
		public const int PurpleStrings = 3;
		private const int StringsInTicks = 14, StringsOutTicks = 14;
		private const float PluckAmplitude = 4f;
		private readonly int[] stringPlucked = new int[PurpleStrings];
		// Yellow, from Deltarune's obj_heart yellow mode / obj_yheart_shot (30 fps numbers halved per tick): shots fly
		// right at 8 px/tick, at most 3 at once; Z held 40+ ticks charges a big shot (4 px/tick, speeding up, 4 damage)
		private const float YellowShotSpeed = 8f;
		private const int YellowMaxShots = 3;
		private const int YellowChargeTicks = 40;
		private const float BigShotSpeed = 4f, BigShotAccel = 0.1f;

		private sealed class YellowShot
		{
			public Vector2 Pos, Vel;
			public float Accel;
			public bool Big;
			public int Damage = 1;
			public float Alpha = 1f, ScaleX = 1f, ScaleY = 1f;
			public readonly HashSet<Bullet> Hit = new();
		}

		private SoulMode soulMode;
		private float soulVy;
		private int shieldDir; // 0 up, 1 right, 2 down, 3 left
		private float shieldAngle;
		private int shieldFlash;
		private int purpleString = 1;
		private int zHold, chargeDelay;
		private bool chargeSounded;
		private Microsoft.Xna.Framework.Audio.SoundEffectInstance chargeLoop;
		private float chargeVolume;

		private void StopChargeLoop()
		{
			if (chargeLoop == null)
				return;
			chargeLoop.Stop();
			chargeLoop.Dispose();
			chargeLoop = null;
		}
		private readonly List<YellowShot> yellowShots = new();
		/// <summary>Bullets the yellow SOUL has hit but not broken yet, and how many more hits they take.</summary>
		private readonly Dictionary<Bullet, int> toughness = new();

		/// <summary>The SOUL mode of the current enemy turn.</summary>
		public SoulMode SoulMode => soulMode;

		/// <summary>The y (centre) of one of the purple strings in the current box.</summary>
		public float PurpleStringY(int i)
		{
			Rectangle box = Box;
			return box.Top + box.Height * (i + 1f) / (PurpleStrings + 1f);
		}

		/// <summary>The green SOUL's fixed centre.</summary>
		public Vector2 BoxCentre => Box.Center.ToVector2();

		/// <summary>Throws a blue SOUL down onto the floor (Sans's slam): attacks call it as they start.</summary>
		public void SlamSoul()
		{
			if (soulMode != SoulMode.Blue)
				return;
			soulVy = BlueMaxFall + 3f;
			ShakeScreen(3);
			AttackSfx.Impact();
		}

		private void BeginSoulMode(SoulMode mode)
		{
			soulMode = mode;
			soulVy = 0f;
			shieldDir = 0;
			shieldAngle = -MathHelper.PiOver2;
			shieldFlash = 0;
			purpleString = PurpleStrings / 2;
			for (int i = 0; i < PurpleStrings; i++)
				stringPlucked[i] = -1000;
			zHold = 0;
			chargeDelay = 0;
			StopChargeLoop();
			chargeSounded = false;
			yellowShots.Clear();
			toughness.Clear();
		}

		/// <summary>Where the SOUL flies to when the box opens: the floor for blue, the middle string for purple.</summary>
		private Vector2 SoulStart
		{
			get
			{
				Vector2 rest = SoulRestPosition;
				if (soulMode == SoulMode.Blue)
					rest.Y = BoxCenterY + BoxSize / 2f - BoxClampHigh;
				return rest;
			}
		}

		/// <summary>The non-red movement; true when it handled the SOUL.</summary>
		private bool MoveSoulMode()
		{
			Rectangle box = Box;
			float minX = box.Left + BoxClampLow, maxX = box.Right - BoxClampHigh;
			float minY = box.Top + BoxClampLow, maxY = box.Bottom - BoxClampHigh;
			float speed = Held(Keys.X) && !disableSlow ? SoulSlowSpeed : SoulSpeed;
			if (!Held(Keys.X))
				disableSlow = false;
			float px = (Held(Keys.Right) ? speed : 0f) - (Held(Keys.Left) ? speed : 0f);

			switch (soulMode)
			{
				case SoulMode.Blue:
				{
					soul.X = MathHelper.Clamp(soul.X + px, minX, maxX);
					bool grounded = soul.Y >= maxY - 0.01f;
					if (grounded && Held(Keys.Up))
						soulVy = -BlueJumpSpeed;
					// Let go of Up to stop rising early
					if (!Held(Keys.Up) && soulVy < -BlueJumpCut)
						soulVy = -BlueJumpCut;
					soulVy = Math.Min(soulVy + BlueGravity, soulVy > BlueMaxFall ? soulVy : BlueMaxFall);
					soul.Y += soulVy;
					if (soul.Y >= maxY)
					{
						if (soulVy > BlueMaxFall)
							ShakeScreen(2); // landing from a slam
						soul.Y = maxY;
						soulVy = 0f;
					}
					if (soul.Y <= minY)
					{
						soul.Y = minY;
						soulVy = Math.Max(0f, soulVy);
					}
					return true;
				}
				case SoulMode.Green:
				{
					soul = BoxCentre - new Vector2(SoulSize / 2f);
					int before = shieldDir;
					if (Pressed(Keys.Up)) shieldDir = 0;
					if (Pressed(Keys.Right)) shieldDir = 1;
					if (Pressed(Keys.Down)) shieldDir = 2;
					if (Pressed(Keys.Left)) shieldDir = 3;
					if (shieldDir != before)
						Sfx("menumove");
					float want = -MathHelper.PiOver2 + shieldDir * MathHelper.PiOver2;
					shieldAngle += MathHelper.WrapAngle(want - shieldAngle) * 0.5f;
					if (shieldFlash > 0)
						shieldFlash--;
					return true;
				}
				case SoulMode.Purple:
				{
					soul.X = MathHelper.Clamp(soul.X + px, minX, maxX);
					if (phaseTicks == 1)
					{
						// The strings stretch out of the SOUL: a twang, and they all quiver
						AttackSfx.Vanilla(SoundID.Item26, 0.45f, -0.35f);
						for (int i = 0; i < PurpleStrings; i++)
							stringPlucked[i] = (int)time + i * 2;
					}
					int was = purpleString;
					if (Pressed(Keys.Up) && purpleString > 0)
						purpleString--;
					if (Pressed(Keys.Down) && purpleString < PurpleStrings - 1)
						purpleString++;
					if (purpleString != was)
						stringPlucked[purpleString] = (int)time + 3; // it wobbles as the SOUL lands
					float targetY = PurpleStringY(purpleString) - SoulSize / 2f;
					soul.Y += (targetY - soul.Y) * 0.45f;
					if (Math.Abs(targetY - soul.Y) < 0.3f)
						soul.Y = targetY;
					return true;
				}
				case SoulMode.Yellow:
				{
					// Moves like red; shooting is handled with the bullets
					return false;
				}
			}
			return false;
		}

		/// <summary>The green SOUL's shield, as a box for blocking.</summary>
		private Rectangle ShieldRect
		{
			get
			{
				Vector2 dir = (shieldDir * MathHelper.PiOver2 - MathHelper.PiOver2).ToRotationVector2();
				Vector2 c = BoxCentre + dir * ShieldDistance;
				bool vertical = shieldDir % 2 == 0;
				float w = vertical ? ShieldWidth : ShieldThickness + 4f, h = vertical ? ShieldThickness + 4f : ShieldWidth;
				return new Rectangle((int)(c.X - w / 2f), (int)(c.Y - h / 2f), (int)w, (int)h);
			}
		}

		/// <summary>True if the green shield stopped this bullet (it's destroyed with a clink).</summary>
		private bool ShieldBlocks(Bullet b)
		{
			if (soulMode != SoulMode.Green || !b.DestroyOnHit || b.HitTest != null || !b.Touches(ShieldRect))
				return false;
			b.Dead = true;
			LabBlocks++;
			shieldFlash = 8;
			AttackSfx.Vanilla(SoundID.Tink, 0.7f, 0.3f);
			Sparks.Burst(this, b.Position, 5, SoulMode.Green.Color(), 2.2f);
			var mp = Player.GetModPlayer<MercyPlayer>();
			mp.TP = Math.Min(100f, mp.TP + 0.6f);
			return true;
		}

		/// <summary>
		/// Yellow (Deltarune chapter 2): Z fires a shot to the right; let go after holding a moment and it fires too;
		/// hold it 40 ticks and let go for a big piercing shot. Shots break bullets (big ones take a few hits).
		/// </summary>
		private void UpdateYellowShots()
		{
			if (soulMode != SoulMode.Yellow)
				return;
			if (chargeDelay > 0)
				chargeDelay--;
			bool released = !Held(Keys.Z) && zHold > 0;
			if (Pressed(Keys.Z) || released && zHold >= 10 && zHold <= 39)
			{
				// instance_number(obj_yheart_shot) < 3: big shots count too
				if (yellowShots.Count < YellowMaxShots && chargeDelay == 0)
				{
					yellowShots.Add(new YellowShot { Pos = SoulCenter, Vel = new Vector2(YellowShotSpeed, 0f) });
					Sfx("attack");
				}
			}
			// obj_heart yellow: the charge hum starts at z_hold 20, fading in to 0.3 and rising in pitch (0.1 -> 1.1 by 40)
			if (zHold == 20 && !chargeSounded)
			{
				chargeSounded = true;
				chargeLoop = DeltaruneAssets.CreateLoop("chargeshot", out chargeVolume);
				if (chargeLoop != null)
					chargeLoop.Play();
				else
					AttackSfx.Vanilla(SoundID.Item13, 0.4f);
			}
			if (chargeLoop != null && zHold >= 20)
			{
				float pitch = 0.1f + Math.Min(20, zHold - 20) / 20f; // GameMaker's pitch is a speed multiplier
				chargeLoop.Pitch = MathHelper.Clamp((float)Math.Log2(pitch), -1f, 1f);
				chargeLoop.Volume = MathHelper.Clamp(chargeVolume * 0.3f * Math.Min(1f, (zHold - 20) / 40f), 0f, 1f);
			}
			if (released && zHold >= YellowChargeTicks)
			{
				StopChargeLoop();
				DeltaruneAssets.Play("chargefire", SoundID.Item12 with { Volume = 0.8f, Pitch = -0.3f });
				yellowShots.Add(new YellowShot
				{
					Pos = SoulCenter, Vel = new Vector2(BigShotSpeed, 0f), Accel = BigShotAccel, Big = true, Damage = 4,
					Alpha = 0.5f, ScaleX = 0.1f, ScaleY = 2f,
				});
				chargeDelay = 5;
			}
			zHold = Held(Keys.Z) ? zHold + 1 : 0;
			if (zHold == 0)
			{
				chargeSounded = false;
				StopChargeLoop();
			}

			Rectangle box = Box;
			for (int i = yellowShots.Count - 1; i >= 0; i--)
			{
				YellowShot s = yellowShots[i];
				s.Vel.X += s.Accel;
				s.Pos += s.Vel;
				if (s.Big)
				{
					// obj_yheart_shot Step, big: fades in and un-squashes
					s.Alpha = Math.Min(1f, s.Alpha + 0.05f);
					s.ScaleX = Math.Min(1f, s.ScaleX + 0.05f);
					s.ScaleY = Math.Max(1f, s.ScaleY - 0.05f);
				}
				if (s.Pos.X > box.Right + 30f)
				{
					yellowShots.RemoveAt(i);
					continue;
				}
				float r = s.Big ? 14f : 5f;
				var shot = new Rectangle((int)(s.Pos.X - r), (int)(s.Pos.Y - r), (int)(r * 2), (int)(r * 2));
				foreach (Bullet b in Bullets)
				{
					if (b.Dead || !b.Harmful || b.Waiting || !b.DestroyOnHit || b.HitTest != null || s.Hit.Contains(b) || !b.Hitbox.Intersects(shot))
						continue;
					s.Hit.Add(b);
					AddEffect(new SpriteAnim("spr_yheart_shot_hit", s.Pos, 0.25f, s.Big ? 3f : 1f));
					if (!toughness.TryGetValue(b, out int left))
						left = b.Toughness > 0 ? b.Toughness : Math.Max(b.HitSize.X, b.HitSize.Y) >= 18f ? 3 : 1;
					left -= b.MaxShotDamage > 0 ? Math.Min(s.Damage, b.MaxShotDamage) : s.Damage;
					b.Flash = 6;
					if (left <= 0)
					{
						b.Dead = true;
						LabBroken++;
						toughness.Remove(b);
						Sparks.Burst(this, b.Position, 7, SoulMode.Yellow.Color(), 2.6f);
						Sfx("damage");
						var mp = Player.GetModPlayer<MercyPlayer>();
						mp.TP = Math.Min(100f, mp.TP + 0.8f);
					}
					else
					{
						toughness[b] = left;
						AttackSfx.Vanilla(SoundID.Tink, 0.4f, 0.6f);
					}
					// Small shots stop at the first thing they hit; the big one goes through
					if (!s.Big)
					{
						yellowShots.RemoveAt(i);
						break;
					}
				}
			}
		}

		/// <summary>
		/// The purple SOUL's strings: out from the SOUL across the box at the start of the turn, back into it at the end
		/// (drawn even after the SOUL has flown home, so they finish pulling in), each a plucked wave that settles.
		/// </summary>
		private void DrawPurpleStrings()
		{
			if (soulMode != SoulMode.Purple)
				return;
			float extend;
			if (phase == Phase.EnemyTurn)
				extend = Ease(Math.Min(1f, phaseTicks / (float)StringsInTicks));
			else if (phase == Phase.Build && duelWith >= 0)
				// The builder watching the other player's purple SOUL
				extend = Ease(Math.Min(1f, (time - mirroredSoulSince) / (float)StringsInTicks));
			else if (phase == Phase.EnemyOutro)
				extend = 1f - Ease(Math.Min(1f, phaseTicks / (float)StringsOutTicks));
			else
				return;
			if (extend <= 0f)
				return;
			Rectangle box = Box;
			float cx = phase == Phase.EnemyOutro ? soulFrom.X + SoulSize / 2f : SoulCenter.X;
			Color c = SoulMode.Purple.Color() * (0.85f * Math.Min(1f, extend * 1.5f));
			const int segments = 16;
			for (int i = 0; i < PurpleStrings; i++)
			{
				float y = PurpleStringY(i);
				float left = MathHelper.Lerp(cx, box.Left + 3, extend), right = MathHelper.Lerp(cx, box.Right - 3, extend);
				float since = (float)time - stringPlucked[i];
				float amp = since < 0f ? 0f : PluckAmplitude * (float)Math.Pow(0.9, since);
				Vector2 prev = new(left, y);
				for (int k = 1; k <= segments; k++)
				{
					float u = k / (float)segments;
					float x = MathHelper.Lerp(left, right, u);
					// A standing wave between the two ends
					float off = amp * (float)Math.Sin(u * MathHelper.Pi) * (float)Math.Cos(since * 1.3f);
					var next = new Vector2(x, y + off);
					DrDraw.Line(prev, next, 2f, c);
					prev = next;
				}
				// Little knots where the strings meet the box
				if (extend >= 1f)
				{
					DrDraw.Rect(left - 2f, y - 2f, 4f, 4f, c);
					DrDraw.Rect(right - 2f, y - 2f, 4f, 4f, c);
				}
			}
		}

		/// <summary>When the builder's copy of the other player's SOUL last changed mode.</summary>
		private int mirroredSoulSince;
		private bool mirroredSoulHopping;

		/// <summary>
		/// The builder's copy of the other player's SOUL follows its real mode (the dodger's game may not have taken a
		/// forced change, or took it late), and its strings wobble when it hops onto another one.
		/// </summary>
		private void MirrorRemoteSoulMode(SoulMode mode, Vector2 was)
		{
			if (!Enum.IsDefined(mode))
				return;
			if (mode != soulMode)
			{
				BeginSoulMode(mode);
				mirroredSoulSince = time;
				if (mode == SoulMode.Purple)
					for (int i = 0; i < PurpleStrings; i++)
						stringPlucked[i] = time + i * 2;
				return;
			}
			if (mode != SoulMode.Purple)
				return;
			// Hopping slides it over a few ticks: the string wobbles once it lands
			if (Math.Abs(duelRemoteSoul.Y - was.Y) >= 1f)
			{
				mirroredSoulHopping = true;
				return;
			}
			if (!mirroredSoulHopping)
				return;
			mirroredSoulHopping = false;
			float y = duelRemoteSoul.Y + SoulSize / 2f;
			int nearest = 0;
			for (int i = 1; i < PurpleStrings; i++)
				if (Math.Abs(PurpleStringY(i) - y) < Math.Abs(PurpleStringY(nearest) - y))
					nearest = i;
			stringPlucked[nearest] = time;
		}

		private static float Ease(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

		/// <summary>The SOUL in its mode's colour, with the mode's extras (strings, shield, shots).</summary>
		private void DrawSoulMode(int frame, float alpha)
		{
			Rectangle box = Box;
			if (soulMode == SoulMode.Yellow)
				foreach (YellowShot s in yellowShots)
				{
					string sprite = s.Big ? "spr_yheart_bigshot" : "spr_yheart_shot";
					if (!DrDraw.Sprite(sprite, 0, s.Pos.X, s.Pos.Y, Color.White, new Vector2(s.ScaleX, s.ScaleY), 0f, s.Alpha))
					{
						float w = (s.Big ? 26f : 12f) * s.ScaleX, h = (s.Big ? 18f : 4f) * s.ScaleY;
						DrDraw.Rect(s.Pos.X - w / 2f, s.Pos.Y - h / 2f, w, h, SoulMode.Yellow.Color() * s.Alpha);
						DrDraw.Rect(s.Pos.X - w / 4f, s.Pos.Y - h / 4f, w / 2f, h / 2f, Color.White * s.Alpha);
					}
				}

			Color c = soulMode.Color();
			if (frame == 1)
				c = Color.Lerp(c, Color.Black, 0.5f);
			if (soulMode == SoulMode.Red)
			{
				if (!DrDraw.Sprite("spr_dodgeheart", frame, soul.X, soul.Y, Color.White, 1f, 0f, alpha))
					DrDraw.HeartShapeAt(soul.X + 2, soul.Y + 2, 16, frame == 1 ? new Color(128, 0, 0) : Color.Red);
			}
			else if (soulMode == SoulMode.Yellow && DeltaruneAssets.Sprite("spr_yellowheart") is DrSprite y)
			{
				// Deltarune's own yellow SOUL; frame 2 once a big shot is charged, with its pulsing glow
				// The charge: four sparks spiral in from 35 px (spr_yheart_charge), then the SOUL pulses
				if (zHold >= 15)
				{
					float zc = Math.Min(35, zHold - 15);
					for (int i = 0; i < 4; i++)
					{
						float rot = MathHelper.ToRadians(i * 90f + zc * 5f);
						var at = soul + new Vector2(9f, 10f) - new Vector2((float)Math.Sin(rot), (float)Math.Cos(rot)) * (35f - zc);
						float sc = 4f - zc * 2f / 35f;
						if (!DrDraw.Sprite("spr_yheart_charge", 0, at.X, at.Y, Color.White, sc, 0f, Math.Min(1f, zc / 5f)))
							DrDraw.Rect(at.X - sc, at.Y - sc, sc * 2f, sc * 2f, SoulMode.Yellow.Color() * Math.Min(1f, zc / 5f));
					}
				}
				int charge = zHold - 35;
				if (charge >= 0)
				{
					float k = Math.Abs((float)Math.Sin(charge / 10f));
					DrDraw.Sb.Draw(y.Frame(0), soul - new Vector2(k * 10f), null, Color.White * 0.3f, 0f, y.Origin, 1f + k, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0f);
					k = Math.Abs((float)Math.Sin(charge / 14f));
					DrDraw.Sb.Draw(y.Frame(0), soul - new Vector2(2f + k * 10f), null, Color.White * 0.3f, 0f, y.Origin, 1.2f + k, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0f);
				}
				DrDraw.Sb.Draw(y.Frame(zHold >= YellowChargeTicks ? 2 : 0), soul, null, Color.White * alpha, 0f, y.Origin, 1f, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0f);
			}
			else if (DeltaruneAssets.Sprite("soul_" + soulMode.ToString().ToLowerInvariant()) is DrSprite own)
			{
				// Deltarune's own SOUL in this colour, if the installed chapters have one
				DrDraw.Sb.Draw(own.Frame(0), soul, null, (frame == 1 ? Color.Gray : Color.White) * alpha, 0f, own.Origin, 1f, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0f);
			}
			else if (DeltaruneAssets.Sprite("spr_dodgeheart") is DrSprite s)
			{
				// The red SOUL recoloured, keeping its edge and shading
				DrDraw.Sb.Draw(Recolor.Of(s.Frame(frame), soulMode.Color()), soul, null, Color.White * alpha, 0f, s.Origin, 1f, Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0f);
			}
			else
			{
				DrDraw.HeartShapeAt(soul.X + 2, soul.Y + 2, 16, c * alpha);
			}

			if (soulMode == SoulMode.Green)
			{
				// A ring around the SOUL and the shield on the side it faces
				Vector2 centre = BoxCentre;
				for (int i = 0; i < 24; i++)
				{
					float a0 = MathHelper.TwoPi * i / 24f, a1 = MathHelper.TwoPi * (i + 1) / 24f;
					DrDraw.Line(centre + a0.ToRotationVector2() * (ShieldDistance + 4f), centre + a1.ToRotationVector2() * (ShieldDistance + 4f), 1f, SoulMode.Green.Color() * 0.5f * alpha);
				}
				Vector2 dir = shieldAngle.ToRotationVector2();
				Vector2 side = new(-dir.Y, dir.X);
				Vector2 mid = centre + dir * ShieldDistance;
				Color shield = shieldFlash > 0 ? Color.White : new Color(80, 160, 255);
				if (DeltaruneAssets.Sprite("shield") is DrSprite sh)
				{
					// Deltarune's shield sprite: a wide one faces up, a tall one faces right; turned to face the shield's way
					var tex = sh.Frame(0);
					float rot = tex.Width >= tex.Height ? shieldAngle + MathHelper.PiOver2 : shieldAngle;
					float fit = ShieldWidth / Math.Max(tex.Width, tex.Height) * 1.1f;
					DrDraw.Sb.Draw(tex, mid, null, (shieldFlash > 0 ? Color.White : Color.White * 0.95f) * alpha, rot, new Vector2(tex.Width, tex.Height) / 2f,
						Math.Max(1f, (float)Math.Round(fit)), Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0f);
				}
				else
					DrDraw.Line(mid - side * ShieldWidth / 2f, mid + side * ShieldWidth / 2f, ShieldThickness, shield * alpha);
			}
		}
	}
}
