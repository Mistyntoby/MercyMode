using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using MercyMode.Battle.Encounters;

namespace MercyMode.Battle
{
	/// <summary>
	/// Battles with up to three enemies, like Deltarune's. A regular enemy pulls nearby ones in (an army's squad
	/// during an event); each has its own HP, MERCY, ACTs and spot on screen. FIGHT, ACT and SPARE pick a target;
	/// the battle is won when none are left. Bosses still fight alone.
	/// </summary>
	public partial class BattleSystem
	{
		/// <summary>One enemy in the battle and everything the battle screen keeps about it.</summary>
		private sealed class BattleEnemy
		{
			public Encounter E;
			// where it stood in the world, for the glide in and out
			public Vector2 WorldScreen;
			public float WorldScale, WorldRotation;
			// the battle screen
			public EnemySnapshot Snap;
			public BattleEffect Override; // spare / death animation
			public int Shake;
			public readonly List<EnemyTrail> Trail = new();
			/// <summary>Spared or defeated: no longer targeted or attacking (its animation may still be playing).</summary>
			public bool Out;
			public bool Living => !Out && E.Alive;
			/// <summary>The frame shown for it, and since when (frames change at a steady pace, not every tick).</summary>
			public Rectangle ShownFrame;
			public int ShownFrameAt;
			/// <summary>Terraria's own frame last tick, and how much it's been flickering (high: hold still).</summary>
			public Rectangle RawFrame;
			public int RawFrameTick = -1;
			public float FrameJitter;
			/// <summary>How often Terraria's frame changed since the shown one last did.</summary>
			public int RawChanges;
			/// <summary>The frames (their Y) Terraria has picked lately, and when last.</summary>
			public readonly Dictionary<int, int> FramesSeen = new();
			/// <summary>Its speech bubble this turn, and when it started.</summary>
			public string Bubble;
			public int BubbleAt;
			/// <summary>Turns it sat out (its lines move on even when it doesn't attack).</summary>
			public int IdleTalks;
		}

		private struct EnemyTrail
		{
			public Vector2 Pos;
			public float Scale, Age;
		}

		/// <summary>The most enemies one battle pulls in, and how close (in pixels) they must be.</summary>
		private const int MaxEnemies = 3;
		private const float GatherRange = 640f;

		private readonly List<BattleEnemy> enemies = new();
		/// <summary>The enemy FIGHT, ACT and SPARE are aimed at; <see cref="encounter"/> is its encounter.</summary>
		private BattleEnemy targetEnemy;
		/// <summary>The fewest ticks between two of an enemy's frames on the battle screen.</summary>
		private const int EnemyFrameTicks = 5;
		/// <summary>Regular enemies hold each frame at least this long, so every walker steps at one steady pace.</summary>
		private const int RegularEnemyFrameTicks = 9;
		/// <summary>The enemy the per-enemy fields below refer to: the target, or the one being drawn or stepped.</summary>
		private BattleEnemy focus;

		// Per-enemy state, through whichever enemy has the focus
		private EnemySnapshot enemySnap { get => focus?.Snap ?? default; set { if (focus != null) focus.Snap = value; } }
		private BattleEffect enemyOverride { get => focus?.Override; set { if (focus != null) focus.Override = value; } }
		private int enemyShake { get => focus?.Shake ?? 0; set { if (focus != null) focus.Shake = value; } }
		private Vector2 enemyWorldScreen => focus?.WorldScreen ?? Vector2.Zero;
		private float enemyWorldScale => focus?.WorldScale ?? 1f;
		private float enemyWorldRotation => focus?.WorldRotation ?? 0f;

		private List<BattleEnemy> LivingEnemies => enemies.Where(e => e.Living).ToList();
		/// <summary>Every encounter in this battle (test commands).</summary>
		public IEnumerable<Encounter> Encounters => enemies.Select(e => e.E);

		private void SetTarget(BattleEnemy e)
		{
			targetEnemy = e;
			focus = e;
			encounter = e?.E;
		}

		/// <summary>Points the per-enemy fields (and <see cref="encounter"/>) at another enemy for a moment.</summary>
		private void WithEnemy(BattleEnemy e, Action action)
		{
			BattleEnemy savedFocus = focus;
			Encounter savedEncounter = encounter;
			focus = e;
			encounter = e.E;
			try
			{
				action();
			}
			finally
			{
				focus = savedFocus;
				encounter = savedEncounter;
			}
		}

		/// <summary>Keeps the target on someone still fighting.</summary>
		private void RetargetIfNeeded()
		{
			if (targetEnemy != null && targetEnemy.Living)
				return;
			BattleEnemy next = LivingEnemies.FirstOrDefault();
			if (next != null)
				SetTarget(next);
		}

