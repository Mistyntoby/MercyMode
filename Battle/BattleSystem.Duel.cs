using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using MercyMode.Battle.Net;
using static MercyMode.Battle.BattleConstants;

namespace MercyMode.Battle
{
	/// <summary>The other player in a duel, as an enemy: their name, ACTs, and an attack built live by them.</summary>
	public sealed class DuelEncounter : Encounter
	{
		public int Opponent;
		/// <summary>Where they stand on the battle screen (mirroring the hero), body centre.</summary>
		public Vector2 Center;

		private Player P => Main.player[Opponent];

		public override string Name => P.name;
		public override string EncounterText => $"* {P.name} challenges you!";
		public override bool IsBoss => false;
		/// <summary>The bullets carry their own damage (from the weapon the piece came from).</summary>
		public override int Damage => 1;
		public override Vector2 DrawCenter => Center;

		private static readonly string[] Lines =
		{
			"* {0} is sizing you up.",
			"* {0} grips their weapon.",
			"* {0} circles you warily.",
			"* {0} is plotting something.",
			"* Smells like a rivalry.",
		};

		public override string FlavorText()
		{
			if (Mercy >= 100f)
				return $"* {P.name} looks ready to call it a draw.";
			if (LifeRatio < 0.35f)
				return $"* {P.name} is breathing hard.";
			return string.Format(Lines[Turn % Lines.Length], P.name);
		}

		public override List<ActOption> Acts(BattleSystem battle) => new()
		{
			new ActOption
			{
				Name = "Check",
				Description = "Useless\nanalysis",
				Run = b => new List<string> { $"* {P.name} - HP {Math.Max(0, Npc.life)}/{Npc.lifeMax} DF {P.statDefense}\n* Another adventurer. Definitely armed." },
			},
			AskAct("Talk", "Talk it\nout", 40f, $"* You tried to talk {P.name} down."),
			AskAct("Compliment", "Say something\nnice", 34f, $"* You complimented {P.name}'s gear."),
			AskAct("Handshake", "Offer a\nhandshake", 50f, $"* You offered {P.name} a handshake."),
			HealPrayerAct(),
		};

		public override EnemyAttack NextAttack(BattleSystem battle) => battle.NewDuelAttack();

		/// <summary>A MERCY act the other player has to accept: asked now, the MERCY comes with their yes.</summary>
		private ActOption AskAct(string name, string description, float mercy, string line) => new()
		{
			Name = name,
			Description = description,
			Run = b =>
			{
				b.DuelAskMercy(name, mercy, line);
				return new List<string> { line };
			},
		};

		/// <summary>They said yes: the MERCY (halved for each repeat of the same act, like any act). Lines to show.</summary>
		public List<string> Accepted(string act, float amount)
		{
			float gained = GainMercy(act, amount);
			var lines = new List<string> { $"* {P.name} accepted!" + (gained > 0 ? "" : "\n* But it didn't change much.") };
			if (Mercy >= 100f)
				lines.Add($"* {P.name} doesn't want to fight anymore.");
			return lines;
		}
	}

	public partial class BattleSystem
	{
		/// <summary>The duel opponent (-1: not in a duel).</summary>
		private int duelWith = -1;
		/// <summary>Our turn to choose (the other one waits, then builds the attack we dodge).</summary>
		private bool duelMyTurn;
		/// <summary>Won, lost, spared or left: nothing more is sent or taken.</summary>
		private bool duelOver;
		private DuelEncounter duelEnc;
		private int duelHp = -1, duelHpMax = 100, duelDef;
		private Vector2 duelRemoteSoul;
		private bool duelRemoteSoulSet;
		private int duelSentHp = -1;
		/// <summary>The attack we're dodging, fed by the other player's pieces.</summary>
		private DuelAttack duelLive;

		public static bool InDuel => Active && Instance.duelWith >= 0;

		/// <summary>The opponent's damage numbers on us: their party colour, as light as ours are.</summary>
		private Color DuelOppDamageColor => duelWith >= 0
			? Color.Lerp(Net.PartyColors.Of(Main.player[duelWith]), Color.White, 0.5f) : Color.White;

		/// <summary>FIGHT against a player does this share of its damage (the pieces do half their weapon's too).</summary>
		private const float DuelFightScale = 0.5f;

		/// <summary>The proxy NPC slot: index 200 exists in Terraria's array but is never updated, drawn or synced.</summary>
		private static NPC DuelProxy => Main.npc[Main.maxNPCs];

		public static bool IsDuelProxy(NPC npc) => npc != null && npc.whoAmI >= Main.maxNPCs;

		/// <summary>The server paired us with this player. False if a battle can't start here.</summary>
		internal bool StartDuel(int opponent, bool meFirst)
		{
			Player o = Main.player[opponent];
			if (Active || Player.dead || !o.active || o.dead || ModContent.GetInstance<MercyConfig>()?.TurnBasedBattles == false)
				return false;
			NPC proxy = DuelProxy;
			proxy.SetDefaults(NPCID.TargetDummy);
			proxy.whoAmI = Main.maxNPCs;
			proxy.active = true;
			proxy.immortal = true;
			proxy.dontTakeDamage = false;
			proxy.friendly = false;
			proxy.realLife = -1;
			proxy.damage = 0;
			duelWith = opponent;
			duelMyTurn = meFirst;
			duelOver = false;
			duelHp = o.statLife;
			duelHpMax = o.statLifeMax2;
			duelDef = o.statDefense;
			duelRemoteSoulSet = false;
			duelSentHp = -1;
			duelLive = null;
			duelAskPending = false;
			duelOppHurt = -1;
			duelOppPose = HeroPose.Idle;
			duelOppAttackAt = 0;
			duelSentItem = -1;
			SyncDuelProxy();
			duelEnc = new DuelEncounter
			{
				Npc = proxy,
				Opponent = opponent,
				Center = new Vector2(ScreenWidth - HeroFeet.X, HeroFeet.Y - 21f * HeroScale),
			};
			Start(proxy, "duel", new List<NPC> { proxy });
			return true;
		}

		/// <summary>The proxy stands where the other player is, with their HP, so FIGHT, the HP bars and the glide just work.</summary>
		private void SyncDuelProxy()
		{
			if (duelWith < 0)
				return;
			Player o = Main.player[duelWith];
			NPC proxy = DuelProxy;
			proxy.active = true;
			proxy.width = o.width;
			proxy.height = o.height;
			proxy.position = o.position;
			proxy.lifeMax = Math.Max(1, duelHpMax);
			proxy.life = Math.Clamp(duelHp, 0, proxy.lifeMax);
			proxy.defense = duelDef;
			proxy.velocity = Vector2.Zero;
		}

		/// <summary>Every tick of a duel: the proxy, our HP and SOUL to the other player, and whether they're still here.</summary>
		private void UpdateDuel()
		{
			if (duelWith < 0)
				return;
			if (!Main.player[duelWith].active && !duelOver)
			{
				OnDuelEnd();
				return;
			}
			SyncDuelProxy();
			if (duelOver)
				return;
			int hp = Math.Max(0, battleLife);
			if (hp != duelSentHp || time % 30 == 0)
			{
				duelSentHp = hp;
				BattleNet.SendDuel(BattleNet.DuelKind.Hp, w =>
				{
					w.Write((short)hp);
					w.Write((short)Player.statLifeMax2);
					w.Write((short)Player.statDefense);
				});
			}
			if (duelOppHurt >= 0 && ++duelOppHurt > 30)
				duelOppHurt = -1;
			SendBeamState();
			if (!duelOppBeamOn)
				duelOppBeamCharge = Math.Max(0f, duelOppBeamCharge - 0.04f);
			// How we stand and what we hold, whenever it changes (the swing itself goes as a Fire)
			HeroPose pose = heroPose == HeroPose.Attack ? HeroPose.AttackReady : heroPose;
			int held = heroPose is HeroPose.Item or HeroPose.ItemReady ? usedItemType : WeaponForDisplay()?.type ?? 0;
			if (phase == Phase.WeaponSelect || phase == Phase.Build && !buildDone)
				pose = HeroPose.AttackReady;
			if (pose != duelSentPose || held != duelSentItem || time % 60 == 0)
			{
				duelSentPose = pose;
				duelSentItem = held;
				BattleNet.SendDuel(BattleNet.DuelKind.Pose, w =>
				{
					w.Write((byte)pose);
					w.Write(held);
				});
			}
			if (phase is Phase.EnemyIntro or Phase.EnemyTurn or Phase.EnemyOutro && time % 2 == 0)
				BattleNet.SendDuel(BattleNet.DuelKind.Soul, w =>
				{
					w.Write((short)soul.X);
					w.Write((short)soul.Y);
					// 0x80 grazing, 0x40 the dark frame of the hit-invincibility blink
					w.Write((byte)((grazeTimer > 0 ? 0x80 : 0) | (inv > 0 && inv / SoulBlinkTicks % 2 == 1 ? 0x40 : 0)));
				});
		}

