using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace MercyMode.Battle
{
	// ====================================================================== bosses' signature attacks

	/// <summary>
	/// The Roaring Knight's sword lines (Deltarune chapter 3), from how the fight plays (chapter 3's code isn't
	/// available to read): a line of swords flickers into being one after another just outside the box, holds, then
	/// they fire across it in the same order, like dominoes. Each line leaves one gap within reach of the SOUL; the
	/// lines come from different sides, so later ones cross the earlier ones into a grid.
	/// </summary>
	public class SwordLines : RepeatingAttack
	{
		/// <summary>The colour of sword number i in its line (the Empress's lances: a rainbow).</summary>
		public Func<int, Color> Color = _ => Microsoft.Xna.Framework.Color.White;
		public float Spacing = 15f;
		/// <summary>Ticks between two swords appearing (and between two firing).</summary>
		public int AppearStep = 2, FireStep = 1;
		/// <summary>Ticks the full line waits before the first sword fires.</summary>
		public int Hold = 26;
		public float Speed = 5f;
		public float GapSize = 44f;
		/// <summary>Lines may come in at an angle, not only from the four sides.</summary>
		public bool Diagonals;
		public float SwordLength = 24f;
		private int lastSide = -1;

		public SwordLines(int every = 48)
		{
			Every = every;
			StopBeforeEnd = 90;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			Vector2 centre = box.Center.ToVector2();
			float hw = box.Width / 2f, hh = box.Height / 2f;
			// Which way they fly: never the same side twice in a row
			int side = Main.rand.Next(4);
			if (side == lastSide)
				side = (side + 1 + Main.rand.Next(3)) % 4;
			lastSide = side;
			float angle = side * MathHelper.PiOver2;
			if (Diagonals && Main.rand.NextBool())
				angle += MathHelper.PiOver4 * (Main.rand.NextBool() ? 1 : -1);
			Vector2 dir = angle.ToRotationVector2();
			Vector2 across = new(-dir.Y, dir.X);
			// The line stands just outside the box on the side they come from
			float back = Math.Abs(dir.X) * hw + Math.Abs(dir.Y) * hh + SwordLength;
			float half = Math.Abs(across.X) * hw + Math.Abs(across.Y) * hh + 8f;
			Vector2 lineCentre = centre - dir * back;
			// The gap: near the SOUL (a short walk away at most), inside the box
			float soulAlong = Vector2.Dot(battle.SoulCenter - centre, across);
			float limit = Math.Max(0f, half - 8f - GapSize / 2f);
			float gap = MathHelper.Clamp(soulAlong + Main.rand.NextFloat(-36f, 36f), -limit, limit);
			int count = (int)(half * 2f / Spacing) + 1;
			int lastAppear = (count - 1) * AppearStep;
			AttackSfx.Appear();
			int fired = 0;
			for (int i = 0; i < count; i++)
			{
				float along = -half + i * Spacing;
				if (Math.Abs(along - gap) < GapSize / 2f)
					continue;
				int appearAt = i * AppearStep;
				int fireAt = lastAppear + Hold + i * FireStep;
				Vector2 at = lineCentre + across * along;
				Color color = Color(i);
				float speed = Speed, length = SwordLength;
				bool first = fired++ == 0;
				var b = new Bullet
				{
					Position = at,
					Harmful = false,
					StartDelay = appearAt,
					Lifetime = fireAt - appearAt + (int)((back * 2f + 60f) / speed),
					OffscreenMargin = 200f,
					DestroyOnHit = false,
					GrazePoints = 1.5f,
					DamageMult = 0.8f,
				};
				b.OnUpdate = x =>
				{
					int age = x.Age + appearAt;
					if (age == fireAt)
					{
						x.Harmful = true;
						x.Velocity = dir * speed;
						if (first)
							AttackSfx.Slash();
					}
				};
				b.HitTest = (x, r) => x.Harmful && Beam.SegmentNear(x.Position - dir * length * 0.5f, x.Position + dir * length * 0.5f, r, 2.5f);
				b.OnDraw = x =>
				{
					int age = x.Age;
					// Pops in big and white, settles into its colour
					float pop = Math.Min(1f, age / 6f);
					float scale = MathHelper.Lerp(1.5f, 1f, pop);
					Color c = Microsoft.Xna.Framework.Color.Lerp(Microsoft.Xna.Framework.Color.White, color, pop) * Math.Min(1f, age / 3f);
					// A shiver right before it fires
					Vector2 p = x.Position;
					int untilFire = fireAt - appearAt - age;
					if (untilFire > 0 && untilFire < 8)
						p += across * ((age % 2 == 0 ? 1f : -1f) * 1f);
					DrawSword(p, dir, across, length * scale, c);
				};
				battle.Spawn(b);
			}
		}

		/// <summary>A plain sword pointing along <paramref name="dir"/>: blade, guard and grip.</summary>
		public static void DrawSword(Vector2 at, Vector2 dir, Vector2 across, float length, Color color)
		{
			Vector2 tip = at + dir * length * 0.5f, hilt = at - dir * length * 0.2f, end = at - dir * length * 0.5f;
			DrDraw.Line(hilt, tip, 4f, Microsoft.Xna.Framework.Color.Black * (color.A / 255f));
			DrDraw.Line(hilt, tip - dir * 1f, 2.5f, color);
			DrDraw.Line(hilt - across * 5f, hilt + across * 5f, 2.5f, color);
			DrDraw.Line(end, hilt, 2f, color * 0.8f);
		}
	}

	/// <summary>
	/// Queen Bee: the box is a honeycomb. A batch of cells lights up amber, then fills with honey for a moment; a few
	/// cells together near the SOUL always stay clear, a short walk away.
	/// </summary>
	public class Honeycomb : RepeatingAttack
	{
		public float Radius = 15f;
		public int Warn = 46, Active = 28;
		/// <summary>Share of the other cells that fill.</summary>
		public float Fill = 0.55f;
		public Color Honey = new(255, 190, 40);

		public Honeycomb(int every = 84)
		{
			Every = every;
			FirstAt = 6;
			StopBeforeEnd = 80;
		}

		private List<Vector2> Cells(Rectangle box)
		{
			var cells = new List<Vector2>();
			float w = (float)Math.Sqrt(3) * Radius, h = 1.5f * Radius;
			int row = 0;
			for (float y = box.Top + Radius * 0.6f; y < box.Bottom; y += h, row++)
				for (float x = box.Left + (row % 2 == 0 ? w / 2f : w) - 4f; x < box.Right + 2f; x += w)
					cells.Add(new Vector2(x, y));
			return cells;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			List<Vector2> cells = Cells(box);
			// The clear patch: around a spot near the SOUL
			Vector2 near = battle.SoulCenter + Main.rand.NextVector2Circular(34f, 34f);
			Vector2 safe = cells.OrderBy(c => Vector2.DistanceSquared(c, near)).First();
			float r = Radius;
			int warn = Warn, active = Active;
			Color honey = Honey;
			AttackSfx.Appear();
			foreach (Vector2 c in cells)
			{
				if (Vector2.Distance(c, safe) < r * 2.1f || Main.rand.NextFloat() > Fill)
					continue;
				Vector2 cell = c;
				battle.Spawn(new Bullet
				{
					Position = cell,
					Harmful = false,
					DestroyOnHit = false,
					Lifetime = warn + active,
					GrazePoints = 1f,
					DamageMult = 0.7f,
					OnUpdate = x =>
					{
						if (x.Age == warn)
						{
							x.Harmful = true;
							if (cell == safe)
								AttackSfx.Vanilla(SoundID.Item154, 0.5f);
						}
					},
					HitTest = (x, rect) => x.Harmful && Vector2.Distance(rect.Center.ToVector2(), cell) < r * 0.8f,
					OnDraw = x =>
					{
						if (x.Age < warn)
						{
							float a = (x.Age / 4) % 2 == 0 ? 0.9f : 0.5f;
							DrawHex(cell, r - 1f, honey * a, fill: false);
							return;
						}
						float life = (x.Age - warn) / (float)active;
						float fade = life > 0.75f ? (1f - life) / 0.25f : 1f;
						DrawHex(cell, r - 1f, honey * (0.85f * fade), fill: true);
						DrawHex(cell, r - 1f, new Color(255, 240, 160) * fade, fill: false);
					},
				});
			}
		}

		/// <summary>A pointy-topped hexagon: its outline, or filled in thin rows.</summary>
		public static void DrawHex(Vector2 c, float r, Color color, bool fill)
		{
			if (fill)
			{
				float halfW = (float)Math.Sqrt(3) * r / 2f;
				for (float y = -r; y < r; y += 2f)
				{
					float ay = Math.Abs(y + 1f);
					float w = ay <= r / 2f ? halfW : halfW * (r - ay) / (r / 2f);
					DrDraw.Rect(c.X - w, c.Y + y, w * 2f, 2f, color);
				}
				return;
			}
			for (int k = 0; k < 6; k++)
			{
				float a0 = MathHelper.Pi / 6f + k * MathHelper.Pi / 3f, a1 = a0 + MathHelper.Pi / 3f;
				DrDraw.Line(c + a0.ToRotationVector2() * r, c + a1.ToRotationVector2() * r, 2f, color);
			}
		}
	}

	/// <summary>
	/// Skeletron: bone columns slide across the box (Papyrus style), each with a gap; the gap drifts up and down along
	/// a smooth path slow enough to follow.
	/// </summary>
	public class BoneTunnel : RepeatingAttack
	{
		public float Speed = 2.2f;
		public float GapSize = 46f;
		/// <summary>How far the gap wanders from the middle, and how fast (radians per tick).</summary>
		public float Wander = 40f, WanderSpeed = 0.018f;
		public int Side = 1;
		public Color Color = new(240, 235, 220);
		private float phase = -1f;

		public BoneTunnel(int every = 11)
		{
			Every = every;
			StopBeforeEnd = 90;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			if (phase < 0f)
			{
				// Start with the gap where the SOUL is
				float start = MathHelper.Clamp((battle.SoulCenter.Y - box.Center.Y) / Math.Max(1f, Wander), -1f, 1f);
				phase = (float)Math.Asin(start);
			}
			float t = phase + index * Every * WanderSpeed;
			float limit = Math.Min(Wander, box.Height / 2f - GapSize / 2f - 6f);
			float gapY = box.Center.Y + (float)Math.Sin(t) * limit;
			float x0 = Side > 0 ? box.Right + 8f : box.Left - 8f;
			float vx = -Side * Speed;
			float top = box.Top - 4f, bottom = box.Bottom + 4f;
			float gapTop = gapY - GapSize / 2f, gapBottom = gapY + GapSize / 2f;
			Color color = Color;
			battle.Spawn(new Bullet
			{
				Position = new Vector2(x0, gapY),
				Velocity = new Vector2(vx, 0f),
				DestroyOnHit = false,
				Lifetime = (int)((box.Width + 40f) / Speed),
				OffscreenMargin = 200f,
				DamageMult = 0.7f,
				HitTest = (x, r) =>
				{
					var upper = new Rectangle((int)(x.Position.X - 3f), (int)top, 6, (int)(gapTop - top));
					var lower = new Rectangle((int)(x.Position.X - 3f), (int)gapBottom, 6, (int)(bottom - gapBottom));
					return upper.Intersects(r) || lower.Intersects(r);
				},
				OnDraw = x =>
				{
					DrawBone(x.Position.X, top, gapTop, color);
					DrawBone(x.Position.X, gapBottom, bottom, color);
				},
			});
		}

		/// <summary>An upright bone from <paramref name="y0"/> to <paramref name="y1"/>, knobbed at both ends.</summary>
		public static void DrawBone(float x, float y0, float y1, Color color)
		{
			if (y1 - y0 < 4f)
				return;
			DrDraw.Rect(x - 3f, y0, 6f, y1 - y0, color);
			DrDraw.Rect(x - 5f, y0, 10f, 4f, color);
			DrDraw.Rect(x - 5f, y1 - 4f, 10f, 4f, color);
		}
	}

	/// <summary>
	/// The Wall of Flesh pushes in: its side of the box fills with flesh (a pulsing warning edge first), squeezing the
	/// SOUL toward the other side, then pulls back. Never more than <see cref="MaxPush"/> of the box.
	/// </summary>
	public class FleshPush : EnemyAttack
	{
		public int Side = -1;
		public float MaxPush = 0.55f;
		public int Warn = 36, In = 50, Stay = 60, Out = 40;
		public Color Flesh = new(170, 40, 60);
		private bool spawned;

		public FleshPush() => Duration = BattleConstants.DefaultEnemyTurnTicks;

		public override void Update(BattleSystem battle, int tick)
		{
			if (spawned)
				return;
			spawned = true;
			int warn = Warn, inT = In, stay = Stay, outT = Out, side = Side;
			float maxPush = MaxPush;
			Color flesh = Flesh;
			AttackSfx.Vanilla(SoundID.NPCDeath10, 0.6f);
			battle.Spawn(new Bullet
			{
				Position = battle.Box.Center.ToVector2(),
				Harmful = false,
				DestroyOnHit = false,
				Lifetime = warn + inT + stay + outT,
				OffscreenMargin = 2000f,
				DamageMult = 0.8f,
				OnUpdate = x =>
				{
					if (x.Age == warn)
					{
						x.Harmful = true;
						battle.ShakeScreen(3);
					}
				},
				HitTest = (x, r) => x.Harmful && Area(battle.Box, Depth(x.Age, warn, inT, stay, outT) * maxPush, side).Intersects(r),
				OnDraw = x =>
				{
					Rectangle box = battle.Box;
					if (x.Age < warn)
					{
						// Where it will reach, flickering
						Rectangle reach = Area(box, maxPush, side);
						float a = (x.Age / 4) % 2 == 0 ? 0.25f : 0.12f;
						DrDraw.Rect(reach.X, reach.Y, reach.Width, reach.Height, flesh * a);
						return;
					}
					Rectangle now = Area(box, Depth(x.Age, warn, inT, stay, outT) * maxPush, side);
					DrDraw.Rect(now.X, now.Y, now.Width, now.Height, flesh);
					// Its pulsing edge
					float edgeX = side < 0 ? now.Right : now.Left;
					for (int y = now.Top; y < now.Bottom; y += 6)
					{
						float bulge = 3f + (float)Math.Sin(x.Age * 0.3f + y * 0.2f) * 2f;
						DrDraw.Rect(side < 0 ? edgeX - 1f : edgeX - bulge, y, bulge + 1f, 5f, new Color(230, 90, 110));
					}
				},
			});
		}

		private static float Depth(int age, int warn, int inT, int stay, int outT)
		{
			int t = age - warn;
			if (t < 0)
				return 0f;
			if (t < inT)
			{
				float u = t / (float)inT;
				return u * u * (3f - 2f * u);
			}
			t -= inT;
			if (t < stay)
				return 1f;
			t -= stay;
			return 1f - Math.Min(1f, t / (float)outT);
		}

		/// <summary>The part of the box the flesh covers, <paramref name="depth"/> of its width from its side.</summary>
		private static Rectangle Area(Rectangle box, float depth, int side)
		{
			int w = (int)(box.Width * depth);
			return side < 0 ? new Rectangle(box.Left, box.Top, w, box.Height) : new Rectangle(box.Right - w, box.Top, w, box.Height);
		}
	}
}

