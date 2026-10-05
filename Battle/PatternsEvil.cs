using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace MercyMode.Battle
{
	// ====================================================================== the world-evil bosses' attacks
	// The Eater of Worlds' worms and the Brain of Cthulhu's mind games, drawn with Terraria's own sprites.

	/// <summary>
	/// A worm made of bullets: the head is steered, and every segment sits a fixed distance behind the one ahead of it
	/// along the path the head took (so it bends the way a Terraria worm does, whatever the head's speed).
	/// </summary>
	internal sealed class WormRig
	{
		public readonly List<Bullet> Segs = new();
		public readonly List<Vector2> Path = new();
		public Vector2 HeadPos, Vel;
		public float Spacing = 13f;
		public int Age;
		/// <summary>Changes <see cref="Vel"/> each tick (the rig and its age).</summary>
		public Action<WormRig, int> Steer;

		/// <summary>
		/// A worm starting with its head at <paramref name="head"/> and its body trailing straight back from
		/// <paramref name="vel"/>: the segments start laid out, not bunched up on the head.
		/// </summary>
		public WormRig(Vector2 head, Vector2 vel, float spacing)
		{
			HeadPos = head;
			Vel = vel;
			Spacing = spacing;
		}

		public void Seed(int count)
		{
			Vector2 back = Vel.LengthSquared() > 0.001f ? -Vector2.Normalize(Vel) : Vector2.UnitX;
			for (int i = count; i >= 1; i--)
				Path.Add(HeadPos + back * Spacing * i);
			Path.Add(HeadPos);
		}

		public bool Alive(BattleSystem battle, Bullet s) => !s.Dead && battle.Bullets.Contains(s);

		public void Step()
		{
			Age++;
			Steer?.Invoke(this, Age);
			HeadPos += Vel;
			Path.Add(HeadPos);
			// Only the bit the body needs is kept
			float need = Spacing * (Segs.Count + 2), have = 0f;
			int keep = Path.Count - 1;
			while (keep > 0 && have < need)
			{
				have += Vector2.Distance(Path[keep], Path[keep - 1]);
				keep--;
			}
			if (keep > 2)
				Path.RemoveRange(0, keep - 1);
			Place();
		}

		/// <summary>Puts each segment its spacing further back along the path, turned along the body.</summary>
		public void Place()
		{
			int idx = Path.Count - 1;
			float walked = 0f;
			Vector2 ahead = HeadPos + Vel;
			for (int i = 0; i < Segs.Count; i++)
			{
				float want = i * Spacing;
				Vector2 at = Path[idx];
				while (idx > 0)
				{
					float d = Vector2.Distance(Path[idx], Path[idx - 1]);
					if (walked + d >= want)
					{
						at = Vector2.Lerp(Path[idx], Path[idx - 1], d > 0.001f ? (want - walked) / d : 0f);
						break;
					}
					walked += d;
					idx--;
					at = Path[idx];
				}
				Bullet s = Segs[i];
				s.Position = at;
				s.Velocity = Vector2.Zero;
				Vector2 dir = ahead - at;
				if (dir.LengthSquared() > 0.01f)
					s.Rotation = dir.ToRotation() + MathHelper.PiOver2;
				ahead = at;
			}
		}
	}

	/// <summary>Builds worms out of a Terraria worm's head, body and tail sprites.</summary>
	public sealed class WormLook
	{
		public int Head, Body, Tail;
		public float Scale = 0.7f, Damage = 0.8f;
		public Vector2 HeadHit = new(16, 16), BodyHit = new(13, 13);

		public static readonly WormLook Eater = new() { Head = NPCID.EaterofWorldsHead, Body = NPCID.EaterofWorldsBody, Tail = NPCID.EaterofWorldsTail };

		public Bullet Make(int type, Vector2 at, Vector2 hit, int toughness)
		{
			Bullet b = Shots.Npc(type, at, Vector2.Zero, Scale, Damage, hit, rotate: false);
			b.Toughness = toughness;
			b.Lifetime = 2000;
			b.OffscreenMargin = 400f;
			return b;
		}

		internal WormRig Build(BattleSystem battle, Vector2 head, Vector2 vel, int segments, float spacing, int toughness = 0)
		{
			var rig = new WormRig(head, vel, spacing);
			rig.Seed(segments);
			for (int i = 0; i < segments; i++)
			{
				int type = i == 0 ? Head : i == segments - 1 ? Tail : Body;
				Bullet b = Make(type, head, i == 0 ? HeadHit : BodyHit, toughness > 0 ? (i == 0 ? toughness + 1 : toughness) : 0);
				rig.Segs.Add(b);
			}
			rig.Place();
			foreach (Bullet b in rig.Segs)
				battle.Spawn(b);
			return rig;
		}

		/// <summary>Turns a body segment into a head (a worm that split grows a new one, like in Terraria).</summary>
		public void MakeHead(Bullet b)
		{
			Bullet look = Shots.Npc(Head, b.Position, Vector2.Zero, Scale, Damage, HeadHit, rotate: false);
			b.Texture = look.Texture;
			b.Source = look.Source;
			b.HitSize = HeadHit;
			b.Flash = 6;
		}
	}

	/// <summary>
	/// Yellow SOUL: Eater of Worlds worms swim in from the right in a wave. Shoot a segment out and the worm splits
	/// there; the back half grows a head and comes at the SOUL on its own, just like the Eater in Terraria.
	/// </summary>
	public class SplittingWorms : RepeatingAttack
	{
		public WormLook Look = WormLook.Eater;
		public int Segments = 9, Toughness = 2;
		public float Speed = 1.5f, Wave = 1.1f, SplitSpeed = 1.9f;
		/// <summary>Red SOUL instead: the worm splits by itself in the middle of the box, into three.</summary>
		public bool SplitsItself;
		private readonly List<WormRig> rigs = new();

		public SplittingWorms(int every = 120)
		{
			Every = every;
			StopBeforeEnd = 110;
			Soul = SoulMode.Yellow;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			float y = Main.rand.NextFloat(box.Top + 30, box.Bottom - 30);
			float phase = Main.rand.NextFloat(MathHelper.TwoPi), speed = Speed, wave = Wave;
			WormRig rig = Look.Build(battle, new Vector2(box.Right + 24f, y), new Vector2(-speed, 0f), Segments, 13f, SplitsItself ? 0 : Toughness);
			rig.Steer = (r, age) => r.Vel = new Vector2(-speed, (float)Math.Cos(age / 22f + phase) * wave);
			rigs.Add(rig);
			AttackSfx.Vanilla(SoundID.Roar, 0.35f, 0.4f);
		}

		public override void Update(BattleSystem battle, int tick)
		{
			base.Update(battle, tick);
			for (int r = rigs.Count - 1; r >= 0; r--)
			{
				WormRig rig = rigs[r];
				if (SplitsItself && rig.Segs.Count >= 6 && rig.HeadPos.X < battle.Box.Center.X + 10f && rig.Age > 10)
					SplitInThree(battle, rig);
				else
					Split(battle, rig);
			}
			foreach (WormRig rig in rigs)
				rig.Step();
			rigs.RemoveAll(rig => rig.Segs.Count == 0);
		}

		/// <summary>Breaks a worm wherever a segment is gone: each surviving run behind a gap becomes its own worm.</summary>
		private void Split(BattleSystem battle, WormRig rig)
		{
			if (rig.Segs.All(s => rig.Alive(battle, s)))
				return;
			var runs = new List<List<Bullet>>();
			List<Bullet> run = null;
			bool headAlive = rig.Alive(battle, rig.Segs[0]);
			foreach (Bullet s in rig.Segs)
			{
				if (rig.Alive(battle, s))
				{
					run ??= new List<Bullet>();
					run.Add(s);
				}
				else if (run != null)
				{
					runs.Add(run);
					run = null;
				}
			}
			if (run != null)
				runs.Add(run);
			rig.Segs.Clear();
			for (int i = 0; i < runs.Count; i++)
			{
				if (i == 0 && headAlive)
				{
					rig.Segs.AddRange(runs[i]);
					continue;
				}
				Sprout(battle, runs[i], Vector2.Normalize(battle.SoulCenter - runs[i][0].Position) * SplitSpeed);
			}
		}

		private void SplitInThree(BattleSystem battle, WormRig rig)
		{
			var segs = rig.Segs.Where(s => rig.Alive(battle, s)).ToList();
			rig.Segs.Clear();
			int third = Math.Max(2, segs.Count / 3);
			float[] turns = { -0.6f, 0f, 0.6f };
			for (int i = 0; i < 3; i++)
			{
				var part = segs.Skip(i * third).Take(i == 2 ? segs.Count : third).ToList();
				if (part.Count == 0)
					continue;
				Vector2 v = new Vector2(-SplitSpeed, 0f).RotatedBy(turns[i]);
				Sprout(battle, part, v);
			}
			AttackSfx.Vanilla(SoundID.NPCHit1, 0.6f, -0.3f);
		}

		/// <summary>A run of segments becomes a new worm: its first segment turns into a head and leads off.</summary>
		private void Sprout(BattleSystem battle, List<Bullet> segs, Vector2 vel)
		{
			if (segs[0].Texture != null && segs[0].HitSize != Look.HeadHit)
				Look.MakeHead(segs[0]);
			var rig = new WormRig(segs[0].Position, vel, 13f);
			for (int k = segs.Count - 1; k >= 0; k--)
				rig.Path.Add(segs[k].Position);
			rig.Segs.AddRange(segs);
			Vector2 v = vel;
			rig.Steer = (r, age) => r.Vel = v;
			rigs.Add(rig);
		}
	}

	/// <summary>
	/// The Eater bursts up out of the ground: a crack flashes under the box, then a worm leaps up through it in an arc
	/// and dives back down, its body following the head's path.
	/// </summary>
	public class Eruption : RepeatingAttack
	{
		public WormLook Look = WormLook.Eater;
		public int Segments = 8, Warn = 40;
		public float Gravity = 0.1f, Launch = 6.1f;
		private readonly List<WormRig> rigs = new();

		public Eruption(int every = 90)
		{
			Every = every;
			StopBeforeEnd = 150;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			// Never right under the SOUL: it has the warning to move, but starting under you reads as unfair
			float x = battle.SoulCenter.X + Main.rand.NextFloat(-50f, 50f);
			x = MathHelper.Clamp(x, box.Left + 20, box.Right - 20);
			float drift = (battle.SoulCenter.X - x) / 140f;
			int warn = Warn;
			const float lane = 28f;
			// The ground cracking: a column flashes with dirt kicked up at the bottom of the box
			battle.Spawn(new Bullet
			{
				Position = new Vector2(x, box.Bottom),
				Harmful = false,
				Lifetime = warn,
				OnDraw = b =>
				{
					float a = b.Age / 3 % 2 == 0 ? 0.4f : 0.18f;
					DrDraw.Rect(x - lane / 2f, box.Top, lane, box.Height, new Color(140, 70, 200) * a);
					for (int i = 0; i < 4; i++)
					{
						float rx = x + (float)Math.Sin(b.Age * 0.7f + i * 2.1f) * lane * 0.45f;
						DrDraw.Ball(new Vector2(rx, box.Bottom - 2f - (b.Age * (i + 1)) % 9), 2f, new Color(120, 90, 60) * 0.9f);
					}
				},
			});
			float launch = Launch, gravity = Gravity;
			int segments = Segments;
			battle.Spawn(new Bullet
			{
				Position = new Vector2(x, box.Bottom + 40f),
				Harmful = false,
				Lifetime = warn + 1,
				OnDraw = _ => { },
				OnUpdate = b =>
				{
					if (b.Age != warn)
						return;
					WormRig rig = Look.Build(battle, new Vector2(x, box.Bottom + 30f), new Vector2(drift, -launch), segments, 13f);
					rig.Steer = (r, age) => r.Vel.Y += gravity;
					rigs.Add(rig);
					battle.ShakeScreen(3f);
					AttackSfx.Vanilla(SoundID.Roar, 0.45f, 0.2f);
				},
			});
		}

		public override void Update(BattleSystem battle, int tick)
		{
			base.Update(battle, tick);
			foreach (WormRig rig in rigs)
				rig.Step();
			rigs.RemoveAll(rig => rig.HeadPos.Y > battle.Box.Bottom + 260f);
		}
	}

	/// <summary>
	/// The Eater coils around the box, closing in, and spits at the SOUL from its head. Its body covers part of the
	/// circle; stay ahead of it.
	/// </summary>
	public class Constrict : EnemyAttack
	{
		public WormLook Look = WormLook.Eater;
		public int Segments = 14, SpitEvery = 44;
		public float StartRadius = 112f, EndRadius = 54f, Turn = 0.03f;
		public Func<Vector2, Vector2, Bullet> Spit;
		private WormRig rig;
		private float angle;

		public override void Update(BattleSystem battle, int tick)
		{
			Vector2 c = battle.Box.Center.ToVector2();
			float RadiusAt(int t) => MathHelper.Lerp(StartRadius, EndRadius, MathHelper.Clamp(t / (Duration * 0.6f), 0f, 1f));
			if (rig == null)
			{
				angle = Main.rand.NextFloat(MathHelper.TwoPi);
				Vector2 head = c + angle.ToRotationVector2() * RadiusAt(0);
				Vector2 tangent = (angle + MathHelper.PiOver2).ToRotationVector2();
				rig = Look.Build(battle, head, tangent, Segments, 13f);
				foreach (Bullet s in rig.Segs)
				{
					// It slithers in from outside before it's dangerous
					s.Alpha = 0f;
					s.Harmful = false;
					s.OnUpdate += x =>
					{
						x.Alpha = Math.Min(1f, x.Age / 30f);
						x.Harmful = x.Age > 24;
					};
				}
				AttackSfx.Vanilla(SoundID.Roar, 0.4f);
			}
			float turn = Turn;
			int t = tick;
			rig.Steer = (r, age) =>
			{
				angle += turn;
				Vector2 want = c + angle.ToRotationVector2() * RadiusAt(t);
				r.Vel = want - r.HeadPos;
			};
			rig.Step();
			if (Spit != null && tick > 40 && tick < Duration - 50 && tick % SpitEvery == 0 && rig.Segs.Count > 0 && rig.Alive(battle, rig.Segs[0]))
			{
				Vector2 from = rig.Segs[0].Position;
				battle.Spawn(Spit(from, Vector2.Normalize(battle.SoulCenter - from) * 2.2f));
				AttackSfx.Vanilla(SoundID.NPCDeath13, 0.4f);
			}
		}
	}

	/// <summary>
	/// Yellow SOUL: blobs of Vile Spit drift in from the right, bending a little toward the SOUL. Shoot them down.
	/// </summary>
	public class VileDrift : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public float Speed = 1.2f, Turn = 0.018f;

		public VileDrift(Func<Vector2, Vector2, Bullet> make, int every = 26)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 80;
			Soul = SoulMode.Yellow;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			Vector2 from = new(box.Right + 20f, Main.rand.NextFloat(box.Top + 10, box.Bottom - 10));
			Bullet b = Make(from, new Vector2(-Speed, Main.rand.NextFloat(-0.4f, 0.4f)));
			float speed = Speed, turn = Turn;
			b.OnUpdate += x =>
			{
				if (x.Age > 150)
					return;
				float want = (battle.SoulCenter - x.Position).ToRotation();
				float now = x.Velocity.ToRotation();
				x.Velocity = (now + MathHelper.Clamp(MathHelper.WrapAngle(want - now), -turn, turn)).ToRotationVector2() * speed;
			};
			battle.Spawn(b);
		}
	}

	// ---------------------------------------------------------------------- Brain of Cthulhu

	/// <summary>
	/// The Brain's confusion (Terraria's Confused debuff): a swirl over the SOUL, then the arrow keys are reversed for
	/// the rest of the turn while Creepers drift slowly across.
	/// </summary>
	public class MindFlip : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public int Warn = 50;
		public float Speed = 1.3f;

		public MindFlip(Func<Vector2, Vector2, Bullet> make, int every = 36)
		{
			Make = make;
			Every = every;
			FirstAt = 30;
			StopBeforeEnd = 70;
		}

		public override void Update(BattleSystem battle, int tick)
		{
			if (tick == 1)
			{
				int warn = Warn, duration = Duration;
				AttackSfx.Vanilla(SoundID.Item8, 0.6f, -0.5f);
				// The swirl, then the debuff's icon, over the SOUL for the whole turn
				battle.Spawn(new Bullet
				{
					Harmful = false,
					Lifetime = duration,
					OffscreenMargin = 9999f,
					OnUpdate = b => b.Position = battle.SoulCenter,
					OnDraw = b =>
					{
						Vector2 at = battle.SoulCenter;
						if (b.Age < warn)
						{
							float k = b.Age / (float)warn;
							for (int i = 0; i < 6; i++)
							{
								float a = b.Age * 0.25f + i * MathHelper.TwoPi / 6f;
								DrDraw.Ball(at + a.ToRotationVector2() * (24f * (1f - k) + 6f), 2f, new Color(220, 120, 255) * (0.4f + 0.6f * k));
							}
						}
						Texture2D icon = TextureAssets.Buff[BuffID.Confused].Value;
						float alpha = Math.Min(1f, b.Age / (float)warn) * (b.Age > duration - 12 ? (duration - b.Age) / 12f : 1f);
						DrDraw.Sb.Draw(icon, at + new Vector2(0f, -22f), null, Color.White * alpha, 0f, icon.Size() / 2f, 0.7f, SpriteEffects.None, 0f);
					},
				});
			}
			if (tick >= Warn && tick < Duration - 8)
				battle.Confuse();
			base.Update(battle, tick);
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			bool fromLeft = index % 2 == 0;
			float y = MathHelper.Clamp(battle.SoulCenter.Y + Main.rand.NextFloat(-40f, 40f), box.Top + 12, box.Bottom - 12);
			Bullet b = Make(new Vector2(fromLeft ? box.Left - 20f : box.Right + 20f, y), new Vector2(fromLeft ? Speed : -Speed, 0f));
			battle.Spawn(b);
		}
	}

	/// <summary>
	/// The Brain's illusions: copies of it fade in around the box, then all of them charge at the SOUL. The fakes
	/// flicker and pass straight through you; only the steady one is real.
	/// </summary>
	public class BrainIllusions : RepeatingAttack
	{
		public int Copies = 3, Warn = 44;
		public float Speed = 5f;

		public BrainIllusions(int every = 84)
		{
			Every = every;
			StopBeforeEnd = 90;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Vector2 c = battle.Box.Center.ToVector2();
			float start = Main.rand.NextFloat(MathHelper.TwoPi);
			int real = Main.rand.Next(Copies), warn = Warn;
			float speed = Speed;
			AttackSfx.Appear();
			for (int i = 0; i < Copies; i++)
			{
				bool isReal = i == real;
				Vector2 at = c + (start + MathHelper.TwoPi * i / Copies).ToRotationVector2() * 118f;
				Bullet b = Shots.Npc(NPCID.BrainofCthulhu, at, Vector2.Zero, 0.42f, 1f, new Vector2(28, 24), rotate: false);
				b.Harmful = false;
				b.Alpha = 0f;
				b.DestroyOnHit = isReal;
				b.Lifetime = warn + 90;
				b.OnUpdate += x =>
				{
					if (x.Age < warn)
					{
						float fade = Math.Min(1f, x.Age / (warn * 0.5f));
						// The fakes shimmer from the start; the real one holds still
						x.Alpha = isReal ? fade : fade * (0.35f + 0.3f * (float)Math.Sin(x.Age * 0.5f));
						x.Position += Main.rand.NextVector2Circular(isReal ? 0f : 0.8f, isReal ? 0f : 0.8f);
						return;
					}
					if (x.Age == warn)
					{
						x.Velocity = Vector2.Normalize(battle.SoulCenter - x.Position) * speed;
						x.Harmful = isReal;
						x.Trail = 3;
						if (isReal)
							AttackSfx.Vanilla(SoundID.ForceRoar, 0.4f, 0.3f);
					}
					if (!isReal)
						x.Alpha = 0.3f + 0.2f * (float)Math.Sin(x.Age * 0.6f);
				};
				battle.Spawn(b);
			}
		}
	}

	/// <summary>
	/// Creepers circle the box like they circle the Brain, then break off one at a time and ram the SOUL (each flashes
	/// first).
	/// </summary>
	public class CreeperCharge : EnemyAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public int Count = 6, Every = 38, FirstAt = 50, Flash = 16;
		public float Radius = 108f, Spin = 0.018f, Speed = 4.2f;
		private readonly List<(Bullet B, float A)> ring = new();
		private int next;

		public override void Update(BattleSystem battle, int tick)
		{
			Vector2 c = battle.Box.Center.ToVector2();
			if (tick == 1)
			{
				float start = Main.rand.NextFloat(MathHelper.TwoPi);
				for (int i = 0; i < Count; i++)
				{
					Bullet b = Make(c, Vector2.Zero);
					b.Harmful = false;
					b.Alpha = 0f;
					b.Lifetime = Duration + 60;
					b.OffscreenMargin = 300f;
					ring.Add((b, start + MathHelper.TwoPi * i / Count));
					battle.Spawn(b);
				}
				AttackSfx.Appear();
			}
			int launch = tick - FirstAt;
			if (launch >= 0 && launch % Every == 0 && next < ring.Count && tick < Duration - 60)
			{
				Bullet b = ring[next].B;
				next++;
				int flash = Flash;
				float speed = Speed;
				int at = b.Age;
				b.OnUpdate += x =>
				{
					if (x.Age - at < flash)
					{
						x.Flash = (x.Age / 2) % 2 == 0 ? 2 : 0;
						return;
					}
					if (x.Age - at == flash)
					{
						x.Velocity = Vector2.Normalize(battle.SoulCenter - x.Position) * speed;
						x.Harmful = true;
						x.Trail = 3;
						AttackSfx.Fire();
					}
				};
			}
			// The rest keep circling (one that has broken off flies on its own)
			for (int i = next; i < ring.Count; i++)
			{
				var (b, a0) = ring[i];
				float a = a0 + Spin * tick;
				b.Position = c + a.ToRotationVector2() * Radius;
				b.Alpha = Math.Min(1f, tick / 25f);
			}
			for (int i = 0; i < next; i++)
			{
				Bullet b = ring[i].B;
				if (b.Velocity == Vector2.Zero && !b.Harmful)
				{
					float a = ring[i].A + Spin * tick;
					b.Position = c + a.ToRotationVector2() * Radius;
				}
			}
		}
	}

	/// <summary>
	/// Neurons light up inside the box and fire along the lines between them: a thin flicker shows where, then the
	/// synapse burns for a moment.
	/// </summary>
	public class NeuronWeb : RepeatingAttack
	{
		public int Nodes = 3, Warn = 42, Active = 18;
		public float Width = 7f;
		public Color Color = new(255, 110, 150);

		public NeuronWeb(int every = 66)
		{
			Every = every;
			StopBeforeEnd = 70;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			var pts = new List<Vector2>();
			for (int i = 0; i < Nodes; i++)
				pts.Add(new Vector2(Main.rand.NextFloat(box.Left + 14, box.Right - 14), Main.rand.NextFloat(box.Top + 14, box.Bottom - 14)));
			int warn = Warn, active = Active;
			float width = Width;
			Color color = Color;
			AttackSfx.Appear();
			for (int i = 0; i + 1 < pts.Count; i++)
			{
				Vector2 a = pts[i], b = pts[i + 1];
				battle.Spawn(new Bullet
				{
					Position = (a + b) / 2f,
					Harmful = false,
					DestroyOnHit = false,
					Lifetime = warn + active,
					GrazePoints = 4f,
					OnUpdate = x =>
					{
						if (x.Age == warn)
						{
							x.Harmful = true;
							AttackSfx.Vanilla(SoundID.Item93, 0.35f, 0.4f);
						}
					},
					HitTest = (x, soul) =>
					{
						Vector2 p = soul.Center.ToVector2();
						Vector2 ab = b - a;
						float t = MathHelper.Clamp(Vector2.Dot(p - a, ab) / Math.Max(1f, ab.LengthSquared()), 0f, 1f);
						return Vector2.Distance(p, a + ab * t) < width / 2f + soul.Width / 2f - 2f;
					},
					OnDraw = x =>
					{
						if (x.Age < warn)
						{
							float flick = x.Age / 3 % 2 == 0 ? 0.55f : 0.25f;
							DrDraw.Line(a, b, 1.5f, color * flick);
						}
						else
						{
							float fade = 1f - (x.Age - warn) / (float)active;
							DrDraw.Line(a, b, width + 4f, color * (0.35f * fade));
							DrDraw.Line(a, b, width, color * fade);
							DrDraw.Line(a, b, 2f, Color.White * fade);
						}
						// The neurons themselves: a soft glow, a body and a bright nucleus
						foreach (Vector2 n in new[] { a, b })
						{
							DrDraw.Glow(n, 10f, color * 0.5f);
							DrDraw.Ball(n, 4.5f, new Color(170, 40, 80));
							DrDraw.Ball(n, 2f, Color.White);
						}
					},
				});
			}
		}
	}

	/// <summary>Spinning vertebrae lobbed from the Brain's side in arcs that come down where the SOUL is.</summary>
	public class BoneLob : RepeatingAttack
	{
		public Func<Vector2, Vector2, Bullet> Make;
		public float Gravity = 0.08f;
		public float Flight = 70f;

		public BoneLob(Func<Vector2, Vector2, Bullet> make, int every = 22)
		{
			Make = make;
			Every = every;
			StopBeforeEnd = 80;
		}

		protected override void Spawn(BattleSystem battle, int index)
		{
			Rectangle box = battle.Box;
			Vector2 from = new(box.Right + 16f, Main.rand.NextFloat(box.Top - 10, box.Center.Y));
			Vector2 to = battle.SoulCenter + new Vector2(Main.rand.NextFloat(-22f, 22f), 0f);
			float t = Flight * Main.rand.NextFloat(0.85f, 1.15f);
			var vel = new Vector2((to.X - from.X) / t, (to.Y - from.Y - 0.5f * Gravity * t * t) / t);
			Bullet b = Make(from, vel);
			b.Acceleration = new Vector2(0f, Gravity);
			b.RotateWithVelocity = false;
			b.OnUpdate += x => x.Rotation += 0.2f;
			battle.Spawn(b);
		}
	}
}