		// ================================================================== MERCY acts, answered by the other player

		/// <summary>Our act waits on their answer (what it was, how much MERCY, how long we've waited).</summary>
		private bool duelAskPending;
		private string duelAskAct;
		private float duelAskAmount;
		/// <summary>The act they used on us, waiting for our answer; the cursor (0 accept, 1 refuse).</summary>
		private string duelPromptText;
		private int duelPromptChoice;
		private const int MercyAnswerTicks = 30 * 60;

		internal void DuelAskMercy(string act, float amount, string line)
		{
			if (duelWith < 0 || duelOver)
				return;
			duelAskPending = true;
			duelAskAct = act;
			duelAskAmount = amount;
			// As they read it: "Nico offered you a handshake." (us by name first, then them as "you")
			string them = Main.player[duelWith].name;
			string text = Narration.ThirdPerson(line, Player.name).Replace(them + "'s", "your").Replace(them, "you");
			BattleNet.SendDuel(BattleNet.DuelKind.MercyAsk, w => w.Write(text));
		}

		private void UpdateMercyWait()
		{
			// No answer for a long while (they're away): taken as a no, so the duel goes on
			if (phaseTicks > MercyAnswerTicks + 10 * 60)
				OnMercyAnswer(false);
		}

		private void OnMercyAnswer(bool yes)
		{
			if (!duelAskPending)
				return;
			duelAskPending = false;
			string name = Main.player[duelWith].name;
			var lines = yes && duelEnc != null ? duelEnc.Accepted(duelAskAct, duelAskAmount)
				: new List<string> { $"* {name} refused." };
			Sfx(yes ? "boost" : "cantselect");
			if (phase == Phase.MercyWait)
				ShowMessages(lines, StartEnemyTurn);
		}

		private void UpdateMercyPrompt()
		{
			if (Pressed(Keys.Left) || Pressed(Keys.Right))
			{
				duelPromptChoice = 1 - duelPromptChoice;
				Sfx("menumove");
			}
			bool timedOut = phaseTicks > MercyAnswerTicks;
			if (!Confirm && !timedOut)
				return;
			bool yes = duelPromptChoice == 0 && !timedOut;
			Sfx("select");
			BattleNet.SendDuel(BattleNet.DuelKind.MercyAnswer, w => w.Write(yes));
			SetText(yes ? "* You accepted." : "* You refused.");
			SetPhase(Phase.DuelWait);
		}

		private void DrawMercyPrompt(float y)
		{
			DrDraw.Text(duelPromptText ?? "", 30, y, Color.White);
			float oy = y + 66;
			string[] options = { "ACCEPT", "REFUSE" };
			for (int i = 0; i < 2; i++)
			{
				float ox = 80 + i * 230;
				DrDraw.Text(options[i], ox, oy, Color.White);
				if (i == duelPromptChoice)
					DrawHeartCursor(ox - 25, oy + 10);
			}
			int left = Math.Max(0, (MercyAnswerTicks - phaseTicks + 59) / 60);
			if (left <= 10)
				DrDraw.Text($"{left}s", 560, oy, new Color(255, 220, 64), DrDraw.SmallFont);
		}

		// ================================================================== the opponent's poses, weapons and shots

		private HeroPose duelSentPose = HeroPose.Idle;
		private int duelSentItem = -1;
		/// <summary>The opponent as they told us: their pose, since when, what they hold, and their last swing or shot.</summary>
		private HeroPose duelOppPose = HeroPose.Idle;
		private uint duelOppPoseSince;
		private uint duelOppAttackAt;
		private int duelOppItem;

		/// <summary>We swung or shot (FIGHT, or a piece placed): they see it from us. <paramref name="at"/> null: at them.</summary>
		private void DuelSendFire(int item, int proj, Vector2? at)
		{
			if (duelWith < 0 || duelOver)
				return;
			BattleNet.SendDuel(BattleNet.DuelKind.Fire, w =>
			{
				w.Write(item);
				w.Write(proj);
				w.Write((short)(at?.X ?? -1));
				w.Write((short)(at?.Y ?? -1));
			});
		}

		/// <summary>Where the opponent's weapon is, roughly (their hand, facing us).</summary>
		private Vector2 DuelOppHand => EnemyPosNow + new Vector2(-22f, -4f);

		private void OnDuelFire(int item, int proj, Vector2 at)
		{
			duelOppAttackAt = Main.GameUpdateCount;
			if (item > 0)
				duelOppItem = item;
			Net.BattleNet.AllyWeapons[duelWith] = duelOppItem;
			// Their weapon's own sound, like ours
			if (item > 0 && ContentSamples.ItemsByType.TryGetValue(item, out Item sample) && sample.UseSound is Terraria.Audio.SoundStyle use)
				AttackSfx.Vanilla(use);
			else
				Sfx("attack");
			Vector2 from = DuelOppHand;
			// At us: our heart; otherwise the spot in the box where the piece goes
			Vector2 to = at.X < 0 ? HeroFeetNow + new Vector2(0f, -40f) : at;
			if (IsBeamWeapon(ContentSamples.ItemsByType.TryGetValue(item, out Item beamItem) ? beamItem : null))
			{
				AddEffect(new MuzzleFlash(from));
				AddEffect(new BeamEffect(from, to, item == ItemID.LastPrism, 22f));
				return;
			}
			if (proj <= 0)
				return;
			AddEffect(new MuzzleFlash(from));
			AddEffect(new ShotProjectile(proj, from, to, 10f));
		}

		/// <summary>The opponent's pose to draw now: a fresh swing first, then what they told us.</summary>
		private (HeroPose, float) DuelOppPoseNow()
		{
			if (battleOver)
				return (HeroPose.Idle, 0f);
			uint sinceAttack = Main.GameUpdateCount - duelOppAttackAt;
			if (duelOppAttackAt != 0 && sinceAttack < 30)
				return (HeroPose.Attack, sinceAttack / (float)TicksPerFrame);
			return (duelOppPose, (Main.GameUpdateCount - duelOppPoseSince) / (float)TicksPerFrame);
		}

		/// <summary>The opponent's held beam (their Last Prism in FIGHT), aimed at us.</summary>
		private bool duelOppBeamOn;
		private float duelOppBeamCharge;
		private int duelOppBeamItem;
		private bool duelSentBeamOn;
		private uint duelOppBeamSeen;

		private void SendBeamState()
		{
			if (duelWith < 0 || duelOver)
				return;
			bool on = beamMode && beamOn && phase == Phase.FightBar;
			if (on == duelSentBeamOn && (!on || time % 8 != 0))
				return;
			duelSentBeamOn = on;
			BattleNet.SendDuel(BattleNet.DuelKind.BeamState, w =>
			{
				w.Write(on);
				w.Write(beamCharge);
				w.Write(fightWeapon?.Item?.type ?? 0);
			});
		}

		/// <summary>Their beam, from their prism to us.</summary>
		private void DrawDuelOppBeam()
		{
			if (duelWith < 0)
				return;
			// A lost "off" message can't leave it on for good
			if (duelOppBeamOn && Main.GameUpdateCount - duelOppBeamSeen > 40)
				duelOppBeamOn = false;
			if (!duelOppBeamOn)
				return;
			bool prism = duelOppBeamItem == ItemID.LastPrism;
			Vector2 to = HeroFeetNow + new Vector2(0f, -40f);
			WeaponBeam.Held(DuelOppHand, to, duelOppBeamCharge, prism, 1f);
			if (time % 3 == 0)
				Sparks.Burst(this, to + Main.rand.NextVector2Circular(10f, 10f), 2, prism ? WeaponBeam.Rainbow(Main.rand.NextFloat()) : new Color(90, 220, 255), 2f);
		}

		/// <summary>Drawing the duel opponent mid-flinch (their hurt frame).</summary>
		private bool duelDrawingHurt;

		/// <summary>Ticks since the opponent was hit in their box (their flinch on our screen), -1: not hurt.</summary>
		private int duelOppHurt = -1;

		/// <summary>A bullet of theirs hit our SOUL: they see the number and us flinch.</summary>
		private void DuelSendHurt(int dealt)
		{
			if (duelWith < 0 || duelOver)
				return;
			BattleNet.SendDuel(BattleNet.DuelKind.Hurt, w => w.Write(dealt));
		}

		/// <summary>Our turn's over (our box closed): the other one chooses, and we wait.</summary>
		private void DuelBoxClosed()
		{
			inv = -1;
			duelMyTurn = false;
			duelLive = null;
			BattleNet.SendDuel(BattleNet.DuelKind.YourTurn);
			EnterDuelWait();
		}

		private void EnterDuelWait()
		{
			defending = false;
			faceAction = FaceNone;
			if (heroPose == HeroPose.Defend)
				SetHeroPose(HeroPose.Idle);
			SetText($"* {Main.player[duelWith].name} is deciding what to do...");
			SetPhase(Phase.DuelWait);
		}

