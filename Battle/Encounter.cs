using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using MercyMode.Battle.Encounters;

namespace MercyMode.Battle
{
	/// <summary>One entry in the ACT menu.</summary>
	public class ActOption
	{
		public string Name;
		/// <summary>Short text shown on the right while the act is highlighted.</summary>
		public string Description = "";
		/// <summary>Its colour in the ACT list (PACIFY is blue while the target is TIRED), or null for white.</summary>
		public Microsoft.Xna.Framework.Color? Color;
		/// <summary>TP cost in Mercy Mode's 0-100 TP. 0 for normal acts.</summary>
		public float TPCost;
		/// <summary>Runs the act and returns the lines to show. Each string is one text box.</summary>
		public Func<BattleSystem, List<string>> Run;
	}

	/// <summary>
	/// An enemy turn: spawns bullets while the turn timer runs. Update is called once per tick with the tick
	/// count since the box finished growing.
	/// </summary>
	public abstract class EnemyAttack
	{
		/// <summary>Turn length in ticks (global.turntimer).</summary>
		public int Duration = BattleConstants.DefaultEnemyTurnTicks;
		/// <summary>
		/// The box opens up into an arena over the whole battle screen and everything else goes dark, like the
		/// Roaring Knight's attacks. Patterns built on <see cref="BattleSystem.Box"/> fill the arena.
		/// </summary>
		public bool FullScreen;
		/// <summary>How the SOUL moves during this attack (red: freely).</summary>
		public SoulMode Soul;
		public abstract void Update(BattleSystem battle, int tick);
	}

	/// <summary>
	/// Battle content for one enemy (or one multi-part boss): name, acts, text and bullet patterns.
	/// <see cref="Npc"/> is the NPC the battle started with; <see cref="Members"/> are all the NPCs that make up
	/// the fight (worm segments, Skeletron's hands, the Brain's Creepers...).
	/// </summary>
	public abstract class Encounter
	{
		public NPC Npc;
		/// <summary>How hard its attack is going right now (0-1), on the battle screen: for poses and frames.</summary>
		public float AttackEnergy;
		public int Turn;
		/// <summary>A boss's one-time all-out turn at low HP: announced, then used.</summary>
		public bool DesperationAnnounced, DesperationUsed;
		/// <summary>How many times each act has been used, by name.</summary>
		public readonly Dictionary<string, int> ActUses = new();
		private int lifeMax;

		public virtual string Name => Npc.GivenOrTypeName.ToUpperInvariant();
		public virtual string EncounterText => $"* {Name} drew near!";
		/// <summary>The line shown in the text box at the start of a player turn.</summary>
		public abstract string FlavorText();
		public abstract List<ActOption> Acts(BattleSystem battle);
		public abstract EnemyAttack NextAttack(BattleSystem battle);

		// ---- members and health ----

		/// <summary>Every NPC that belongs to this fight. Default: the NPC and anything sharing its health.</summary>
		public virtual IEnumerable<NPC> Members()
		{
			if (Npc.active)
				yield return Npc;
			for (int i = 0; i < Main.maxNPCs; i++)
			{
				NPC n = Main.npc[i];
				if (n.active && i != Npc.whoAmI && n.realLife == Npc.whoAmI)
					yield return n;
			}
		}

		public bool Alive => Members().Any(m => m.active && m.life > 0);

		/// <summary>Members with their own health pool (worm segments share their head's).</summary>
		public IEnumerable<NPC> HealthPools() => Members().Where(m => m.active && (m.realLife < 0 || m.realLife == m.whoAmI));

		public int Life => HealthPools().Sum(m => Math.Max(0, m.life));

		public int LifeMax
		{
			get
			{
				lifeMax = Math.Max(lifeMax, HealthPools().Sum(m => m.lifeMax));
				return Math.Max(1, lifeMax);
			}
		}

		public float LifeRatio => Life / (float)LifeMax;

		/// <summary>Contact damage the bullets are based on.</summary>
		public virtual int Damage
		{
			get
			{
				// Capped at twice the normal damage: daytime Skeletron and other enraged bosses set 9999
				int d = Members().Where(m => m.active)
					.Select(m => m.defDamage > 0 ? Math.Min(m.damage, m.defDamage * 2) : m.damage)
					.DefaultIfEmpty(0).Max();
				return d > 0 ? d : 10;
			}
		}