namespace MercyMode.Battle
{
	/// <summary>
	/// The Eye of Cthulhu watches where you've been: tears well up along the path the SOUL took a moment ago (a
	/// harmless shimmer first), so standing still or doubling back hurts.
	/// </summary>
	public class EchoTrail : EnemyAttack
	{
		public int RecordEvery = 6, Delay = 36, Warn = 14, Active = 34;
		/// <summary>
		/// Where each drop is thrown from (the boss, on the battle screen). Set, every spot the SOUL passes gets a
		/// harmless drop flying at it from there first, so the trail is seen coming instead of just appearing under you.
		/// </summary>
		public Func<BattleSystem, Vector2> From;
		/// <summary>Nothing is recorded before this tick: a beat for the first throws to show where it's coming from.</summary>
		public int LeadIn;
		public Func<Vector2, Vector2, Bullet> Make;
		private readonly Queue<(int At, Vector2 Pos)> path = new();

		public EchoTrail(Func<Vector2, Vector2, Bullet> make) => Make = make;

		public override void Update(BattleSystem battle, int tick)
		{
			if (tick >= LeadIn && tick < Duration - 60 && tick % RecordEvery == 0)
			{
				Vector2 spot = battle.SoulCenter;
				path.Enqueue((tick, spot));
				if (From != null)
					Throw(battle, From(battle), spot);
			}
			while (path.Count > 0 && tick - path.Peek().At >= Delay)
			{
				Vector2 at = path.Dequeue().Pos;
				Bullet b = Make(at, Vector2.Zero);
				int warn = Warn;
				Color baseColor = b.Color;
				b.Harmful = false;
				b.Lifetime = Warn + Active;
				b.DestroyOnHit = false;
				b.OnUpdate += x =>
				{
					if (x.Age < warn)
					{
						x.Alpha = 0.25f + 0.25f * ((x.Age / 2) % 2);
						x.Scale = 0.6f + 0.4f * x.Age / warn;
					}
					else if (x.Age == warn)
					{
						x.Harmful = true;
						x.Alpha = 1f;
						x.Flash = 4;
					}
					else if (x.Age > x.Lifetime - 10)
						x.Alpha = (x.Lifetime - x.Age) / 10f;
				};
				battle.Spawn(b);
			}
		}