		/// <summary>Lines of our own text box, for the other player to read while they wait.</summary>
		private void DuelShareText(string line)
		{
			if (duelWith < 0 || duelOver || !duelMyTurn || string.IsNullOrEmpty(line))
				return;
			BattleNet.SendDuel(BattleNet.DuelKind.Text, w => w.Write(line));
		}

		/// <summary>Our FIGHT (or summon) landed: the other player takes it.</summary>
		private void DuelSendHit(int damage, bool crit)
		{
			if (duelWith < 0 || duelOver)
				return;
			BattleNet.SendDuel(BattleNet.DuelKind.Hit, w =>
			{
				w.Write(damage);
				w.Write(crit);
			});
		}

		/// <summary>Their FIGHT landed on us: real damage (their side already took our defense off).</summary>
		private void TakeDuelHit(int damage)
		{
			if (Player.dead || phase == Phase.Death || damage <= 0)
				return;
			Player.immune = false;
			Player.immuneTime = 0;
			HurtingPlayer = true;
			double dealt;
			try
			{
				string by = Main.player[duelWith].name;
				dealt = Player.Hurt(PlayerDeathReason.ByCustomReason(NetworkText.FromLiteral($"{Player.name} was slain by {by}.")),
					damage, 0, knockback: 0f, scalingArmorPenetration: 1f);
			}
			finally
			{
				HurtingPlayer = false;
			}
			Player.immune = false;
			Player.immuneTime = 0;
			battleLife = Player.statLife;
			Sfx("hurt");
			hurtTimer = 0;
			shake = 4;
			HeroNumber((int)dealt, DuelOppDamageColor);
		}

		/// <summary>Our SOUL broke: the other player won.</summary>
		private void DuelOnDeath()
		{
			if (duelWith < 0 || duelOver)
				return;
			duelOver = true;
			BattleNet.SendDuel(BattleNet.DuelKind.Died);
		}

		/// <summary>SPARE with their MERCY full: the duel ends peacefully for both.</summary>
		private void DuelSpare()
		{
			faceAction = FaceSpare;
			string name = Main.player[duelWith].name;
			if (encounter.Mercy < 100f)
			{
				SetHeroPose(HeroPose.Act);
				ShowMessages(new[] { $"* You spared {name}!\n* But their name wasn't YELLOW..." }, StartEnemyTurn);
				return;
			}
			BattleNet.SendDuel(BattleNet.DuelKind.Spared);
			duelOver = true;
			targetEnemy.Out = true;
			battleOver = true;
			SetHeroPose(HeroPose.Victory);
			ShowMessages(new[] { $"* You spared {name}!\n* The duel ends peacefully." }, StartOutro);
		}

		/// <summary>Something from the other player.</summary>
		internal void OnDuel(BattleNet.DuelKind kind, BinaryReader r)
		{
			if (duelWith < 0 || !Active)
				return;
			switch (kind)
			{
				case BattleNet.DuelKind.Hp:
					duelHp = r.ReadInt16();
					duelHpMax = r.ReadInt16();
					duelDef = r.ReadInt16();
					break;
				case BattleNet.DuelKind.Soul:
				{
					duelRemoteSoul = new Vector2(r.ReadInt16(), r.ReadInt16());
					byte flags = r.ReadByte();
					if (!duelRemoteSoulSet)
						soul = duelRemoteSoul;
					duelRemoteSoulSet = true;
					// Their graze flash and hit blink, on the SOUL we draw while building
					if (phase == Phase.Build)
					{
						if ((flags & 0x80) != 0)
							grazeTimer = Math.Max(grazeTimer, 3 * TicksPerFrame);
						inv = (flags & 0x40) != 0 ? SoulBlinkTicks : 0;
					}
					break;
				}
				case BattleNet.DuelKind.MercyAsk:
				{
					string line = r.ReadString();
					// Only while we wait on them (anything else: no answer possible, so it's a no)
					if (duelOver || phase != Phase.DuelWait)
					{
						BattleNet.SendDuel(BattleNet.DuelKind.MercyAnswer, w => w.Write(false));
						break;
					}
					duelPromptText = line + "\n* Accept it?";
					duelPromptChoice = 0;
					Sfx("menumove");
					SetPhase(Phase.MercyPrompt);
					break;
				}
				case BattleNet.DuelKind.MercyAnswer:
					OnMercyAnswer(r.ReadBoolean());
					break;
				case BattleNet.DuelKind.Pose:
				{
					var pose = (HeroPose)r.ReadByte();
					int held = r.ReadInt32();
					if (pose != duelOppPose)
					{
						duelOppPose = pose;
						duelOppPoseSince = Main.GameUpdateCount;
					}
					duelOppItem = held;
					// Their weapon (or the item they use) in their hand, through the allies' drawing
					Net.BattleNet.AllyWeapons[duelWith] = held;
					Net.BattleNet.ReadyFaces[duelWith] = pose is HeroPose.Item or HeroPose.ItemReady ? FaceItem : FaceFight;
					break;
				}
				case BattleNet.DuelKind.Fire:
				{
					int item = r.ReadInt32();
					int proj = r.ReadInt32();
					var at = new Vector2(r.ReadInt16(), r.ReadInt16());
					if (!duelOver)
						OnDuelFire(item, proj, at);
					break;
				}
				case BattleNet.DuelKind.ForceSoul:
				{
					var mode = (SoulMode)r.ReadByte();
					if (duelOver || phase is not (Phase.EnemyIntro or Phase.EnemyTurn) || mode == soulMode)
						break;
					// Their doing: the SOUL changes mid-turn, with a flash
					BeginSoulMode(mode);
					if (attack != null)
						attack.Soul = mode;
					AddEffect(new Shockwave(SoulCenter, mode.Color(), 30f));
					Sfx("boost");
					break;
				}
				case BattleNet.DuelKind.BeamState:
				{
					duelOppBeamOn = r.ReadBoolean();
					duelOppBeamCharge = r.ReadSingle();
					duelOppBeamItem = r.ReadInt32();
					duelOppBeamSeen = Main.GameUpdateCount;
					if (duelOppBeamOn)
					{
						Net.BattleNet.AllyWeapons[duelWith] = duelOppBeamItem;
						if (time % 12 == 0)
							AttackSfx.Vanilla(SoundID.Item15 with { Volume = 0.3f + 0.2f * duelOppBeamCharge, Pitch = -0.2f + 0.5f * duelOppBeamCharge });
					}
					break;
				}
				case BattleNet.DuelKind.Hurt:
				{
					int dealt = r.ReadInt32();
					if (duelOver || phase != Phase.Build)
						break;
					// Their flinch and the number off them, like ours when we're hit
					Sfx("hurt");
					duelOppHurt = 0;
					// Our attack hit them: our colour, like our FIGHT's numbers
					EnemyNumber(dealt, HeroDamageColor, at: EnemyPosNow);
					break;
				}
				case BattleNet.DuelKind.Hit:
				{
					int damage = r.ReadInt32();
					r.ReadBoolean();
					if (!duelOver)
						TakeDuelHit(damage);
					break;
				}
				case BattleNet.DuelKind.BoxOpen:
					if (!duelOver && phase == Phase.DuelWait)
						EnterBuild();
					break;
				case BattleNet.DuelKind.Place:
				{
					DuelPiece piece = DuelPiece.Read(r);
					if (duelLive != null && phase is Phase.EnemyIntro or Phase.EnemyTurn)
						duelLive.Incoming.Enqueue(piece);
					break;
				}
				case BattleNet.DuelKind.BuildDone:
					if (duelLive != null)
						duelLive.BuilderDone = true;
					break;
				case BattleNet.DuelKind.YourTurn:
					if (!duelOver && phase is Phase.Build or Phase.DuelWait)
					{
						// Their SOUL's blink and graze were shown on ours: none of it carries into our box
						inv = -1;
						grazeTimer = 0;
						duelOppHurt = -1;
						soulMode = SoulMode.Red;
						SetHeroPose(HeroPose.Idle);
						Bullets.Clear();
						boxTimer = 0;
						duelMyTurn = true;
						BeginPlayerTurn();
					}
					break;
				case BattleNet.DuelKind.Text:
				{
					string line = r.ReadString();
					if (phase == Phase.DuelWait)
						SetText(line);
					break;
				}
				case BattleNet.DuelKind.Spared:
					if (duelOver)
						break;
					duelOver = true;
					targetEnemy.Out = true;
					battleOver = true;
					Bullets.Clear();
					SetHeroPose(HeroPose.Idle);
					ShowMessages(new[] { $"* {Main.player[duelWith].name} spared you.\n* The duel ends peacefully." }, StartOutro);
					break;
				case BattleNet.DuelKind.Died:
					if (duelOver)
						break;
					duelOver = true;
					duelHp = 0;
					SyncDuelProxy();
					// Our own FIGHT did it: the result screen says so when the bar is done
					if (phase is Phase.FightBar or Phase.FightResult)
						break;
					battleOver = true;
					targetEnemy.Out = true;
					Bullets.Clear();
					SetHeroPose(HeroPose.Victory);
					ShowMessages(new[] { $"* YOU WON!\n* {Main.player[duelWith].name} was defeated." }, StartOutro);
					break;
			}
		}