		// ---- breakable parts ----

		/// <summary>
		/// FIGHT picks a part (Skeletron's hands or head, either Twin...) instead of the boss as a whole. Each part has
		/// its own HP; breaking one takes it out of the fight, and breaking <see cref="CorePart"/> ends it.
		/// </summary>
		public virtual bool TargetableParts => false;
		/// <summary>The part FIGHT is aimed at (null: <see cref="StrikeTarget"/> decides).</summary>
		public NPC ChosenPart;
		/// <summary>The part whose loss ends the fight (the rest go with it); null when every part has to go (the Twins).</summary>
		public virtual NPC CorePart => Npc.active ? Npc : null;
		public virtual string PartName(NPC part) => Lang.GetNPCNameValue(part.type).ToUpperInvariant();
		/// <summary>Lets guarded parts be hit once what guards them is gone (the AI that would do it is paused).</summary>
		public virtual void UnlockParts() { }

		public static bool CanHit(NPC n) => n != null && n.active && n.life > 0 && !n.dontTakeDamage;

		/// <summary>The parts FIGHT can choose from: the core first, then by name.</summary>
		public List<NPC> TargetParts()
		{
			UnlockParts();
			NPC core = CorePart;
			return HealthPools().Where(m => m.life > 0).Distinct()
				.OrderBy(m => m == core ? 0 : 1).ThenBy(PartName).ToList();
		}

		/// <summary>Left or right of the core, for naming paired parts.</summary>
		protected string Side(NPC part) => part.Center.X < (CorePart ?? Npc).Center.X ? "LEFT" : "RIGHT";

		/// <summary>The NPC that FIGHT hits. Skips invulnerable parts while something else can be hurt.</summary>
		public virtual NPC StrikeTarget()
		{
			if (Npc.active && !Npc.dontTakeDamage)
				return Npc;
			NPC other = Members().FirstOrDefault(m => m.active && m.life > 0 && !m.dontTakeDamage);
			return other ?? Members().FirstOrDefault(m => m.active) ?? Npc;
		}

		/// <summary>The NPC drawn on the battle screen.</summary>
		public virtual NPC DrawNpc => Npc.active ? Npc : Members().FirstOrDefault(m => m.active);

		/// <summary>
		/// Draw with Terraria's own NPC renderer, all of <see cref="DrawParts"/> together: for bosses made of several
		/// NPCs or drawn by special code (wings, legs, arms), which a single sprite sheet would leave out.
		/// </summary>
		public virtual bool DrawWithTerraria => false;
		/// <summary>The NPCs drawn together when <see cref="DrawWithTerraria"/> is on.</summary>
		public virtual IEnumerable<NPC> DrawParts() => Members();
		/// <summary>
		/// Place the drawn NPC itself (its centre) on <see cref="DrawCenter"/> at the hero's scale, instead of fitting and
		/// centring the whole group: a worm's head on its spot with the body trailing off the screen's right edge.
		/// </summary>
		public virtual bool LeadWithDrawNpc => false;
		/// <summary>The scale <see cref="LeadWithDrawNpc"/> draws at (the hero's is 1.5).</summary>
		public virtual float LeadScale => BattleConstants.BattleCharacterScale;
		/// <summary>Each part floats on its own rhythm while idle, and the whole body breathes and bobs (off for worms: they hold still).</summary>
		public virtual bool SwayParts => true;
		/// <summary>Draw fully opaque even if the battle froze it mid fade-in (the Empress).</summary>
		public virtual bool ForceOpaque => false;
		/// <summary>
		/// The least world area (pixels) the whole drawn boss covers, for scaling. Hitboxes and frames can be much smaller
		/// than what Terraria draws (the Moon Lord's torso, the Empress's wings) or bunched up just after spawning.
		/// </summary>
		public virtual Vector2 CompositeSize => Vector2.Zero;
		/// <summary>The battle-screen area the drawn boss is fitted into.</summary>
		public virtual Vector2 CompositeArea => new(260f, 250f);

		/// <summary>
		/// Poses the parts for the battle screen (their AI is paused, so they'd stay however the world left them):
		/// positions, rotations, frames. Only for the draw; everything is put back right after.
		/// </summary>
		/// <param name="attacking">0..1, how hard the enemy is attacking this moment.</param>
		public virtual void PoseForBattle(List<NPC> parts, NPC anchor, int time, float attacking) { }

