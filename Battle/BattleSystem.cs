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
	/// The Deltarune-style battle screen. Starts when the player touches or hits a boss that has a
	/// <see cref="BossBattle"/>, freezes the world, and runs player turns and enemy turns until the boss is
	/// spared or defeated (or the player dies).
	/// </summary>
	public class BattleSystem : ModSystem
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
		private BossBattle encounter;
		private NPC boss;
		public NPC Boss => boss;
		public Player Player => Main.LocalPlayer;
		public BossBattle Encounter => encounter;

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

		// fight
		private float boltX; // frames since the bar appeared (boltx)
		private bool boltAlive;
		private int boltPoints = -1;
		private Vector2 burstPos;
		private int burstTimer;
		private float fightFade;
		private int slashTimer = -1;
		private int enemyShake;
		private readonly List<Popup> popups = new();

		// music
		private SoundEffectInstance music;

		// world snapshot so everything resumes where it was
		private readonly Dictionary<int, (int type, Vector2 velocity)> npcVelocities = new();
		private readonly Dictionary<int, (int type, Vector2 velocity)> projVelocities = new();
		private Vector2 playerPosition;

		private class Popup
		{
			public string Text;
			public int Number = -1;
			public Vector2 Pos;
			public Color Color;
			public int Age;
		}

		// ================================================================== lifecycle

		public static bool CanStart(NPC npc, Player player)
		{
			if (Active || !MercyMode.IsSingleplayer || player.whoAmI != Main.myPlayer || player.dead)
				return false;
			var config = ModContent.GetInstance<MercyConfig>();
			if (config != null && !config.TurnBasedBattles)
				return false;
			NPC root = MercyMode.Root(npc);
			return root.active && root.boss && BossBattle.HasBattle(root);
		}

		public static void TryStart(NPC npc, Player player)
		{
			if (CanStart(npc, player))
				Instance.Start(MercyMode.Root(npc));
		}

		private void Start(NPC root)
		{
			boss = root;
			encounter = BossBattle.Create(root);
			time = 0;
			battleOver = false;
			defending = false;
			menuChoice = Choice.Fight;
			Bullets.Clear();
			popups.Clear();
			messages.Clear();
			panel = 0;
			panelDir = 1;
			screenFade = 0;
			slashTimer = -1;
			enemyShake = 0;
			var mp = Player.GetModPlayer<MercyPlayer>();
			tpApparent = tpCurrent = mp.TP / TensionToTP;

			npcVelocities.Clear();
			foreach (NPC n in Main.ActiveNPCs)
				npcVelocities[n.whoAmI] = (n.type, n.velocity);
			projVelocities.Clear();
			foreach (Projectile p in Main.ActiveProjectiles)
				projVelocities[p.whoAmI] = (p.type, p.velocity);
			playerPosition = Player.position;

			DeltaruneAssets.Play("battleenter", SoundID.Roar);
			if (DeltaruneAssets.BattleMusic != null)
			{
				music = DeltaruneAssets.BattleMusic.CreateInstance();
				music.IsLooped = true;
				music.Volume = MathHelper.Clamp(Main.musicVolume, 0f, 1f);
				music.Play();
			}

			SetText(encounter.EncounterText);
			SetPhase(Phase.Intro);
			Mod.Logger.Info($"Battle started with {boss.FullName} ({boss.life}/{boss.lifeMax} HP)");
		}

		private void End()
		{
			Mod.Logger.Info($"Battle ended (boss active: {boss?.active}, player dead: {Player.dead})");
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

		public override void OnWorldUnload()
		{
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
			Mod.Logger.Debug($"Battle phase {p} (turn {encounter?.Turn}, boss {boss?.life}/{boss?.lifeMax}, mercy {encounter?.Mercy:0}, TP {Player.GetModPlayer<MercyPlayer>().TP:0.0}, HP {Player.statLife})");
		}

		private void SetText(string s)
		{
			text = DrDraw.Wrap(s, 570f);
			textShown = 0;
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

		public override void PostUpdateEverything()
		{
			if (phase == Phase.None)
				return;

			if (boss == null || Player.dead)
			{
				End();
				return;
			}
			// Boss gone without us ending the battle (despawned, killed some other way)
			if (!boss.active && phase != Phase.Outro && phase != Phase.Message && phase != Phase.FightResult)
			{
				battleOver = true;
				StartOutro();
			}

			time++;
			phaseTicks++;
			if (music != null)
				music.Volume = MathHelper.Clamp(Main.musicVolume, 0f, 1f);

			// Hold the player in place, no falling or fall damage
			Player.position = playerPosition;
			Player.velocity = Vector2.Zero;
			Player.fallStart = (int)(Player.position.Y / 16f);

			if (time % TicksPerFrame == 0)
				UpdateHudFrame();
			if (textShown < text.Length)
				textShown += TextCharsPerTick;
			if (grazeTimer > 0)
				grazeTimer--;
			if (enemyShake > 0)
				enemyShake--;
			if (burstTimer > 0)
				burstTimer--;
			if (slashTimer >= 0 && ++slashTimer > 20)
				slashTimer = -1;
			for (int i = popups.Count - 1; i >= 0; i--)
				if (++popups[i].Age > 90)
					popups.RemoveAt(i);

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
		private void UpdateHudFrame()
		{
			// Bottom panel slide (bp)
			if (panelDir > 0 && panel < PanelHeight)
			{
				if (PanelHeight - panel < 40)
					panel += (float)Math.Round((PanelHeight - panel) / 2.5f);
				else
					panel += PanelSlidePerFrame;
				if (panel >= PanelHeight - 1)
					panel = PanelHeight;
			}
			else if (panelDir < 0 && panel > 0)
			{
				panel = Math.Max(0, panel - PanelSlidePerFrame);
			}

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

			float fadeTarget = phase == Phase.Outro ? 0f : 1f;
			screenFade = MathHelper.Clamp(screenFade + (fadeTarget > screenFade ? 0.1f : -0.1f), 0f, 1f);
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
			if (panel >= PanelHeight && phaseTicks > 20)
			{
				Sfx("weaponpull");
				BeginPlayerTurn(keepText: true);
			}
		}

		private void BeginPlayerTurn(bool keepText = false)
		{
			defending = false;
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
			List<string> lines = act.Run(this);
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
			if (MoveInGrid(items.Count))
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

			int before = Player.statLife;
			Player.Heal(heal);
			int healed = Player.statLife - before;
			Sfx("heal");
			AddPopup(healed > 0 ? null : "MAX", healed, PartyBox.Center.ToVector2() + new Vector2(0, -20), new Color(0, 255, 0));

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
				MercyGlobalNPC.Spare(boss);
				ShowMessages(new[] { spared }, StartOutro);
				return;
			}
			ShowMessages(new[] { spared + "\n* But its name wasn't YELLOW..." }, StartEnemyTurn);
		}

		private void DoDefend()
		{
			defending = true;
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
				ResolveAttack();
				return;
			}
			if (boltAlive && BoltStartFrame - boltX < -BoltWindowLate)
			{
				boltAlive = false;
				boltPoints = 0;
				ResolveAttack();
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

		private void ResolveAttack()
		{
			if (boltPoints <= 0)
			{
				AddPopup("MISS", -1, encounter.DrawCenter + new Vector2(0, -40), new Color(192, 192, 192));
				SetPhase(Phase.FightResult);
				return;
			}

			var config = ModContent.GetInstance<MercyConfig>();
			float mult = config?.FightDamageMultiplier ?? 1f;
			int raw = (int)Math.Round(AttackStat() * boltPoints / DamagePointsDivisor * mult);
			int dealt = boss.SimpleStrikeNPC(raw, Player.direction, crit: false, knockBack: 0f);

			Sfx(boltPoints == 150 ? "crit" : "slash");
			Sfx("damage");
			slashTimer = 0;
			enemyShake = 18;
			AddPopup(null, dealt, encounter.DrawCenter + new Vector2(0, -40), boltPoints == 150 ? new Color(255, 255, 0) : Color.White);

			if (dealt > 0)
			{
				var mp = Player.GetModPlayer<MercyPlayer>();
				mp.TP = Math.Min(100f, mp.TP + (float)Math.Round(boltPoints / HitTensionDivisor) * TensionToTP);
			}
			SetPhase(Phase.FightResult);
		}

		private void UpdateFightResult()
		{
			if (phaseTicks > FightPostTicks)
				fightFade += FightFadePerTick;
			if (fightFade < 1f)
				return;

			if (!boss.active || boss.life <= 0)
			{
				battleOver = true;
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
			attack = encounter.NextAttack(this);
			encounter.Turn++;
			turnTimer = attack.Duration;
			boxTimer = 0;
			text = "";
			soulFrom = new Vector2(PartyBox.Center.X, PartyBox.Top) - new Vector2(SoulSize / 2f);
			soul = soulFrom;
			disableSlow = Held(Keys.X);
			SetPhase(Phase.EnemyIntro);
		}

		public Rectangle Box => new((int)(BoxCenterX - BoxSize / 2f), (int)(BoxCenterY - BoxSize / 2f), BoxSize, BoxSize);
		public Vector2 SoulCenter => soul + new Vector2(SoulSize / 2f);
		public void Spawn(Bullet b) => Bullets.Add(b);
		public bool BossInSecondPhase => boss.life < boss.lifeMax / 2;

		private Vector2 SoulRestPosition => new Vector2(BoxCenterX, BoxCenterY) - new Vector2(SoulSize / 2f);

		private void UpdateEnemyIntro()
		{
			boxTimer = Math.Min(BoxGrowTicks, boxTimer + 1);
			soul = Vector2.Lerp(soulFrom, SoulRestPosition, boxTimer / BoxGrowTicks);
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
			int damage = Math.Max(1, (int)Math.Round(boss.damage * b.DamageMult));
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

			inv = InvincibleTicks;
			Sfx("hurt");
			AddPopup(null, (int)dealt, PartyBox.Center.ToVector2() + new Vector2(0, -24), new Color(255, 64, 64));
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
			if (boxTimer <= 0)
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
			if (panel <= 0 && screenFade <= 0f)
				End();
		}

		private void AddPopup(string label, int number, Vector2 pos, Color color)
		{
			popups.Add(new Popup { Text = label, Number = label == null ? number : -1, Pos = pos, Color = color });
		}

		// ================================================================== drawing

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
				bool raised = phase == Phase.Menu || phase == Phase.EnemySelect || phase == Phase.ActSelect || phase == Phase.ItemSelect;
				int top = (int)(ScreenHeight - panel) - (raised ? 32 : 0);
				return new Rectangle(214, top, 212, 36);
			}
		}

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			if (phase == Phase.None)
				return;

			// Hide the normal HUD; keep the logic layers and the pause menu
			foreach (var l in layers)
			{
				if (l.Name.Contains("Logic") || l.Name.Contains("Ingame Options") || l.Name.Contains("Cursor"))
					continue;
				l.Active = false;
			}

			int index = layers.FindIndex(l => l.Name.Contains("Ingame Options"));
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
			float raw = Math.Min(Main.screenWidth / (float)ScreenWidth, Main.screenHeight / (float)ScreenHeight);
			float scale = raw >= 2f ? (float)Math.Floor(raw) : raw;
			float ox = (Main.screenWidth - ScreenWidth * scale) / 2f;
			float oy = (Main.screenHeight - ScreenHeight * scale) / 2f;
			Matrix m = Matrix.CreateScale(scale, scale, 1f) * Matrix.CreateTranslation(ox, oy, 0f);

			sb.End();
			sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, null, m);
			DrDraw.Sb = sb;
			try
			{
				float left = -ox / scale, top = -oy / scale, width = Main.screenWidth / scale, height = Main.screenHeight / scale;
				DrawBackground(left, top, width, height);
				DrawEnemy();
				DrawPopups();
				DrawBox();
				DrawTPBar();
				DrawPanel(left, width);
			}
			finally
			{
				sb.End();
				sb.Begin(SpriteSortMode.Deferred, null, null, null, null, null, Matrix.Identity);
			}
		}

		private void DrawBackground(float left, float top, float width, float height)
		{
			// The world stays visible behind a dark veil, with Deltarune's scrolling battle grid on top
			DrDraw.Rect(left, top, width, height, Color.Black * (0.75f * screenFade));
			Color grid = new Color(80, 32, 120) * (0.35f * screenFade);
			const int cell = 50;
			float scroll = time * 0.5f % cell;
			for (float x = left - cell + scroll; x < left + width; x += cell)
				DrDraw.Rect(x, top, 1, height, grid);
			for (float y = top - cell + scroll; y < top + height; y += cell)
				DrDraw.Rect(left, y, width, 1, grid);
		}

		private void DrawEnemy()
		{
			if (boss == null || !boss.active)
				return;
			Main.instance.LoadNPC(boss.type);
			Texture2D tex = TextureAssets.Npc[boss.type].Value;
			Rectangle frame = boss.frame.Width > 0 ? boss.frame : new Rectangle(0, 0, tex.Width, tex.Height / Math.Max(1, Main.npcFrameCount[boss.type]));
			Vector2 pos = encounter.DrawCenter + new Vector2(0, (float)Math.Sin(time / 20f) * 4f);
			if (enemyShake > 0)
				pos.X += (enemyShake % 4 < 2 ? 1 : -1) * enemyShake / 2f;
			float alpha = screenFade;
			bool selecting = phase == Phase.EnemySelect || phase == Phase.ActSelect;
			Color color = Color.White * alpha;
			DrDraw.Sb.Draw(tex, pos, frame, color, encounter.DrawRotation(time), frame.Size() / 2f, encounter.DrawScale, SpriteEffects.None, 0f);
			if (selecting)
			{
				// Deltarune flashes the targeted enemy white
				float flash = (float)(Math.Sin(time / 5f) * 0.5 + 0.5) * 0.6f;
				DrDraw.Sb.Draw(tex, pos, frame, new Color(255, 255, 255, 0) * flash, encounter.DrawRotation(time), frame.Size() / 2f, encounter.DrawScale, SpriteEffects.None, 0f);
			}

			if (slashTimer >= 0)
			{
				int f = Math.Min(4, slashTimer / 4); // image_speed 0.5: two frames per sprite frame
				float s = boltPoints == 150 ? 2.5f : 2f;
				if (!DrDraw.Sprite("spr_attack_cut1", f, pos.X, pos.Y, Color.White, s))
					DrDraw.Rect(pos.X - 30 + slashTimer * 3, pos.Y - 30 + slashTimer * 3, 8, 8, Color.White);
			}
		}

		private void DrawPopups()
		{
			foreach (Popup p in popups)
			{
				// Bounce up, settle, then fade (obj_dmgwriter)
				float t = p.Age;
				float bounce = t < 10 ? -t * 2f : t < 20 ? -20 + (t - 10) * 2f : 0f;
				float alpha = t > 60 ? 1f - (t - 60) / 30f : 1f;
				Vector2 pos = p.Pos + new Vector2(0, bounce);
				if (p.Number >= 0)
					DrDraw.Number(p.Number, pos.X, pos.Y, p.Color, alpha);
				else
					DrDraw.Text(p.Text, pos.X - DrDraw.Measure(p.Text, DrDraw.BigFont) / 2f, pos.Y, p.Color * alpha);
			}
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
			var c = new Vector2(BoxCenterX, BoxCenterY);

			if (!DrDraw.Sprite("spr_battlebg_0", 1, c.X, c.Y, BoxGreen, s, angle, alpha) | !DrDraw.Sprite("spr_battlebg_0", 0, c.X, c.Y, BoxGreen, s, angle, alpha))
			{
				float size = BoxSize * t;
				DrDraw.Rect(c.X - size / 2, c.Y - size / 2, size, size, Color.Black * alpha);
				DrDraw.Outline(c.X - size / 2, c.Y - size / 2, size, size, BoxGreen * alpha, 3);
			}

			foreach (Bullet b in Bullets)
				b.Draw();

			// SOUL (spr_dodgeheart flips frames while invincible) and the graze flash
			int frame = inv > 0 ? (inv / SoulBlinkTicks) % 2 : 0;
			if (!DrDraw.Sprite("spr_dodgeheart", frame, soul.X, soul.Y, Color.White))
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

		private void DrawTPBar()
		{
			// obj_tensionbar slides in from x = -40 to 38
			float slide = MathHelper.Clamp(panel / PanelHeight, 0f, 1f);
			float x = -40 + 78 * slide, y = 40;
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
			DrDraw.Rect(left, top, width, panel + 1, Color.Black);
			DrDraw.Rect(left, top - 2, width, 2, PanelLine);
			DrDraw.Rect(left, top + 34, width, 2, PanelLine);
			if (panel <= 0)
				return;

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
					DrawGrid(textY, items.Select(i => ($"{i.name} x{i.count}", false)).ToList());
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
			bool raised = r.Top < ScreenHeight - panel;
			if (raised)
			{
				// Outline in the character's colour, black inside
				DrDraw.Rect(r.X, r.Y - 2, r.Width, 2, KrisCyan);
				DrDraw.Rect(r.X, r.Y, 2, r.Height + 32, KrisCyan);
				DrDraw.Rect(r.Right - 2, r.Y, 2, r.Height + 32, KrisCyan);
				DrDraw.Rect(r.X + 2, r.Y, r.Width - 4, r.Height + 32, Color.Black);
			}

			DrDraw.Text(Player.name.ToUpperInvariant(), r.X + 12, r.Y + 6, Color.White, DrDraw.SmallFont);
			if (!DrDraw.Sprite("spr_hpname", 0, r.X + 109, r.Y + 11, Color.White))
				DrDraw.Text("HP", r.X + 106, r.Y + 6, Color.White, DrDraw.SmallFont);
			float ratio = MathHelper.Clamp(Player.statLife / (float)Player.statLifeMax2, 0f, 1f);
			DrDraw.Rect(r.X + 128, r.Y + 11, 76, 9, new Color(128, 0, 0));
			DrDraw.Rect(r.X + 128, r.Y + 11, (float)Math.Ceiling(ratio * 76), 9, KrisCyan);
			string hp = $"{Player.statLife}/{Player.statLifeMax2}";
			Color hpColor = ratio <= 0.25f ? new Color(255, 255, 0) : Color.White;
			DrDraw.Text(hp, r.X + 205 - DrDraw.Measure(hp, DrDraw.SmallFont), r.Y - 8, hpColor, DrDraw.SmallFont);

			if (!raised)
				return;
			string[] names = { "spr_btfight", "spr_btact", "spr_btitem", "spr_btspare", "spr_btdefend" };
			string[] labels = { "FIGHT", "ACT", "ITEM", "SPARE", "DEFEND" };
			float by = ScreenHeight - panel + 5; // 485 - bp
			for (int i = 0; i < 5; i++)
			{
				bool selected = (int)menuChoice == i && phase == Phase.Menu || (int)pendingChoice == i && phase != Phase.Menu && phase != Phase.ItemSelect
					|| i == (int)Choice.Item && phase == Phase.ItemSelect;
				float bx = r.X + 15 + 35 * i;
				if (!DrDraw.Sprite(names[i], selected ? 1 : 0, bx, by, Color.White))
				{
					DrDraw.Outline(bx, by, 31, 32, selected ? new Color(255, 255, 0) : Orange, 2);
					DrDraw.Text(labels[i].Substring(0, 1), bx + 10, by + 8, selected ? new Color(255, 255, 0) : Orange, DrDraw.SmallFont);
				}
				// SPARE glows when the enemy can be spared
				if (i == (int)Choice.Spare && encounter.Mercy >= 100f)
					DrDraw.Sprite(names[i], 2, bx, by, Color.White, 1f, 0f, 0.4f + (float)Math.Sin(time / 12f) * 0.4f);
			}
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
			float hp = MathHelper.Clamp(boss.life / (float)boss.lifeMax, 0f, 1f);
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