		/// <summary>The other player left the duel (or the game).</summary>
		internal void OnDuelEnd()
		{
			if (duelWith < 0 || !Active || duelOver || phase is Phase.Death or Phase.Outro)
				return;
			duelOver = true;
			targetEnemy.Out = true;
			battleOver = true;
			Bullets.Clear();
			ShowMessages(new[] { $"* {Main.player[duelWith].name} left the battle." }, StartOutro);
		}

		/// <summary>The battle is closing: tell the server we're out, and put the proxy away.</summary>
		private void EndDuel()
		{
			if (duelWith < 0)
				return;
			BattleNet.SendDuelEnd();
			Net.BattleNet.AllyWeapons.Remove(duelWith);
			Net.BattleNet.ReadyFaces.Remove(duelWith);
			duelWith = -1;
			duelEnc = null;
			duelLive = null;
			DuelProxy.active = false;
			DuelProxy.life = 0;
		}

		/// <summary>The other player in place of the enemy sprite: facing us, gliding in from where they stood.</summary>
		private void DrawDuelOpponent(SpriteBatch sb, Matrix m)
		{
			if (duelWith < 0)
				return;
			Player o = Main.player[duelWith];
			if (!o.active)
				return;
			float glide = FlyProgress();
			float scale = MathHelper.Lerp(WorldPixelScale(), HeroScale, glide);
			Vector2 center = EnemyPosNow;
			if (enemyShake > 0)
				center.X += (enemyShake % 4 < 2 ? 1 : -1) * enemyShake / 2f;
			// Hit in their box: they slide back and flinch, mirrored from ours (they face left)
			if (duelOppHurt >= 0)
				center.X += 20f - Math.Min(2f, duelOppHurt / (float)TicksPerFrame / 2f) * 10f;
			Vector2 feet = center + new Vector2(0f, o.height / 2f * scale);
			float bob = (float)Math.Round(Math.Sin((time + 40) / 20f)) * glide;
			HeroLight = glide >= 1f ? Color.White : Color.Lerp(Lighting.GetColor(o.Center.ToTileCoordinates()), Color.White, glide);
			duelDrawingHurt = duelOppHurt >= 0;
			var (oppPose, oppTimer) = glide >= 1f ? DuelOppPoseNow() : (HeroPose.Idle, 0f);
			DrawPlayerPose(sb, m, o, feet + new Vector2(0f, bob), scale, oppPose, oppTimer, 0f, ally: true, facing: -1);
			duelDrawingHurt = false;
			HeroLight = Color.White;
			DrawSlash(center);
		}

		// ================================================================== the attack we dodge

		/// <summary>An attack built live by the other player: their pieces arrive and turn into bullets.</summary>
		private sealed class DuelAttack : EnemyAttack
		{
			public readonly Queue<DuelPiece> Incoming = new();
			public bool BuilderDone;
			/// <summary>The builder's own copy: shows the pieces, can't hurt anyone.</summary>
			public bool Preview;
			private int doneFor;

			public DuelAttack()
			{
				Duration = DuelAttackTicks;
				Soul = SoulMode.Red;
			}

			public override void Update(BattleSystem b, int tick)
			{
				while (Incoming.Count > 0)
					b.SpawnPiece(Incoming.Dequeue());
				if (Preview || !BuilderDone)
					return;
				// Finished: once the last of it has flown by, the box closes
				if (b.Bullets.All(x => x.Dead || !x.Harmful))
				{
					if (++doneFor > 30)
						b.turnTimer = Math.Min(b.turnTimer, 1);
				}
				else
					doneFor = 0;
			}
		}

		/// <summary>The longest an attack can run (the builder gets <see cref="DuelBuildTicks"/>, plus time for the last pieces).</summary>
		private const int DuelAttackTicks = 22 * 60;

		internal EnemyAttack NewDuelAttack()
		{
			duelLive = new DuelAttack();
			return duelLive;
		}

		/// <summary>We're in the box: the other player starts building.</summary>
		private void DuelBoxOpened()
		{
			if (duelWith >= 0 && !duelOver)
				BattleNet.SendDuel(BattleNet.DuelKind.BoxOpen);
		}

		// ================================================================== pieces

		internal enum PieceKind : byte { Slash, Thrust, Arrow, Spray, Orb, Minion, Bounce, Shot, Beam, Explosive }

		/// <summary>One placed attack piece: what, from which weapon, how hard, where and which way.</summary>
		internal struct DuelPiece
		{
			public PieceKind Kind;
			public int Item, Proj, Damage;
			public Vector2 At, Dir;
			/// <summary>Extra ticks before it comes (a line sweeps, a rain falls one by one).</summary>
			public int Delay;

			public void Write(BinaryWriter w)
			{
				w.Write((short)Delay);
				w.Write((byte)Kind);
				w.Write(Item);
				w.Write(Proj);
				w.Write((short)Damage);
				w.Write((short)At.X);
				w.Write((short)At.Y);
				w.Write(Dir.X);
				w.Write(Dir.Y);
			}

			public static DuelPiece Read(BinaryReader r) => new()
			{
				Delay = r.ReadInt16(),
				Kind = (PieceKind)r.ReadByte(),
				Item = r.ReadInt32(),
				Proj = r.ReadInt32(),
				Damage = r.ReadInt16(),
				At = new Vector2(r.ReadInt16(), r.ReadInt16()),
				Dir = new Vector2(r.ReadSingle(), r.ReadSingle()),
			};
		}

		/// <summary>How far a beam piece reaches.</summary>
		private const float BeamLength = 700f;

		private static Color PieceColor(DuelPiece pc) => KindColor(pc.Kind);

		/// <summary>The colour of a kind of weapon's sparks (the same in battles against monsters and in duels).</summary>
		private static Color KindColor(PieceKind kind) => kind switch
		{
			PieceKind.Arrow => new Color(220, 190, 140),
			PieceKind.Spray => new Color(255, 230, 120),
			PieceKind.Orb => new Color(230, 120, 255),
			PieceKind.Minion => new Color(120, 220, 255),
			PieceKind.Beam => WeaponBeam.Rainbow(0f),
			PieceKind.Explosive => new Color(255, 150, 60),
			_ => Color.White,
		};

		/// <summary>Ticks a piece shows as a warning before its bullets come.</summary>
		private const int PieceWarnTicks = 36;

		private static (Texture2D tex, Rectangle src) ItemTexture(int type)
		{
			if (type <= 0 || type >= TextureAssets.Item.Length)
				return (null, default);
			Main.instance.LoadItem(type);
			Texture2D tex = TextureAssets.Item[type].Value;
			Rectangle src = Main.itemAnimations[type] != null ? Main.itemAnimations[type].GetFrame(tex) : tex.Bounds;
			return (tex, src);
		}

		private static (Texture2D tex, Rectangle src) ProjTexture(int type)
		{
			if (type <= 0 || type >= TextureAssets.Projectile.Length)
				return (null, default);
			Main.instance.LoadProjectile(type);
			Texture2D tex = TextureAssets.Projectile[type].Value;
			int frames = Math.Max(1, Main.projFrames[type]);
			return (tex, new Rectangle(0, 0, tex.Width, tex.Height / frames));
		}

		/// <summary>A bullet showing a texture about <paramref name="size"/> px across.</summary>
		private static Bullet TexBullet((Texture2D tex, Rectangle src) t, Vector2 at, Vector2 vel, float size, float hit)
		{
			var b = new Bullet { Position = at, Velocity = vel, HitSize = new Vector2(hit) };
			if (t.tex != null && !Main.dedServ)
			{
				b.Texture = t.tex;
				b.Source = t.src;
				b.Scale = size / Math.Max(1f, Math.Max(t.src.Width, t.src.Height));
			}
			return b;
		}

