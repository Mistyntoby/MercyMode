using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using MercyMode.Deltarune;
using static MercyMode.Battle.BattleConstants;

namespace MercyMode.Battle
{
	/// <summary>
	/// The Deltarune-style battle screen. Starts when the player touches or hits an enemy that has an
	/// <see cref="Battle.Encounter"/> (see <see cref="EncounterRegistry"/>), freezes the world, and runs player
	/// turns and enemy turns until the enemy is spared or defeated (or the player dies).
	/// </summary>
	public partial class BattleSystem : ModSystem
	{
		public enum Phase { None, Intro, Menu, WeaponSelect, EnemySelect, ActSelect, ItemSelect, FightBar, FightResult, Message, EnemyIntro, EnemyTurn, EnemyOutro, Outro, Death, Waiting, PartySelect, SummonSelect, DuelWait, Build, MercyWait, MercyPrompt, EnemyTalk, Talk, Choice, ChoiceWait }
		private enum Choice { Fight, Act, Item, Spare, Defend }

		public static BattleSystem Instance => ModContent.GetInstance<BattleSystem>();
		public static bool Active => Instance != null && Instance.phase != Phase.None;
		/// <summary>True while the battle itself is hurting the player, so other damage stays blocked.</summary>
		public static bool HurtingPlayer;
		public static bool Defending => Active && Instance.defending;
		/// <summary>
		/// True while Terraria's music should be silent: Rude Buster is playing, or the SOUL is breaking (Deltarune cuts
		/// the music then, boss tracks included).
		/// </summary>
		public static bool SilenceTerrariaMusic => Active && (Instance.music != null || Instance.phase == Phase.Death);

		// ---- state ----
		private Phase phase = Phase.None;
		private int phaseTicks;
		private int time;
		private Encounter encounter;
		private NPC boss;
		public NPC Boss => boss;
		public Player Player => Main.LocalPlayer;
		public Encounter Encounter => encounter;
		/// <summary>No new battle right after one ends, so you can walk away from a crowd.</summary>
		private uint lastEndTick;
		private const int GraceTicks = 150;

		private Choice menuChoice;
		private Choice pendingChoice;
		private int listIndex;
		private bool defending;
		private bool battleOver;

		// text box
		private string text = "";
		private float textShown;
		private readonly Queue<string> messages = new();
		private Action afterMessages;

		// HUD animation (Deltarune frames, advanced every other tick)
		private float panel; // bp: 0 hidden .. PanelHeight shown
		private int panelDir; // 1 sliding in, -1 sliding out
		private float selectedBarPhase;
		private float tpApparent, tpCurrent; // in tension units (0-250), like obj_tensionbar
		private float tpPreview; // tensionselect: cost of the highlighted act
		private float screenFade; // world dim 0..1

		// enemy turn
		public readonly List<Bullet> Bullets = new();
		private readonly List<Bullet> spawnedDuringUpdate = new();
		private bool updatingBullets;
		private EnemyAttack attack;
		private float turnTimer;
		private Vector2 soul; // top-left of spr_dodgeheart, like obj_heart's x/y
		private Vector2 soulFrom;
		private int inv = -1; // global.inv in ticks
		private bool disableSlow;
		private int grazeTimer;
		private float boxTimer; // 0..BoxGrowTicks
		private struct BoxAfterimage
		{
			public float Scale, Rotation, Alpha;
			public float Age; // frames
		}
		private readonly List<BoxAfterimage> boxAfterimages = new();

		// fight
		private float boltX; // frames since the bar appeared (boltx)
		private float fightFade;
		private int slashTimer = -1;
		/// <summary>The enemy the current slash struck.</summary>
		private BattleEnemy slashEnemy;
		private float enemyAttackEnergy;
		private Vector2 enemyAttackDirection;
		private int patternSoundCooldown;
		private int textSoundedThrough;

		// music
		private SoundEffectInstance music;
		private bool musicStarted;

		// world snapshot so everything resumes where it was
		private readonly Dictionary<int, (int type, Vector2 velocity)> npcVelocities = new();
		private readonly Dictionary<int, (int type, Vector2 velocity)> projVelocities = new();
		private Vector2 playerPosition;
		/// <summary>Only the battle changes HP: no natural regen, potions' regen or debuffs while it's open.</summary>
		private int battleLife;
		private int lastHeal = -1;
		private float soulAlpha = 1f;
		private float partyLift;
		private float musicVolumeCurrent;
		private RightFacingHead playerHeadPortrait;
		/// <summary>
		/// global.faceaction: the nameplate shows an icon for the chosen command instead of the head (a frame of
		/// spr_headkris) from the moment it's chosen until the next player turn.
		/// </summary>
		private int faceAction;
		private const int FaceNone = 0, FaceFight = 1, FaceItem = 3, FaceDefend = 4, FaceAct = 6, FaceSpare = 10;

		/// <summary>Sets the HP the battle holds the player at (test command).</summary>
		public void SetBattleLife(int life) => battleLife = life;

		/// <summary>Heals the player during the battle. Returns how much HP was actually restored.</summary>
		public int HealPlayer(int amount)
		{
			int before = Player.statLife;
			Player.Heal(amount);
			battleLife = Player.statLife;
			lastHeal = Player.statLife - before;
			return lastHeal;
		}

		// ================================================================== lifecycle

		public static bool CanStart(NPC npc, Player player, bool ignoreGrace = false)
		{
			if (Active || !(MercyMode.IsSingleplayer || Net.BattleNet.Online) || player.whoAmI != Main.myPlayer || player.dead)
				return false;
			var config = ModContent.GetInstance<MercyConfig>();
			if (config != null && !config.TurnBasedBattles)
				return false;
			if (!ignoreGrace && Main.GameUpdateCount - Instance.lastEndTick < GraceTicks && Instance.lastEndTick != 0)
				return false;
			// Just came into the world (back after leaving mid-battle, say): a moment before anything starts a battle
			if (!ignoreGrace && Instance.enterGrace > 0)
				return false;
			return EncounterRegistry.Eligible(EncounterRegistry.ResolveRoot(npc));
		}

		public static void TryStart(NPC npc, Player player, string reason = "", bool queued = false)
		{
			bool command = reason == "command" || queued;
			if (!CanStart(npc, player, command))
				return;
			if (IsFirstStrikeReason(reason))
				firstStrikeAskedAt = Main.GameUpdateCount;
			NPC root = EncounterRegistry.ResolveRoot(npc);
			// A hit on an ordinary enemy that the opening strike would finish anyway: it just dies, no battle screen
			if (IsFirstStrikeReason(reason) && Instance.FinishWithoutBattle(root, player))
				return;
			// Multiplayer: the server sets the battle up (and pulls nearby players in), then tells us to start
			if (Net.BattleNet.Online)
			{
				Net.BattleNet.RequestBattle(root, Instance.GatherEnemies(root));
				return;
			}
			Instance.Start(root, reason);
		}

		/// <summary>Multiplayer: the server put this player in a battle with these enemies. False if it can't start.</summary>
		internal bool StartNet(List<(NPC Npc, float Mercy)> roots, bool spectate)
		{
			if (Player.dead || ModContent.GetInstance<MercyConfig>()?.TurnBasedBattles == false)
				return false;
			Start(roots[0].Npc, spectate ? "joined" : "multiplayer", roots.Select(r => r.Npc).ToList());
			spectating = spectate;
			foreach (BattleEnemy e in enemies)
			{
				var r = roots.FirstOrDefault(x => x.Npc == e.E.Npc);
				if (r.Npc != null)
					e.E.SetMercyQuiet(r.Mercy);
			}
			return true;
		}

		/// <summary>Player turns left before a potion can be used again (Terraria's potion sickness, in turns).</summary>
		private int potionSickTurns;

		/// <summary>How many turns the current potion sickness started with (for its bar).</summary>
		private int potionSickFull;

		/// <summary>Ticks a turn of potion sickness stands for, outside the battle.</summary>
		private const int SickTurnTicks = 600;

		/// <summary>
		/// Shows the battle's potion sickness as Terraria's own debuff (so it's in the effects list with its icon and
		/// description), kept at the turns left in time; whatever is left when the battle ends carries on outside.
		/// </summary>
		private void SyncPotionSickness()
		{
			if (potionSickTurns > 0)
			{
				int ticks = potionSickTurns * SickTurnTicks;
				int i = Player.FindBuffIndex(BuffID.PotionSickness);
				if (i < 0)
					Player.AddBuff(BuffID.PotionSickness, ticks, quiet: true);
				else
					Player.buffTime[i] = ticks;
				Player.potionDelay = ticks;
			}
			else if (Player.HasBuff(BuffID.PotionSickness))
			{
				Player.ClearBuff(BuffID.PotionSickness);
				Player.potionDelay = 0;
			}
		}

		/// <summary>Whether potion sickness keeps this item from being used right now.</summary>
		private bool SickLocked(int type) =>
			potionSickTurns > 0 && Terraria.ID.ContentSamples.ItemsByType.TryGetValue(type, out Item sample) && sample.potion;

		private void Start(NPC root, string reason, List<NPC> given = null)
		{
			// Sickness from before the battle doesn't carry in: the battle counts its own, in turns
			potionSickTurns = 0;
			Player.ClearBuff(BuffID.PotionSickness);
			Player.potionDelay = 0;
			boss = root;
			// The enemy, plus nearby ones (a squad, during an event) for regular fights; the first is the target
			SetUpEnemies(root, given);
			ResetNet();
			time = 0;
			summonLungeTime = -1000;
			battleOver = false;
			defending = false;
			menuChoice = Choice.Fight;
			Bullets.Clear();
			boxAfterimages.Clear();
			messages.Clear();
			effects.Clear();
			SetHeroPose(HeroPose.Idle);
			hurtTimer = -1;
			shake = 0;
			pendingHealFx = -1;
			usedItemType = 0;
			faceAction = FaceNone;
			fightWeaponSlot = -1;
			fightWeapon = null;
			heroRecoil = 0f;
			partScreen.Clear();
			slashPart = -1;
			// The party and the enemy fly in from where they stood in the world
			CaptureWorldPositions();
			tpBarX = -40f;
			tpBarIn = -1f;
			panel = 0;
			selectedBarPhase = 0f;
			partyLift = 0f;
			panelDir = 0;
			screenFade = 0;
			arenaBlend = 0f;
			slashTimer = -1;
			enemyAttackEnergy = 0f;
			enemyAttackDirection = Vector2.Zero;
			patternSoundCooldown = 0;
			musicVolumeCurrent = 0f;
			musicStarted = false;
			deathPending = false;
			deathReason = null;
			soulShards.Clear();
			var mp = Player.GetModPlayer<MercyPlayer>();
			tpApparent = tpCurrent = mp.TP / TensionToTP;

			// The swing or shot that started the battle stops here: no frozen mid-swing pose, no hits in the background
			Player.itemAnimation = 0;
			Player.itemTime = 0;
			Player.channel = false;
			foreach (Projectile p in Main.ActiveProjectiles)
				if (p.owner == Player.whoAmI && p.friendly && !p.npcProj && !p.minion && !p.sentry)
					p.Kill();

			npcVelocities.Clear();
			projVelocities.Clear();
			// In multiplayer the server holds (and later releases) the battle's enemies; nothing else stops
			if (!Net.BattleNet.Online)
			{
				foreach (NPC n in Main.ActiveNPCs)
					npcVelocities[n.whoAmI] = (n.type, n.velocity);
				foreach (Projectile p in Main.ActiveProjectiles)
					projVelocities[p.whoAmI] = (p.type, p.velocity);
			}
			playerPosition = Player.position;
			// Multiplayer: from outside, the party lines up on the enemy's left, facing it (only where there's room)
			if (Net.BattleNet.Online && duelWith < 0)
				LineUpForOutsiders(root);
			battleLife = Player.statLife;
			CreatePlayerHeadPortrait();

			// Bosses keep their own Terraria music (BattleMusicScene steps aside); other battles get Rude Buster
			bool bossMusic = encounter.IsBoss && (ModContent.GetInstance<MercyConfig>()?.BossBattleMusic ?? true);
			if (DeltaruneAssets.BattleMusic != null && !bossMusic)
			{
				music = DeltaruneAssets.BattleMusic.CreateInstance();
				music.IsLooped = true;
				music.Volume = 0f;
			}

			// No owls or wind over the battle; Terraria's ambience comes back when it ends
			AmbienceMute.Mute();
			// The boss's own track, turned up to sit level with the battle's sounds
			if (bossMusic)
				AmbienceMute.BoostMusic(ModContent.GetInstance<MercyConfig>()?.BossMusicBoost ?? 1.6f);

			firstStrike = OpensWithStrike(reason);
			// A -test run is timed and its turns counted
			testRun = Player.GetModPlayer<TestLoadoutPlayer>().Testing;
			testTurns = 0;
			testClock.Restart();
			firstStrikeAskedAt = 0;
			SetText(OpeningText());
			SetPhase(Phase.Intro);
			Mod.Logger.Info($"Battle started with {boss.FullName} as {encounter.GetType().Name} ({encounter.Life}/{encounter.LifeMax} HP) by {reason}");
		}

		/// <summary>Moves the player to the enemy's left side, at their own height, if that spot is clear.</summary>
		private void LineUpForOutsiders(NPC root)
		{
			// The party list may not have arrived yet: fall back to the player slot so two players don't share a spot
			int slot = Net.BattleNet.Party.IndexOf(Player.whoAmI) is int i && i >= 0 ? i : Player.whoAmI % 3;
			// On whichever side of the enemy they already are (not always the left: there may be a wall there)
			int side = Player.Center.X <= root.Center.X ? -1 : 1;
			float x = side < 0 ? root.Left.X - 40f - slot * 28f - Player.width : root.Right.X + 40f + slot * 28f;
			// Terrain: the nearest height within a few tiles where they stand on solid ground with room to stand
			Vector2? best = null;
			for (int dy = 0; dy <= 6 * 16; dy += 8)
			{
				foreach (int sign in new[] { 1, -1 })
				{
					var spot = new Vector2(x, Player.position.Y + dy * sign);
					if (Collision.SolidCollision(spot, Player.width, Player.height))
						continue;
					if (!Collision.SolidCollision(spot + new Vector2(0f, Player.height), Player.width, 6))
						continue;
					best = spot;
					break;
				}
				if (best != null)
					break;
			}
			// Only a short step, and only with a clear line there (never through a wall)
			if (best is Vector2 to && Math.Abs(to.X - Player.position.X) < 8f * 16f
				&& Collision.CanHit(Player.position, Player.width, Player.height, to, Player.width, Player.height))
			{
				Player.position = to;
				playerPosition = to;
			}
			Player.direction = root.Center.X >= Player.Center.X ? 1 : -1;
		}