		/// <summary>A harmless drop arcs from the boss and lands on the spot just as its warning starts.</summary>
		private void Throw(BattleSystem battle, Vector2 from, Vector2 to)
		{
			Bullet b = Make(from, Vector2.Zero);
			int flight = Delay;
			b.Harmful = false;
			b.DestroyOnHit = false;
			b.Lifetime = flight;
			b.Scale *= 0.7f;
			b.Alpha = 0.8f;
			b.OnUpdate += x =>
			{
				float t = MathHelper.Clamp(x.Age / (float)flight, 0f, 1f);
				// Eased in so it slows as it lands, with a small hop so the stream reads as thrown
				float e = 1f - (1f - t) * (1f - t);
				x.Position = Vector2.Lerp(from, to, e) - new Vector2(0f, (float)Math.Sin(t * MathHelper.Pi) * 24f);
				x.Velocity = Vector2.Zero;
			};
			battle.Spawn(b);
		}
	}

	/// <summary>
	/// King Slime slams down somewhere in the box and rings ripple out from the spot, each with one gap (shown by a
	/// marker while it winds up); rings later in the turn turn their gap a little further round.
	/// </summary>
	public class ShockwaveRings : RepeatingAttack
	{
		public float GrowSpeed = 1.5f, Thickness = 5f, GapAngle = 1.1f;
		/// <summary>How far from the SOUL a slam lands at the least.</summary>
		public float MinFromSoul = 60f;
		public int Warn = 26, RingsPerSlam = 2, RingGap = 22;
		public Color Color = new(90, 150, 255);