		/// <summary>
		/// Extras Terraria draws apart from the NPC's sprite sheet (King Slime's ninja and crown), drawn behind or over
		/// the battle sprite. <paramref name="pos"/> is the frame's centre; <paramref name="scale"/> is battle pixels per
		/// sprite pixel; <paramref name="color"/> is the light on it (not the sprite's own tint).
		/// </summary>
		/// <summary>A frame of the sheet to show instead of the NPC's own (its AI, which would change it, is paused); null: its own.</summary>
		public virtual int? FrameOverride(int time, int frameCount) => null;

		public virtual void DrawBehindSprite(Vector2 pos, int frame, float rotation, Vector2 scale, Color color) { }
		/// <inheritdoc cref="DrawBehindSprite"/>
		public virtual void DrawOverSprite(Vector2 pos, int frame, float rotation, Vector2 scale, Color color) { }

		/// <summary>Ends the fight peacefully: removes every other part, then drops the loot from one.</summary>
		public virtual void Spare()
		{
			NPC keep = DrawNpc ?? Npc;
			foreach (NPC m in Members().ToList())
			{
				if (m.whoAmI == keep.whoAmI || m.realLife == keep.whoAmI)
					continue;
				m.active = false;
				m.life = 0;
			}
			MercyGlobalNPC.Spare(keep);
		}

		// ---- MERCY ----

		private float mercy = -1f;

		/// <summary>Kept on the encounter (a worm segment can die mid-fight) and mirrored onto the NPC's bar.</summary>
		public float Mercy
		{
			get
			{
				// Bosses can't be spared: no MERCY at all
				if (IsBoss)
					return 0f;
				if (mercy < 0f)
					mercy = Npc.GetGlobalNPC<MercyGlobalNPC>().Mercy;
				return mercy;
			}
			set
			{
				if (IsBoss)
					return;
				float before = Mercy;
				mercy = MathHelper.Clamp(value, 0f, 100f);
				if (Npc.active)
					Npc.GetGlobalNPC<MercyGlobalNPC>().Mercy = mercy;
				// Multiplayer: the server adds it up for the whole party
				Net.BattleNet.SendAddMercy(Npc, mercy - before);
			}
		}

		/// <summary>MERCY as the server has it (multiplayer), without sending it back.</summary>
		public void SetMercyQuiet(float value) => mercy = MathHelper.Clamp(value, 0f, 100f);

		/// <summary>
		/// Bosses' ACTs give this share of their listed MERCY, so sparing a boss takes about as many turns as beating it
		/// (around 6-8 instead of 4).
		/// </summary>
		public const float BossMercyScale = 0.65f;

		/// <summary>Adds MERCY, halving it each time the same act is repeated. Returns what was actually gained.</summary>
		protected float GainMercy(string actName, float amount)
		{
			// Bosses can't be spared: their ACTs are flavour (no MERCY, and no "no effect" line either)
			if (IsBoss)
				return amount * BossMercyScale;
			ActUses.TryGetValue(actName, out int uses);
			ActUses[actName] = uses + 1;
			float before = Mercy;
			Mercy = before + amount / (1 << Math.Min(uses, 4));
			float gained = Mercy - before;
			BattleSystem.Instance.ShowMercyGain(gained);
			return gained;
		}

		/// <summary>The line Deltarune adds when an enemy becomes spareable.</summary>
		protected string SpareableLine => $"* {Name} doesn't want to fight anymore.";

		// ---- act builders ----

		protected ActOption CheckAct(string description) => new()
		{
			Name = "Check",
			Description = "Useless\nanalysis",
			Run = b => new List<string> { $"* {Name} - AT {Damage} DF {Npc.defense}\n{description}" },
		};

		/// <summary>An act that shows a line and raises MERCY.</summary>
		protected ActOption MercyAct(string name, string description, float mercy, string line) => new()
		{
			Name = name,
			Description = description,
			Run = b =>
			{
				float gained = GainMercy(name, mercy);
				var lines = new List<string> { line + (gained > 0 ? "" : "\n* It didn't seem to have any effect.") };
				if (Mercy >= 100f)
					lines.Add(SpareableLine);
				return lines;
			},
		};