		private void End(bool killPlayer = false)
		{
			Mod.Logger.Info($"Battle ended (enemy alive: {encounter?.Alive}, player dead: {Player.dead}, killed by the battle: {killPlayer})");
			ReportTestRun(killPlayer);
			EndBoulder(killPlayer);
			cutscene = null;
			lastEndTick = (uint)Main.GameUpdateCount;
			// Lost (the SOUL broke): the enemies leave instead of carrying on, like a boss leaving when its target dies;
			// nothing is killed, so no loot, no kill credit, no achievement. In multiplayer the server does this once the
			// whole party is gone (BattleNet.ServerLeave)
			if (killPlayer && !Net.BattleNet.Online)
				DespawnEnemies();
			// Put everything back in motion where it was
			foreach (var (i, (type, vel)) in npcVelocities)
			{
				NPC n = Main.npc[i];
				if (n.active && n.type == type)
					n.velocity = vel;
			}
			foreach (var (i, (type, vel)) in projVelocities)
			{
				Projectile p = Main.projectile[i];
				if (p.active && p.type == type)
					p.velocity = vel;
			}
			npcVelocities.Clear();
			projVelocities.Clear();
			Net.BattleNet.LeaveBattle();
			EndDuel();

			music?.Stop();
			music?.Dispose();
			music = null;
			StopChargeLoop();
			AmbienceMute.Restore();
			ReleasePlayerHeadPortrait();
			Bullets.Clear();
			phase = Phase.None;
			encounter = null;
			boss = null;
			enemies.Clear();
			SetTarget(null);

			// A -test kit only lasts the battle
			if (!killPlayer)
				Player.GetModPlayer<TestLoadoutPlayer>().Restore();

			if (killPlayer)
			{
				// The SOUL has shattered: now the player really dies, in the world, with Terraria's own death
				PlayerDeathReason reason = deathReason ?? PlayerDeathReason.ByCustomReason(NetworkText.FromLiteral($"{Player.name} was defeated."));
				deathReason = null;
				Player.statLife = 0;
				Player.KillMe(reason, Math.Max(1.0, deathDamage), 0);
				// After the death, so a mediumcore character drops the test kit, never their own gear
				Player.GetModPlayer<TestLoadoutPlayer>().Restore();
				return;
			}

			// A moment of mercy so the boss can't hit you the instant the world unfreezes
			if (!Player.dead)
			{
				Player.immune = true;
				Player.immuneTime = Math.Max(Player.immuneTime, 60);
			}
		}

		// ---- -test runs: turns and time ----

		private bool testRun;
		private int testTurns;
		/// <summary>The last -test run's report.</summary>
		internal static string LastTestReport;
		private readonly System.Diagnostics.Stopwatch testClock = new();