		/// <summary>A piece's bullets (the same on both screens), after a short warning where it'll come from.</summary>
		private void SpawnPiece(DuelPiece pc)
		{
			Vector2 dir = pc.Dir.LengthSquared() > 0.001f ? Vector2.Normalize(pc.Dir) : Vector2.UnitX;
			// The warning: a flashing ring with a line the way it'll go
			Spawn(new Bullet
			{
				Position = pc.At,
				Harmful = false,
				StartDelay = pc.Delay,
				Lifetime = PieceWarnTicks,
				OnDraw = b =>
				{
					float a = b.Age / 4 % 2 == 0 ? 0.9f : 0.4f;
					DrDraw.Outline(b.Position.X - 9, b.Position.Y - 9, 18, 18, new Color(255, 80, 80) * a, 2);
					// A beam warns along its whole length
					DrDraw.Line(b.Position, b.Position + dir * (pc.Kind == PieceKind.Beam ? BeamLength : 40f), 2f, new Color(255, 80, 80) * a * 0.7f);
				},
			});
			var item = ItemTexture(pc.Item);
			var proj = pc.Proj > 0 ? ProjTexture(pc.Proj) : item;
			var bullets = new List<Bullet>();
			switch (pc.Kind)
			{
				case PieceKind.Slash:
				{
					Bullet b = TexBullet(item, pc.At, dir * 4.5f, 32f, 18f);
					b.OnUpdate = x => x.Rotation += 0.35f;
					b.Trail = 4;
					bullets.Add(b);
					break;
				}
				case PieceKind.Thrust:
				{
					Bullet b = TexBullet(item, pc.At, dir * 8.5f, 32f, 12f);
					b.RotateWithVelocity = true;
					b.RotationOffset = MathHelper.PiOver4;
					b.Trail = 5;
					bullets.Add(b);
					break;
				}
				case PieceKind.Arrow:
					foreach (float spread in new[] { -0.22f, 0f, 0.22f })
					{
						Bullet b = TexBullet(proj, pc.At, dir.RotatedBy(spread) * 6f, 22f, 8f);
						b.RotateWithVelocity = true;
						b.RotationOffset = MathHelper.PiOver2;
						bullets.Add(b);
					}
					break;
				case PieceKind.Spray:
					foreach (float spread in new[] { -0.36f, -0.18f, 0f, 0.18f, 0.36f })
						bullets.Add(new Bullet { Position = pc.At, Velocity = dir.RotatedBy(spread) * 7f, HitSize = new Vector2(6), Color = new Color(255, 230, 120) });
					break;
				case PieceKind.Orb:
				{
					Bullet b = TexBullet(proj, pc.At, dir * 2.6f, 24f, 12f);
					b.Lifetime = 320;
					b.Trail = 4;
					b.OnUpdate = x =>
					{
						x.Rotation += 0.1f;
						if (x.Age < 200)
							x.Velocity = Vector2.Lerp(x.Velocity, (SoulCenter - x.Position).SafeNormalize(Vector2.UnitX) * 2.6f, 0.03f);
					};
					bullets.Add(b);
					break;
				}
				case PieceKind.Beam:
				{
					// Held on one line for a moment: thin while it charges, then wide; it hurts once it's wide
					bool prism = pc.Item == ItemID.LastPrism;
					Vector2 end = pc.At + dir * BeamLength;
					const int life = 70;
					bullets.Add(new Bullet
					{
						Position = pc.At,
						HitSize = new Vector2(8),
						Lifetime = life,
						DestroyOnHit = false,
						OffscreenMargin = 2000f,
						HitTest = (x, area) => x.Age > life * 0.2f && x.Age < life * 0.85f && WeaponBeam.Touches(pc.At, end, 6f, area),
						OnDraw = x => WeaponBeam.Draw(pc.At, end, x.Age / (float)life, prism, 1f),
						SoundOnSpawn = true,
					});
					break;
				}
				case PieceKind.Explosive:
				{
					// Flies a little way, then bursts: a ring of shrapnel and a shockwave
					Bullet b = TexBullet(proj, pc.At, dir * 3.4f, 22f, 10f);
					b.RotateWithVelocity = true;
					b.RotationOffset = MathHelper.PiOver2;
					b.Trail = 3;
					int dmg = Math.Max(1, pc.Damage);
					b.OnUpdate = x =>
					{
						if (x.Age < 45)
							return;
						x.Dead = true;
						AddEffect(new Shockwave(x.Position, new Color(255, 150, 60), 46f));
						Sparks.Burst(this, x.Position, 14, new Color(255, 190, 80), 3.5f);
						ShakeScreen(2f);
						Terraria.Audio.SoundEngine.PlaySound(SoundID.Item14 with { Volume = 0.6f });
						for (int i = 0; i < 8; i++)
						{
							Vector2 v = (MathHelper.TwoPi * i / 8f).ToRotationVector2() * 3.6f;
							Spawn(new Bullet { Position = x.Position, Velocity = v, HitSize = new Vector2(6), Color = new Color(255, 170, 70), DamageMult = dmg, Lifetime = 90 });
						}
					};
					bullets.Add(b);
					break;
				}
				case PieceKind.Minion when pc.Proj == ProjectileID.StardustDragon1:
					bullets.Add(DragonBullet(pc.At, dir));
					break;
				case PieceKind.Minion:
				{
					Bullet b = TexBullet(proj, pc.At, dir * 1.9f, 28f, 14f);
					b.Trail = 4;
					b.Lifetime = 260;
					b.OnUpdate = x =>
					{
						x.Velocity = Vector2.Lerp(x.Velocity, (SoulCenter - x.Position).SafeNormalize(Vector2.UnitX) * 1.9f, 0.06f);
						x.FlipX = x.Velocity.X < 0f;
					};
					bullets.Add(b);
					break;
				}
				case PieceKind.Bounce:
				{
					Bullet b = TexBullet(item, pc.At, dir * 4.2f, 26f, 14f);
					b.Lifetime = 280;
					b.OnUpdate = x =>
					{
						x.Rotation += 0.25f;
						Rectangle box = Box;
						if (x.Position.X < box.Left + 6 && x.Velocity.X < 0 || x.Position.X > box.Right - 6 && x.Velocity.X > 0)
							x.Velocity.X = -x.Velocity.X;
						if (x.Position.Y < box.Top + 6 && x.Velocity.Y < 0 || x.Position.Y > box.Bottom - 6 && x.Velocity.Y > 0)
							x.Velocity.Y = -x.Velocity.Y;
					};
					bullets.Add(b);
					break;
				}
				default:
				{
					Bullet b = TexBullet(item, pc.At, dir * 6f, 24f, 10f);
					b.RotateWithVelocity = true;
					b.RotationOffset = MathHelper.PiOver4;
					bullets.Add(b);
					break;
				}
			}
			// A puff where the piece comes in, in its colour
			if (bullets.Count > 0)
			{
				Bullet first = bullets[0];
				Action<Bullet> then = first.OnUpdate;
				Color puff = PieceColor(pc);
				first.OnUpdate = x =>
				{
					if (x.Age == 0)
						Sparks.Burst(this, x.Position, 7, puff, 2.2f);
					then?.Invoke(x);
				};
			}
			foreach (Bullet b in bullets)
			{
				b.StartDelay = PieceWarnTicks + pc.Delay;
				b.DamageMult = Math.Max(1, pc.Damage);
				b.OffscreenMargin = 120f;
				Spawn(b);
			}
		}

		/// <summary>
		/// The Stardust Dragon as a piece: its head chases the SOUL and its body and tail follow its path, each piece
		/// turned along it. Every piece of it hurts.
		/// </summary>
		private Bullet DragonBullet(Vector2 at, Vector2 dir)
		{
			const int segments = 6;
			const float spacing = 13f, size = 24f;
			var path = new List<Vector2> { at };
			var spots = new Vector2[segments + 1];
			var rots = new float[segments + 1];
			var head = ProjTexture(ProjectileID.StardustDragon1);
			var body = ProjTexture(ProjectileID.StardustDragon2);
			var body2 = ProjTexture(ProjectileID.StardustDragon3);
			var tail = ProjTexture(ProjectileID.StardustDragon4);

			void Lay(Bullet x)
			{
				// Pieces a spacing apart along the path the head took
				int j = path.Count - 1;
				float walked = 0f;
				Vector2 last = x.Position;
				for (int i = 0; i <= segments; i++)
				{
					float want = i * spacing;
					while (j > 0 && walked + Vector2.Distance(path[j], path[j - 1]) < want)
					{
						walked += Vector2.Distance(path[j], path[j - 1]);
						j--;
					}
					spots[i] = path[j];
					Vector2 d = i == 0 ? x.Velocity : last - spots[i];
					rots[i] = d.LengthSquared() > 0.01f ? d.ToRotation() + MathHelper.PiOver2 : rots[Math.Max(0, i - 1)];
					last = spots[i];
				}
			}

			Bullet b = TexBullet(head, at, dir * 2.2f, size, 14f);
			b.Lifetime = 300;
			b.OnUpdate = x =>
			{
				x.Velocity = Vector2.Lerp(x.Velocity, (SoulCenter - x.Position).SafeNormalize(Vector2.UnitX) * 2.2f, 0.05f);
				path.Add(x.Position + x.Velocity);
				if (path.Count > 400)
					path.RemoveAt(0);
				Lay(x);
			};
			b.HitTest = (x, area) =>
			{
				for (int i = 0; i <= segments; i++)
				{
					var r = new Rectangle((int)(spots[i].X - 6), (int)(spots[i].Y - 6), 12, 12);
					if (r.Intersects(area))
						return true;
				}
				return false;
			};
			b.OnDraw = x =>
			{
				if (spots[0] == Vector2.Zero)
					Lay(x);
				// Tail first, the head on top
				for (int i = segments; i >= 0; i--)
				{
					var t = i == 0 ? head : i == segments ? tail : i % 2 == 1 ? body : body2;
					if (t.tex == null || Main.dedServ)
					{
						DrDraw.Rect(spots[i].X - 5, spots[i].Y - 5, 10, 10, Color.White * x.Alpha);
						continue;
					}
					float sc = size / Math.Max(1f, Math.Max(t.src.Width, t.src.Height));
					DrDraw.Sb.Draw(t.tex, spots[i], t.src, Color.White * x.Alpha, rots[i], t.src.Size() / 2f, sc, SpriteEffects.None, 0f);
				}
			};
			return b;
		}