		public ShockwaveRings(int every = 70)
		{
			Every = every;
			StopBeforeEnd = 100;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			// Never right on the SOUL: a ring starting under it hit before there was time to move
			Vector2 origin = Vector2.Zero;
			for (int tries = 0; tries < 30; tries++)
			{
				origin = new(Main.rand.NextFloat(box.Left + 30, box.Right - 30), Main.rand.NextFloat(box.Top + 30, box.Bottom - 30));
				if (Vector2.Distance(origin, battle.SoulCenter) >= MinFromSoul)
					break;
			}
			// The first gap points at the SOUL's side of the spot, give or take
			float gap = (battle.SoulCenter - origin).ToRotation() + Main.rand.NextFloat(-0.6f, 0.6f);
			float maxR = new Vector2(box.Width, box.Height).Length();
			AttackSfx.Appear();
			for (int k = 0; k < RingsPerSlam; k++)
			{
				int delay = k * RingGap;
				bool firstRing = k == 0;
				float ringGap = gap + k * 0.7f * (index % 2 == 0 ? 1 : -1);
				int warn = Warn;
				float grow = GrowSpeed, thick = Thickness, gapHalf = GapAngle / 2f;
				Color color = Color;
				battle.Spawn(new Bullet
				{
					Position = origin,
					Harmful = false,
					DestroyOnHit = false,
					StartDelay = delay,
					Lifetime = warn + (int)(maxR / grow),
					OffscreenMargin = 2000f,
					DamageMult = 0.8f,
					OnUpdate = x =>
					{
						if (x.Age == warn)
						{
							x.Harmful = true;
							if (firstRing)
							{
								AttackSfx.Impact();
								battle.ShakeScreen(3);
							}
						}
					},
					HitTest = (x, r) =>
					{
						if (!x.Harmful)
							return false;
						float radius = (x.Age - warn) * grow;
						// Harmless while it's still the slam itself
						if (radius < 14f)
							return false;
						Vector2 d = r.Center.ToVector2() - origin;
						if (Math.Abs(d.Length() - radius) > thick + Math.Max(r.Width, r.Height) / 2f)
							return false;
						return Math.Abs(MathHelper.WrapAngle(d.ToRotation() - ringGap)) > gapHalf;
					},
					OnDraw = x =>
					{
						if (x.Age < warn)
						{
							// The slam spot: a faint ring of what's coming, with its way through lit up in white
							float t = x.Age / (float)warn;
							float pulse = (x.Age / 4) % 2 == 0 ? 1f : 0.6f;
							const int dots = 24;
							for (int s = 0; s < dots; s++)
							{
								float a = MathHelper.TwoPi * s / dots;
								if (Math.Abs(MathHelper.WrapAngle(a - ringGap)) <= gapHalf)
									continue;
								DrDraw.Ball(origin + a.ToRotationVector2() * (14f + t * 10f), 1.6f, color * (0.5f * pulse));
							}
							// The way out: a bright wedge and an arrow along it
							Vector2 dir = ringGap.ToRotationVector2();
							Vector2 side = new(-dir.Y, dir.X);
							float reach = 18f + t * 16f;
							DrDraw.Line(origin + dir * 8f, origin + dir * reach, 3f, Microsoft.Xna.Framework.Color.White * pulse);
							DrDraw.Line(origin + dir * reach, origin + dir * (reach - 6f) + side * 5f, 3f, Microsoft.Xna.Framework.Color.White * pulse);
							DrDraw.Line(origin + dir * reach, origin + dir * (reach - 6f) - side * 5f, 3f, Microsoft.Xna.Framework.Color.White * pulse);
							DrDraw.Ball(origin, 5f + (float)Math.Sin(x.Age * 0.5f) * 1.5f, color * pulse);
							return;
						}
						float radius = (x.Age - warn) * grow;
						float fadeOut = Math.Min(1f, (x.Lifetime - x.Age) / 20f);
						const int segs = 64;
						// Gel: a dark rim, the body, a light inner edge, wobbling a little; bubbles riding along it
						var rim = new Color(20, 40, 120) * fadeOut;
						var shine = new Color(190, 220, 255) * fadeOut;
						for (int s = 0; s < segs; s++)
						{
							float a0 = MathHelper.TwoPi * s / segs, a1 = MathHelper.TwoPi * (s + 1) / segs;
							if (Math.Abs(MathHelper.WrapAngle((a0 + a1) / 2f - ringGap)) <= gapHalf)
								continue;
							float w0 = radius + (float)Math.Sin(a0 * 6f + x.Age * 0.25f) * 1.5f;
							float w1 = radius + (float)Math.Sin(a1 * 6f + x.Age * 0.25f) * 1.5f;
							Vector2 p0 = origin + a0.ToRotationVector2() * w0, p1 = origin + a1.ToRotationVector2() * w1;
							DrDraw.Line(p0, p1, thick + 4f, rim);
							DrDraw.Line(p0, p1, thick, color * fadeOut);
							DrDraw.Line(origin + a0.ToRotationVector2() * (w0 - thick * 0.35f), origin + a1.ToRotationVector2() * (w1 - thick * 0.35f), 1.5f, shine);
							if (s % 8 == 3)
								DrDraw.Ball(origin + a0.ToRotationVector2() * (w0 + 1f), thick * 0.55f, shine * 0.8f);
						}
						// Glints on both edges of the way through
						foreach (float edge in new[] { ringGap - gapHalf, ringGap + gapHalf })
						{
							Vector2 at = origin + edge.ToRotationVector2() * radius;
							float g = 2.5f + (float)Math.Sin(x.Age * 0.4f) * 1f;
							DrDraw.Ball(at, g, Microsoft.Xna.Framework.Color.White * fadeOut);
						}
					},
				});
			}
		}
	}