		private static string Clock(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

		/// <summary>The turn and the clock in the corner, during a -test run.</summary>
		private void DrawTestRun()
		{
			if (!testRun || phase == Phase.None)
				return;
			string text = $"TEST  TURN {Math.Max(1, testTurns)}  {Clock(testClock.Elapsed)}";
			float w = DrDraw.Measure(text, DrDraw.SmallFont) * 0.8f;
			DrDraw.Rect(ScreenWidth - w - 14, 4, w + 10, 18, Color.Black * 0.6f);
			DrDraw.Text(text, ScreenWidth - w - 9, 5, MercyMode.MercyYellow, DrDraw.SmallFont, 0.8f);
		}

		/// <summary>How a -test run went, in chat (before the player's own gear comes back).</summary>
		private void ReportTestRun(bool lost)
		{
			if (!testRun)
				return;
			testRun = false;
			testClock.Stop();
			string name = encounter?.Name ?? boss?.FullName ?? "the boss";
			string result = lost ? "LOST" : encounter != null && !encounter.Alive ? "WON" : "ENDED";
			string hp = lost ? "" : $", {Math.Max(0, Player.statLife)}/{Player.statLifeMax2} HP left";
			string text = $"* Test run vs {name}: {result} after {testTurns} turn{(testTurns == 1 ? "" : "s")} in {Clock(testClock.Elapsed)}{hp}.";
			Mod.Logger.Info(text);
			LastTestReport = text;
			if (!Main.dedServ)
				Main.NewText(text, MercyMode.MercyYellow);
		}

		/// <summary>Removes the battle's enemies from the world, every part of them, without killing them.</summary>
		private void DespawnEnemies()
		{
			foreach (BattleEnemy en in enemies)
				foreach (NPC m in en.E.Members().Append(en.E.Npc).Where(m => m.active).Distinct().ToList())
				{
					m.active = false;
					m.netUpdate = true;
				}
		}

		private void CreatePlayerHeadPortrait()
		{
			playerHeadPortrait ??= new RightFacingHead();
			playerHeadPortrait.Use(Player);
			playerHeadPortrait.UseColor(KrisCyan);
			if (!Main.ContentThatNeedsRenderTargets.Contains(playerHeadPortrait))
				Main.ContentThatNeedsRenderTargets.Add(playerHeadPortrait);
			playerHeadPortrait.Request();
		}

		private void ReleasePlayerHeadPortrait()
		{
			ReleaseAllyHeads();
			if (playerHeadPortrait == null)
				return;

			Main.ContentThatNeedsRenderTargets.Remove(playerHeadPortrait);
			playerHeadPortrait.GetTarget()?.Dispose();
			playerHeadPortrait.Reset();
			playerHeadPortrait = null;
		}

		public override void Load()
		{
			// Leaving a multiplayer game mid-battle (or being disconnected) doesn't always unload the world right away,
			// and the battle music kept playing over the menu: stop everything as soon as we're back in the menu
			Main.OnTickForThirdPartySoftwareOnly += StopInMenu;
		}

		public override void Unload()
		{
			Main.OnTickForThirdPartySoftwareOnly -= StopInMenu;
		}

		private static void StopInMenu()
		{
			if (Main.gameMenu && !Main.dedServ && (Instance?.phase != Phase.None || Instance?.music != null))
				Instance.OnWorldUnload();
		}

		public override void PreSaveAndQuit()
		{
			// Quitting mid-test: save the player's own gear, not the test kit
			if (!Main.dedServ)
				Main.LocalPlayer.GetModPlayer<TestLoadoutPlayer>().Restore();
			OnWorldUnload();
		}

		public override void OnWorldUnload()
		{
			ReleasePlayerHeadPortrait();
			// The music and the muted ambience go back, battle or not
			music?.Stop();
			music?.Dispose();
			music = null;
			AmbienceMute.Restore();
			if (phase != Phase.None)
			{
				StopChargeLoop();
				phase = Phase.None;
				encounter = null;
				boss = null;
				enemies.Clear();
				SetTarget(null);
				Bullets.Clear();
			}
			// Out of the world mid-duel: nothing of it carries into the next battle (the server ends it on disconnect)
			duelWith = -1;
			duelEnc = null;
			duelLive = null;
			DuelProxy.active = false;
			Net.BattleNet.ChallengeWith = -1;
		}

		private void SetPhase(Phase p)
		{
			// The yellow SOUL's charge hum only lasts as long as the turn
			if (p != Phase.EnemyTurn)
			{
				StopChargeLoop();
				zHold = 0;
			}
			phase = p;
			phaseTicks = 0;
			Mod.Logger.Debug($"Battle phase {p} (turn {encounter?.Turn}, boss {encounter?.Life}/{encounter?.LifeMax}, mercy {encounter?.Mercy:0}, TP {Player.GetModPlayer<MercyPlayer>().TP:0.0}, HP {Player.statLife})");
		}

		private void SetText(string s)
		{
			messageTicks = 0;
			// Multiplayer: the others watch this player's turn in their own text box
			if (executing)
				Net.BattleNet.SendPartyText(s);
			rawText = s ?? "";
			textWrap = TextWrapWidth;
			text = DrDraw.Wrap(rawText, textWrap);
			textShown = 0;
			textSoundedThrough = 0;
		}

		/// <summary>Shows text boxes one after another (Z to continue), then runs <paramref name="then"/>.</summary>
		private void ShowMessages(IEnumerable<string> lines, Action then)
		{
			messages.Clear();
			foreach (string l in lines)
				messages.Enqueue(Narration.ThirdPerson(l, Player.name));
			afterMessages = then;
			if (messages.Count == 0)
			{
				then();
				return;
			}
			string first = messages.Dequeue();
			DuelShareText(first);
			SetText(first);
			SetPhase(Phase.Message);
		}

		// ================================================================== update

		private int queuedNpc = -1;
		private const int QueueRetryTicks = 300;
		private int queuedTicks;

		/// <summary>Starts a battle with this NPC after a few ticks (test command).</summary>
		public static void QueueStart(NPC npc, int ticks, string reason = "command")
		{
			Instance.queuedNpc = npc.whoAmI;
			Instance.queuedTicks = ticks;
			Instance.queuedReason = reason;
		}

		/// <summary>Why the queued battle starts ("command", or "hit by ..." for a first strike).</summary>
		private string queuedReason = "command";

		/// <summary>Ticks after entering a world during which touching an enemy doesn't start a battle.</summary>
		private int enterGrace;
		private const int EnterGraceTicks = 5 * 60;

		/// <summary>Entering a world: everything about battles starts fresh, with a moment's grace.</summary>
		public void OnEnterWorld()
		{
			if (phase != Phase.None)
				End();
			enterGrace = EnterGraceTicks;
			queuedNpc = -1;
		}

		public override void PostUpdateEverything()
		{
			if (enterGrace > 0)
				enterGrace--;
			if (queuedNpc >= 0 && --queuedTicks <= 0)
			{
				NPC q = Main.npc[queuedNpc];
				// Some bosses aren't ready right away (the Moon Lord spends a second rising before its head and
				// hands exist): keep trying for a few seconds
				if (q.active && CanStart(EncounterRegistry.ResolveRoot(q), Player, ignoreGrace: true))
				{
					queuedNpc = -1;
					TryStart(q, Player, queuedReason, queued: true);
				}
				else if (!q.active || queuedTicks < -QueueRetryTicks)
				{
					queuedNpc = -1;
				}
			}
			if (phase == Phase.None)
				return;
			playerHeadPortrait?.Request();
			SyncPotionSickness();

			if (boss == null || Player.dead)
			{
				End();
				return;
			}
			// Enemies gone without us ending the battle (despawned, killed some other way)
			foreach (BattleEnemy en in enemies)
			{
				// (Our own killing blow marks it in ResolveHit; in a party someone else's lands any time, even mid-bar)
				if (!en.Out && !en.E.Alive && duelWith < 0 && (Net.BattleNet.Online || !(en == targetEnemy && phase is Phase.FightBar or Phase.FightResult)))
				{
					en.Out = true;
					// Multiplayer: another party member finished it off
					if (Net.BattleNet.Online && en.Override == null)
						WithEnemy(en, PlayEnemyDeath);
				}
			}
			if (LivingEnemies.Count == 0 && phase != Phase.Outro && phase != Phase.Message && phase != Phase.FightBar && phase != Phase.FightResult && phase != Phase.Death)
			{
				battleOver = true;
				// Multiplayer: someone else won it; this player gets the same YOU WON! before the battle closes
				if (Net.BattleNet.InParty)
				{
					SetHeroPose(HeroPose.Victory);
					string how = enemies.All(e => e.Override is SpareAnimation) ? "* Everyone was spared." :
						enemies.Count == 1 ? $"* {enemies[0].E.Name} was defeated." : "* Every enemy was defeated.";
					ShowMessages(new[] { "* YOU WON!\n" + how }, StartOutro);
				}
				else
					StartOutro();
			}

			time++;
			phaseTicks++;
			// A lethal hit (or drowning, poison...) held back by BattlePlayer.PreKill: break the SOUL first
			if (deathPending)
				BeginSoulDeath();
			// Full volume from the first beat; only the end of the battle fades it (and the volume setting still applies live)
			if (music != null && musicStarted && phase != Phase.Outro && phase != Phase.Death)
			{
				musicVolumeCurrent = DeltaruneAssets.BattleMusicVolume;
				music.Volume = musicVolumeCurrent;
			}

			// Hold the player in place, no falling or fall damage
			Player.position = playerPosition;
			// Multiplayer: facing the enemy, as the others see us
			if (Net.BattleNet.Online && boss != null)
				Player.direction = boss.Center.X >= Player.Center.X ? 1 : -1;
			Player.velocity = Vector2.Zero;
			Player.fallStart = (int)(Player.position.Y / 16f);
			// Downed (multiplayer): the battle's HP is below zero, the character stays alive at 1
			Player.statLife = downed ? 1 : Math.Min(battleLife, Player.statLifeMax2);
			CheckDowned();
			Player.lifeRegenCount = 0;

			// Every tick (60 fps), stepped in half Deltarune frames
			UpdateHudFrame();
			UpdateHeroFrame();
			UpdateArena();
			if (textShown < text.Length)
			{
				int visibleBefore = (int)textShown;
				textShown = Math.Min(text.Length, textShown + TextCharsPerTick);
				int visibleAfter = (int)textShown;
				if (visibleAfter > visibleBefore && phase is Phase.Menu or Phase.Message)
					PlayTextSoundThrough(visibleAfter);
			}
			if (grazeTimer > 0)
				grazeTimer--;
			foreach (BattleEnemy en in enemies)
				if (en.Shake > 0)
					en.Shake--;
			if (patternSoundCooldown > 0)
				patternSoundCooldown--;
			if (slashTimer >= 0 && ++slashTimer > 20)
				slashTimer = -1;

			switch (phase)
			{
				case Phase.Intro: UpdateIntro(); break;
				case Phase.Menu: UpdateMenu(); break;
				case Phase.WeaponSelect: UpdateWeaponSelect(); break;
				case Phase.EnemySelect: UpdateEnemySelect(); break;
				case Phase.ActSelect: UpdateActSelect(); break;
				case Phase.ItemSelect: UpdateItemSelect(); break;
				case Phase.PartySelect: UpdatePartySelect(); break;
				case Phase.SummonSelect: UpdateSummonSelect(); break;
				case Phase.FightBar: UpdateFightBar(); break;
				case Phase.FightResult: UpdateFightResult(); break;
				case Phase.Message: UpdateMessage(); break;
				case Phase.EnemyIntro: UpdateEnemyIntro(); break;
				case Phase.EnemyTurn: UpdateEnemyTurn(); break;
				case Phase.EnemyOutro: UpdateEnemyOutro(); break;
				case Phase.Outro: UpdateOutro(); break;
				case Phase.Death: UpdateSoulDeath(); break;
				case Phase.Waiting: UpdateWaiting(); break;
				case Phase.Build: UpdateBuild(); break;
				case Phase.EnemyTalk: UpdateEnemyTalk(); break;
				case Phase.MercyWait: UpdateMercyWait(); break;
				case Phase.MercyPrompt: UpdateMercyPrompt(); break;
				case Phase.Talk: UpdateTalk(); break;
				case Phase.Choice: UpdateChoice(); break;
				case Phase.ChoiceWait: messageTicks++; break;
			}
			UpdateCutscene();
			foreach (BattleEnemy en in enemies)
				en.E.AttackEnergy = phase == Phase.EnemyTurn ? enemyAttackEnergy : 0f;
			SendSoul();
			UpdateDuel();
			UpdateBuffDrawer();
		}

		/// <summary>The parts of obj_battlecontroller / obj_tensionbar that count in Deltarune frames.</summary>
		private float tpBarX = -40f;
		/// <summary>Frames since the TP bar started sliding in, or -1 before it does.</summary>
		private float tpBarIn = -1f;

		/// <summary>Every tick: Deltarune's per-frame HUD motion, stepped in half frames so it moves at 60 fps.</summary>
		private void UpdateHudFrame()
		{
			const float dt = FrameStep;
			// obj_tensionbar: x = -40, hspeed 13, friction 1 -> stops at 38 after 12 frames. After n frames that's
			// -40 + 13n - n(n+1)/2, which also gives the in-between positions. Slides back out at the end.
			if (panelDir < 0)
			{
				tpBarX = Math.Max(-40f, tpBarX - 13f * dt);
			}
			else if (tpBarIn >= 0f)
			{
				tpBarIn = Math.Min(12f, tpBarIn + dt);
				tpBarX = -40f + 13f * tpBarIn - tpBarIn * (tpBarIn + 1f) / 2f;
			}
			// Damp the panel toward its target instead of stepping 30 pixels per frame.
			float panelTarget = panelDir > 0 ? PanelHeight : panelDir < 0 ? 0f : panel;
			float panelEase = panelDir < 0 ? 0.68f : 0.5f;
			panel = MathHelper.Lerp(panel, panelTarget, EasePerTick(panelEase));
			if (Math.Abs(panelTarget - panel) < 0.75f)
				panel = panelTarget;
			panel = MathHelper.Clamp(panel, 0f, PanelHeight);
			float liftTarget = phase is Phase.Menu or Phase.WeaponSelect or Phase.EnemySelect or Phase.ActSelect or Phase.ItemSelect or Phase.PartySelect or Phase.SummonSelect ? 32f : 0f;
			partyLift = MathHelper.Lerp(partyLift, liftTarget, EasePerTick(liftTarget == 0f ? 0.68f : 0.5f));
			if (Math.Abs(liftTarget - partyLift) < 0.5f)
				partyLift = liftTarget;
			selectedBarPhase += 2f * dt;

			// TP bar: apparent moves 20 a frame, current catches up after a short delay
			float tension = Player.GetModPlayer<MercyPlayer>().TP / TensionToTP;
			if (Math.Abs(tpApparent - tension) < 20 * dt)
				tpApparent = tension;
			else
				tpApparent += (tpApparent < tension ? 20 : -20) * dt;
			float d = tpApparent - tpCurrent;
			if (d != 0)
			{
				float step = 2 + (Math.Abs(d) > 10 ? 2 : 0) + (Math.Abs(d) > 25 ? 3 : 0) + (Math.Abs(d) > 50 ? 4 : 0) + (Math.Abs(d) > 100 ? 5 : 0);
				tpCurrent += Math.Sign(d) * step * dt;
				if (Math.Abs(tpApparent - tpCurrent) < 3 * dt)
					tpCurrent = tpApparent;
			}

			// Step toward the target without overshooting (stepping past it made the veil and enemy flicker 0.9/1.0)
			float fadeTarget = phase == Phase.Outro ? 0f : 1f;
			if (screenFade < fadeTarget)
				screenFade = Math.Min(fadeTarget, screenFade + 0.1f * dt);
			else if (screenFade > fadeTarget)
				screenFade = Math.Max(fadeTarget, screenFade - 0.1f * dt);
		}

		private void PlayTextSoundThrough(int visibleCharacters)
		{
			if (visibleCharacters <= textSoundedThrough)
				return;

			for (int i = textSoundedThrough; i < visibleCharacters; i++)
			{
				if (!char.IsLetterOrDigit(text[i]))
					continue;
				Sfx("text");
				break;
			}
			textSoundedThrough = visibleCharacters;
		}

		// ---- input ----

		private static bool InputBlocked => Main.drawingPlayerChat || Main.editSign || Main.editChest || Main.gameMenu || Main.ingameOptionsWindow || !Main.hasFocus;
		// The battle's keys are written as Deltarune's (Z, X, arrows) and mapped to whatever the player bound in
		// Settings > Controls (MercyMode.BattleKeys)
		private static bool Pressed(Keys k)
		{
			if (InputBlocked)
				return false;
			foreach (Keys b in MercyMode.BoundKeys(k))
				if (Main.keyState.IsKeyDown(b) && !Main.oldKeyState.IsKeyDown(b))
					return true;
			// Mouse buttons bound in Controls (not while the duel builder uses the mouse to place pieces)
			if (Instance?.phase != Phase.Build)
				foreach (int mb in MercyMode.BoundMouse(k))
					if (MercyMode.MouseDown(mb) && !MercyMode.MouseDown(mb, old: true))
						return true;
			return false;
		}

		private static bool Held(Keys k)
		{
			if (InputBlocked)
				return false;
			foreach (Keys b in MercyMode.BoundKeys(k))
				if (Main.keyState.IsKeyDown(b))
					return true;
			foreach (int mb in MercyMode.BoundMouse(k))
				if (MercyMode.MouseDown(mb))
					return true;
			return false;
		}
		private static bool Confirm => Pressed(Keys.Z);
		private static bool Cancel => Pressed(Keys.X);

		private static void Sfx(string role) => DeltaruneAssets.Play(role, role switch
		{
			"menumove" => SoundID.MenuTick,
			"select" => SoundID.MenuOpen,
			"cantselect" => SoundID.MenuClose,
			"hurt" => SoundID.PlayerHit,
			"damage" => SoundID.NPCHit1,
			"attack" => SoundID.Item1,
			"text" => SoundID.MenuTick,
			"slash" => SoundID.Item1,
			"crit" => SoundID.Item1,
			"heal" => SoundID.Item4,
			"spare" => SoundID.Item4,
			"boost" => SoundID.Item4,
			"weaponpull" => SoundID.Item1,
			_ => SoundID.MenuTick,
		});

		// ---- phases ----

		private void UpdateIntro()
		{
			// 1. glide in (FlyProgress) while the background fades in
			// 2. the hero swings their weapon, with the weapon-draw sound
			// (or, having hit the enemy to start the battle, strikes it: see FirstStrikeUpdate)
			if (phaseTicks == IntroSwingAt && !firstStrike)
			{
				SetHeroPose(HeroPose.Attack);
				Sfx("weaponpull");
			}
			FirstStrikeUpdate();
			// The strike may have won the battle outright
			if (!encounter.Alive)
				return;
			// 3. after the swing, the bottom UI glides up and the TP bar slides in (hspeed 13, friction 1)
			if (phaseTicks == IntroPanelTick)
			{
				panelDir = 1;
				tpBarIn = 0f;
				SetHeroPose(HeroPose.Idle);
				if (music != null)
				{
					musicVolumeCurrent = DeltaruneAssets.BattleMusicVolume;
					music.Volume = musicVolumeCurrent;
					music.Play();
					musicStarted = true;
				}
			}
			textShown = 0; // the encounter text types out once the panel is up
			if (panel >= PanelHeight && phaseTicks > IntroPanelTick)
			{
				// Multiplayer: the party's bullet box already started, or joined mid-battle and watching for now
				if (enemyTurnQueued)
				{
					enemyTurnQueued = false;
					enemyTurnGranted = true;
					StartEnemyTurn();
				}
				else if (spectating)
					EnterWaiting();
				else
					BeginPlayerTurn(keepText: true);
			}
		}

		private void BeginPlayerTurn(bool keepText = false)
		{
			if (potionSickTurns > 0 && phase != Phase.Intro)
				potionSickTurns--;
			defending = false;
			enemyTurnGranted = false;
			faceAction = FaceNone;
			if (heroPose == HeroPose.Defend)
				SetHeroPose(HeroPose.Idle);
			tpPreview = 0;
			RetargetIfNeeded();
			string line = keepText ? rawText : encounter.FlavorText();
			// A campfire or Heart Lantern nearby heals a little each turn, said under the turn's line
			if (phase != Phase.Intro && !downed && CozyHeal() is string cozy)
				line += "\n" + cozy;
			if (!keepText || line != rawText)
				SetText(line);
			// Downed (multiplayer): back up once the HP is above zero; until then the turn is skipped
			if (downed && !RecoverDowned())
				return;
			// A duel: the other player's turn first (we wait, then build their attack)
			if (duelWith >= 0 && !duelMyTurn)
			{
				EnterDuelWait();
				return;
			}
			SetPhase(Phase.Menu);
		}

		/// <summary>
		/// Terraria's Cozy Fire (+1 life regen) and Heart Lamp (+2): a small heal at the start of each of our turns, 2%
		/// and 3% of max HP. Returns the line to show, or null.
		/// </summary>
		private string CozyHeal()
		{
			bool fire = Player.HasBuff(BuffID.Campfire), lamp = Player.HasBuff(BuffID.HeartLamp);
			if (!fire && !lamp || Player.statLife <= 0 || Player.statLife >= Player.statLifeMax2)
				return null;
			int amount = (fire ? Math.Max(2, Player.statLifeMax2 * 2 / 100) : 0) + (lamp ? Math.Max(3, Player.statLifeMax2 * 3 / 100) : 0);
			// Through the battle's own HP (it's copied back onto the character every tick)
			amount = HealPlayer(amount);
			if (amount <= 0)
				return null;
			if (Main.netMode == NetmodeID.MultiplayerClient)
				NetMessage.SendData(MessageID.PlayerLifeMana, -1, -1, null, Player.whoAmI);
			PlayHealFx(amount);
			string from = fire && lamp ? "The campfire and the Heart Lantern" : fire ? "The campfire's warmth" : "The Heart Lantern";
			return $"* {from} restored {amount} HP.";
		}

		private void UpdateMenu()
		{
			if (Pressed(Keys.Left))
			{
				menuChoice = (Choice)(((int)menuChoice + 4) % 5);
				Sfx("menumove");
			}
			if (Pressed(Keys.Right))
			{
				menuChoice = (Choice)(((int)menuChoice + 1) % 5);
				Sfx("menumove");
			}
			if (!Confirm)
				return;

			switch (menuChoice)
			{
				case Choice.Fight:
					// Pick the weapon first, then the enemy
					Sfx("select");
					pendingChoice = menuChoice;
					OpenWeaponSelect();
					break;
				case Choice.Act:
				case Choice.Spare:
					Sfx("select");
					pendingChoice = menuChoice;
					OpenEnemySelect();
					break;
				case Choice.Item:
					if (HealingItems().Count == 0)
					{
						Sfx("cantselect");
						break;
					}
					Sfx("select");
					listIndex = 0;
					SetPhase(Phase.ItemSelect);
					break;
				case Choice.Defend:
					Sfx("select");
					Commit(FaceDefend, DoDefend);
					break;
			}
		}

		/// <summary>The enemy list, with the cursor on the current target.</summary>
		/// <summary>One row of the enemy list: an enemy, or one part of a boss when FIGHT can pick parts.</summary>
		private readonly struct TargetRow
		{
			public readonly BattleEnemy Enemy;
			public readonly NPC Part;

			public TargetRow(BattleEnemy enemy, NPC part)
			{
				Enemy = enemy;
				Part = part;
			}

			public bool Locked => Part != null && !Encounter.CanHit(Part);
		}

		/// <summary>The rows for the current command: parts of breakable bosses for FIGHT, whole enemies otherwise.</summary>
		private List<TargetRow> TargetRows()
		{
			var rows = new List<TargetRow>();
			foreach (BattleEnemy en in LivingEnemies)
			{
				if (pendingChoice == Choice.Fight && en.E.TargetableParts)
				{
					List<NPC> parts = en.E.TargetParts();
					if (parts.Count > 0)
					{
						foreach (NPC p in parts)
							rows.Add(new TargetRow(en, p));
						continue;
					}
				}
				rows.Add(new TargetRow(en, null));
			}
			return rows;
		}

		/// <summary>The enemy list, with the cursor on the current target (and part).</summary>
		private void OpenEnemySelect()
		{
			RetargetIfNeeded();
			List<TargetRow> rows = TargetRows();
			int at = rows.FindIndex(r => r.Enemy == targetEnemy && (r.Part == null || r.Part == targetEnemy.E.ChosenPart));
			if (at < 0)
				at = Math.Max(0, rows.FindIndex(r => r.Enemy == targetEnemy && !r.Locked));
			listIndex = Math.Max(0, at);
			ApplyRow(rows);
			SetPhase(Phase.EnemySelect);
		}

		private void ApplyRow(List<TargetRow> rows)
		{
			if (rows.Count == 0)
				return;
			listIndex = Math.Clamp(listIndex, 0, rows.Count - 1);
			TargetRow row = rows[listIndex];
			SetTarget(row.Enemy);
			row.Enemy.E.ChosenPart = row.Part;
		}

		private void UpdateEnemySelect()
		{
			List<TargetRow> rows = TargetRows();
			if (rows.Count > 1)
			{
				int before = listIndex;
				if (Pressed(Keys.Down))
					listIndex = (listIndex + 1) % rows.Count;
				if (Pressed(Keys.Up))
					listIndex = (listIndex + rows.Count - 1) % rows.Count;
				if (listIndex != before)
					Sfx("menumove");
			}
			ApplyRow(rows);
			if (Cancel)
			{
				if (pendingChoice == Choice.Fight && summonMenuShown)
					BackToSummonSelect();
				else if (pendingChoice == Choice.Fight)
					OpenWeaponSelect();
				else
					SetPhase(Phase.Menu);
				return;
			}
			if (!Confirm)
				return;
			// A guarded part (Golem's body behind its head, the Moon Lord's heart behind its eyes) can't be picked yet
			if (rows.Count > 0 && rows[Math.Clamp(listIndex, 0, rows.Count - 1)].Locked)
			{
				Sfx("cantselect");
				return;
			}
			Sfx("select");
			switch (pendingChoice)
			{
				case Choice.Fight:
					Commit(FaceFight, () =>
					{
						faceAction = FaceFight;
						StartFightBar();
					});
					break;
				case Choice.Act:
					listIndex = 0;
					SetPhase(Phase.ActSelect);
					break;
				case Choice.Spare:
					Commit(FaceSpare, DoSpare);
					break;
			}
		}

		// ---- ACT ----

		private List<ActOption> currentActs = new();

		private void UpdateActSelect()
		{
			currentActs = encounter.Acts(this);
			if (MoveInGrid(currentActs.Count))
				Sfx("menumove");
			ActOption act = currentActs[listIndex];
			tpPreview = act.TPCost / TensionToTP;

			if (Cancel)
			{
				tpPreview = 0;
				OpenEnemySelect();
				return;
			}
			if (!Confirm)
				return;

			var mp = Player.GetModPlayer<MercyPlayer>();
			if (act.TPCost > 0 && mp.TP < act.TPCost)
			{
				Sfx("cantselect");
				return;
			}
			Sfx("select");
			tpPreview = 0;
			Commit(FaceAct, () =>
			{
				faceAction = FaceAct;
				// TP may have gone since it was picked (multiplayer): the act still happens, as far as TP goes
				mp.TP = Math.Max(0f, mp.TP - act.TPCost);
				lastHeal = -1;
				List<string> lines = act.Run(this);
				SetHeroPose(HeroPose.Act);
				if (lastHeal >= 0)
					QueueHealFx(lastHeal, ActHealFrame);
				ShowMessages(lines, StartEnemyTurn);
			});
		}

		/// <summary>Two-column list navigation like Deltarune's ACT and ITEM menus.</summary>
		private bool MoveInGrid(int count)
		{
			int before = listIndex;
			if (Pressed(Keys.Right) && listIndex % 2 == 0 && listIndex + 1 < count)
				listIndex++;
			if (Pressed(Keys.Left) && listIndex % 2 == 1)
				listIndex--;
			if (Pressed(Keys.Down) && listIndex + 2 < count)
				listIndex += 2;
			if (Pressed(Keys.Up) && listIndex - 2 >= 0)
				listIndex -= 2;
			listIndex = Math.Clamp(listIndex, 0, Math.Max(0, count - 1));
			return listIndex != before;
		}

		// ---- ITEM ----

		/// <summary>Healing items in the main inventory, one entry per item type.</summary>
		public List<(int type, int count, string name, int heal)> HealingItems()
		{
			var list = new List<(int, int, string, int)>();
			var seen = new Dictionary<int, int>();
			for (int i = 0; i < 50; i++)
			{
				Item item = Player.inventory[i];
				if (item.IsAir || item.healLife <= 0 || !item.consumable)
					continue;
				if (seen.TryGetValue(item.type, out int idx))
				{
					var e = list[idx];
					list[idx] = (e.Item1, e.Item2 + item.stack, e.Item3, e.Item4);
					continue;
				}
				seen[item.type] = list.Count;
				list.Add((item.type, item.stack, item.Name, Player.GetHealLife(item, true)));
			}
			return list;
		}

		private void UpdateItemSelect()
		{
			var items = HealingItems();
			if (items.Count == 0)
			{
				SetPhase(Phase.Menu);
				return;
			}
			// Terraria item names are long, so ITEM is one column instead of Deltarune's two
			int before = listIndex;
			if (Pressed(Keys.Down) && listIndex + 1 < items.Count)
				listIndex++;
			if (Pressed(Keys.Up) && listIndex > 0)
				listIndex--;
			listIndex = Math.Clamp(listIndex, 0, items.Count - 1);
			if (listIndex != before)
				Sfx("menumove");
			if (Cancel)
			{
				SetPhase(Phase.Menu);
				return;
			}
			if (!Confirm)
				return;

			var (type, _, name, heal) = items[listIndex];
			// Potion sickness locks potions only; anything else in the list can still be used
			if (SickLocked(type))
			{
				Sfx("cantselect");
				return;
			}
			// In a party: on whom? (a downed partner can be brought back up)
			if (Net.BattleNet.InParty && HealTargets().Count > 1)
			{
				pickedItem = (type, name, heal);
				listIndex = 0;
				Sfx("select");
				SetPhase(Phase.PartySelect);
				return;
			}
			usedItemType = type;
			Commit(FaceItem, () => UseItem(type, name, heal));
		}

		private void UseItem(int type, string name, int heal, int target = -1)
		{
			for (int i = 0; i < 50; i++)
			{
				Item item = Player.inventory[i];
				if (item.type != type || item.IsAir)
					continue;
				heal = Player.GetHealLife(item, true);
				// Terraria's potion sickness, counted in turns (about 10 seconds each) instead of time
				if (item.potion)
					// (two turns shorter than a straight conversion: six turns of a twelve-to-sixteen-turn fight was too long)
					potionSickFull = potionSickTurns = Math.Max(1, (int)Math.Ceiling((item.type == ItemID.RestorationPotion ? Player.restorationDelayTime : Player.potionDelayTime) / 600f) - 2);
				item.stack--;
				if (item.stack <= 0)
					item.TurnToAir();
				break;
			}

			// On a partner: the heal goes to them through the server
			if (target >= 0 && target != Player.whoAmI)
			{
				// The heal shows on them when their HP comes back up (OnAllyHealed)
				Net.BattleNet.SendHealAlly(target, heal);
				usedItemType = type;
				faceAction = FaceItem;
				SetHeroPose(HeroPose.Item);
				Player who = Main.player[target];
				ShowMessages(new[] { $"* {Player.name} used the {name} on {who.name}!\n* {who.name} recovered {heal} HP!" }, StartEnemyTurn);
				return;
			}

			int healed = HealPlayer(heal);
			// The heal sound plays with the sparkles and the green number (PlayHealFx), not on the key press
			usedItemType = type;
			faceAction = FaceItem;
			SetHeroPose(HeroPose.Item);
			QueueHealFx(healed, ItemUseFrame);

			// Named, not "you": the others read this in their text box too
			string result = Player.statLife >= Player.statLifeMax2 ? $"* {Player.name}'s HP was maxed out." : $"* {Player.name} recovered {healed} HP!";
			ShowMessages(new[] { $"* {Player.name} used the {name}!\n{result}" }, StartEnemyTurn);
		}

		// ---- SPARE / DEFEND ----

		private void DoSpare()
		{
			if (duelWith >= 0)
			{
				DuelSpare();
				return;
			}
			faceAction = FaceSpare;
			string spared = $"* {Player.name} spared {encounter.Name}!";
			// Bosses fight to the end
			if (encounter.IsBoss)
			{
				SetHeroPose(HeroPose.Act);
				ShowMessages(new[] { $"* {Player.name} tried to spare {encounter.Name}...\n* But it won't back down." }, StartEnemyTurn);
				return;
			}
			if (encounter.Mercy >= 100f)
			{
				BattleEnemy who = targetEnemy;
				who.Out = true;
				PlayEnemySpared();
				// Multiplayer: the server spares it for real (the loot drops there) and tells the party
				if (Net.BattleNet.Online)
					Net.BattleNet.SendSpare(encounter.Npc);
				else
					encounter.Spare();
				var said = new List<string> { spared };
				if (LivingEnemies.Count == 0)
				{
					battleOver = true;
					SetHeroPose(HeroPose.Victory);
					ShowMessages(said, StartOutro);
					return;
				}
				// Others are still fighting: the squad reacts, then it's their turn
				string squad = OnEnemySpared(who);
				if (squad != null)
					said.Add(squad);
				RetargetIfNeeded();
				SetHeroPose(HeroPose.Act);
				ShowMessages(said, StartEnemyTurn);
				return;
			}
			SetHeroPose(HeroPose.Act);
			ShowMessages(new[] { spared + "\n* But its name wasn't YELLOW..." }, StartEnemyTurn);
		}

		private void DoDefend()
		{
			faceAction = FaceDefend;
			defending = true;
			SetHeroPose(HeroPose.Defend);
			var mp = Player.GetModPlayer<MercyPlayer>();
			mp.TP = Math.Min(100f, mp.TP + DefendTension * TensionToTP);
			Sfx("boost");
			StartEnemyTurn();
		}

		// ---- FIGHT ----

		// ---- text boxes ----

		/// <summary>Ticks the current text box has been up.</summary>
		private int messageTicks;
		/// <summary>Once the battle is won, its last text boxes move on by themselves after this long.</summary>
		private const int WonAutoContinueTicks = 5 * 60;

		private void UpdateMessage()
		{
			messageTicks++;
			// Won, or our action's lines in a party (the others wait on them): they move on by themselves
			bool auto = battleOver && messageTicks >= WonAutoContinueTicks || Net.BattleNet.InParty && executing && messageTicks >= 4 * 60;
			if (Cancel || auto)
				textShown = text.Length;
			if (textShown < text.Length || !(Confirm || auto))
				return;
			if (messages.Count > 0)
			{
				string next = messages.Dequeue();
				DuelShareText(next);
				SetText(next);
				return;
			}
			afterMessages?.Invoke();
		}

		// ---- enemy turn ----

		private void StartEnemyTurn()
		{
			// A duel's MERCY act: the other player answers first
			if (duelAskPending)
			{
				SetText($"* Waiting for {Main.player[duelWith].name} to answer...");
				SetPhase(Phase.MercyWait);
				return;
			}
			// Nobody left (another party member finished the last one while this player read a message)
			if (battleOver || LivingEnemies.Count == 0)
			{
				battleOver = true;
				StartOutro();
				return;
			}
			// Multiplayer: this player's action is over; the bullet box starts for everyone at once, from the server
			if (Net.BattleNet.InParty && !enemyTurnGranted)
			{
				if (executing)
				{
					executing = false;
					Net.BattleNet.SendActionDone();
				}
				if (!enemyTurnQueued)
				{
					EnterWaiting();
					return;
				}
				enemyTurnQueued = false;
				enemyTurnGranted = true;
			}
			executing = false;
			// A boss at the end of its rope says so, then goes all out (in a party there's no stopping for it: the box
			// opens for everyone together)
			if (DesperateBoss() is BattleEnemy desperate && !desperate.E.DesperationAnnounced)
			{
				desperate.E.DesperationAnnounced = true;
				ShakeScreen(3);
				if (!Net.BattleNet.InParty)
				{
					ShowMessages(new[] { $"* {desperate.E.Name} is fighting with everything it has left!" }, StartEnemyTurn);
					return;
				}
			}
			// The box opens: the party boxes go back to showing heads (not the command just used)
			faceAction = FaceNone;
			Bullets.Clear();
			boxAfterimages.Clear();
			enemyAttackEnergy = 0f;
			enemyAttackDirection = Vector2.Zero;
			RetargetIfNeeded();
			// Multiplayer: the same attack on every screen (shared seed; turn counters from the server's round)
			if (Net.BattleNet.InParty)
				foreach (BattleEnemy en in enemies)
					en.E.Turn = netRound;
			WithNetRand(() =>
			{
				attack = BuildEnemyTurn();
				testTurns++;
				BeginSoulMode(attack.Soul);
			});
			turnTimer = attack.Duration;
			boxTimer = 0;
			text = "";
			// A talker holds forth in the text box first (and may ask something)
			if (BeginTalkers())
				return;
			// Deltarune: the enemies say their piece first, then their bubbles go and the box opens
			if (enemies.Any(e => e.Living && !string.IsNullOrEmpty(e.Bubble)))
			{
				foreach (BattleEnemy e in enemies)
					e.BubbleAt = time;
				SetPhase(Phase.EnemyTalk);
				return;
			}
			OpenBulletBox();
		}

		/// <summary>The bubbles stay until they've been read (Z skips once typed), then the box opens.</summary>
		private void UpdateEnemyTalk()
		{
			var talking = enemies.Where(e => e.Living && !string.IsNullOrEmpty(e.Bubble)).ToList();
			int longest = talking.Select(e => BubblePlain(e.Bubble).Length).DefaultIfEmpty(0).Max();
			int typed = longest * 2 + 4;
			// Each one's voice as it starts (a little apart in a group), then soft blips while the words type out
			for (int i = 0; i < talking.Count; i++)
				if (phaseTicks == 1 + i * 8 && talking[i].E.Voice is Terraria.Audio.SoundStyle voice)
					AttackSfx.Vanilla(voice with { PitchVariance = 0.15f }, 0.7f);
			if (phaseTicks < typed && phaseTicks % 4 == 2)
				DeltaruneAssets.Play("text", Terraria.ID.SoundID.MenuTick with { Volume = 0.4f });
			// The same length on every screen in a party (the box opens together); alone, Z moves it on
			bool skip = !Net.BattleNet.InParty && phaseTicks > typed && phaseTicks > 12 && Confirm;
			if (skip || phaseTicks >= typed + EnemyTalkHoldTicks)
				OpenBulletBox();
		}

		/// <summary>How long the bubbles stay once they're typed out, before the box opens by itself.</summary>
		private const int EnemyTalkHoldTicks = 60;

		/// <summary>scr_moveheart: the SOUL bursts out of the hero and flies to the box in 8 frames.</summary>
		private void OpenBulletBox()
		{
			foreach (BattleEnemy e in enemies)
				e.Bubble = null;
			soulFrom = HeroHeart;
			soul = soulFrom;
			soulAlpha = 0f;
			AddEffect(new HeartBurst(HeroHeart));
			disableSlow = Held(Keys.X);
			SetPhase(Phase.EnemyIntro);
			DuelBoxOpened();
		}

		/// <summary>The bullet box: the normal square, opening into <see cref="FullScreenArena"/> for full-screen attacks.</summary>
		public Rectangle Box
		{
			get
			{
				var small = new Rectangle((int)(BoxCenterX - BoxSize / 2f), (int)(BoxCenterY - BoxSize / 2f), BoxSize, BoxSize);
				if (arenaBlend <= 0f)
					return small;
				Rectangle big = arenaRect;
				float t = arenaBlend;
				int left = (int)MathHelper.Lerp(small.Left, big.Left, t), top = (int)MathHelper.Lerp(small.Top, big.Top, t);
				int right = (int)MathHelper.Lerp(small.Right, big.Right, t), bottom = (int)MathHelper.Lerp(small.Bottom, big.Bottom, t);
				return new Rectangle(left, top, right - left, bottom - top);
			}
		}

		/// <summary>0 = the normal box, 1 = a full-screen arena (eased while it opens and closes).</summary>
		private float arenaBlend;

		/// <summary>What the box opens to: the full-screen arena, or a bigger box (kept until the next one opens).</summary>
		private Rectangle arenaRect = FullScreenArena;
		private bool arenaIsFull = true;

		private void UpdateArena()
		{
			bool open = phase == Phase.EnemyTurn && attack != null && (attack.FullScreen || attack.Grow > 0f && attack.Grow != 1f);
			if (open)
			{
				arenaIsFull = attack.FullScreen;
				int size = (int)(BoxSize * attack.Grow);
				// A bigger box grows from the normal one's centre, but no lower than the screen allows
				arenaRect = arenaIsFull ? FullScreenArena
					: new Rectangle((int)(BoxCenterX - size / 2f), (int)Math.Max(FullScreenArena.Top, BoxCenterY - size / 2f), size, size);
			}
			float target = open ? 1f : 0f;
			arenaBlend = MathHelper.Lerp(arenaBlend, target, EasePerTick(ArenaEase));
			if (Math.Abs(arenaBlend - target) < 0.002f)
				arenaBlend = target;
		}
		public Vector2 SoulCenter => soul + new Vector2(SoulSize / 2f);
		private Vector2 soulPull;
		/// <summary>Pulls the (red) SOUL this much this tick, on top of its own movement (capped below its speed).</summary>
		public void PullSoul(Vector2 by) => soulPull += by;
		private int confusedTicks;
		/// <summary>Reverses the arrow keys for the (red) SOUL this tick (the Brain of Cthulhu's confusion).</summary>
		public void Confuse() => confusedTicks = 2;
		private Action overBullets;
		private int overBulletsUntil;
		/// <summary>Draws this over every bullet in the box this tick (set it again each tick to keep it).</summary>
		public void DrawOverBullets(Action draw)
		{
			overBullets = draw;
			overBulletsUntil = time + 1;
		}
		private int slipTicks;
		private Vector2 slipVelocity;
		/// <summary>The (red) SOUL slides on ice this tick: it speeds up and slows down gradually instead of stopping dead.</summary>
		public void MakeSlippery() => slipTicks = 2;
		public void Spawn(Bullet b)
		{
			b.Owner ??= spawnOwner;
			b.DamageMult *= spawnDamageScale * talkDamageMult;
			// Bullets can spawn others from their OnUpdate (a slam's shockwave, a firework's burst) while the
			// bullet list is being walked; those join after the walk
			if (updatingBullets)
				spawnedDuringUpdate.Add(b);
			else
				Bullets.Add(b);
			Vector2 motion = b.Velocity;
			if (motion.LengthSquared() < 0.01f)
				motion = b.Position - Box.Center.ToVector2();
			if (motion.LengthSquared() > 0.01f)
				enemyAttackDirection = Vector2.Lerp(enemyAttackDirection, Vector2.Normalize(motion), 0.3f);
			enemyAttackEnergy = Math.Min(1.5f, enemyAttackEnergy + 0.22f);

			// Pattern volleys can create many projectiles in one tick; throttle the cue so it reads as attack rhythm.
			if ((b.OnDraw == null || b.SoundOnSpawn) && patternSoundCooldown <= 0)
			{
				Sfx("attack");
				patternSoundCooldown = 12;
			}
		}
		public bool BossInSecondPhase => encounter.LifeRatio < 0.5f;

		private void CaptureBoxAfterimage()
		{
			if (phaseTicks % TicksPerFrame != 0 || boxTimer <= 0f)
				return;
			float t = boxTimer / BoxGrowTicks;
			float angle = phase == Phase.EnemyTurn ? 0f : -MathHelper.ToRadians(180f + 180f * t);
			float opacity = 0.5f + t * 0.5f;
			boxAfterimages.Add(new BoxAfterimage
			{
				Scale = 2f * t,
				Rotation = angle,
				Alpha = (1f - opacity) + 0.1f,
			});
		}

		private Vector2 SoulRestPosition => new Vector2(BoxCenterX, BoxCenterY) - new Vector2(SoulSize / 2f);

		private void UpdateEnemyIntro()
		{
			boxTimer = Math.Min(BoxGrowTicks, boxTimer + 1);
			CaptureBoxAfterimage();
			// obj_moveheart: flytime 8 frames, image_alpha += 0.334 per frame
			soul = Vector2.Lerp(soulFrom, SoulStart, Math.Min(1f, phaseTicks / (8f * TicksPerFrame)));
			// A SOUL mode other than red announces itself as the SOUL lands: a ring in its colour and a chime
			if (soulMode != SoulMode.Red && phaseTicks == 8 * TicksPerFrame)
			{
				AddEffect(new Shockwave(SoulStart + new Vector2(SoulSize / 2f), soulMode.Color(), 34f));
				DeltaruneAssets.Play("soulchange", Terraria.ID.SoundID.Item35 with { Volume = 0.5f, Pitch = 0.4f });
			}
			soulAlpha = Math.Min(1f, phaseTicks / (float)TicksPerFrame * 0.334f);
			if (boxTimer >= BoxGrowTicks)
				SetPhase(Phase.EnemyTurn);
		}

		private void UpdateEnemyTurn()
		{
			WithNetRand(() => attack.Update(this, phaseTicks));
			Vector2 soulBefore = soul;
			MoveSoul();
			soulMoved = Vector2.DistanceSquared(soulBefore, soul) > 0.04f;
			UpdateYellowShots();

			// obj_heart: global.inv -= 1 every frame
			inv--;
			Rectangle soulHit = new((int)soul.X + SoulHitInset, (int)soul.Y + SoulHitInset, SoulHitSize, SoulHitSize);
			Vector2 c = SoulCenter;
			Rectangle grazeBox = new((int)(c.X - GrazeSize / 2f), (int)(c.Y - GrazeSize / 2f), GrazeSize, GrazeSize);
			var mp = Player.GetModPlayer<MercyPlayer>();

			updatingBullets = true;
			foreach (Bullet b in Bullets)
			{
				b.Update();
				// Bullets still waiting to appear (StartDelay) are invisible, so they can't hurt or be grazed yet
				if (b.Dead || !b.Harmful || b.Waiting)
					continue;
				if (ShieldBlocks(b))
					continue;
				if (inv < 0 && b.Touches(soulHit) && ColourAllows(b))
				{
					HitSoul(b);
					if (b.DestroyOnHit)
						b.Dead = true; // obj_collidebullet destroys itself on hit
					continue;
				}
				// Downed: no SOUL in the box, so no grazing either
				if (inv < 0 && !downed && b.Touches(grazeBox))
					Graze(b, mp);
			}
			updatingBullets = false;
			Bullets.AddRange(spawnedDuringUpdate);
			spawnedDuringUpdate.Clear();
			Bullets.RemoveAll(b => b.Dead);
			if (deathPending)
			{
				BeginSoulDeath();
				return;
			}

			turnTimer--;
			if (turnTimer <= 0 && !Player.dead)
			{
				Bullets.Clear();
				soulFrom = soul;
				SetPhase(Phase.EnemyOutro);
			}
		}

		/// <summary>obj_heart Step: 4 px per frame, half while X is held, kept inside the box.</summary>
		private void MoveSoul()
		{
			if (MoveSoulMode())
				return;
			float px = 0, py = 0;
			float speed = SoulSpeed;
			if (Held(Keys.Right)) px = speed;
			if (Held(Keys.Left)) px = -speed;
			if (Held(Keys.Down)) py = speed;
			if (Held(Keys.Up)) py = -speed;
			if (Held(Keys.X))
			{
				if (!disableSlow)
				{
					px = Math.Sign(px) * SoulSlowSpeed;
					py = Math.Sign(py) * SoulSlowSpeed;
				}
			}
			else
			{
				disableSlow = false;
			}
			if (confusedTicks > 0)
			{
				px = -px;
				py = -py;
				confusedTicks--;
			}
			if (slipTicks > 0)
			{
				// On ice: the arrow keys push, the SOUL keeps sliding
				slipVelocity = Vector2.Lerp(slipVelocity, new Vector2(px, py), 0.06f);
				slipTicks--;
				soul += slipVelocity;
			}
			else
			{
				slipVelocity = new Vector2(px, py);
				soul += slipVelocity;
			}
			// An attack pulling at it (the Moon Lord's black hole); never faster than the SOUL can walk away
			if (soulPull != Vector2.Zero)
			{
				float max = SoulSpeed * 0.6f;
				soul += soulPull.Length() > max ? Vector2.Normalize(soulPull) * max : soulPull;
				soulPull = Vector2.Zero;
			}

			Rectangle box = Box;
			soul.X = MathHelper.Clamp(soul.X, box.Left + BoxClampLow, box.Right - BoxClampHigh);
			soul.Y = MathHelper.Clamp(soul.Y, box.Top + BoxClampLow, box.Bottom - BoxClampHigh);
		}

		/// <summary>The least share of max HP (after defense) a regular enemy's bullet takes off.</summary>
		private const float RegularMinLifeShare = 0.03f;

		/// <summary>The SOUL moved this tick (falling counts): blue bones hurt it, orange ones don't.</summary>
		private bool soulMoved;

		/// <summary>A blue bone only hurts a moving SOUL, an orange one only a still one; anything else always does.</summary>
		private bool ColourAllows(Bullet b) => b.Sans switch { 1 => soulMoved, 2 => !soulMoved, _ => true };

		private void HitSoul(Bullet b)
		{
			// Downed: out of the box
			if (downed)
				return;
			Encounter by = b.Owner ?? encounter;
			// Bosses: sized against a typical player for their stage (contact damage left early hits at 1-5 after
			// defense, and King Slime's 40 was a third of a starting health bar)
			int damage = by != null && by.IsBoss && duelWith < 0
				? BossBulletDamage(by, b.DamageMult, Player.statDefense)
				: Math.Max(1, (int)Math.Round((b.Owner?.Damage ?? turnDamage) * b.DamageMult));
			// A hazard hurts like one whatever the enemy: enough that after defense (Terraria takes half of it) at least
			// its share of max HP comes off
			// (Any regular enemy's bullet: at least 3%, so defense never turns a hit into a scratch)
			float share = b.MinLifeShare > 0f ? b.MinLifeShare : RegularMinLifeShare;
			if (by == null || !by.IsBoss && duelWith < 0)
				damage = Math.Max(damage, (int)Math.Ceiling(Player.statLifeMax2 * share + Player.statDefense / 2f));
			Player.immune = false;
			Player.immuneTime = 0;
			HurtingPlayer = true;
			double dealt;
			try
			{
				dealt = Player.Hurt(PlayerDeathReason.ByCustomReason(NetworkText.FromLiteral($"{Player.name} was defeated by {by.Name}.")),
					damage, 0, knockback: 0f);
			}
			finally
			{
				HurtingPlayer = false;
			}
			// Terraria's own hit invincibility would hide the SOUL's; the battle handles it
			Player.immune = false;
			Player.immuneTime = 0;
			// A lethal hit just put us DOWN (battleLife is below zero now): keep it
			if (!downed)
				battleLife = Player.statLife;

			inv = InvincibleTicks;
			Sfx("hurt");
			// scr_damage: the hero flinches, the screen shakes, the number pops off the hero
			hurtTimer = 0;
			shake = 4;
			// A duel: the number in the colour of the player whose attack it was
			HeroNumber((int)dealt, duelWith >= 0 ? DuelOppDamageColor : Color.White);
			DuelSendHurt((int)dealt);
		}

		private void Graze(Bullet b, MercyPlayer mp)
		{
			float tension;
			if (!b.Grazed)
			{
				b.Grazed = true;
				// One TP per bullet grazed (Deltarune's graze points filled the bar far too fast here)
				tension = GrazeTP / TensionToTP;
				if (turnTimer >= GrazeTurnCutMinTicks && duelWith < 0)
					turnTimer -= b.TimePoints * TicksPerFrame;
				grazeTimer = GrazeFlashTicks;
				DeltaruneAssets.PlayIfLoaded("graze");
			}
			else
			{
				// Staying close keeps the graze flash (and cuts the turn short) but gives no more TP
				tension = 0f;
				if (turnTimer >= GrazeTurnCutMinTicks && duelWith < 0)
					turnTimer -= b.TimePoints / GrazeHoldDivisor;
				if (grazeTimer >= 0 && grazeTimer < 4 * TicksPerFrame)
					grazeTimer = 3 * TicksPerFrame;
				if (grazeTimer < 2 * TicksPerFrame)
					grazeTimer = 2 * TicksPerFrame;
			}
			mp.TP = Math.Min(100f, mp.TP + tension * TensionToTP);
		}

		private void UpdateEnemyOutro()
		{
			// A full-screen arena closes back into the box first, carrying the SOUL in with it
			if (arenaBlend > 0.01f)
			{
				Rectangle box = Box;
				soul.X = MathHelper.Clamp(soul.X, box.Left + BoxClampLow, box.Right - BoxClampHigh);
				soul.Y = MathHelper.Clamp(soul.Y, box.Top + BoxClampLow, box.Bottom - BoxClampHigh);
				soulFrom = soul;
				phaseTicks = 0;
				return;
			}
			boxTimer = Math.Max(0, boxTimer - 1);
			CaptureBoxAfterimage();
			// obj_returnheart: back to the hero in 8 frames, then a burst; the next turn starts 15 frames after the end
			soul = Vector2.Lerp(soulFrom, HeroHeart, Math.Min(1f, phaseTicks / (8f * TicksPerFrame)));
			if (phaseTicks == 8 * TicksPerFrame)
				AddEffect(new HeartBurst(HeroHeart));
			if (boxTimer <= 0 && phaseTicks >= 15 * TicksPerFrame)
			{
				inv = -1;
				if (duelWith >= 0)
					DuelBoxClosed();
				else
					BeginPlayerTurn();
			}
		}

		// ---- ending ----

		private void StartOutro()
		{
			// Won while down (multiplayer): back up at 1 HP
			if (downed)
			{
				downed = false;
				battleLife = 1;
				Player.statLife = 1;
			}
			Bullets.Clear();
			panelDir = -1;
			text = "";
			SetPhase(Phase.Outro);
		}

		private void UpdateOutro()
		{
			if (music != null)
				music.Volume *= 0.9f;
			// Wait for the last afterimages to fade on their own, so the trail never just vanishes
			if (panel <= 0 && screenFade <= 0f && phaseTicks >= GlideTicks && trail.Count == 0 && enemies.All(e => e.Trail.Count == 0))
				End();
		}

		// ================================================================== drawing

		private const float BackgroundBleed = 8f;
		private const float FightBarX = 2;
		private const float FightBarY = 365;
		private static readonly Color PanelLine = MergeColor(MergeColor(new Color(128, 0, 128), Color.Black, 0.7f), new Color(64, 64, 64), 0.5f);
		private static readonly Color Orange = new(255, 160, 64);
		private static readonly Color BoxGreen = MergeColor(new Color(0, 128, 0), new Color(0, 255, 0), 0.5f);

		private static Color MergeColor(Color a, Color b, float t) => Color.Lerp(a, b, t);

		/// <summary>The party member's panel: rises 32 px while choosing, like Deltarune's active character.</summary>
		private Rectangle PartyBox
		{
			get
			{
				int top = (int)(ScreenHeight - panel - partyLift);
				return new Rectangle(PartyBoxX(Net.BattleNet.MyPartyIndex), top, 212, 36);
			}
		}


		/// <summary>
		/// While a battle runs: the scroll wheel and hotbar keys don't switch items underneath it, and Esc (the
		/// inventory key, locked during battles) opens the pause menu instead, so Save & Exit is still there.
		/// </summary>
		public override void PostUpdateInput()
		{
			if (phase == Phase.None || Main.gameMenu)
				return;
			// The duel builder scrolls its weapons with the wheel (kept before it's taken from the hotbar)
			buildScroll += PlayerInput.ScrollWheelDelta;
			PlayerInput.ScrollWheelDelta = 0;
			PlayerInput.ScrollWheelDeltaForUI = 0;
			foreach (TriggersSet set in new[] { PlayerInput.Triggers.Current, PlayerInput.Triggers.JustPressed })
				foreach (string key in set.KeyStatus.Keys.Where(k => k.StartsWith("Hotbar") || k.StartsWith("DpadRadial")).ToList())
					set.KeyStatus[key] = false;
			// Esc opens and closes the pause menu, handled here entirely: Terraria's own inventory toggle would close
			// the menu on the same press (while paused it reads the key directly), so it never sees a held Esc
			if (PlayerInput.Triggers.Current.Inventory)
				Main.LocalPlayer.releaseInventory = false;
			if (PlayerInput.Triggers.JustPressed.Inventory && !Main.drawingPlayerChat)
			{
				if (Main.ingameOptionsWindow)
					IngameOptions.Close();
				else
					IngameOptions.Open();
			}
			// Closing the pause menu (Esc or its button) opens the inventory, which the battle hides; with auto-pause on
			// that paused the game for good. There's no inventory in a battle
			if (!Main.ingameOptionsWindow)
				Main.playerInventory = false;
		}

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			if (phase == Phase.None)
				return;

			// Hide the normal HUD; keep the logic layers, the pause menu and chat (battle keys are ignored while
			// chat is open, so it has to stay visible)
			foreach (var l in layers)
			{
				if (l.Name.Contains("Logic") || l.Name.Contains("Ingame Options") || l.Name.Contains("Cursor") || l.Name.Contains("Player Chat"))
					continue;
				l.Active = false;
			}

			// Under the pause menu (so Save & Exit shows over the battle), and so under chat too
			int index = layers.FindIndex(l => l.Name.Contains("Ingame Options"));
			if (index < 0)
				index = layers.FindIndex(l => l.Name.Contains("Player Chat"));
			if (index < 0)
				index = layers.Count;
			layers.Insert(index, new LegacyGameInterfaceLayer("MercyMode: Battle", () =>
			{
				DrawBattle(Main.spriteBatch);
				return true;
			}, InterfaceScaleType.None));
		}