		// ================================================================== building (the other player is in the box)

		/// <summary>An attack piece the builder can place: one per kind of weapon they carry, from their best one.</summary>
		private sealed class PieceOption
		{
			public PieceKind Kind;
			public Item Item;
			public int Proj, Damage, Cost;
			public string Label;
		}

		private readonly List<PieceOption> pieceOptions = new();
		private int piecePick;
		private float buildInk;
		private int buildTicks;
		private bool buildDone;
		private Vector2? dragFrom;
		private bool mouseWasDown;
		private DuelAttack buildPreview;

		private const int DuelBuildTicks = 15 * 60;
		private const float InkMax = 100f, InkStart = 45f, InkPerTick = 0.3f;

		private static readonly Dictionary<PieceKind, (string label, int cost)> PieceInfo = new()
		{
			[PieceKind.Slash] = ("SLASH", 25),
			[PieceKind.Thrust] = ("THRUST", 20),
			[PieceKind.Arrow] = ("ARROWS", 25),
			[PieceKind.Spray] = ("SPRAY", 30),
			[PieceKind.Orb] = ("ORB", 35),
			[PieceKind.Minion] = ("MINION", 40),
			[PieceKind.Bounce] = ("BOUNCE", 30),
			[PieceKind.Shot] = ("SHOT", 15),
			[PieceKind.Beam] = ("BEAM", 40),
			[PieceKind.Explosive] = ("BOOM", 35),
		};

		private static PieceKind Classify(Item it)
		{
			if (IsBeamWeapon(it))
				return PieceKind.Beam;
			if (it.useAmmo == AmmoID.Rocket || it.shoot is ProjectileID.Grenade or ProjectileID.StickyGrenade or ProjectileID.BouncyGrenade
				or ProjectileID.Bomb or ProjectileID.StickyBomb or ProjectileID.BouncyBomb or ProjectileID.Dynamite or ProjectileID.StickyDynamite
				or ProjectileID.BouncyDynamite or ProjectileID.Beenade or ProjectileID.PartyGirlGrenade or ProjectileID.MolotovCocktail)
				return PieceKind.Explosive;
			if (it.shoot > ProjectileID.None && ProjectileID.Sets.IsAWhip[it.shoot])
				return PieceKind.Thrust;
			if (it.CountsAsClass(DamageClass.Summon))
				return PieceKind.Minion;
			if (it.useAmmo == AmmoID.Arrow)
				return PieceKind.Arrow;
			if (it.useAmmo == AmmoID.Bullet)
				return PieceKind.Spray;
			if (it.CountsAsClass(DamageClass.Magic))
				return PieceKind.Orb;
			if (it.shoot > ProjectileID.None && it.noUseGraphic)
				return ItemID.Sets.Spears[it.type] ? PieceKind.Thrust : PieceKind.Bounce;
			if (it.CountsAsClass(DamageClass.Melee))
				return PieceKind.Slash;
			return PieceKind.Shot;
		}

		/// <summary>The pieces this player's weapons give: the strongest weapon of each kind.</summary>
		private void RefreshPieces()
		{
			pieceOptions.Clear();
			var best = new Dictionary<PieceKind, Item>();
			for (int i = 0; i < 50; i++)
			{
				Item it = Player.inventory[i];
				if (it == null || it.IsAir || it.damage <= 0 || it.accessory || it.ammo > 0 || it.useStyle == ItemUseStyleID.None || it.createTile >= 0 && it.pick <= 0)
					continue;
				if (it.consumable && it.shoot <= ProjectileID.None)
					continue;
				PieceKind k = Classify(it);
				if (!best.TryGetValue(k, out Item have) || Player.GetWeaponDamage(it) > Player.GetWeaponDamage(have))
					best[k] = it;
			}
			// Every kind (the palette scrolls)
			foreach (var (k, it) in best.OrderBy(kv => (int)kv.Key))
			{
				int proj = it.shoot is > ProjectileID.None and not ProjectileID.AbigailCounter and not ProjectileID.StormTigerGem ? it.shoot : 0;
				if (k == PieceKind.Arrow)
					proj = it.shoot > ProjectileID.None && it.shoot != ProjectileID.WoodenArrowFriendly ? it.shoot : ProjectileID.WoodenArrowFriendly;
				pieceOptions.Add(new PieceOption
				{
					Kind = k,
					Item = it,
					Proj = proj,
					Damage = Math.Max(1, (int)(Player.GetWeaponDamage(it) * 0.5f)),
					Cost = PieceInfo[k].cost,
					Label = PieceInfo[k].label,
				});
			}
			// Nothing to fight with: fists
			if (pieceOptions.Count == 0)
				pieceOptions.Add(new PieceOption { Kind = PieceKind.Shot, Item = null, Proj = 0, Damage = 3, Cost = 15, Label = "PUNCH" });
			piecePick = Math.Clamp(piecePick, 0, pieceOptions.Count - 1);
		}

		private void EnterBuild()
		{
			Bullets.Clear();
			boxAfterimages.Clear();
			RefreshPieces();
			buildInk = InkStart;
			buildTicks = DuelBuildTicks;
			buildDone = false;
			dragFrom = null;
			mouseWasDown = Main.mouseLeft;
			buildPreview = new DuelAttack { Preview = true };
			soulForceCooldown = 0;
			pieceFirst = Math.Clamp(Math.Min(pieceFirst, piecePick), Math.Max(0, piecePick - PiecesShown + 1), Math.Max(0, pieceOptions.Count - PiecesShown));
			buildScroll = 0;
			boxTimer = 0;
			if (!duelRemoteSoulSet)
				soul = new Vector2(BoxCenterX - SoulSize / 2f, BoxCenterY - SoulSize / 2f);
			BeginSoulMode(SoulMode.Red);
			SetText("");
			SetPhase(Phase.Build);
			SetHeroPose(HeroPose.AttackReady);
			Sfx("boost");
		}

		/// <summary>The mouse on the battle screen.</summary>
		private Vector2 MouseBattle()
		{
			ComputeScreenTransform();
			// The raw window position (Main.mouseX can be in UI-scaled units, the battle is drawn unscaled)
			return new Vector2((Terraria.GameInput.PlayerInput.MouseX - drOx) / drScale, (Terraria.GameInput.PlayerInput.MouseY - drOy) / drScale);
		}

		private float PaletteTop => ScreenHeight - panel + 44f;

		// ---- forcing the dodger's SOUL mode ----

		/// <summary>Ticks until the builder can change the SOUL again.</summary>
		private int soulForceCooldown;
		private const int SoulForceCooldownTicks = 6 * 60;
		private static readonly SoulMode[] ForceModes = { SoulMode.Red, SoulMode.Blue, SoulMode.Green, SoulMode.Purple, SoulMode.Yellow };

		private Rectangle SoulButton(int i) => new(540 + i * 17, (int)PaletteTop + 28, 15, 15);

		private void ForceSoul(SoulMode mode)
		{
			if (soulForceCooldown > 0 || buildDone)
			{
				Sfx("cantselect");
				return;
			}
			if (mode == soulMode)
				return;
			soulForceCooldown = SoulForceCooldownTicks;
			// Ours shows it at once; theirs changes when it arrives
			soulMode = mode;
			AddEffect(new Shockwave(SoulCenter, mode.Color(), 30f));
			Sfx("boost");
			BattleNet.SendDuel(BattleNet.DuelKind.ForceSoul, w => w.Write((byte)mode));
		}

		/// <summary>Weapons shown at once in the palette (it scrolls when there are more).</summary>
		private const int PiecesShown = 5;
		private int pieceFirst;
		/// <summary>Mouse wheel turned since the builder last looked (kept from PostUpdateInput).</summary>
		private int buildScroll;

		/// <summary>The palette slot of a piece (null: scrolled out of view).</summary>
		private Rectangle? PieceButton(int i)
		{
			int slot = i - pieceFirst;
			if (slot < 0 || slot >= PiecesShown)
				return null;
			return new Rectangle(28 + slot * 86, (int)PaletteTop - 6, 82, 56);
		}