		/// <summary>Mercy Mode's Heal Prayer, available in every battle.</summary>
		protected static ActOption HealPrayerAct() => new()
		{
			Name = "HealPrayer",
			Description = "Heal\nyourself",
			TPCost = MercyPlayer.HealPrayerCost,
			Run = b =>
			{
				// The heal sound plays with the green number (BattleSystem.PlayHealFx)
				int healed = b.HealPlayer(Math.Max(20, b.Player.statLifeMax2 / 4));
				return new List<string> { $"* {b.Player.name} cast HEAL PRAYER!\n* Recovered {healed} HP." };
			},
		};

		/// <summary>Picks the attack for this turn from a list, cycling through it.</summary>
		protected EnemyAttack Cycle(params Func<EnemyAttack>[] attacks) => attacks[Turn % attacks.Length]();

		// ---- drawing ----

		/// <summary>Where the enemy is drawn on the battle screen (its centre).</summary>
		public virtual Vector2 DrawCenter => new(500, 190);
		/// <summary>In a battle with several enemies: this one's spot and the size it must fit in.</summary>
		public Vector2? Slot;
		public Vector2? SlotArea;
		/// <summary>Where it's actually drawn: its slot in a group, otherwise <see cref="DrawCenter"/>.</summary>
		public Vector2 ScreenCenter => Slot ?? DrawCenter;

		/// <summary>The opening line when it leads a group of several enemies.</summary>
		public virtual string GroupEncounterText(int others) =>
			others == 1 ? $"* {Name} and a friend drew near!" : $"* {Name} and {others} others drew near!";
		/// <summary>Extra rotation for the sprite on the battle screen.</summary>
		public virtual float DrawRotation(int time) => 0f;

		/// <summary>
		/// Draws NPC art at the same scale as the battle-screen player (1.5x world size), so enemies keep their real
		/// size next to you; only very big ones are shrunk to fit. (Sizing by hitbox made most enemies too small:
		/// sprites are usually bigger than their hitboxes.)
		/// </summary>
		public virtual float DrawScale(Rectangle frame)
		{
			NPC npc = DrawNpc ?? Npc;
			float worldScale = (npc?.scale ?? 1f) * BattleConstants.BattleCharacterScale;
			float fitToBattleArea = Math.Min(
				200f / Math.Max(1, frame.Height),
				220f / Math.Max(1, frame.Width));
			return Math.Min(worldScale, fitToBattleArea);
		}

		/// <summary>Tint used for the sprite (slimes and other recoloured enemies use npc.color).</summary>
		public virtual Color DrawColor(NPC npc)
		{
			// Slimes and other recoloured NPCs have a grey texture tinted by npc.color (Terraria uses GetColor).
			// Use it opaque-ish: the sprite batch expects premultiplied colours.
			if (npc.color != default)
				return new Color(npc.color.R, npc.color.G, npc.color.B) * 0.9f;
			Color c = npc.GetAlpha(Color.White);
			return c.A == 0 ? Color.White : c;
		}

		/// <summary>True for regular enemies (shorter turns, MERCY rises faster).</summary>
		public virtual bool IsBoss => true;

		/// <summary>Deltarune's TIRED: its name shows blue, and PACIFY puts it to sleep (spared).</summary>
		public virtual bool Tired => false;

		/// <summary>The sound it makes as it starts talking (a zombie's moan), or null.</summary>
		public virtual Terraria.Audio.SoundStyle? Voice => Npc?.HitSound;

		/// <summary>What it says in its speech bubble as its turn starts (null: nothing).</summary>
		public virtual string Bubble(int turn) => IsBoss ? Encounters.BossBubbles.Get(Npc.type, turn) : null;

		/// <summary>
		/// A talker (Talk.cs) speaks in the text box at the bottom as its turn starts instead of a bubble, and may
		/// ask a question. Null: it doesn't.
		/// </summary>
		public virtual TalkTurn Talk(int turn) => Npc == null ? null : Talkers.For(Npc.type, turn);