		private void DrawBattle(SpriteBatch sb)
		{
			ComputeScreenTransform();
			float scale = drScale, ox = drOx, oy = drOy;
			Matrix m = ShakeMatrix * Matrix.CreateScale(scale, scale, 1f) * Matrix.CreateTranslation(ox, oy, 0f);
			DrDraw.Transform = m;

			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, m);
			DrDraw.Sb = sb;
			try
			{
				float left = -ox / scale, top = -oy / scale, width = Main.screenWidth / scale, height = Main.screenHeight / scale;
				DrawBackground(left - BackgroundBleed, top - BackgroundBleed,
					width + BackgroundBleed * 2f, height + BackgroundBleed * 2f);
				DrawEnemy(sb, m);
				DrawBubbles();
				DrawAllies(sb, m);
				// Summons behind the player
				DrawMinions(sb, m);
				DrawHero(sb, m);
				DrawSwingSlash();
				DrawFightBeam();
				DrawDuelOppBeam();
				DrawEffects();
				DrawFirstStrikeLine();
				DrawTestRun();
				if (arenaBlend > 0f && arenaIsFull)
				{
					// A full-screen attack: everything, the HUD included, goes black; only the arena's border,
					// the SOUL and the attacks are left
					DrawTPBar();
					DrawPanel(left - BackgroundBleed, width + BackgroundBleed * 2f, top + height + BackgroundBleed);
					DrDraw.Rect(left - BackgroundBleed, top - BackgroundBleed, width + BackgroundBleed * 2f, height + BackgroundBleed * 2f,
						Color.Black * arenaBlend);
					DrawBox();
				}
				else
				{
					DrawBox();
					DrawTPBar();
					DrawPanel(left - BackgroundBleed, width + BackgroundBleed * 2f, top + height + BackgroundBleed);
					DrawBuffs(left - BackgroundBleed, width + BackgroundBleed * 2f);
				}
				DrawBuildOverlay();
				DrawSoulDeath(left - BackgroundBleed, top - BackgroundBleed, width + BackgroundBleed * 2f, height + BackgroundBleed * 2f);
			}
			finally
			{
				sb.End();
				sb.Begin(SpriteSortMode.Deferred, null, null, null, null, null, Matrix.Identity);
			}
		}