		private Rectangle ScrollLeft => new(6, (int)PaletteTop + 12, 18, 22);
		private Rectangle ScrollRight => new(460, (int)PaletteTop + 12, 18, 22);
		private Rectangle ShapeButton(int i) => new(28 + i * 72, (int)PaletteTop + 56, 68, 24);
		private Rectangle DoneButton => new(540, (int)PaletteTop + 50, 84, 30);

		private void PickPiece(int i)
		{
			if (pieceOptions.Count == 0)
				return;
			piecePick = Math.Clamp(i, 0, pieceOptions.Count - 1);
			// Keep it in view
			if (piecePick < pieceFirst)
				pieceFirst = piecePick;
			if (piecePick >= pieceFirst + PiecesShown)
				pieceFirst = piecePick - PiecesShown + 1;
			pieceFirst = Math.Clamp(pieceFirst, 0, Math.Max(0, pieceOptions.Count - PiecesShown));
			Sfx("menumove");
		}

		// ---- shapes: one weapon placed several times at once ----

		internal enum Shape { Single, Line, Ring, Rain, Fan, Stream }

		private Shape shape = Shape.Single;

		private static readonly (Shape shape, string label, float costMul)[] Shapes =
		{
			(Shape.Single, "SINGLE", 1f),
			(Shape.Line, "LINE", 2.4f),
			(Shape.Ring, "RING", 3.2f),
			(Shape.Rain, "RAIN", 2.8f),
			(Shape.Fan, "FAN", 2.4f),
			(Shape.Stream, "STREAM", 2.2f),
		};

		private static float CostMul(Shape s) => Shapes.First(x => x.shape == s).costMul;

		private int ShapeCost(PieceOption o) => (int)Math.Ceiling(o.Cost * CostMul(shape));

		/// <summary>
		/// Where each piece of the shape goes, which way, and how long after the first: from where the mouse went down
		/// (<paramref name="a"/>) to where it came up (<paramref name="b"/>). A plain click aims at the SOUL.
		/// </summary>
		private List<(Vector2 At, Vector2 Dir, int Delay)> ShapePieces(Vector2 a, Vector2 b)
		{
			var list = new List<(Vector2, Vector2, int)>();
			Vector2 drag = b - a;
			bool dragged = drag.Length() >= 8f;
			Vector2 aim = dragged ? drag : SoulCenter - a;
			if (aim.LengthSquared() < 0.01f)
				aim = Vector2.UnitX;
			aim.Normalize();
			switch (shape)
			{
				case Shape.Line:
				{
					// Dragged: along the drag, firing across it toward the SOUL. Clicked: a line across the aim
					Vector2 along = dragged ? Vector2.Normalize(drag) : new Vector2(-aim.Y, aim.X);
					float length = dragged ? drag.Length() : 110f;
					Vector2 start = dragged ? a : a - along * length / 2f;
					Vector2 across = new(-along.Y, along.X);
					if (Vector2.Dot(across, SoulCenter - (start + along * length / 2f)) < 0f)
						across = -across;
					for (int i = 0; i < 5; i++)
						list.Add((start + along * (length * i / 4f), across, i * 6));
					break;
				}
				case Shape.Ring:
				{
					// Around the spot, all closing in on it; the size is how far it was dragged
					float radius = dragged ? MathHelper.Clamp(drag.Length(), 40f, 160f) : 80f;
					for (int i = 0; i < 8; i++)
					{
						Vector2 off = (MathHelper.TwoPi * i / 8f).ToRotationVector2() * radius;
						list.Add((a + off, -off, i * 3));
					}
					break;
				}
				case Shape.Rain:
				{
					// From above the box, spread around the spot, falling (or the way it was dragged)
					Rectangle box = Box;
					Vector2 fall = dragged ? aim : Vector2.UnitY;
					for (int i = 0; i < 6; i++)
					{
						float x = a.X - 75f + i * 30f;
						list.Add((new Vector2(x, box.Top - 24f), fall, (i * 7 + i * i * 3) % 30));
					}
					break;
				}
				case Shape.Fan:
					for (int i = 0; i < 5; i++)
						list.Add((a, aim.RotatedBy((i - 2) * 0.32f), 0));
					break;
				case Shape.Stream:
					for (int i = 0; i < 4; i++)
						list.Add((a, aim, i * 12));
					break;
				default:
					list.Add((a, aim, 0));
					break;
			}
			return list;
		}

		private void UpdateBuild()
		{
			boxTimer = Math.Min(BoxGrowTicks, boxTimer + 1);
			if (duelRemoteSoulSet)
				soul = Vector2.Lerp(soul, duelRemoteSoul, 0.5f);
			buildPreview?.Update(this, phaseTicks);
			// The preview's bullets move like the real ones, but nothing hits
			updatingBullets = true;
			foreach (Bullet b in Bullets)
				b.Update();
			updatingBullets = false;
			Bullets.AddRange(spawnedDuringUpdate);
			spawnedDuringUpdate.Clear();
			Bullets.RemoveAll(b => b.Dead);

			// After a swing, back to holding the piece's weapon ready
			if (heroPose == HeroPose.Attack && heroTimer > 20f)
				SetHeroPose(buildDone ? HeroPose.Idle : HeroPose.AttackReady);
			if (buildDone)
				return;
			buildInk = Math.Min(InkMax, buildInk + InkPerTick);
			if (soulForceCooldown > 0)
				soulForceCooldown--;
			if (--buildTicks <= 0)
			{
				FinishBuild();
				return;
			}
			// Number keys pick a weapon, the wheel scrolls through them
			for (int i = 0; i < Math.Min(9, pieceOptions.Count); i++)
				if (Main.keyState.IsKeyDown(Keys.D1 + i) && !Main.oldKeyState.IsKeyDown(Keys.D1 + i))
					PickPiece(i);
			if (buildScroll != 0)
			{
				PickPiece(piecePick + (buildScroll < 0 ? 1 : -1));
				buildScroll = 0;
			}
			// Q / E: the shape
			int shapeStep = (Main.keyState.IsKeyDown(Keys.E) && !Main.oldKeyState.IsKeyDown(Keys.E) ? 1 : 0)
				- (Main.keyState.IsKeyDown(Keys.Q) && !Main.oldKeyState.IsKeyDown(Keys.Q) ? 1 : 0);
			if (shapeStep != 0)
			{
				shape = (Shape)(((int)shape + shapeStep + Shapes.Length) % Shapes.Length);
				Sfx("menumove");
			}

			Player.mouseInterface = true;
			bool down = Main.mouseLeft;
			Vector2 mouse = MouseBattle();
			if (down && !mouseWasDown)
			{
				// The palette and the DONE button, or the start of a placement
				Point mp = mouse.ToPoint();
				int hit = Enumerable.Range(0, pieceOptions.Count).FirstOrDefault(i => PieceButton(i)?.Contains(mp) == true, -1);
				int shapeHit = Enumerable.Range(0, Shapes.Length).FirstOrDefault(i => ShapeButton(i).Contains(mp), -1);
				int soulHit = Enumerable.Range(0, ForceModes.Length).FirstOrDefault(i => SoulButton(i).Contains(mp), -1);
				if (soulHit >= 0)
					ForceSoul(ForceModes[soulHit]);
				else if (hit >= 0)
					PickPiece(hit);
				else if (shapeHit >= 0)
				{
					shape = Shapes[shapeHit].shape;
					Sfx("menumove");
				}
				else if (ScrollLeft.Contains(mp))
					PickPiece(Math.Min(piecePick - 1, pieceFirst - 1));
				else if (ScrollRight.Contains(mp))
					PickPiece(Math.Max(piecePick + 1, pieceFirst + PiecesShown));
				else if (DoneButton.Contains(mp))
					FinishBuild();
				else if (mouse.Y < PaletteTop - 10f)
					dragFrom = mouse;
			}
			if (!down && mouseWasDown && dragFrom is Vector2 from)
			{
				dragFrom = null;
				PlacePiece(from, mouse);
			}
			// Right click: let go of a placement
			if (Main.mouseRight)
				dragFrom = null;
			mouseWasDown = down;
		}

		private void PlacePiece(Vector2 at, Vector2 to)
		{
			if (pieceOptions.Count == 0)
				return;
			PieceOption o = pieceOptions[Math.Clamp(piecePick, 0, pieceOptions.Count - 1)];
			int cost = ShapeCost(o);
			if (buildInk < cost)
			{
				Sfx("cantselect");
				return;
			}
			buildInk -= cost;
			// Every piece of the shape, to them and into our preview
			foreach (var (pat, pdir, delay) in ShapePieces(at, to))
			{
				var piece = new DuelPiece { Kind = o.Kind, Item = o.Item?.type ?? 0, Proj = o.Proj, Damage = o.Damage, At = pat, Dir = pdir, Delay = delay };
				BattleNet.SendDuel(BattleNet.DuelKind.Place, piece.Write);
				buildPreview?.Incoming.Enqueue(piece);
			}
			// We swing or shoot it, here and on their screen
			int shot = PieceShot(o);
			SetHeroPose(HeroPose.Attack);
			if (o.Item?.UseSound is Terraria.Audio.SoundStyle use)
				AttackSfx.Vanilla(use);
			Vector2 muzzle = HeroFeetNow + new Vector2(28f, -36f);
			if (o.Kind == PieceKind.Beam)
			{
				AddEffect(new MuzzleFlash(muzzle));
				AddEffect(new BeamEffect(muzzle, at, o.Item?.type == ItemID.LastPrism, 22f));
			}
			else if (shot > 0)
			{
				AddEffect(new MuzzleFlash(muzzle));
				AddEffect(new ShotProjectile(shot, muzzle, at, 10f));
			}
			DuelSendFire(o.Item?.type ?? 0, shot, at);
			Sfx("select");
		}