		/// <summary>The enemies a battle with <paramref name="root"/> is fought against: it, plus nearby ones for regular fights.</summary>
		private List<NPC> GatherEnemies(NPC root)
		{
			var list = new List<NPC> { root };
			if (EncounterRegistry.IsBossFight(root))
				return list;
			ArmyKind army = Armies.ArmyOf(root);
			var found = new List<NPC>();
			foreach (NPC n in Main.ActiveNPCs)
			{
				NPC r = EncounterRegistry.ResolveRoot(n);
				if (r == root || list.Contains(r) || found.Contains(r) || EncounterRegistry.IsBossFight(r) || !EncounterRegistry.Eligible(r))
					continue;
				if (r.DistanceSQ(Player.Center) > GatherRange * GatherRange)
					continue;
				// During an event, a squad is made of the same army
				if (army != ArmyKind.None && Armies.ArmyOf(r) != army)
					continue;
				found.Add(r);
			}
			list.AddRange(found.OrderBy(r => r.DistanceSQ(Player.Center)).Take(MaxEnemies - 1));
			return list;
		}

		/// <summary>Battle-screen spots for 1-3 enemies (a staggered column on the right, like Deltarune's) and their size limits.</summary>
		private static (Vector2 center, Vector2 area)[] Formation(int count) => count switch
		{
			2 => new[] { (new Vector2(470f, 135f), new Vector2(160f, 120f)), (new Vector2(545f, 235f), new Vector2(160f, 120f)) },
			3 => new[]
			{
				(new Vector2(455f, 100f), new Vector2(130f, 95f)),
				(new Vector2(550f, 180f), new Vector2(130f, 95f)),
				(new Vector2(455f, 262f), new Vector2(130f, 95f)),
			},
			_ => new[] { ((Vector2)default, (Vector2)default) },
		};

		private void SetUpEnemies(NPC root, List<NPC> given = null)
		{
			enemies.Clear();
			List<NPC> npcs = given ?? GatherEnemies(root);
			var spots = Formation(npcs.Count);
			for (int i = 0; i < npcs.Count; i++)
			{
				// A duel: the other player, through the proxy NPC
				Encounter e = duelWith >= 0 && duelEnc != null ? duelEnc : EncounterRegistry.Create(npcs[i]);
				if (npcs.Count > 1)
				{
					e.Slot = spots[i].center;
					e.SlotArea = spots[i].area;
				}
				enemies.Add(new BattleEnemy { E = e });
			}
			SetTarget(enemies[0]);
		}

		/// <summary>The line the battle opens with: the enemy's own, or a group's.</summary>
		private string OpeningText()
		{
			if (enemies.Count == 1)
				return encounter.EncounterText;
			return encounter.GroupEncounterText(enemies.Count - 1);
		}

		// ---- squads ----

		/// <summary>After an enemy is spared: the rest of an army's squad loses heart.</summary>
		private string OnEnemySpared(BattleEnemy spared)
		{
			if (spared.E is not ArmyEnemy army)
				return null;
			bool any = false;
			foreach (BattleEnemy e in LivingEnemies)
			{
				if (e.E is ArmyEnemy other && other.Kind == army.Kind)
				{
					other.Mercy += ArmyEnemy.MoraleOnSpare;
					any = true;
				}
			}
			return any ? army.SquadSparedLine : null;
		}

		/// <summary>After an enemy is defeated: the rest of an army's squad gets angry and attacks harder.</summary>
		private void OnEnemyDefeated(BattleEnemy defeated)
		{
			if (defeated.E is not ArmyEnemy army)
				return;
			foreach (BattleEnemy e in LivingEnemies)
				if (e.E is ArmyEnemy other && other.Kind == army.Kind)
					other.Enraged = true;
		}

		// ---- enemy turns ----

		/// <summary>Who spawned the bullets being made right now (for their damage).</summary>
		private Encounter spawnOwner;
		/// <summary>Damage for bullets with no known owner this turn (spawned by other bullets).</summary>
		private int turnDamage;
		private float spawnDamageScale = 1f;

		/// <summary>Bosses go all out once, below this much health.</summary>
		private const float DesperationLife = 0.3f;

		/// <summary>The boss about to go all out (it gets a line first), or null.</summary>
		private BattleEnemy DesperateBoss()
		{
			List<BattleEnemy> living = LivingEnemies;
			if (living.Count != 1 || !living[0].E.IsBoss || living[0].E.DesperationUsed || living[0].E.LifeRatio >= DesperationLife)
				return null;
			return living[0];
		}