		private static readonly System.Diagnostics.Stopwatch BackgroundClock = System.Diagnostics.Stopwatch.StartNew();

		private void DrawBackground(float left, float top, float width, float height)
		{
			// obj_battleback: black fades in at 0.1 per frame, then bg_battleback1 tiled twice, scrolling
			// diagonally: one layer +0.5 px/frame at half alpha, the other -1 px/frame
			DrDraw.Rect(left, top, width, height, Color.Black * screenFade);
			var tile = DeltaruneAssets.Sprite("bg_battleback1");
			// Real time, not game ticks: it scrolls smoothly at whatever rate the screen draws (ticks moved it in steps,
			// and rounding to whole pixels made it jump every few ticks)
			float frames = (float)(BackgroundClock.Elapsed.TotalSeconds * 30.0 % 200.0);
			float siner = frames * 0.5f % 100f, siner2 = frames % 100f;
			// Tiles land on whole screen pixels (no seams or shimmer between them), at any battle scale
			float Snap(float v) => (float)Math.Round(v * drScale) / drScale;
			if (tile == null)
			{
				Color grid = new Color(80, 32, 120) * (0.35f * screenFade);
				for (float x = left - 50 + siner % 50; x < left + width; x += 50)
					DrDraw.Rect(x, top, 1, height, grid);
				for (float y = top - 50 + siner % 50; y < top + height; y += 50)
					DrDraw.Rect(left, y, width, 1, grid);
				return;
			}
			void tiled(float ox, float oy, float alpha)
			{
				int w = tile.Width, h = tile.Height;
				float startX = ox + (float)Math.Floor((left - ox) / w) * w;
				float startY = oy + (float)Math.Floor((top - oy) / h) * h;
				for (float x = startX; x < left + width; x += w)
					for (float y = startY; y < top + height; y += h)
						DrDraw.Sb.Draw(tile.Frames[0], new Vector2(Snap(x), Snap(y)), Color.White * alpha);
			}
			tiled(-100 + siner, -100 + siner, screenFade / 2f);
			tiled(-200 - siner2, -210 - siner2, screenFade);
		}