	/// <summary>
	/// The Brain of Cthulhu's illusions: bullets drift across, all flickering between real (solid) and false (a faint
	/// outline you can pass through) together. They blink a warning before turning real.
	/// </summary>
	public class PhaseBullets : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public int RealTicks = 50, FalseTicks = 50, WarnTicks = 14;
		public float Speed = 1.4f;

		public PhaseBullets(Func<Vector2, Vector2, Bullet> make, int every = 10)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 70;
		}

		/// <summary>Real at this tick of the turn? It starts false, so the first wave can be read.</summary>
		private bool Real(int tick) => tick % (RealTicks + FalseTicks) >= FalseTicks;

		private int turnTick;

		public override void Update(BattleSystem battle, int tick)
		{
			turnTick = tick;
			base.Update(battle, tick);
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			int side = Main.rand.NextBool() ? -1 : 1;
			var pos = new Vector2(side < 0 ? box.Left - 14 : box.Right + 14, Main.rand.NextFloat(box.Top + 8, box.Bottom - 8));
			Bullet b = Make(pos, new Vector2(-side * Speed, Main.rand.NextFloat(-0.3f, 0.3f)));
			float baseAlpha = b.Alpha;
			int cycle = RealTicks + FalseTicks, falseT = FalseTicks, warnT = WarnTicks;
			int born = turnTick;
			b.Lifetime = (int)((box.Width + 40) / Speed);
			b.OnUpdate += x =>
			{
				int t = (born + x.Age) % cycle;
				bool real = t >= falseT;
				x.Harmful = real;
				bool warning = !real && falseT - t <= warnT;
				x.Alpha = real ? baseAlpha : warning && (t / 2) % 2 == 0 ? 0.8f : 0.22f;
			};
			battle.Spawn(b);
		}
	}

	/// <summary>
	/// Deerclops's shadow hands creep in from the edges; every so often they all freeze (flickering), then each turns
	/// to the SOUL and lunges at it.
	/// </summary>
	public class FreezeAndFire : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public float CreepSpeed = 0.7f, LungeSpeed = 3.6f;
		public int FreezeEvery = 80, FreezeTicks = 26;
		private readonly List<Bullet> mine = new();

		public FreezeAndFire(Func<Vector2, Vector2, Bullet> make, int every = 14)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 70;
		}

		public override void Update(BattleSystem battle, int tick)
		{
			base.Update(battle, tick);
			mine.RemoveAll(b => b.Dead);
			int t = tick % FreezeEvery;
			if (tick < FreezeEvery - 10)
				return;
			if (t == FreezeEvery - FreezeTicks)
			{
				AttackSfx.Vanilla(SoundID.Item103, 0.6f);
				foreach (Bullet b in mine.Where(b => b.Velocity.LengthSquared() < CreepSpeed * CreepSpeed * 1.5f))
				{
					b.Velocity = Vector2.Zero;
					b.Flash = FreezeTicks;
				}
			}
			if (t == 0)
			{
				bool any = false;
				foreach (Bullet b in mine.Where(b => b.Velocity == Vector2.Zero))
				{
					b.Velocity = Vector2.Normalize(battle.SoulCenter - b.Position + new Vector2(0.01f, 0f)) * LungeSpeed;
					any = true;
				}
				if (any)
					AttackSfx.Fire();
			}
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			int edge = Main.rand.Next(4);
			Vector2 pos = edge switch
			{
				0 => new Vector2(Main.rand.NextFloat(box.Left, box.Right), box.Top - 12),
				1 => new Vector2(box.Right + 12, Main.rand.NextFloat(box.Top, box.Bottom)),
				2 => new Vector2(Main.rand.NextFloat(box.Left, box.Right), box.Bottom + 12),
				_ => new Vector2(box.Left - 12, Main.rand.NextFloat(box.Top, box.Bottom)),
			};
			Vector2 v = Vector2.Normalize(box.Center.ToVector2() - pos) * CreepSpeed;
			Bullet b = Make(pos, v);
			b.Lifetime = 60 * 8;
			b.OffscreenMargin = 60f;
			mine.Add(b);
			battle.Spawn(b);
		}
	}

	/// <summary>
	/// Retinazer's laser grid: two or three horizontal and vertical beams flash at once, then fire together, leaving
	/// safe squares between them (never closer together than a SOUL can fit through).
	/// </summary>
	public class LaserGrid : RepeatingAttack
	{
		public int Warn = 42, Active = 14;
		public float Width = 8f;
		public int Lines = 2;
		public Color Color = new(255, 60, 60);

		public LaserGrid(int every = 72)
		{
			Every = every;
			StopBeforeEnd = 70;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			// Lines on a coarse lattice (fifths of the box), so the squares between them are always roomy
			float[] fractions = { 0.2f, 0.4f, 0.6f, 0.8f };
			var xs = fractions.OrderBy(_ => Main.rand.Next()).Take(Lines).ToList();
			var ys = fractions.OrderBy(_ => Main.rand.Next()).Take(Lines).ToList();
			bool sounded = false;
			void fire()
			{
				if (sounded)
					return;
				sounded = true;
				AttackSfx.Vanilla(SoundID.Item33, 0.8f);
				battle.ShakeScreen(2);
			}
			AttackSfx.Appear();
			foreach (float fx in xs)
				battle.Spawn(Beam.Make(battle, new Vector2(box.Left + box.Width * fx, box.Center.Y), MathHelper.PiOver2, Warn, Active, Width, Color, 0.8f, fire));
			foreach (float fy in ys)
				battle.Spawn(Beam.Make(battle, new Vector2(box.Center.X, box.Top + box.Height * fy), 0f, Warn, Active, Width, Color, 0.8f, fire));
		}
	}
}
