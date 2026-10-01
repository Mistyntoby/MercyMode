using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
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
		public enum Phase { None, Intro, Menu, EnemySelect, ActSelect, ItemSelect, FightBar, FightResult, Message, EnemyIntro, EnemyTurn, EnemyOutro, Outro }
		private enum Choice { Fight, Act, Item, Spare, Defend }

		public static BattleSystem Instance => ModContent.GetInstance<BattleSystem>();
		public static bool Active => Instance != null && Instance.phase != Phase.None;
		/// <summary>True while the battle itself is hurting the player, so other damage stays blocked.</summary>
		public static bool HurtingPlayer;
		public static bool Defending => Active && Instance.defending;

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
			public int Age;
		}
		private readonly List<BoxAfterimage> boxAfterimages = new();

		// fight
		private float boltX; // frames since the bar appeared (boltx)
		private bool boltAlive;
		private int boltPoints = -1;
		private Vector2 burstPos;
		private int burstTimer;
		private float fightFade;
		private int slashTimer = -1;
		private int enemyShake;
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
		private int attackPending = -1;
		private float soulAlpha = 1f;
		private float partyLift;
		private float musicVolumeCurrent;
		private PlayerHeadDrawRenderTargetContent playerHeadPortrait;

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
			if (Active || !MercyMode.IsSingleplayer || player.whoAmI != Main.myPlayer || player.dead)
				return false;
			var config = ModContent.GetInstance<MercyConfig>();
			if (config != null && !config.TurnBasedBattles)
				return false;
			if (!ignoreGrace && Main.GameUpdateCount - Instance.lastEndTick < GraceTicks && Instance.lastEndTick != 0)
				return false;
			return EncounterRegistry.Eligible(EncounterRegistry.ResolveRoot(npc));
		}

		public static void TryStart(NPC npc, Player player, string reason = "")
		{
			bool command = reason == "command";
			if (CanStart(npc, player, command))
				Instance.Start(EncounterRegistry.ResolveRoot(npc), reason);
		}

		private void Start(NPC root, string reason)
		{
			boss = root;
			encounter = EncounterRegistry.Create(root);
			time = 0;
			battleOver = false;
			defending = false;
			menuChoice = Choice.Fight;
			Bullets.Clear();
			boxAfterimages.Clear();
			messages.Clear();
			effects.Clear();
			enemyOverride = null;
			enemySnap = default;
			SetHeroPose(HeroPose.Idle);
			hurtTimer = -1;
			shake = 0;
			pendingHealFx = -1;
			usedItemType = 0;
			attackPending = -1;
			// The party and the enemy fly in from where they stood in the world
			CaptureWorldPositions(root);
			enemyWorldRotation = root.rotation;
			tpBarX = -40f;
			tpBarSpeed = 0f;
			panel = 0;
			selectedBarPhase = 0f;
			partyLift = 0f;
			panelDir = 0;
			screenFade = 0;
			slashTimer = -1;
			enemyShake = 0;
			enemyAttackEnergy = 0f;
			enemyAttackDirection = Vector2.Zero;
			patternSoundCooldown = 0;
			musicVolumeCurrent = 0f;
			musicStarted = false;
			var mp = Player.GetModPlayer<MercyPlayer>();
			tpApparent = tpCurrent = mp.TP / TensionToTP;

			npcVelocities.Clear();
			foreach (NPC n in Main.ActiveNPCs)
				npcVelocities[n.whoAmI] = (n.type, n.velocity);
			projVelocities.Clear();
			foreach (Projectile p in Main.ActiveProjectiles)
				projVelocities[p.whoAmI] = (p.type, p.velocity);
			playerPosition = Player.position;
			battleLife = Player.statLife;
			CreatePlayerHeadPortrait();

			if (DeltaruneAssets.BattleMusic != null)
			{
				music = DeltaruneAssets.BattleMusic.CreateInstance();
				music.IsLooped = true;
				music.Volume = 0f;
			}

			SetText(encounter.EncounterText);
			SetPhase(Phase.Intro);
			Mod.Logger.Info($"Battle started with {boss.FullName} as {encounter.GetType().Name} ({encounter.Life}/{encounter.LifeMax} HP) by {reason}");
		}

		private void End()
		{
			Mod.Logger.Info($"Battle ended (enemy alive: {encounter?.Alive}, player dead: {Player.dead})");
			lastEndTick = (uint)Main.GameUpdateCount;
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

			music?.Stop();
			music?.Dispose();
			music = null;
			ReleasePlayerHeadPortrait();
			Bullets.Clear();
			phase = Phase.None;
			encounter = null;
			boss = null;

			// A moment of mercy so the boss can't hit you the instant the world unfreezes
			if (!Player.dead)
			{
				Player.immune = true;
				Player.immuneTime = Math.Max(Player.immuneTime, 60);
			}
		}

		private void CreatePlayerHeadPortrait()
		{
			playerHeadPortrait ??= new PlayerHeadDrawRenderTargetContent();
			playerHeadPortrait.UsePlayer(Player);
			playerHeadPortrait.UseColor(KrisCyan);
			if (!Main.ContentThatNeedsRenderTargets.Contains(playerHeadPortrait))
				Main.ContentThatNeedsRenderTargets.Add(playerHeadPortrait);
			playerHeadPortrait.Request();
		}

		private void ReleasePlayerHeadPortrait()
		{
			if (playerHeadPortrait == null)
				return;

			Main.ContentThatNeedsRenderTargets.Remove(playerHeadPortrait);
			playerHeadPortrait.GetTarget()?.Dispose();
			playerHeadPortrait.Reset();
			playerHeadPortrait = null;
		}

		public override void OnWorldUnload()
		{
			ReleasePlayerHeadPortrait();
			if (phase != Phase.None)
			{
				music?.Stop();
				music?.Dispose();
				music = null;
				phase = Phase.None;
				encounter = null;
				boss = null;
				Bullets.Clear();
			}
		}

		private void SetPhase(Phase p)
		{
			phase = p;
			phaseTicks = 0;
			Mod.Logger.Debug($"Battle phase {p} (turn {encounter?.Turn}, boss {encounter?.Life}/{encounter?.LifeMax}, mercy {encounter?.Mercy:0}, TP {Player.GetModPlayer<MercyPlayer>().TP:0.0}, HP {Player.statLife})");
		}

		private void SetText(string s)
		{
			text = DrDraw.Wrap(s, 570f);
			textShown = 0;
			textSoundedThrough = 0;
		}

		/// <summary>Shows text boxes one after another (Z to continue), then runs <paramref name="then"/>.</summary>
		private void ShowMessages(IEnumerable<string> lines, Action then)
		{
			messages.Clear();
			foreach (string l in lines)
				messages.Enqueue(l);
			afterMessages = then;
			if (messages.Count == 0)
			{
				then();
				return;
			}
			SetText(messages.Dequeue());
			SetPhase(Phase.Message);
		}

		// ================================================================== update

		private int queuedNpc = -1;
		private int queuedTicks;

		/// <summary>Starts a battle with this NPC after a few ticks (test command).</summary>
		public static void QueueStart(NPC npc, int ticks)
		{
			Instance.queuedNpc = npc.whoAmI;
			Instance.queuedTicks = ticks;
		}

		public override void PostUpdateEverything()
		{
			if (queuedNpc >= 0 && --queuedTicks <= 0)
			{
				NPC q = Main.npc[queuedNpc];
				queuedNpc = -1;
				if (q.active)
					TryStart(q, Player, "command");
			}
			if (phase == Phase.None)
				return;
			playerHeadPortrait?.Request();

			if (boss == null || Player.dead)
			{
				End();
				return;
			}
			// Enemy gone without us ending the battle (despawned, killed some other way)
			if (!encounter.Alive && phase != Phase.Outro && phase != Phase.Message && phase != Phase.FightResult)
			{
				battleOver = true;
				StartOutro();
			}

			time++;
			phaseTicks++;
			if (music != null && musicStarted && phase != Phase.Outro)
			{
				musicVolumeCurrent = MathHelper.Lerp(musicVolumeCurrent, DeltaruneAssets.BattleMusicVolume, 0.08f);
				music.Volume = musicVolumeCurrent;
			}

			// Hold the player in place, no falling or fall damage
			Player.position = playerPosition;
			Player.velocity = Vector2.Zero;
			Player.fallStart = (int)(Player.position.Y / 16f);
			Player.statLife = Math.Min(battleLife, Player.statLifeMax2);
			Player.lifeRegenCount = 0;

			if (time % TicksPerFrame == 0)
			{
				UpdateHudFrame();
				UpdateHeroFrame();
			}
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
			if (enemyShake > 0)
				enemyShake--;
			if (patternSoundCooldown > 0)
				patternSoundCooldown--;
			if (burstTimer > 0)
				burstTimer--;
			if (slashTimer >= 0 && ++slashTimer > 20)
				slashTimer = -1;

			switch (phase)
			{
				case Phase.Intro: UpdateIntro(); break;
				case Phase.Menu: UpdateMenu(); break;
				case Phase.EnemySelect: UpdateEnemySelect(); break;
				case Phase.ActSelect: UpdateActSelect(); break;
				case Phase.ItemSelect: UpdateItemSelect(); break;
				case Phase.FightBar: UpdateFightBar(); break;
				case Phase.FightResult: UpdateFightResult(); break;
				case Phase.Message: UpdateMessage(); break;
				case Phase.EnemyIntro: UpdateEnemyIntro(); break;
				case Phase.EnemyTurn: UpdateEnemyTurn(); break;
				case Phase.EnemyOutro: UpdateEnemyOutro(); break;
				case Phase.Outro: UpdateOutro(); break;
			}
		}

		/// <summary>The parts of obj_battlecontroller / obj_tensionbar that count in Deltarune frames.</summary>
		private float tpBarX = -40f, tpBarSpeed;
		private float enemyWorldRotation;

		private void UpdateHudFrame()
		{
			// obj_tensionbar: x = -40, hspeed 13, friction 1 -> stops at 38. Slides back out at the end.
			if (panelDir < 0)
			{
				tpBarX = Math.Max(-40f, tpBarX - 13f);
			}
			else if (tpBarSpeed > 0f)
			{
				tpBarSpeed = Math.Max(0f, tpBarSpeed - 1f);
				tpBarX += tpBarSpeed;
			}
			// Damp the panel toward its target instead of stepping 30 pixels per frame.
			float panelTarget = panelDir > 0 ? PanelHeight : panelDir < 0 ? 0f : panel;
			float panelEase = panelDir < 0 ? 0.68f : 0.5f;
			panel = MathHelper.Lerp(panel, panelTarget, panelEase);
			if (Math.Abs(panelTarget - panel) < 0.75f)
				panel = panelTarget;
			panel = MathHelper.Clamp(panel, 0f, PanelHeight);
			float liftTarget = phase is Phase.Menu or Phase.EnemySelect or Phase.ActSelect or Phase.ItemSelect ? 32f : 0f;
			partyLift = MathHelper.Lerp(partyLift, liftTarget, liftTarget == 0f ? 0.68f : 0.5f);
			if (Math.Abs(liftTarget - partyLift) < 0.5f)
				partyLift = liftTarget;
			selectedBarPhase += 2f;

			// TP bar: apparent jumps 20 at a time, current catches up after a short delay
			float tension = Player.GetModPlayer<MercyPlayer>().TP / TensionToTP;
			if (Math.Abs(tpApparent - tension) < 20)
				tpApparent = tension;
			else
				tpApparent += tpApparent < tension ? 20 : -20;
			float d = tpApparent - tpCurrent;
			if (d != 0)
			{
				float step = 2 + (Math.Abs(d) > 10 ? 2 : 0) + (Math.Abs(d) > 25 ? 3 : 0) + (Math.Abs(d) > 50 ? 4 : 0) + (Math.Abs(d) > 100 ? 5 : 0);
				tpCurrent += Math.Sign(d) * step;
				if (Math.Abs(tpApparent - tpCurrent) < 3)
					tpCurrent = tpApparent;
			}

			// Step toward the target without overshooting (stepping past it made the veil and enemy flicker 0.9/1.0)
			float fadeTarget = phase == Phase.Outro ? 0f : 1f;
			if (screenFade < fadeTarget)
				screenFade = Math.Min(fadeTarget, screenFade + 0.1f);
			else if (screenFade > fadeTarget)
				screenFade = Math.Max(fadeTarget, screenFade - 0.1f);
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
		private static bool Pressed(Keys k) => !InputBlocked && Main.keyState.IsKeyDown(k) && !Main.oldKeyState.IsKeyDown(k);
		private static bool Held(Keys k) => !InputBlocked && Main.keyState.IsKeyDown(k);
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
			if (phaseTicks == IntroSwingAt)
			{
				SetHeroPose(HeroPose.Attack);
				Sfx("weaponpull");
			}
			// 3. after the swing, the bottom UI glides up and the TP bar slides in (hspeed 13, friction 1)
			if (phaseTicks == IntroPanelAt)
			{
				panelDir = 1;
				tpBarSpeed = 13f;
				SetHeroPose(HeroPose.Idle);
				if (music != null)
				{
					music.Play();
					musicStarted = true;
				}
			}
			textShown = 0; // the encounter text types out once the panel is up
			if (panel >= PanelHeight && phaseTicks > IntroPanelAt)
				BeginPlayerTurn(keepText: true);
		}

		private void BeginPlayerTurn(bool keepText = false)
		{
			defending = false;
			if (heroPose == HeroPose.Defend)
				SetHeroPose(HeroPose.Idle);
			tpPreview = 0;
			if (!keepText)
				SetText(encounter.FlavorText());
			SetPhase(Phase.Menu);
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
				case Choice.Act:
				case Choice.Spare:
					Sfx("select");
					pendingChoice = menuChoice;
					listIndex = 0;
					SetPhase(Phase.EnemySelect);
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
					DoDefend();
					break;
			}
		}

		private void UpdateEnemySelect()
		{
			if (Cancel)
			{
				SetPhase(Phase.Menu);
				return;
			}
			if (!Confirm)
				return;
			Sfx("select");
			switch (pendingChoice)
			{
				case Choice.Fight:
					StartFightBar();
					break;
				case Choice.Act:
					listIndex = 0;
					SetPhase(Phase.ActSelect);
					break;
				case Choice.Spare:
					DoSpare();
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
				SetPhase(Phase.EnemySelect);
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
			mp.TP -= act.TPCost;
			lastHeal = -1;
			List<string> lines = act.Run(this);
			SetHeroPose(HeroPose.Act);
			if (lastHeal >= 0)
				QueueHealFx(lastHeal, 8);
			ShowMessages(lines, StartEnemyTurn);
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
			for (int i = 0; i < 50; i++)
			{
				Item item = Player.inventory[i];
				if (item.type != type || item.IsAir)
					continue;
				heal = Player.GetHealLife(item, true);
				item.stack--;
				if (item.stack <= 0)
					item.TurnToAir();
				break;
			}

			int healed = HealPlayer(heal);
			Sfx("heal");
			// obj_heroparent state 4: the item is used 15 frames into the animation
			usedItemType = type;
			SetHeroPose(HeroPose.Item);
			QueueHealFx(healed, 15);

			string result = Player.statLife >= Player.statLifeMax2 ? "* Your HP was maxed out." : $"* You recovered {healed} HP!";
			ShowMessages(new[] { $"* {Player.name} used the {name}!\n{result}" }, StartEnemyTurn);
		}

		// ---- SPARE / DEFEND ----

		private void DoSpare()
		{
			string spared = $"* {Player.name} spared {encounter.Name}!";
			if (encounter.Mercy >= 100f)
			{
				battleOver = true;
				PlayEnemySpared();
				encounter.Spare();
				SetHeroPose(HeroPose.Victory);
				ShowMessages(new[] { spared }, StartOutro);
				return;
			}
			SetHeroPose(HeroPose.Act);
			ShowMessages(new[] { spared + "\n* But its name wasn't YELLOW..." }, StartEnemyTurn);
		}

		private void DoDefend()
		{
			defending = true;
			SetHeroPose(HeroPose.Defend);
			var mp = Player.GetModPlayer<MercyPlayer>();
			mp.TP = Math.Min(100f, mp.TP + DefendTension * TensionToTP);
			Sfx("boost");
			StartEnemyTurn();
		}

		// ---- FIGHT ----

		private void StartFightBar()
		{
			boltX = 0;
			boltAlive = true;
			boltPoints = -1;
			fightFade = 0;
			SetPhase(Phase.FightBar);
		}

		private void UpdateFightBar()
		{
			boltX += 1f / TicksPerFrame;
			// Deltarune checks presses once per frame, so score on the frame this tick belongs to
			int close = BoltStartFrame - (int)Math.Floor(boltX);

			if (boltAlive && Confirm && close < BoltWindowEarly && close > -BoltWindowLate)
			{
				boltAlive = false;
				boltPoints = BoltPoints(close);
				burstPos = new Vector2(FightBarX + 80 + (BoltStartFrame - boltX) * BoltSpeed, FightBarY);
				burstTimer = 20;
				StartHeroAttack();
				return;
			}
			if (boltAlive && BoltStartFrame - boltX < -BoltWindowLate)
			{
				boltAlive = false;
				boltPoints = 0;
				StartHeroAttack();
			}
		}

		/// <summary>Weapon damage stands in for Kris's AT: the held weapon, or the best one in the hotbar.</summary>
		public int AttackStat()
		{
			Item held = Player.HeldItem;
			if (!held.IsAir && held.damage > 0 && held.useStyle != ItemUseStyleID.None && !held.accessory)
				return Math.Max(1, Player.GetWeaponDamage(held));
			int best = 0;
			for (int i = 0; i < 10; i++)
			{
				Item it = Player.inventory[i];
				if (!it.IsAir && it.damage > 0 && !it.accessory && it.ammo == AmmoID.None)
					best = Math.Max(best, Player.GetWeaponDamage(it));
			}
			return Math.Max(5, best);
		}

		/// <summary>obj_heroparent state 1: the swing starts now, the hit lands 10 frames later (alarm[1] = 10).</summary>
		private void StartHeroAttack()
		{
			SetHeroPose(HeroPose.Attack);
			attackPending = 10 * TicksPerFrame;
			Sfx("slash");
			if (boltPoints == 150)
			{
				Sfx("crit");
				for (int i = 0; i < 3; i++)
					AddEffect(new CritSparkle(new Vector2(HeroX + 68 + Main.rand.NextFloat(50f), HeroY + 30 + Main.rand.NextFloat(30f))));
			}
			SetPhase(Phase.FightResult);
		}

		/// <summary>The colour of damage the hero deals: merge_color(c_aqua, c_white, 0.5).</summary>
		private static readonly Color HeroDamageColor = new(128, 255, 255);

		private void ResolveAttack()
		{
			if (boltPoints <= 0)
			{
				EnemyNumber(0, HeroDamageColor, DamageNumber.MissFrame);
				return;
			}

			var config = ModContent.GetInstance<MercyConfig>();
			float mult = config?.FightDamageMultiplier ?? 1f;
			int raw = (int)Math.Round(AttackStat() * boltPoints / DamagePointsDivisor * mult);
			NPC target = encounter.StrikeTarget();
			int dealt = target.SimpleStrikeNPC(raw, Player.direction, crit: false, knockBack: 0f);

			Sfx("damage");
			slashTimer = 0;
			enemyShake = 18;
			if (dealt > 0)
				EnemyNumber(dealt, HeroDamageColor);
			else
				EnemyNumber(0, HeroDamageColor, DamageNumber.MissFrame);

			// A killing blow: the enemy breaks apart right away (obj_deathanim), from how it looked a moment ago
			if (!encounter.Alive)
				PlayEnemyDeath();

			if (dealt > 0)
			{
				var mp = Player.GetModPlayer<MercyPlayer>();
				mp.TP = Math.Min(100f, mp.TP + (float)Math.Round(boltPoints / HitTensionDivisor) * TensionToTP);
			}
		}

		private void UpdateFightResult()
		{
			if (attackPending > 0 && --attackPending == 0)
			{
				attackPending = -1;
				ResolveAttack();
			}
			if (attackPending > 0)
				return;
			if (phaseTicks > FightPostTicks)
				fightFade += FightFadePerTick;
			if (fightFade < 1f)
				return;

			if (!encounter.Alive)
			{
				battleOver = true;
				SetHeroPose(HeroPose.Victory);
				ShowMessages(new[] { $"* YOU WON!\n* {encounter.Name} was defeated." }, StartOutro);
				return;
			}
			StartEnemyTurn();
		}

		// ---- text boxes ----

		private void UpdateMessage()
		{
			if (Cancel)
				textShown = text.Length;
			if (textShown < text.Length || !Confirm)
				return;
			if (messages.Count > 0)
			{
				SetText(messages.Dequeue());
				return;
			}
			afterMessages?.Invoke();
		}

		// ---- enemy turn ----

		private void StartEnemyTurn()
		{
			if (battleOver)
			{
				StartOutro();
				return;
			}
			Bullets.Clear();
			boxAfterimages.Clear();
			enemyAttackEnergy = 0f;
			enemyAttackDirection = Vector2.Zero;
			attack = encounter.NextAttack(this);
			encounter.Turn++;
			turnTimer = attack.Duration;
			boxTimer = 0;
			text = "";
			// scr_moveheart: the SOUL bursts out of the hero and flies to the box in 8 frames
			soulFrom = HeroHeart;
			soul = soulFrom;
			soulAlpha = 0f;
			AddEffect(new HeartBurst(HeroHeart));
			disableSlow = Held(Keys.X);
			SetPhase(Phase.EnemyIntro);
		}

		public Rectangle Box => new((int)(BoxCenterX - BoxSize / 2f), (int)(BoxCenterY - BoxSize / 2f), BoxSize, BoxSize);
		public Vector2 SoulCenter => soul + new Vector2(SoulSize / 2f);
		public void Spawn(Bullet b)
		{
			Bullets.Add(b);
			Vector2 motion = b.Velocity;
			if (motion.LengthSquared() < 0.01f)
				motion = b.Position - Box.Center.ToVector2();
			if (motion.LengthSquared() > 0.01f)
				enemyAttackDirection = Vector2.Lerp(enemyAttackDirection, Vector2.Normalize(motion), 0.3f);
			enemyAttackEnergy = Math.Min(1.5f, enemyAttackEnergy + 0.22f);

			// Pattern volleys can create many projectiles in one tick; throttle the cue so it reads as attack rhythm.
			if (b.OnDraw == null && patternSoundCooldown <= 0)
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
			soul = Vector2.Lerp(soulFrom, SoulRestPosition, Math.Min(1f, phaseTicks / (8f * TicksPerFrame)));
			soulAlpha = Math.Min(1f, phaseTicks / (float)TicksPerFrame * 0.334f);
			if (boxTimer >= BoxGrowTicks)
				SetPhase(Phase.EnemyTurn);
		}

		private void UpdateEnemyTurn()
		{
			attack.Update(this, phaseTicks);
			MoveSoul();

			// obj_heart: global.inv -= 1 every frame
			inv--;
			Rectangle soulHit = new((int)soul.X + SoulHitInset, (int)soul.Y + SoulHitInset, SoulHitSize, SoulHitSize);
			Vector2 c = SoulCenter;
			Rectangle grazeBox = new((int)(c.X - GrazeSize / 2f), (int)(c.Y - GrazeSize / 2f), GrazeSize, GrazeSize);
			var mp = Player.GetModPlayer<MercyPlayer>();

			foreach (Bullet b in Bullets)
			{
				b.Update();
				if (b.Dead || !b.Harmful)
					continue;
				Rectangle hb = b.Hitbox;

				if (inv < 0 && hb.Intersects(soulHit))
				{
					HitSoul(b);
					b.Dead = true; // obj_collidebullet destroys itself on hit
					continue;
				}
				if (inv < 0 && hb.Intersects(grazeBox))
					Graze(b, mp);
			}
			Bullets.RemoveAll(b => b.Dead);

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
			soul += new Vector2(px, py);

			Rectangle box = Box;
			soul.X = MathHelper.Clamp(soul.X, box.Left + BoxClampLow, box.Right - BoxClampHigh);
			soul.Y = MathHelper.Clamp(soul.Y, box.Top + BoxClampLow, box.Bottom - BoxClampHigh);
		}

		private void HitSoul(Bullet b)
		{
			int damage = Math.Max(1, (int)Math.Round(encounter.Damage * b.DamageMult));
			Player.immune = false;
			Player.immuneTime = 0;
			HurtingPlayer = true;
			double dealt;
			try
			{
				dealt = Player.Hurt(PlayerDeathReason.ByCustomReason(NetworkText.FromLiteral($"{Player.name} was defeated by {boss.GivenOrTypeName}.")),
					damage, 0, knockback: 0f);
			}
			finally
			{
				HurtingPlayer = false;
			}
			// Terraria's own hit invincibility would hide the SOUL's; the battle handles it
			Player.immune = false;
			Player.immuneTime = 0;
			battleLife = Player.statLife;

			inv = InvincibleTicks;
			Sfx("hurt");
			// scr_damage: the hero flinches, the screen shakes, the number pops off the hero
			hurtTimer = 0;
			shake = 4;
			HeroNumber((int)dealt, Color.White);
		}

		private void Graze(Bullet b, MercyPlayer mp)
		{
			float tension;
			if (!b.Grazed)
			{
				b.Grazed = true;
				tension = b.GrazePoints;
				if (turnTimer >= GrazeTurnCutMinTicks)
					turnTimer -= b.TimePoints * TicksPerFrame;
				grazeTimer = GrazeFlashTicks;
				DeltaruneAssets.PlayIfLoaded("graze");
			}
			else
			{
				// grazepoints / 20 per frame = / 40 per tick
				tension = b.GrazePoints / GrazeHoldDivisor / TicksPerFrame;
				if (turnTimer >= GrazeTurnCutMinTicks)
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
			boxTimer = Math.Max(0, boxTimer - 1);
			CaptureBoxAfterimage();
			// obj_returnheart: back to the hero in 8 frames, then a burst; the next turn starts 15 frames after the end
			soul = Vector2.Lerp(soulFrom, HeroHeart, Math.Min(1f, phaseTicks / (8f * TicksPerFrame)));
			if (phaseTicks == 8 * TicksPerFrame)
				AddEffect(new HeartBurst(HeroHeart));
			if (boxTimer <= 0 && phaseTicks >= 15 * TicksPerFrame)
			{
				inv = -1;
				BeginPlayerTurn();
			}
		}

		// ---- ending ----

		private void StartOutro()
		{
			Bullets.Clear();
			panelDir = -1;
			text = "";
			SetPhase(Phase.Outro);
		}

		private void UpdateOutro()
		{
			if (music != null)
				music.Volume *= 0.9f;
			if (panel <= 0 && screenFade <= 0f && phaseTicks >= GlideTicks)
				End();
		}

		// ================================================================== drawing

		private const float BackgroundBleed = 8f;
		private const float FightBarX = 2;
		private const float FightBarY = 365;
		private static readonly Color PanelLine = MergeColor(MergeColor(new Color(128, 0, 128), Color.Black, 0.7f), new Color(64, 64, 64), 0.5f);
		private static readonly Color Orange = new(255, 160, 64);
		private static readonly Color KrisCyan = new(0, 255, 255);
		private static readonly Color BoxGreen = MergeColor(new Color(0, 128, 0), new Color(0, 255, 0), 0.5f);

		private static Color MergeColor(Color a, Color b, float t) => Color.Lerp(a, b, t);

		/// <summary>The party member's panel: rises 32 px while choosing, like Deltarune's active character.</summary>
		private Rectangle PartyBox
		{
			get
			{
				int top = (int)(ScreenHeight - panel - partyLift);
				return new Rectangle(214, top, 212, 36);
			}
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

			int index = layers.FindIndex(l => l.Name.Contains("Player Chat"));
			if (index < 0)
				index = layers.FindIndex(l => l.Name.Contains("Ingame Options"));
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

			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, m);
			DrDraw.Sb = sb;
			try
			{
				float left = -ox / scale, top = -oy / scale, width = Main.screenWidth / scale, height = Main.screenHeight / scale;
				DrawBackground(left - BackgroundBleed, top - BackgroundBleed,
					width + BackgroundBleed * 2f, height + BackgroundBleed * 2f);
				DrawEnemy();
				DrawHero(sb, m);
				DrawEffects();
				DrawBox();
				DrawTPBar();
				DrawPanel(left - BackgroundBleed, width + BackgroundBleed * 2f);
			}
			finally
			{
				sb.End();
				sb.Begin(SpriteSortMode.Deferred, null, null, null, null, null, Matrix.Identity);
			}
		}

		private void DrawBackground(float left, float top, float width, float height)
		{
			// obj_battleback: black fades in at 0.1 per frame, then bg_battleback1 tiled twice, scrolling
			// diagonally: one layer +0.5 px/frame at half alpha, the other -1 px/frame
			DrDraw.Rect(left, top, width, height, Color.Black * screenFade);
			var tile = DeltaruneAssets.Sprite("bg_battleback1");
			float frames = time / (float)TicksPerFrame;
			float siner = frames * 0.5f % 100f, siner2 = frames % 100f;
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
						DrDraw.Sb.Draw(tile.Frames[0], new Vector2(x, y), Color.White * alpha);
			}
			tiled((float)Math.Round(-100 + siner), (float)Math.Round(-100 + siner), screenFade / 2f);
			tiled((float)Math.Round(-200 - siner2), (float)Math.Round(-210 - siner2), screenFade);
		}

		private void DrawEnemy()
		{
			if (enemyOverride != null)
			{
				enemyOverride.Draw();
				DrawSlash(enemySnap.Position);
				return;
			}
			NPC npc = encounter?.DrawNpc;
			if (npc == null || !npc.active)
				return;
			Main.instance.LoadNPC(npc.type);
			Texture2D tex = TextureAssets.Npc[npc.type].Value;
			int frameCount = Math.Max(1, Main.npcFrameCount[npc.type]);
			int frameHeight = tex.Height / frameCount;
			Rectangle frame = npc.frame.Width > 0 && npc.frame.Height > 0
				? npc.frame
				: new Rectangle(0, 0, tex.Width, frameHeight);
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
			float drawScale = EnemyScaleNow(out _, out _);
			float glide = FlyProgress();
			float attackMotion = phase == Phase.EnemyTurn ? MathHelper.Clamp(enemyAttackEnergy, 0f, 1f) : 0f;
			float rotation = MathHelper.Lerp(enemyWorldRotation, encounter.DrawRotation(time), glide)
				- enemyAttackDirection.X * attackMotion * 0.055f;
			Color baseColor = encounter.DrawColor(npc);
			// Afterimages left behind while gliding, fading out
			foreach (var t in trail)
			{
				float a = 0.5f * (1f - t.Age / (float)TrailLife);
				DrDraw.Sb.Draw(tex, t.EnemyPos, frame, baseColor * a, rotation, frame.Size() / 2f, t.EnemyScale, SpriteEffects.None, 0f);
			}
			Vector2 pos = EnemyPosNow + new Vector2(0, (float)Math.Sin(time / 20f) * 4f * glide);
			pos -= enemyAttackDirection * (attackMotion * 3f);
			float pulse = (float)Math.Sin(time / 3f) * attackMotion * 0.035f;
			Vector2 spriteScale = new(drawScale * (1f + pulse), drawScale * (1f - pulse * 0.65f));
			if (enemyShake > 0)
				pos.X += (enemyShake % 4 < 2 ? 1 : -1) * enemyShake / 2f;
			float alpha = 1f;
			bool selecting = phase == Phase.EnemySelect || phase == Phase.ActSelect;
			Color color = encounter.DrawColor(npc) * alpha;
			DrDraw.Sb.Draw(tex, pos, frame, color, rotation, frame.Size() / 2f, spriteScale, SpriteEffects.None, 0f);
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
			if (slashTimer < 0)
				return;
			int f = Math.Min(4, slashTimer / 4); // image_speed 0.5: two frames per sprite frame
			float s = boltPoints == 150 ? 2.5f : 2f;
			if (!DrDraw.Sprite("spr_attack_cut1", f, pos.X, pos.Y, Color.White, s))
				DrDraw.Rect(pos.X - 30 + slashTimer * 3, pos.Y - 30 + slashTimer * 3, 8, 8, Color.White);
		}

		private void DrawBox()
		{
			if (phase != Phase.EnemyIntro && phase != Phase.EnemyTurn && phase != Phase.EnemyOutro)
				return;

			float t = boxTimer / BoxGrowTicks;
			float s = 2f * t;
			// GameMaker angles go counter-clockwise; XNA's go clockwise
			float angle = phase == Phase.EnemyTurn ? 0f : -MathHelper.ToRadians(180f + 180f * t);
			float alpha = 0.5f + t * 0.5f;
			foreach (BoxAfterimage image in boxAfterimages)
			{
				float ghostAlpha = Math.Max(0f, image.Alpha - image.Age * 0.04f);
				if (ghostAlpha > 0f)
					DrawBoxShape(image.Scale, image.Rotation, ghostAlpha);
			}
			DrawBoxShape(s, angle, alpha);

			foreach (Bullet b in Bullets)
				b.Draw();

			// SOUL (spr_dodgeheart flips frames while invincible) and the graze flash
			int frame = inv > 0 ? (inv / SoulBlinkTicks) % 2 : 0;
			// Once the SOUL is back with the hero it isn't drawn in the box any more
			bool soulHome = phase == Phase.EnemyOutro && phaseTicks >= 8 * TicksPerFrame;
			if (soulHome)
			{
			}
			else if (!DrDraw.Sprite("spr_dodgeheart", frame, soul.X, soul.Y, Color.White, 1f, 0f, phase == Phase.EnemyIntro ? soulAlpha : 1f))
				DrDraw.HeartShapeAt(soul.X + 2, soul.Y + 2, 16, frame == 1 ? new Color(128, 0, 0) : Color.Red);

			if (grazeTimer > 0)
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

		private void DrawPanel(float left, float width)
		{
			float top = ScreenHeight - panel;
			DrDraw.Rect(left, top, width, panel + BackgroundBleed + 1, Color.Black);
			DrDraw.Rect(left, top + 34, width, 2, PanelLine);
			if (panel <= 0)
			{
				DrDraw.Rect(left, top - 2, width, 2, PanelLine);
				return;
			}

			// The purple separator goes under the party box so the raised nameplate covers it.
			DrDraw.Rect(left, top - 2, width, 2, PanelLine);
			DrawPartyBox();

			float textY = top + 48; // 376 when the panel is fully up
			switch (phase)
			{
				case Phase.Intro:
				case Phase.Menu:
				case Phase.Message:
					DrDraw.Text(text.Substring(0, Math.Min(text.Length, (int)textShown)), 30, textY, Color.White);
					break;
				case Phase.EnemySelect:
					DrawEnemyList(textY);
					break;
				case Phase.ActSelect:
					DrawGrid(textY, currentActs.Select(a => (a.Name, a.TPCost > 0 && Player.GetModPlayer<MercyPlayer>().TP < a.TPCost)).ToList());
					DrawActInfo(textY);
					break;
				case Phase.ItemSelect:
					var items = HealingItems();
					DrawItemList(textY, items.Select(i => $"{i.name} x{i.count}").ToList());
					if (listIndex < items.Count)
						DrDraw.Text($"Heals\n{items[listIndex].heal} HP", 500, textY, new Color(128, 128, 128), DrDraw.BigFont);
					break;
				case Phase.FightBar:
				case Phase.FightResult:
					DrawFightBar();
					break;
			}
		}

		private void DrawPartyBox()
		{
			Rectangle r = PartyBox;
			bool choosing = phase == Phase.Menu || phase == Phase.EnemySelect || phase == Phase.ActSelect || phase == Phase.ItemSelect;
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
					bool selected = (int)menuChoice == i && phase == Phase.Menu || (int)pendingChoice == i && phase != Phase.Menu && phase != Phase.ItemSelect
						|| i == (int)Choice.Item && phase == Phase.ItemSelect;
					float bx = r.X + 15 + 35 * i;
					if (!DrDraw.Sprite(names[i], selected ? 1 : 0, bx, buttonsY, Color.White))
					{
						DrDraw.Outline(bx, buttonsY, 31, 32, selected ? new Color(255, 255, 0) : Orange, 2);
						DrDraw.Text(labels[i].Substring(0, 1), bx + 10, buttonsY + 8, selected ? new Color(255, 255, 0) : Orange, DrDraw.SmallFont);
					}
					if (i == (int)Choice.Spare && encounter.Mercy >= 100f)
						DrDraw.Sprite(names[i], 2, bx, buttonsY, Color.White, 1f, 0f, 0.4f + (float)Math.Sin(time / 12f) * 0.4f);
				}
			}

			// The outer frame tracks the nameplate. Every edge uses the same 2 px as the button-row columns,
			// and the divider is the nameplate's bottom edge, so it covers the button row as the box drops.
			const float edge = 2f;
			Color frame = KrisCyan * selectionAlpha;
			DrDraw.Rect(r.X, r.Y, r.Width, 34f, Color.Black);
			DrDraw.Rect(r.X, r.Y - edge, r.Width, edge, frame);
			DrDraw.Rect(r.X, r.Y - edge, edge, 34f + edge, frame);
			DrDraw.Rect(r.Right - edge, r.Y - edge, edge, 34f + edge, frame);
			DrDraw.Rect(r.X, r.Y + 34f - edge, r.Width, edge, frame);

			// The player's own head, enlarged and shifted left to leave clear space before the name.
			if (playerHeadPortrait?.IsReady == true)
			{
				DrDraw.Sb.Draw(playerHeadPortrait.GetTarget(), new Vector2(r.X + 16, r.Y + 15), null,
					new Color(110, 220, 255), 0f, new Vector2(42f), 0.82f, SpriteEffects.None, 0f);
			}
			else
			{
				DrDraw.HeartShapeAt(r.X + 10, r.Y + 5, 17, new Color(64, 220, 255));
			}
			DrDraw.Text(Player.name.ToUpperInvariant(), r.X + 40, r.Y + 7, Color.White, DrDraw.BigFont, 0.68f);
			if (!DrDraw.Sprite("spr_hpname", 0, r.X + 112, r.Y + 22, Color.White))
				DrDraw.Text("HP", r.X + 108, r.Y + 19, Color.White, DrDraw.SmallFont);
			float ratio = MathHelper.Clamp(Player.statLife / (float)Player.statLifeMax2, 0f, 1f);
			const int hpBarX = 130;
			const int hpBarWidth = 74;
			DrDraw.Rect(r.X + hpBarX, r.Y + 19, hpBarWidth, 8, new Color(128, 0, 0));
			DrDraw.Rect(r.X + hpBarX, r.Y + 19, (float)Math.Ceiling(ratio * hpBarWidth), 8, KrisCyan);
			string hp = $"{Player.statLife}/{Player.statLifeMax2}";
			Color hpColor = ratio <= 0.25f ? new Color(255, 255, 0) : Color.White;
			const float hpNumberScale = 0.55f;
			float hpTextWidth = DrDraw.Measure(hp, DrDraw.BigFont) * hpNumberScale;
			DrDraw.Text(hp, r.X + hpBarX + (hpBarWidth - hpTextWidth) / 2f, r.Y - 1f,
				hpColor, DrDraw.BigFont, hpNumberScale);

		}

		private void DrawHeartCursor(float x, float y)
		{
			if (!DrDraw.Sprite("spr_heart", 0, x, y, Color.White))
				DrDraw.HeartShapeAt(x, y, 16, Color.Red);
		}

		private void DrawEnemyList(float y)
		{
			DrawHeartCursor(55, y + 10);
			bool spareable = encounter.Mercy >= 100f;
			DrDraw.Text(encounter.Name, 80, y, spareable ? new Color(255, 255, 0) : Color.White);

			// HP and MERCY columns like Deltarune's enemy list
			Color headerGray = new(128, 128, 128);
			DrDraw.Text("HP", 424, y - 14, Color.White, DrDraw.SmallFont);
			DrDraw.Text("MERCY", 524, y - 14, Color.White, DrDraw.SmallFont);
			float hp = MathHelper.Clamp(encounter.LifeRatio, 0f, 1f);
			DrDraw.Rect(420, y + 5, 81, 16, new Color(128, 0, 0));
			DrDraw.Rect(420, y + 5, (float)Math.Ceiling(hp * 81), 16, new Color(0, 255, 0));
			DrDraw.Text($"{(int)Math.Ceiling(hp * 100)}%", 424, y + 5, Color.White, DrDraw.SmallFont);
			float mercy = MathHelper.Clamp(encounter.Mercy / 100f, 0f, 1f);
			DrDraw.Rect(520, y + 5, 81, 16, new Color(255, 80, 32));
			DrDraw.Rect(520, y + 5, (float)Math.Ceiling(mercy * 81), 16, new Color(255, 255, 0));
			DrDraw.Text($"{(int)encounter.Mercy}%", 524, y + 5, new Color(128, 0, 0), DrDraw.SmallFont);
			_ = headerGray;
		}

		private void DrawGrid(float y, List<(string name, bool greyed)> entries)
		{
			for (int i = 0; i < entries.Count; i++)
			{
				float x = i % 2 == 0 ? 80 : 300;
				float ey = y + (i / 2) * 30;
				DrDraw.Text(entries[i].name, x, ey, entries[i].greyed ? new Color(128, 128, 128) : Color.White);
				if (i == listIndex)
					DrawHeartCursor(x - 25, ey + 10);
			}
		}

		private void DrawItemList(float y, List<string> names)
		{
			const int rows = 3;
			int first = Math.Clamp(listIndex - rows + 1, 0, Math.Max(0, names.Count - rows));
			for (int i = first; i < Math.Min(names.Count, first + rows); i++)
			{
				float ey = y + (i - first) * 30;
				DrDraw.Text(names[i], 80, ey, Color.White);
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

		private void DrawFightBar()
		{
			float x = FightBarX, y = FightBarY;
			float alpha = 1f - MathHelper.Clamp(fightFade, 0f, 1f);
			Color blue = new Color(0, 0, 255) * alpha;
			if (!DrDraw.Sprite("spr_pressfront", 0, x, y, Color.White, 1f, 0f, alpha))
				DrDraw.Text(Player.name.Length > 0 ? Player.name.Substring(0, 1) : "*", x + 20, y + 4, Color.White * alpha);
			DrDraw.Outline(x + 78, y, FightBoxWidth + 3, 37, blue);
			DrDraw.Outline(x + 79, y + 1, FightBoxWidth + 1, 35, blue);
			if (!DrDraw.Sprite("spr_pressspot", 0, x + 80, y, Color.White, 1f, 0f, alpha))
				DrDraw.Rect(x + 80, y, 10, 38, new Color(0, 0, 255) * alpha);

			if (boltAlive)
			{
				float bx = x + 80 + (BoltStartFrame - boltX) * BoltSpeed;
				float boltAlpha = BoltStartFrame - boltX < 0 ? 1f + (BoltStartFrame - boltX) / 3f : 1f;
				// Afterimages every other frame, fading
				for (int k = 2; k >= 1; k--)
					if (!DrDraw.Sprite("spr_attackspot", 0, bx + k * BoltSpeed, y, Color.White, 1f, 0f, 0.4f / k * boltAlpha))
						DrDraw.Rect(bx + k * BoltSpeed + 2, y, 6, 38, Color.White * (0.4f / k * boltAlpha));
				if (!DrDraw.Sprite("spr_attackspot", 0, bx, y, Color.White, 1f, 0f, boltAlpha))
					DrDraw.Rect(bx + 2, y, 6, 38, Color.White * boltAlpha);
			}

			if (burstTimer > 0)
			{
				// obj_burstbolt: grows and fades; yellow for a perfect hit
				float t = 1f - burstTimer / 20f;
				Color c = boltPoints == 150 ? new Color(255, 255, 0) : MergeColor(KrisCyan, Color.White, 0.5f);
				Vector2 sc = new(1f + t * 2f, 1f + t * 0.5f);
				if (!DrDraw.Sprite("spr_attackspot", 0, burstPos.X - 5 * (sc.X - 1), burstPos.Y - 19 * (sc.Y - 1), c, sc, 0f, 1f - t))
					DrDraw.Rect(burstPos.X, burstPos.Y, 10 * sc.X, 38 * sc.Y, c * (1f - t));
			}
		}
	}
}