		/// <summary>Every enemy in the battle, each at its own spot (back row first, so the front one overlaps).</summary>
		private void DrawEnemy(SpriteBatch sb, Matrix m)
		{
			foreach (BattleEnemy en in enemies.OrderBy(e => e.E.ScreenCenter.Y))
			{
				if (!en.Living && en.Override == null)
					continue; // spared or defeated, and its animation is over
				WithEnemy(en, () => DrawOneEnemy(sb, m));
			}
		}

		private void DrawOneEnemy(SpriteBatch sb, Matrix m)
		{
			if (enemyOverride != null)
			{
				enemyOverride.Draw();
				DrawSlash(enemySnap.Position);
				return;
			}
			if (duelWith >= 0)
			{
				DrawDuelOpponent(sb, m);
				return;
			}
			if (encounter != null && encounter.DrawWithTerraria)
			{
				DrawEnemyComposite(sb, m);
				return;
			}
			NPC npc = encounter?.DrawNpc;
			if (npc == null || !npc.active)
				return;
			// Its own sprite (a boulder has no NPC to draw)
			bool custom = encounter.CustomSprite(out Texture2D customTex, out Rectangle customFrame);
			if (!custom)
				Main.instance.LoadNPC(npc.type);
			Texture2D tex = custom ? customTex : TextureAssets.Npc[npc.type].Value;
			int frameCount = custom ? 1 : Math.Max(1, Main.npcFrameCount[npc.type]);
			int frameHeight = custom ? customFrame.Height : tex.Height / frameCount;
			Rectangle frame = custom ? customFrame
				: npc.frame.Width > 0 && npc.frame.Height > 0 ? npc.frame
				: new Rectangle(0, 0, tex.Width, frameHeight);
			// Terraria keeps picking frames for a frozen NPC every tick, and one frozen mid-move (a slime caught in the
			// air) can flip between frames every tick: follow its frame only at a steady animation pace
			if (focus != null && !custom)
			{
				if (focus.ShownFrame.Height == 0 || frame.Height != focus.ShownFrame.Height || frame.Width != focus.ShownFrame.Width)
				{
					focus.ShownFrame = frame;
					focus.ShownFrameAt = time;
				}
				// How often Terraria's frame has been changing lately, and which frames it has been using (once per tick,
				// however often this is drawn)
				if (time != focus.RawFrameTick)
				{
					focus.FrameJitter = Math.Max(0f, focus.FrameJitter - 0.15f);
					if (frame != focus.RawFrame)
					{
						focus.FrameJitter += 1f;
						focus.RawChanges++;
					}
					focus.RawFrame = frame;
					focus.RawFrameTick = time;
					focus.FramesSeen[frame.Y] = time;
					foreach (int y in focus.FramesSeen.Keys.ToList())
						if (time - focus.FramesSeen[y] > 40)
							focus.FramesSeen.Remove(y);
				}
				if (time - focus.ShownFrameAt >= (encounter.IsBoss ? EnemyFrameTicks : RegularEnemyFrameTicks))
				{
					// Changing faster than it's shown (a fish flapping): step through the frames in order rather
					// than catching every other one
					if ((focus.FrameJitter > 2.5f || focus.RawChanges > 1) && focus.FramesSeen.Count > 1)
					{
						// Changing every tick (wings beating, or a slime stuck mid-jump): play the frames it has been
						// using, in order, at a steady pace, instead of copying the flicker
						var ys = focus.FramesSeen.Keys.OrderBy(y => y).ToList();
						int next = ys.FirstOrDefault(y => y > focus.ShownFrame.Y, ys[0]);
						focus.ShownFrame = new Rectangle(frame.X, next, frame.Width, frame.Height);
						focus.ShownFrameAt = time;
						focus.RawChanges = 0;
					}
					else if (frame != focus.ShownFrame)
					{
						focus.ShownFrame = frame;
						focus.ShownFrameAt = time;
						focus.RawChanges = 0;
					}
				}
				frame = focus.ShownFrame;
			}
			if (phase == Phase.EnemyTurn && enemyAttackEnergy > 0.18f && frameCount > 1)
			{
				// The vanilla Eye's extra frames are eye-opening states, not attack poses.
				if (npc.type != NPCID.EyeofCthulhu)
				{
					int baseFrame = Math.Clamp(npc.frame.Y / Math.Max(1, frameHeight), 0, frameCount - 1);
					int attackFrame = (baseFrame + 1 + (int)(time / 8) % (frameCount - 1)) % frameCount;
					frame.Y = attackFrame * frameHeight;
					frame.Height = frameHeight;
				}
			}
			if (encounter.FrameOverride(time, frameCount) is int shownFrame)
			{
				frame.Y = Math.Clamp(shownFrame, 0, frameCount - 1) * frameHeight;
				frame.Height = frameHeight;
			}
			float drawScale = EnemyScaleNow(out _, out _);
			float glide = FlyProgress();
			float attackMotion = phase == Phase.EnemyTurn ? MathHelper.Clamp(enemyAttackEnergy, 0f, 1f) : 0f;
			float rotation = MathHelper.Lerp(enemyWorldRotation, encounter.DrawRotation(time), glide)
				- enemyAttackDirection.X * attackMotion * 0.055f + cutSpin;
			// Lit like the world while gliding in or out, so it turns into the real NPC without a jump in brightness
			Color worldLight = WorldLightTint(npc.Center);
			Color baseColor = Tint(encounter.DrawColor(npc), worldLight);
			// Afterimages left behind while gliding, fading out
			foreach (EnemyTrail t in focus.Trail)
			{
				float a = 0.5f * (1f - t.Age / TrailLife);
				DrDraw.Sb.Draw(tex, t.Pos, frame, baseColor * a, rotation, frame.Size() / 2f, t.Scale, SpriteEffects.None, 0f);
			}
			Vector2 pos = EnemyPosNow + new Vector2(0, (float)Math.Sin(time / 20f) * 4f * glide);
			pos -= enemyAttackDirection * (attackMotion * 3f);
			float pulse = (float)Math.Sin(time / 3f) * attackMotion * 0.035f;
			Vector2 spriteScale = new(drawScale * (1f + pulse), drawScale * (1f - pulse * 0.65f));
			if (enemyShake > 0)
				pos.X += (enemyShake % 4 < 2 ? 1 : -1) * enemyShake / 2f;
			float alpha = 1f;
			// Only the enemy being targeted flashes
			bool selecting = (phase == Phase.EnemySelect || phase == Phase.ActSelect) && focus == targetEnemy;
			Color color = Tint(encounter.DrawColor(npc), worldLight) * alpha;
			int frameIndex = frame.Y / Math.Max(1, frameHeight);
			encounter.DrawBehindSprite(pos, frameIndex, rotation, spriteScale, Tint(Color.White, worldLight) * alpha);
			DrDraw.Sb.Draw(tex, pos, frame, color, rotation, frame.Size() / 2f, spriteScale, SpriteEffects.None, 0f);
			encounter.DrawOverSprite(pos, frameIndex, rotation, spriteScale, Tint(Color.White, worldLight) * alpha);
			enemySnap = new EnemySnapshot
			{
				Texture = tex, Frame = frame, Position = pos, Rotation = rotation,
				Scale = (spriteScale.X + spriteScale.Y) / 2f, Color = color, Valid = true,
			};
			if (selecting)
			{
				// The targeted enemy flashes white: fog alpha (-cos(fsiner / 5) * 0.4) + 0.6, fsiner per frame
				float fsiner = time / (float)TicksPerFrame;
				float flash = (float)(-Math.Cos(fsiner / 5f) * 0.4 + 0.6);
				DrDraw.Sb.Draw(WhiteMask.Of(tex), pos, frame, Color.White * flash, rotation, frame.Size() / 2f, spriteScale, SpriteEffects.None, 0f);
			}

			DrawSlash(pos);
		}

		/// <summary>obj_basicattack: the slash over the enemy (2.5x for a perfect hit).</summary>
		private void DrawSlash(Vector2 pos)
		{
			// Only on the enemy that was hit (every enemy in a group is drawn through here); a killing hit's slash stays
			// on the one it killed, not the next target
			if (slashTimer < 0 || focus != (slashEnemy ?? targetEnemy))
				return;
			int f = Math.Min(4, slashTimer / 4); // image_speed 0.5: two frames per sprite frame
			float s = bestPoints == 150 ? 2.5f : 2f;
			if (!DrDraw.Sprite("spr_attack_cut1", f, pos.X, pos.Y, KrisCyan, s))
				DrDraw.Rect(pos.X - 30 + slashTimer * 3, pos.Y - 30 + slashTimer * 3, 8, 8, KrisCyan);
		}