		/// <summary>Its face beside the text box while it talks: its sprite's first frame, fitted into a square.</summary>
		public virtual void DrawPortrait(Vector2 center, float size, float time)
		{
			if (CustomSprite(out Texture2D custom, out Rectangle customFrame))
			{
				float cs = size / System.Math.Max(customFrame.Width, customFrame.Height);
				DrDraw.Sb.Draw(custom, center, customFrame, Color.White, 0f, customFrame.Size() / 2f, cs, SpriteEffects.None, 0f);
				return;
			}
			if (Npc == null)
				return;
			Main.instance.LoadNPC(Npc.type);
			Texture2D tex = Terraria.GameContent.TextureAssets.Npc[Npc.type].Value;
			int frames = System.Math.Max(1, Main.npcFrameCount[Npc.type]);
			// Gently animated: its frames, slowly
			int f = (int)(time / 10f) % frames;
			var frame = new Rectangle(0, f * (tex.Height / frames), tex.Width, tex.Height / frames);
			float s = System.Math.Min(2f, size / System.Math.Max(frame.Width, frame.Height));
			Color c = DrawColor(Npc);
			DrDraw.Sb.Draw(tex, center, frame, c, 0f, frame.Size() / 2f, s, SpriteEffects.None, 0f);
		}

		/// <summary>A sprite of its own instead of its NPC's (a boulder has no NPC). False: the NPC's.</summary>
		public virtual bool CustomSprite(out Texture2D texture, out Rectangle frame)
		{
			texture = null;
			frame = default;
			return false;
		}
	}

	/// <summary>Decides who gets a battle and which encounter they use.</summary>
	public static class EncounterRegistry
	{
		private static readonly HashSet<int> EaterTypes = new() { NPCID.EaterofWorldsHead, NPCID.EaterofWorldsBody, NPCID.EaterofWorldsTail };

		/// <summary>Bosses with their own acts and patterns.</summary>
		public static bool HasCustom(int type) => type switch
		{
			NPCID.EyeofCthulhu or NPCID.KingSlime or NPCID.BrainofCthulhu or NPCID.QueenBee or NPCID.SkeletronHead
				or NPCID.Deerclops or NPCID.WallofFlesh => true,
			// Hardmode
			NPCID.QueenSlimeBoss or NPCID.Retinazer or NPCID.Spazmatism or NPCID.TheDestroyer or NPCID.SkeletronPrime
				or NPCID.Plantera or NPCID.Golem or NPCID.DukeFishron or NPCID.HallowBoss or NPCID.CultistBoss
				or NPCID.MoonLordCore => true,
			_ => EaterTypes.Contains(type),
		};

		/// <summary>
		/// The NPC a battle should be about when this one is touched or hit: worm segments map to their head, and a
		/// boss's parts and minions (Creepers, Skeletron's hands, the Hungry, Servants, bees) map to the boss.
		/// </summary>
		public static NPC ResolveRoot(NPC npc)
		{
			NPC root = MercyMode.Root(npc);
			switch (root.type)
			{
				case NPCID.Creeper:
					return Find(NPCID.BrainofCthulhu) ?? root;
				case NPCID.SkeletronHand:
					return Find(NPCID.SkeletronHead) ?? root;
				case NPCID.WallofFleshEye:
				case NPCID.TheHungry:
				case NPCID.TheHungryII:
				case NPCID.LeechHead:
				case NPCID.LeechBody:
				case NPCID.LeechTail:
					return Main.wofNPCIndex >= 0 && Main.npc[Main.wofNPCIndex].active ? Main.npc[Main.wofNPCIndex] : root;
				case NPCID.ServantofCthulhu:
					return Find(NPCID.EyeofCthulhu) ?? root;
				case NPCID.Bee:
				case NPCID.BeeSmall:
					return Find(NPCID.QueenBee) ?? root;
				// Hardmode bosses' parts and minions
				case NPCID.QueenSlimeMinionBlue:
				case NPCID.QueenSlimeMinionPink:
				case NPCID.QueenSlimeMinionPurple:
					return Find(NPCID.QueenSlimeBoss) ?? root;
				case NPCID.Probe:
					return Find(NPCID.TheDestroyer) ?? root;
				case NPCID.PrimeCannon:
				case NPCID.PrimeSaw:
				case NPCID.PrimeVice:
				case NPCID.PrimeLaser:
					return Find(NPCID.SkeletronPrime) ?? root;
				case NPCID.PlanterasHook:
				case NPCID.PlanterasTentacle:
				case NPCID.Spore:
					return Find(NPCID.Plantera) ?? root;
				case NPCID.GolemHead:
				case NPCID.GolemHeadFree:
				case NPCID.GolemFistLeft:
				case NPCID.GolemFistRight:
					return Find(NPCID.Golem) ?? root;
				case NPCID.Sharkron:
				case NPCID.Sharkron2:
				case NPCID.DetonatingBubble:
					return Find(NPCID.DukeFishron) ?? root;
				case NPCID.CultistBossClone:
				case NPCID.AncientLight:
				case NPCID.AncientDoom:
				case NPCID.CultistDragonHead:
				case NPCID.CultistDragonBody1:
				case NPCID.CultistDragonBody2:
				case NPCID.CultistDragonBody3:
				case NPCID.CultistDragonBody4:
				case NPCID.CultistDragonTail:
					return Find(NPCID.CultistBoss) ?? root;
				case NPCID.MoonLordHead:
				case NPCID.MoonLordHand:
				case NPCID.MoonLordFreeEye:
				case NPCID.MoonLordLeechBlob:
					return Find(NPCID.MoonLordCore) ?? root;
			}
			return root;
		}