		/// <summary>What flies from the builder to the spot as they place it (nothing for melee: they just swing).</summary>
		private static int PieceShot(PieceOption o) => o.Kind switch
		{
			PieceKind.Arrow or PieceKind.Orb or PieceKind.Shot => o.Proj,
			PieceKind.Spray => o.Proj > 0 ? o.Proj : ProjectileID.Bullet,
			PieceKind.Explosive => o.Proj,
			_ => 0,
		};

		private void FinishBuild()
		{
			if (buildDone)
				return;
			buildDone = true;
			SetHeroPose(HeroPose.Idle);
			dragFrom = null;
			BattleNet.SendDuel(BattleNet.DuelKind.BuildDone);
			Sfx("select");
		}

		/// <summary>Over the battle screen while building: the placement being dragged and the piece at the mouse.</summary>
		private void DrawBuildOverlay()
		{
			if (phase != Phase.Build || buildDone || pieceOptions.Count == 0)
				return;
			Vector2 mouse = MouseBattle();
			PieceOption o = pieceOptions[Math.Clamp(piecePick, 0, pieceOptions.Count - 1)];
			bool afford = buildInk >= ShapeCost(o);
			Color c = afford ? Color.White : new Color(255, 80, 80);
			if (mouse.Y >= PaletteTop - 10f && dragFrom == null)
				return;
			// Where every piece of the shape would go, and which way
			Vector2 from = dragFrom ?? mouse;
			if (dragFrom != null)
				DrDraw.Line(from, mouse, 1f, c * 0.4f);
			foreach (var (pat, pdir, _) in ShapePieces(from, dragFrom != null ? mouse : from))
			{
				DrawPieceIcon(o, pat, 18f, 0.55f);
				DrDraw.Line(pat, pat + Vector2.Normalize(pdir) * 22f, 2f, c * 0.7f);
			}
		}

		private void DrawPieceIcon(PieceOption o, Vector2 at, float size, float alpha)
		{
			var t = o.Proj > 0 && o.Kind is PieceKind.Arrow or PieceKind.Orb or PieceKind.Minion ? ProjTexture(o.Proj) : ItemTexture(o.Item?.type ?? 0);
			if (t.tex == null || Main.dedServ)
			{
				DrDraw.Rect(at.X - 4, at.Y - 4, 8, 8, Color.White * alpha);
				return;
			}
			float scale = size / Math.Max(1f, Math.Max(t.src.Width, t.src.Height));
			DrDraw.Sb.Draw(t.tex, at, t.src, Color.White * alpha, 0f, t.src.Size() / 2f, scale, SpriteEffects.None, 0f);
		}

		/// <summary>The bottom panel while building: the pieces, the ink, the time left and DONE.</summary>
		private void DrawBuildPalette()
		{
			float top = PaletteTop;
			if (buildDone)
			{
				DrDraw.Text("* Your attack is set.\n* Watch them dodge it!", 30, top, Color.White);
				return;
			}
			// The hint goes at the top of the screen (the nameplate sits just above the panel)
			const string hint = "BUILD YOUR ATTACK: click to place, drag to aim/size  |  wheel or 1-9: weapon  |  Q/E: shape";
			DrDraw.Text(hint, ScreenWidth / 2f - DrDraw.Measure(hint, DrDraw.SmallFont) * 0.35f, 10, new Color(255, 220, 64), DrDraw.SmallFont, 0.7f);
			// Scroll arrows when there are more weapons than fit
			if (pieceFirst > 0)
				DrDraw.Text("<", ScrollLeft.X + 3, ScrollLeft.Y, Color.White, DrDraw.BigFont, 0.8f);
			if (pieceFirst + PiecesShown < pieceOptions.Count)
				DrDraw.Text(">", ScrollRight.X + 3, ScrollRight.Y, Color.White, DrDraw.BigFont, 0.8f);
			// The shapes, with what they cost for the picked weapon
			PieceOption picked = pieceOptions[Math.Clamp(piecePick, 0, pieceOptions.Count - 1)];
			for (int i = 0; i < Shapes.Length; i++)
			{
				Rectangle r = ShapeButton(i);
				bool sel = Shapes[i].shape == shape;
				int cost = (int)Math.Ceiling(picked.Cost * Shapes[i].costMul);
				bool afford = buildInk >= cost;
				DrDraw.Rect(r.X, r.Y, r.Width, r.Height, sel ? new Color(10, 40, 60) : new Color(20, 20, 20));
				DrDraw.Outline(r.X, r.Y, r.Width, r.Height, sel ? new Color(120, 200, 255) : new Color(90, 90, 90), sel ? 2 : 1);
				DrDraw.Text(Shapes[i].label, r.X + 4, r.Y + 5, afford ? Color.White : new Color(128, 128, 128), DrDraw.SmallFont, 0.6f);
				DrDraw.Text($"{cost}", r.Right - 20, r.Y + 5, afford ? new Color(120, 200, 255) : new Color(255, 80, 80), DrDraw.SmallFont, 0.6f);
			}
			for (int i = pieceFirst; i < pieceOptions.Count && i < pieceFirst + PiecesShown; i++)
			{
				PieceOption o = pieceOptions[i];
				Rectangle r = PieceButton(i).Value;
				bool sel = i == piecePick;
				int cost = ShapeCost(o);
				bool afford = buildInk >= cost;
				DrDraw.Rect(r.X, r.Y, r.Width, r.Height, (sel ? new Color(60, 40, 10) : new Color(20, 20, 20)));
				DrDraw.Outline(r.X, r.Y, r.Width, r.Height, sel ? new Color(255, 200, 40) : new Color(90, 90, 90), sel ? 2 : 1);
				DrawPieceIcon(o, new Vector2(r.Center.X, r.Y + 17), 24f, afford ? 1f : 0.4f);
				DrDraw.Text(o.Label, r.X + 4, r.Y + 32, afford ? Color.White : new Color(128, 128, 128), DrDraw.SmallFont, 0.7f);
				DrDraw.Text($"{cost}", r.Right - 18, r.Y + 2, afford ? new Color(120, 200, 255) : new Color(255, 80, 80), DrDraw.SmallFont, 0.7f);
				DrDraw.Text($"{o.Damage}", r.X + 4, r.Y + 46, new Color(255, 200, 80), DrDraw.SmallFont, 0.6f);
			}
			// Ink and time
			float inkW = 84f * buildInk / InkMax;
			DrDraw.Text("INK", 540, top - 2, new Color(120, 200, 255), DrDraw.SmallFont, 0.7f);
			DrDraw.Rect(540, top + 14, 84, 8, new Color(30, 30, 60));
			DrDraw.Rect(540, top + 14, inkW, 8, new Color(120, 200, 255));
			DrDraw.Text($"{(buildTicks + 59) / 60}s", 590, top - 2, Color.White, DrDraw.SmallFont, 0.7f);
			// Their SOUL: click a colour to force it (once every few seconds)
			for (int i = 0; i < ForceModes.Length; i++)
			{
				Rectangle sb = SoulButton(i);
				bool now = ForceModes[i] == soulMode;
				float a = soulForceCooldown > 0 && !now ? 0.35f : 1f;
				DrDraw.Rect(sb.X + 2, sb.Y + 2, sb.Width - 4, sb.Height - 4, ForceModes[i].Color() * a);
				DrDraw.Outline(sb.X, sb.Y, sb.Width, sb.Height, now ? Color.White : new Color(90, 90, 90), now ? 2 : 1);
			}
			if (soulForceCooldown > 0)
				DrDraw.Text($"{(soulForceCooldown + 59) / 60}", 628, PaletteTop + 28, new Color(160, 160, 160), DrDraw.SmallFont, 0.6f);
			Rectangle d = DoneButton;
			DrDraw.Rect(d.X, d.Y, d.Width, d.Height, new Color(20, 60, 20));
			DrDraw.Outline(d.X, d.Y, d.Width, d.Height, new Color(80, 255, 80), 2);
			DrDraw.Text("DONE", d.X + 20, d.Y + 6, Color.White, DrDraw.SmallFont, 0.9f);
		}
	}
}