		/// <summary>
		/// The all-out turn: two of the boss's attacks at once, each a little gentler (0.75x damage), longer. A
		/// full-screen attack stays on its own.
		/// </summary>
		private EnemyAttack BuildDesperationTurn(BattleEnemy boss)
		{
			boss.E.DesperationUsed = true;
			EnemyAttack first = null, second = null;
			WithEnemy(boss, () => first = boss.E.NextAttack(this));
			boss.E.Turn++;
			WithEnemy(boss, () => second = boss.E.NextAttack(this));
			boss.E.Turn++;
			turnDamage = boss.E.Damage;
			ShakeScreen(6);
			AttackSfx.Vanilla(Terraria.ID.SoundID.Roar, 0.8f);
			AddEffect(new Shockwave(boss.E.ScreenCenter, new Color(255, 60, 60), 90f));
			if (first.FullScreen || second.FullScreen)
			{
				EnemyAttack big = first.FullScreen ? first : second;
				return new OwnedAttack(boss, big);
			}
			int length = (int)(Math.Max(first.Duration, second.Duration) * 1.25f);
			return new Combo(length, new OwnedAttack(boss, first) { DamageScale = 0.75f }, new OwnedAttack(boss, second) { DamageScale = 0.75f });
		}

		/// <summary>Runs one enemy's attack, tagging its bullets as its own.</summary>
		private sealed class OwnedAttack : EnemyAttack
		{
			private readonly BattleEnemy owner;
			private readonly EnemyAttack inner;
			internal EnemyAttack Inner => inner;
			/// <summary>Multiplies the damage of every bullet this attack makes.</summary>
			public float DamageScale = 1f;

			public OwnedAttack(BattleEnemy owner, EnemyAttack inner)
			{
				this.owner = owner;
				this.inner = inner;
				Duration = inner.Duration;
				FullScreen = inner.FullScreen;
				Soul = inner.Soul;
			}

			public override void Update(BattleSystem battle, int tick)
			{
				battle.spawnOwner = owner.E;
				battle.spawnDamageScale = DamageScale;
				try
				{
					// The attack sees its own enemy as the battle's encounter (where bullets come from, its HP...)
					battle.WithEnemy(owner, () => inner.Update(battle, tick));
				}
				finally
				{
					battle.spawnOwner = null;
					battle.spawnDamageScale = 1f;
				}
			}
		}

		/// <summary>This turn's attack: every enemy's if there are two, two random ones if there are three.</summary>
		private EnemyAttack BuildEnemyTurn()
		{
			if (LabForcedAttack != null)
			{
				turnDamage = targetEnemy.E.Damage;
				return new OwnedAttack(targetEnemy, LabForcedAttack());
			}
			if (DesperateBoss() is BattleEnemy boss && boss.E.DesperationAnnounced)
				return BuildDesperationTurn(boss);
			List<BattleEnemy> living = LivingEnemies;
			List<BattleEnemy> attackers = living.Count <= 2 ? living : living.OrderBy(_ => Main.rand.Next()).Take(2).ToList();
			if (attackers.Count == 0)
				attackers = new List<BattleEnemy> { targetEnemy };
			turnDamage = attackers.Max(a => a.E.Damage);

			var parts = new List<EnemyAttack>();
			foreach (BattleEnemy a in attackers)
			{
				EnemyAttack part = null;
				// What it says as the box opens (Deltarune's speech bubbles)
				a.Bubble = a.E.Bubble(a.E.Turn);
				a.BubbleAt = time;
				// Every third turn (the first included), an enemy with its own attack uses that instead of its family's
				WithEnemy(a, () => part = (a.E is EnemyEncounter ee && a.E.Turn % 3 == 0 ? ee.SignatureAttack(this) : null) ?? a.E.NextAttack(this));
				a.E.Turn++;
				parts.Add(new OwnedAttack(a, part));
			}
			// The ones sitting this turn out still say something (Deltarune: everyone talks before the box opens)
			foreach (BattleEnemy idle in living)
				if (!attackers.Contains(idle))
				{
					idle.Bubble = idle.E.Bubble(idle.E.Turn + 1 + idle.IdleTalks++);
					idle.BubbleAt = time;
				}
			// Two of a kind don't say the same thing at once: the later one moves on to another of its lines
			var saying = new HashSet<string>();
			foreach (BattleEnemy e in living)
			{
				if (string.IsNullOrEmpty(e.Bubble))
					continue;
				for (int k = 1; k < 4 && saying.Contains(e.Bubble); k++)
					e.Bubble = e.E.Bubble(e.E.Turn + k * 7 + e.IdleTalks) ?? e.Bubble;
				saying.Add(e.Bubble);
			}
			if (parts.Count == 1)
				return parts[0];
			// Several enemies at once (Deltarune thins each one's attack out the same way): each fires less often, and
			// their volleys take turns instead of landing together (two zombie walls with their own gaps at the same
			// moment left nowhere to go)
			for (int k = 0; k < parts.Count; k++)
				if (((OwnedAttack)parts[k]).Inner is RepeatingAttack ra)
				{
					ra.Every = (int)Math.Round(ra.Every * (1f + 0.6f * (parts.Count - 1)));
					ra.FirstAt += k * ra.Every / parts.Count;
				}
			return new Combo(parts.Max(p => p.Duration), parts.ToArray()) { FullScreen = parts.Any(p => p.FullScreen) };
		}
	}
}