		private static NPC Find(int type)
		{
			foreach (NPC n in Main.ActiveNPCs)
				if (n.type == type)
					return n;
			return null;
		}

		public static bool IsBossFight(NPC root) => root.boss || HasCustom(root.type);

		/// <summary>An invasion, a Pumpkin or Frost Moon, an eclipse, the Old One's Army or the Lunar Events.</summary>
		public static bool EventActive =>
			Main.invasionType > 0 || Main.pumpkinMoon || Main.snowMoon || Main.eclipse
			|| Terraria.GameContent.Events.DD2Event.Ongoing || NPC.LunarApocalypseIsUp;

		/// <summary>Whether touching/hitting this (root) NPC starts a battle.</summary>
		public static bool Eligible(NPC root)
		{
			if (!root.active || root.life <= 0 || root.friendly || root.townNPC || NPCID.Sets.ActsLikeTownNPC[root.type])
				return false;
			// The Dungeon Guardian stays vanilla: the unstoppable skull that guards the Dungeon before Skeletron falls
			// (as a battle it was either instant death or a two-turn spare past the barrier)
			if (root.type == NPCID.DungeonGuardian)
				return false;
			// The Moon Lord rises for a second before its head and hands exist (core ai[0] = -1)
			if (root.type == NPCID.MoonLordCore && root.ai[0] < 0f)
				return false;
			if (IsBossFight(root))
				return true;

			var config = ModContent.GetInstance<MercyConfig>();
			if (config != null && !config.BattlesWithEnemies)
				return false;
			if (Armies.NeverBattle(root.type))
				return false;
			// During an event, battles take on a squad of that army at a time (or none, if turned off)
			if (EventActive && config?.EventBattles == false)
				return false;
			// A boss's fight already has its minions; leave the rest of the world alone during it
			if (MercyMode.AnyBossAlive())
				return false;
			return root.damage > 0 && root.lifeMax > 5 && !root.dontTakeDamage && !root.immortal
				&& !NPCID.Sets.CountsAsCritter[root.type] && !NPCID.Sets.ProjectileNPC[root.type];
		}

		public static Encounter Create(NPC root)
		{
			Encounter e = root.type switch
			{
				NPCID.EyeofCthulhu => new EyeOfCthulhu(),
				NPCID.KingSlime => new KingSlime(),
				NPCID.BrainofCthulhu => new BrainOfCthulhu(),
				NPCID.QueenBee => new QueenBee(),
				NPCID.SkeletronHead => new Skeletron(),
				NPCID.Deerclops => new Deerclops(),
				NPCID.WallofFlesh => new WallOfFlesh(),
				NPCID.QueenSlimeBoss => new QueenSlime(),
				NPCID.Retinazer or NPCID.Spazmatism => new Twins(),
				NPCID.TheDestroyer => new Destroyer(),
				NPCID.SkeletronPrime => new SkeletronPrime(),
				NPCID.Plantera => new Plantera(),
				NPCID.Golem => new Golem(),
				NPCID.DukeFishron => new DukeFishron(),
				NPCID.HallowBoss => new EmpressOfLight(),
				NPCID.CultistBoss => new LunaticCultist(),
				NPCID.MoonLordCore => new MoonLord(),
				_ when EaterTypes.Contains(root.type) => new EaterOfWorlds(),
				_ when root.boss => new GenericBoss(),
				_ => EnemyFamilies.For(root),
			};
			e.Npc = root;
			return e;
		}
	}
}