		private void DrawBox()
		{
			if (phase != Phase.EnemyIntro && phase != Phase.EnemyTurn && phase != Phase.EnemyOutro && phase != Phase.Build)
				return;

			float t = boxTimer / BoxGrowTicks;
			float s = 2f * t;
			// GameMaker angles go counter-clockwise; XNA's go clockwise
			float angle = phase == Phase.EnemyTurn ? 0f : -MathHelper.ToRadians(180f + 180f * t);
			float alpha = 0.5f + t * 0.5f;
			foreach (BoxAfterimage image in boxAfterimages)
			{
				if (arenaBlend > 0.01f)
					break; // the spinning afterimages belong to the small box
				float ghostAlpha = Math.Max(0f, image.Alpha - image.Age * 0.04f);
				if (ghostAlpha > 0f)
					DrawBoxShape(image.Scale, image.Rotation, ghostAlpha);
			}
			DrawBoxShape(s, angle, alpha);

			DrawPurpleStrings();
			foreach (Bullet b in Bullets)
				b.Draw();
			DrawBulletEffects();
			// An attack's layer over every bullet (the Eater's darkness)
			if (overBullets != null && time <= overBulletsUntil)
				overBullets();

			// SOUL (spr_dodgeheart flips frames while invincible) and the graze flash
			int frame = inv > 0 ? (inv / SoulBlinkTicks) % 2 : 0;
			// Once the SOUL is back with the hero it isn't drawn in the box any more
			bool soulHome = phase == Phase.EnemyOutro && phaseTicks >= 8 * TicksPerFrame || phase == Phase.Build && DuelSoulHome;
			DrawAllySouls();
			if (soulHome)
			{
			}
			else if (!downed)
				DrawSoulMode(frame, phase == Phase.EnemyIntro ? soulAlpha : 1f);

			if (grazeTimer > 0 && !downed)
			{
				float a = grazeTimer / (float)TicksPerFrame / 6f;
				Vector2 gc = SoulCenter;
				if (!DrDraw.Sprite("spr_grazeappear", 0, gc.X, gc.Y, Color.White, 1f, 0f, a))
					DrDraw.Outline(gc.X - GrazeSize / 2f, gc.Y - GrazeSize / 2f, GrazeSize, GrazeSize, Color.White * a, 2);
				else
					DrDraw.Sprite("spr_grazeappear", 3, gc.X, gc.Y, Color.White, 1f, 0f, a - 0.2f);
			}
		}

		private void DrawBoxShape(float scale, float angle, float alpha)
		{
			// Opening into (or out of) a full-screen arena: a plain rectangle that follows Box. Its border fades out
			// as it opens, so a full-screen attack is just black; the arena's edges still stop the SOUL
			if (arenaBlend > 0.01f)
			{
				Rectangle box = Box;
				DrDraw.Rect(box.X, box.Y, box.Width, box.Height, Color.Black * alpha);
				// (A full-screen arena's border fades as it opens; a bigger box keeps its border)
				DrDraw.Outline(box.X, box.Y, box.Width, box.Height, BoxGreen * (alpha * (arenaIsFull ? 1f - arenaBlend : 1f)), 3);
				return;
			}
			float centerX = BoxCenterX, centerY = BoxCenterY;
			bool first = DrDraw.Sprite("spr_battlebg_0", 1, centerX, centerY, BoxGreen, scale, angle, alpha);
			bool second = DrDraw.Sprite("spr_battlebg_0", 0, centerX, centerY, BoxGreen, scale, angle, alpha);
			if (first && second)
				return;

			float size = BoxSize * scale / 2f;
			DrDraw.Rect(centerX - size / 2f, centerY - size / 2f, size, size, Color.Black * alpha);
			DrDraw.Outline(centerX - size / 2f, centerY - size / 2f, size, size, BoxGreen * alpha, 3);
		}

		private void DrawTPBar()
		{
			// obj_tensionbar slides in from x = -40 to 38
			float x = tpBarX, y = 40;
			const int barH = 196;
			float fill(float tension) => MathHelper.Clamp(tension / MaxTension, 0f, 1f) * barH;
			bool maxed = tpCurrent >= MaxTension;

			if (!DrDraw.Sprite("spr_tplogo", 0, x - 30, y + 30, Color.White))
				DrDraw.Text("TP", x - 28, y + 30, Orange, DrDraw.SmallFont);
			if (!DrDraw.Sprite("spr_tensionbar", 1, x, y, Color.White))
				DrDraw.Rect(x, y, 25, barH, new Color(128, 0, 0));

			void bar(float tension, Color col) => DrDraw.Rect(x + 3, y + barH - fill(tension), 22, Math.Max(0, fill(tension) - 1), col);
			Color full = maxed ? MergeColor(new Color(255, 255, 0), Orange, 0.5f) : Orange;
			if (tpApparent < tpCurrent)
			{
				bar(tpCurrent, Color.Red);
				bar(tpApparent, Orange);
			}
			else if (tpApparent > tpCurrent)
			{
				bar(tpApparent, Color.White);
				bar(tpCurrent, full);
			}
			else if (tpCurrent > 0)
			{
				bar(tpCurrent, full);
			}

			// The cost of the highlighted act, flashing
			if (tpPreview > 0)
			{
				float top = y + barH - fill(tpCurrent);
				float bottom = Math.Min(top + fill(tpPreview), y + barH - 1);
				bool enough = tpCurrent >= tpPreview;
				float a = enough ? Math.Abs((float)Math.Sin(time / 16f) * 0.5f) + 0.2f : 0.7f;
				DrDraw.Rect(x + 3, top, 22, bottom - top, (enough ? Color.White : Color.DarkGray) * a);
			}

			if (tpApparent > 20 && tpApparent < MaxTension)
				if (!DrDraw.Sprite("spr_tensionmarker", 0, x + 3, y + barH - fill(tpCurrent), Color.White))
					DrDraw.Rect(x + 3, y + barH - fill(tpCurrent), 19, 2, Color.White);
			if (!DrDraw.Sprite("spr_tensionbar", 0, x, y, Color.White))
				DrDraw.Outline(x, y, 25, barH, Color.White, 2);

			if (maxed)
			{
				Color yel = new(255, 255, 0);
				DrDraw.Text("M", x - 28, y + 70, yel);
				DrDraw.Text("A", x - 24, y + 90, yel);
				DrDraw.Text("X", x - 20, y + 110, yel);
			}
			else
			{
				DrDraw.Text(((int)Math.Floor(tpApparent / MaxTension * 100)).ToString(), x - 30, y + 70, Color.White);
				DrDraw.Text("%", x - 25, y + 95, Color.White);
			}
		}

		/// <param name="bottom">The bottom of the window in battle coordinates: the black panel reaches it even when the
		/// window is taller than 4:3 (or the scale is rounded down and leaves a margin).</param>
		private void DrawPanel(float left, float width, float bottom)
		{
			float top = ScreenHeight - panel;
			DrDraw.Rect(left, top, width, Math.Max(panel + BackgroundBleed + 1, bottom - top), Color.Black);
			DrDraw.Rect(left, top + 34, width, 2, PanelLine);
			if (panel <= 0)
			{
				DrDraw.Rect(left, top - 2, width, 2, PanelLine);
				return;
			}

			// The purple separator goes under the party box so the raised nameplate covers it.
			DrDraw.Rect(left, top - 2, width, 2, PanelLine);
			DrawAllyBoxes(top);
			DrawPartyBox();

			float textY = top + 48; // 376 when the panel is fully up
			// Multiplayer: the others have picked and wait on us; the server goes on without us when this runs out
			int skipIn = Net.BattleNet.SecondsUntilSkip;
			if (skipIn >= 0 && skipIn <= 60 && phase is Phase.Menu or Phase.WeaponSelect or Phase.EnemySelect or Phase.ActSelect or Phase.ItemSelect or Phase.PartySelect or Phase.SummonSelect)
				DrDraw.Text($"The others are waiting: auto-skip in {skipIn}s", 30, ScreenHeight - 18, new Color(255, 220, 64), DrDraw.SmallFont, 0.8f);
			switch (phase)
			{
				case Phase.Waiting when AllyRowsVisible:
					// Others are fighting: their bars (ours comes in when it's our turn)
					DrawAllyFightRows();
					break;
				case Phase.Intro:
				case Phase.Menu:
				case Phase.Message:
				case Phase.Waiting:
				case Phase.DuelWait:
				case Phase.MercyWait:
					DrDraw.Text(text.Substring(0, Math.Min(text.Length, (int)textShown)), 30, textY, Color.White);
					break;
				case Phase.Build:
					DrawBuildPalette();
					break;
				case Phase.MercyPrompt:
					DrawMercyPrompt(textY);
					break;
				case Phase.Talk:
				case Phase.ChoiceWait:
					DrawTalk(textY);
					break;
				case Phase.Choice:
					DrawChoice(textY);
					break;
				case Phase.WeaponSelect:
					DrawWeaponSelect(textY);
					break;
				case Phase.EnemySelect:
					DrawEnemyList(textY);
					break;
				case Phase.ActSelect:
					DrawGrid(textY, currentActs.Select(a => (a.Name, a.TPCost > 0 && Player.GetModPlayer<MercyPlayer>().TP < a.TPCost)).ToList(),
						currentActs.Select(a => a.Color).ToList());
					DrawActInfo(textY);
					break;
				case Phase.PartySelect:
					DrawHealTargets(textY);
					break;
				case Phase.SummonSelect:
					DrawSummonSelect(textY);
					break;
				case Phase.ItemSelect:
					var items = HealingItems();
					DrawItemList(textY, items.Select(i => $"{i.name} x{i.count}").ToList(), items.Select(i => SickLocked(i.type)).ToList());
					if (listIndex < items.Count)
					{
						if (SickLocked(items[listIndex].type))
							DrDraw.Text($"Potion\nsickness:\n{potionSickTurns} turn{(potionSickTurns == 1 ? "" : "s")}", 500, textY, new Color(255, 110, 110), DrDraw.BigFont);
						else
							DrDraw.Text($"Heals\n{items[listIndex].heal} HP", 500, textY, new Color(128, 128, 128), DrDraw.BigFont);
					}
					break;
				case Phase.FightBar:
				case Phase.FightResult:
					DrawFightBar();
					DrawAllyFightRows();
					break;
			}
		}

		private static readonly Color ManaBlue = new(70, 120, 255);

		private void DrawPartyBox()
		{
			Rectangle r = PartyBox;
			bool choosing = phase == Phase.Menu || phase == Phase.WeaponSelect || phase == Phase.EnemySelect || phase == Phase.ActSelect || phase == Phase.ItemSelect || phase == Phase.PartySelect || phase == Phase.SummonSelect;
			float buttonsY = ScreenHeight - panel + 5f;
			float selectionAlpha = choosing ? 1f : MathHelper.Clamp(partyLift / 32f, 0f, 1f);

			if (selectionAlpha > 0.01f)
			{
				// scr_selectionmatrix: sine-eased cyan columns around the button row.
				for (int i = 0; i < 12; i++)
				{
					float angle = selectedBarPhase + i * 10f * MathHelper.Pi;
					float wave = (float)Math.Sin(angle / 60f);
					float alpha = Math.Max(0f, (float)Math.Sin(angle / 60f)) * selectionAlpha;
					DrDraw.Rect(r.X, buttonsY - 8f, 2f, 36f, KrisCyan * alpha);
					DrDraw.Rect(r.Right - 2f, buttonsY - 8f, 2f, 36f, KrisCyan * alpha);
					if (Math.Cos(angle / 60f) < 0f)
					{
						float leftX = r.X + 30f - wave * 30f;
						float rightX = r.Right - 32f + wave * 30f;
						DrDraw.Rect(leftX, buttonsY - 3f, 2f, 31f, KrisCyan * alpha);
						DrDraw.Rect(rightX, buttonsY - 3f, 2f, 31f, KrisCyan * alpha);
					}
				}
			}

			// Draw buttons first, as Deltarune does: the moving nameplate is drawn over them
			// on the way down, so the menu tucks behind the panel as the bullet box opens.
			if (choosing || partyLift > 1f)
			{
				string[] names = { "spr_btfight", "spr_btact", "spr_btitem", "spr_btspare", "spr_btdefend" };
				string[] labels = { "FIGHT", "ACT", "ITEM", "SPARE", "DEFEND" };
				for (int i = 0; i < 5; i++)
				{
					bool itemPhase = phase is Phase.ItemSelect or Phase.PartySelect;
					bool selected = (int)menuChoice == i && phase == Phase.Menu || (int)pendingChoice == i && phase != Phase.Menu && !itemPhase
						|| i == (int)Choice.Item && itemPhase;
					float bx = r.X + 15 + 35 * i;
					if (!DrDraw.Sprite(names[i], selected ? 1 : 0, bx, buttonsY, Color.White))
					{
						DrDraw.Outline(bx, buttonsY, 31, 32, selected ? new Color(255, 255, 0) : Orange, 2);
						DrDraw.Text(labels[i].Substring(0, 1), bx + 10, buttonsY + 8, selected ? new Color(255, 255, 0) : Orange, DrDraw.SmallFont);
					}
					// Something can be spared (SPARE) or PACIFY would work on a TIRED enemy (ACT): the button pulses
					// white, its name showing under it, like Deltarune's
					bool glow = i == (int)Choice.Spare && LivingEnemies.Any(e => e.E.Mercy >= 100f)
						|| i == (int)Choice.Act && LivingEnemies.Any(e => e.E.Tired) && duelWith < 0;
					if (glow)
						DrawButtonGlow(names[i], labels[i], bx, buttonsY);
				}
			}

			// The outer frame tracks the nameplate. Every edge uses the same 2 px as the button-row columns,
			// and the divider is the nameplate's bottom edge, so it covers the button row as the box drops.
			const float edge = 2f;
			// In a party every box keeps a faint frame in its player's colour, ours too
			Color frame = KrisCyan * Math.Max(selectionAlpha, Net.BattleNet.InParty ? 0.55f : 0f);
			DrDraw.Rect(r.X, r.Y, r.Width, 34f, Color.Black);
			DrDraw.Rect(r.X, r.Y - edge, r.Width, edge, frame);
			DrDraw.Rect(r.X, r.Y - edge, edge, 34f + edge, frame);
			DrDraw.Rect(r.Right - edge, r.Y - edge, edge, 34f + edge, frame);
			DrDraw.Rect(r.X, r.Y + 34f - edge, r.Width, edge, frame);

			// The chosen command's icon (spr_headkris frames: sword, ACT waves, bag, shield, X), centred where
			// the head sits; otherwise the player's own head, enlarged and shifted left to clear the name.
			if (faceAction == FaceNone || !DrDraw.Sprite("spr_headkris", faceAction, r.X + 9, r.Y + 4, Color.White))
				DrawPlayerHead(new Vector2(r.X + 22, r.Y + 15), 1f);
			// Long names shrink to fit before the HP label (and are cut short if even that isn't enough)
			const float nameScale = 0.68f, nameMinScale = 0.4f, nameRoom = 60f;
			string name = Player.name.ToUpperInvariant();
			float nameWidth = Math.Max(1f, DrDraw.Measure(name, DrDraw.BigFont));
			float scale = MathHelper.Clamp(nameRoom / nameWidth, nameMinScale, nameScale);
			while (name.Length > 1 && DrDraw.Measure(name, DrDraw.BigFont) * scale > nameRoom)
				name = name.Substring(0, name.Length - 1);
			float nameY = r.Y + 7 + DrDraw.LineHeight(DrDraw.BigFont) * (nameScale - scale) / 2f;
			DrDraw.Text(name, r.X + 46, nameY, Color.White, DrDraw.BigFont, scale);
			// Two rows, HP and MP: label, bar, then the numbers to the right of the bar in one shared size (as big as
			// fits the end of the nameplate)
			const int labelX = 112, barX = 130, barWidth = 28, numberX = barX + barWidth + 3, numberRoom = 208 - numberX;
			const int hpBarY = 6, hpBarHeight = 12;
			// Right under the HP bar
			const int manaBarY = hpBarY + hpBarHeight + 3, manaBarHeight = 6;
			// The small font at (nearly) its own pixel size reads better here than the big one shrunk down
			const float maxNumberScale = 1f;
			bool hasMana = Player.statManaMax2 > 0;

			float ratio = MathHelper.Clamp(ShownLife / (float)Player.statLifeMax2, 0f, 1f);
			string hp = $"{ShownLife}/{Player.statLifeMax2}";
			string mp = $"{Player.statMana}/{Player.statManaMax2}";
			float widest = Math.Max(DrDraw.Measure(hp, DrDraw.SmallFont), hasMana ? DrDraw.Measure(mp, DrDraw.SmallFont) : 0f);
			float numberScale = Math.Min(maxNumberScale, numberRoom / Math.Max(1f, widest));
			float numberHeight = DrDraw.LineHeight(DrDraw.SmallFont) * numberScale;

			if (!DrDraw.Sprite("spr_hpname", 0, r.X + labelX, r.Y + hpBarY + 2, Color.White))
				DrDraw.Text("HP", r.X + labelX - 4, r.Y + hpBarY - 3, Color.White, DrDraw.SmallFont);
			DrDraw.Rect(r.X + barX, r.Y + hpBarY, barWidth, hpBarHeight, new Color(128, 0, 0));
			DrDraw.Rect(r.X + barX, r.Y + hpBarY, (float)Math.Ceiling(ratio * barWidth), hpBarHeight, KrisCyan);
			Color hpColor = ShownLife <= 0 ? new Color(255, 40, 40) : ratio <= 0.25f ? new Color(255, 255, 0) : Color.White;
			DrDraw.Text(hp, r.X + numberX, r.Y + hpBarY + hpBarHeight / 2f - numberHeight / 2f, hpColor, DrDraw.SmallFont, numberScale);

			// Mana (magic weapons spend it per hit), blue like Terraria's
			if (hasMana)
			{
				float mana = MathHelper.Clamp(Player.statMana / (float)Player.statManaMax2, 0f, 1f);
				const float mpLabelScale = 0.8f;
				float mpLabelHeight = DrDraw.LineHeight(DrDraw.SmallFont) * mpLabelScale;
				DrDraw.Text("MP", r.X + labelX + 1, r.Y + manaBarY + manaBarHeight / 2f - mpLabelHeight / 2f, ManaBlue, DrDraw.SmallFont, mpLabelScale);
				DrDraw.Rect(r.X + barX, r.Y + manaBarY, barWidth, manaBarHeight, new Color(20, 28, 90));
				DrDraw.Rect(r.X + barX, r.Y + manaBarY, (float)Math.Ceiling(mana * barWidth), manaBarHeight, ManaBlue);
				DrDraw.Text(mp, r.X + numberX, r.Y + manaBarY + manaBarHeight / 2f - numberHeight / 2f, ManaBlue, DrDraw.SmallFont, numberScale);
			}
		}

		private void DrawHeartCursor(float x, float y)
		{
			if (!DrDraw.Sprite("spr_heart", 0, x, y, Color.White))
				DrDraw.HeartShapeAt(x, y, 16, Color.Red);
		}

		/// <summary>Rows of the enemy list shown at once; longer lists (a boss's parts) scroll.</summary>
		private const int EnemyListRows = 3;

		private void DrawEnemyList(float y)
		{
			List<TargetRow> rows = TargetRows();
			int cursor = Math.Clamp(listIndex, 0, Math.Max(0, rows.Count - 1));
			int first = Math.Clamp(cursor - EnemyListRows + 1, 0, Math.Max(0, rows.Count - EnemyListRows));
			DrawHeartCursor(55, y + 10 + (cursor - first) * 30);

			// HP and MERCY columns like Deltarune's enemy list, one row per enemy (or per part)
			DrDraw.Text("HP", 424, y - 14, Color.White, DrDraw.SmallFont);
			DrDraw.Text("MERCY", 524, y - 14, Color.White, DrDraw.SmallFont);
			for (int i = first; i < Math.Min(rows.Count, first + EnemyListRows); i++)
			{
				TargetRow row = rows[i];
				Encounter e = row.Enemy.E;
				float rowY = y + (i - first) * 30;
				bool spareable = e.Mercy >= 100f;
				string name = row.Part != null ? e.PartName(row.Part) : e.Name;
				// Yellow: can be spared. Blue: TIRED. Both: yellow fading into blue, like Deltarune
				Color nameColor = row.Locked ? new Color(128, 128, 128) : spareable ? new Color(255, 255, 0) : e.Tired ? TiredBlue : Color.White;
				if (!row.Locked && spareable && e.Tired)
					DrDraw.GradientText(name, 80, rowY, new Color(255, 255, 0), TiredBlue);
				else
					DrDraw.Text(name, 80, rowY, nameColor);
				// Deltarune's marks after the name and a space: a star when it can be spared, then z's and a gray
				// "(Tired)" right after them when it's TIRED
				if (!row.Locked && (spareable || e.Tired))
				{
					float mx = 80 + DrDraw.Measure(name + " ", DrDraw.BigFont) + 8;
					if (spareable)
						mx += DrawSpareMark(mx, rowY + 4) + 2;
					if (e.Tired)
					{
						mx += DrawTiredMark(mx, rowY + 6);
						DrDraw.Text("(Tired)", mx, rowY, new Color(128, 128, 128));
					}
				}
				float hp = row.Part != null
					? MathHelper.Clamp(row.Part.life / (float)Math.Max(1, row.Part.lifeMax), 0f, 1f)
					: MathHelper.Clamp(e.LifeRatio, 0f, 1f);
				DrDraw.Rect(420, rowY + 5, 81, 16, new Color(128, 0, 0));
				DrDraw.Rect(420, rowY + 5, (float)Math.Ceiling(hp * 81), 16, row.Locked ? new Color(110, 110, 110) : new Color(0, 255, 0));
				DrDraw.Text(row.Locked ? "GUARDED" : $"{(int)Math.Ceiling(hp * 100)}%", 424, rowY + 5, Color.White, DrDraw.SmallFont);
				float mercy = MathHelper.Clamp(e.Mercy / 100f, 0f, 1f);
				// A boss can't be spared: no MERCY bar for it
				if (e.IsBoss)
				{
					DrDraw.Text("---", 524, rowY + 5, new Color(128, 128, 128), DrDraw.SmallFont);
					continue;
				}
				DrDraw.Rect(520, rowY + 5, 81, 16, new Color(255, 80, 32));
				DrDraw.Rect(520, rowY + 5, (float)Math.Ceiling(mercy * 81), 16, new Color(255, 255, 0));
				DrDraw.Text($"{(int)e.Mercy}%", 524, rowY + 5, new Color(128, 0, 0), DrDraw.SmallFont);
			}
			// More rows above or below
			if (first > 0)
				DrDraw.Text("^", 60, y - 14, Color.White, DrDraw.SmallFont);
			if (first + EnemyListRows < rows.Count)
				DrDraw.Text("v", 60, y + EnemyListRows * 30 - 6, Color.White, DrDraw.SmallFont);
		}

		private void DrawGrid(float y, List<(string name, bool greyed)> entries, List<Color?> colors = null)
		{
			for (int i = 0; i < entries.Count; i++)
			{
				float x = i % 2 == 0 ? 80 : 300;
				float ey = y + (i / 2) * 30;
				Color c = entries[i].greyed ? new Color(128, 128, 128) : colors != null && i < colors.Count && colors[i] is Color own ? own : Color.White;
				DrDraw.Text(entries[i].name, x, ey, c);
				// PACIFY that would work gets the TIRED mark beside it
				if (colors != null && i < colors.Count && colors[i] == TiredBlue && !entries[i].greyed)
					DrawTiredMark(x + DrDraw.Measure(entries[i].name, DrDraw.BigFont) + 6, ey + 2);
				if (i == listIndex)
					DrawHeartCursor(x - 25, ey + 10);
			}
		}

		private void DrawItemList(float y, List<string> names, List<bool> locked = null)
		{
			const int rows = 3;
			int first = Math.Clamp(listIndex - rows + 1, 0, Math.Max(0, names.Count - rows));
			for (int i = first; i < Math.Min(names.Count, first + rows); i++)
			{
				float ey = y + (i - first) * 30;
				bool off = locked != null && i < locked.Count && locked[i];
				DrDraw.Text(names[i], 80, ey, off ? new Color(128, 128, 128) : Color.White);
				if (i == listIndex)
					DrawHeartCursor(55, ey + 10);
			}
			// Scroll arrows when there are more items than rows
			if (first > 0)
				DrDraw.Text("^", 470, y - 4, Color.White, DrDraw.SmallFont);
			if (first + rows < names.Count)
				DrDraw.Text("v", 470, y + 70, Color.White, DrDraw.SmallFont);
		}

		private void DrawActInfo(float y)
		{
			if (listIndex >= currentActs.Count)
				return;
			ActOption act = currentActs[listIndex];
			DrDraw.Text(act.Description, 500, y, new Color(128, 128, 128));
			if (act.TPCost > 0)
				DrDraw.Text($"{(int)act.TPCost}% TP", 500, y + 60, Orange);
		}


		/// <summary>The player's head portrait centred on a point (a cyan heart until it's rendered).</summary>
		private void DrawPlayerHead(Vector2 center, float alpha)
		{
			if (playerHeadPortrait != null && RightFacingHead.Usable(playerHeadPortrait))
			{
				DrDraw.Sb.Draw(playerHeadPortrait.GetTarget(), center, null,
					Color.Lerp(KrisCyan, Color.White, 0.45f) * alpha, 0f, new Vector2(42f), 0.82f, SpriteEffects.None, 0f);
			}
			else
			{
				DrDraw.HeartShapeAt(center.X - 6f, center.Y - 10f, 17, new Color(64, 220, 255) * alpha);
			}
		}

		private void DrawFightBar()
		{
			float x = FightBarX, y = BarY;
			float alpha = 1f - MathHelper.Clamp(fightFade, 0f, 1f);
			// Deltarune's bar is blue; in a party it's in our colour, like the others' rows
			Color blue = (Net.BattleNet.InParty ? KrisCyan : new Color(0, 0, 255)) * alpha;
			// spr_pressfront is Kris's head + "Z" (75x38): keep the Z, put the player's own head where Kris's was
			// Always the Z: some chapters' spr_pressfront says PRESS instead (chapter 5 has none, so it came from an older one)
			DrDraw.Text("Z", x + 50, y + 4, KrisCyan * alpha);
			DrawPlayerHead(new Vector2(x + 19, y + 20), alpha);
			DrDraw.Outline(x + 78, y, FightBoxWidth + 3, 37, blue);
			DrDraw.Outline(x + 79, y + 1, FightBoxWidth + 1, 35, blue);
			// Guns have no line to hit, so no press spot
			// In a party the press spot takes our colour too, like the others' rows
			if (!gunMode && !beamMode && !DrDraw.Sprite("spr_pressspot", 0, x + 80, y, Net.BattleNet.InParty ? KrisCyan : Color.White, 1f, 0f, alpha))
				DrDraw.Rect(x + 80, y, 10, 38, new Color(0, 0, 255) * alpha);

			if (gunMode)
				DrawGunBar(x, y, alpha);
			if (beamMode)
				DrawBeamBar(x, y, alpha);
			// The ghosts the bolts left behind (they stay where they were left and fade)
			foreach (BoltGhost g in boltGhosts)
			{
				if (gunMode || beamMode)
					break;
				float gx = x + 80 + g.Ahead * BoltSpeed;
				if (gx > x + 80 + FightBoxWidth + 4)
					continue;
				float a = g.Alpha * alpha;
				if (!DrDraw.Sprite("spr_attackspot", 0, gx, y, Color.White, 1f, 0f, a))
					DrDraw.Rect(gx + 2, y, 6, 38, Color.White * a);
			}
			foreach (FightBolt bolt in bolts)
			{
				if (gunMode || beamMode)
					break;
				if (!bolt.Alive)
					continue;
				float ahead = bolt.Frame - boltX;
				float bx = x + 80 + ahead * BoltSpeed;
				// Bolts further back start off the right of the bar; only draw them once they're on it
				if (bx > x + 80 + FightBoxWidth + 4)
					continue;
				// Fades with the bar (bolts still on it when the last enemy falls go with it)
				float boltAlpha = (ahead < 0 ? 1f + ahead / 3f : 1f) * alpha;
				if (!DrDraw.Sprite("spr_attackspot", 0, bx, y, Color.White, 1f, 0f, boltAlpha))
					DrDraw.Rect(bx + 2, y, 6, 38, Color.White * boltAlpha);
			}

			foreach (BoltBurst burst in boltBursts)
			{
				// obj_burstbolt: grows and fades; yellow for a perfect hit
				float t = 1f - burst.Timer / 20f;
				Color c = burst.Perfect ? new Color(255, 255, 0) : MergeColor(KrisCyan, Color.White, 0.5f);
				Vector2 sc = new(1f + t * 2f, 1f + t * 0.5f);
				if (!DrDraw.Sprite("spr_attackspot", 0, burst.Position.X - 5 * (sc.X - 1), burst.Position.Y - 19 * (sc.Y - 1), c, sc, 0f, (1f - t) * alpha))
					DrDraw.Rect(burst.Position.X, burst.Position.Y, 10 * sc.X, 38 * sc.Y, c * ((1f - t) * alpha));
			}
		}
	}
}
