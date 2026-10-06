using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using static MercyMode.Battle.BattleConstants;

namespace MercyMode.Battle
{
	/// <summary>
	/// Talkers (Talk.cs): an enemy holding forth in the text box at the bottom, its face beside the words, and the
	/// questions it asks. Alone, you answer; in a party everyone answers and the server counts the votes
	/// (BattleNet.SettleVote). Also little cutscenes: the hero and the enemy moved about on the battle screen while an
	/// ACT's lines play.
	/// </summary>
	public partial class BattleSystem
	{
		private readonly Queue<string> talkLines = new();
		private Encounter talkSpeaker;
		private TalkQuestion talkQuestion;
		private Action talkThen;
		private int choiceIndex;
		/// <summary>This turn's bullets hit this much harder (an answer can change it).</summary>
		private float talkDamageMult = 1f;
		private bool talkSkipAttack;
		/// <summary>How long a talker's line stays up in a party (no Z: the party moves on together).</summary>
		private const int PartyTalkHoldTicks = 70;

		internal bool LabTalking => phase is Phase.Talk or Phase.Choice or Phase.ChoiceWait;
		internal TalkQuestion LabQuestion => talkQuestion;

		/// <summary>
		/// As the enemy turn starts: the first talker with something to say this turn says it (and asks), then the turn
		/// goes on. False if nobody talks.
		/// </summary>
		private bool BeginTalkers()
		{
			talkDamageMult = 1f;
			talkSkipAttack = false;
			foreach (BattleEnemy en in enemies)
			{
				if (!en.Living || duelWith >= 0)
					continue;
				// (Its turn counter has already moved on: the attack was just built)
				TalkTurn t = en.E.Talk(Math.Max(0, en.E.Turn - 1));
				if (t == null || t.Lines.Length == 0 && t.Question == null)
					continue;
				// It speaks down here instead of in a bubble
				en.Bubble = null;
				StartTalk(en.E, t.Lines, t.Question, ContinueEnemyTurn);
				return true;
			}
			return false;
		}

		private void StartTalk(Encounter speaker, IEnumerable<string> lines, TalkQuestion question, Action then)
		{
			talkSpeaker = speaker;
			talkQuestion = question;
			talkThen = then;
			talkLines.Clear();
			foreach (string l in lines)
				talkLines.Enqueue(l);
			if (speaker?.Voice is Terraria.Audio.SoundStyle voice)
				AttackSfx.Vanilla(voice with { PitchVariance = 0.15f }, 0.6f);
			NextTalk();
		}

		private void NextTalk()
		{
			if (talkLines.Count > 0)
			{
				SetText(talkLines.Dequeue());
				SetPhase(Phase.Talk);
				return;
			}
			if (talkQuestion != null)
			{
				choiceIndex = 0;
				SetText(talkQuestion.Prompt);
				SetPhase(Phase.Choice);
				return;
			}
			Action then = talkThen;
			talkThen = null;
			talkSpeaker = null;
			then?.Invoke();
		}

		private void UpdateTalk()
		{
			messageTicks++;
			bool party = Net.BattleNet.InParty;
			if (!party && Cancel)
				textShown = text.Length;
			if (textShown < text.Length)
				return;
			if (party ? messageTicks >= text.Length * 2 + PartyTalkHoldTicks : Confirm)
				NextTalk();
		}

		private int ChoiceCount => talkQuestion?.Answers.Length ?? 0;

		private void UpdateChoice()
		{
			messageTicks++;
			if (Cancel)
				textShown = text.Length;
			int n = ChoiceCount;
			if (n == 0)
			{
				NextTalk();
				return;
			}
			// Two columns: left/right across, up/down between the rows
			int was = choiceIndex;
			if (Pressed(Keys.Right) && choiceIndex % 2 == 0 && choiceIndex + 1 < n)
				choiceIndex++;
			if (Pressed(Keys.Left) && choiceIndex % 2 == 1)
				choiceIndex--;
			if (Pressed(Keys.Down) && choiceIndex + 2 < n)
				choiceIndex += 2;
			if (Pressed(Keys.Up) && choiceIndex >= 2)
				choiceIndex -= 2;
			if (choiceIndex != was)
				Sfx("menumove");
			if (textShown < text.Length || !Confirm)
				return;
			Sfx("select");
			if (Net.BattleNet.InParty)
			{
				Net.BattleNet.SendVote(netRound, choiceIndex, n);
				SetText($"* You voted \"{talkQuestion.Answers[choiceIndex].Option}\".\n* Waiting for the others...");
				SetPhase(Phase.ChoiceWait);
				return;
			}
			ApplyAnswer(choiceIndex, null);
		}

		/// <summary>The party's answer (from the server): everyone applies the same one.</summary>
		internal void OnVoteResult(int round, int choice, bool tie, int[] counts)
		{
			if (round != netRound || phase is not (Phase.Choice or Phase.ChoiceWait) || talkQuestion == null)
				return;
			if (choice < 0 || choice >= talkQuestion.Answers.Length)
				return;
			string picked = talkQuestion.Answers[choice].Option;
			string tally = string.Join(", ", talkQuestion.Answers.Select((a, i) => $"{a.Option} {(i < counts.Length ? counts[i] : 0)}"));
			string line = tie
				? $"* The party was split ({tally}).\n* Fate picked \"{picked}\"."
				: $"* The party chose \"{picked}\" ({tally}).";
			ApplyAnswer(choice, line);
		}

		/// <summary>The answer takes effect, then the talker's reply plays and the turn goes on.</summary>
		private void ApplyAnswer(int choice, string announce)
		{
			TalkAnswer a = talkQuestion.Answers[Math.Clamp(choice, 0, talkQuestion.Answers.Length - 1)];
			Encounter speaker = talkSpeaker;
			talkQuestion = null;
			// MERCY is counted by the server for a party: only one of us sends it (the lowest player number)
			bool counts = !Net.BattleNet.InParty || Net.BattleNet.Party.Count == 0 || Main.myPlayer == Net.BattleNet.Party.Min();
			if (a.Mercy != 0f && speaker != null && !speaker.IsBoss && counts)
				speaker.Mercy += a.Mercy;
			if (a.Heal > 0)
			{
				int healed = HealPlayer(a.Heal);
				if (healed > 0)
					PlayHealFx(healed);
			}
			talkDamageMult *= a.DamageMult;
			talkSkipAttack |= a.Skip;
			var lines = new List<string>();
			if (announce != null)
				lines.Add(announce);
			lines.AddRange(a.Reply);
			if (a.Mercy > 0f && speaker != null && speaker.Mercy >= 100f)
				lines.Add($"* {speaker.Name} doesn't want to fight anymore.");
			StartTalk(speaker, lines, null, talkThen);
		}

		/// <summary>After the talker: a forgotten attack is a quiet turn, then the bubbles and the box as usual.</summary>
		private void ContinueEnemyTurn()
		{
			if (talkSkipAttack)
			{
				attack = new QuietAttack();
				turnTimer = attack.Duration;
				BeginSoulMode(SoulMode.Red);
			}
			text = "";
			if (enemies.Any(e => e.Living && !string.IsNullOrEmpty(e.Bubble)))
			{
				foreach (BattleEnemy e in enemies)
					e.BubbleAt = time;
				SetPhase(Phase.EnemyTalk);
				return;
			}
			OpenBulletBox();
		}

		// ---- drawing ----

		private const float PortraitSize = 64f;

		/// <summary>The talker's face on the left of the text box, the words beside it.</summary>
		private void DrawTalk(float y)
		{
			float x = 30f;
			if (talkSpeaker != null)
			{
				talkSpeaker.DrawPortrait(new Vector2(30f + PortraitSize / 2f, y + 34f), PortraitSize, time);
				x = 40f + PortraitSize;
			}
			DrDraw.Text(text.Substring(0, Math.Min(text.Length, (int)textShown)), x, y, Color.White);
		}

		/// <summary>The question and its answers (two columns, the heart on the one picked).</summary>
		private void DrawChoice(float y)
		{
			DrawTalk(y);
			if (textShown < text.Length || talkQuestion == null)
				return;
			float left = talkSpeaker != null ? 70f + PortraitSize : 80f;
			for (int i = 0; i < talkQuestion.Answers.Length; i++)
			{
				float ox = left + i % 2 * 230f, oy = y + 52f + i / 2 * 30f;
				bool on = i == choiceIndex;
				DrDraw.Text(talkQuestion.Answers[i].Option, ox, oy, on ? new Color(255, 255, 0) : Color.White);
				if (on)
					DrawHeartCursor(ox - 25, oy + 10);
			}
		}

		// ================================================================== boulders

		private Encounters.BoulderEncounter boulderEnc;

		/// <summary>A rolling boulder hit the player: a battle with it (true), or not (false: it hits as usual).</summary>
		public static bool TryStartBoulder(Projectile p, Player player)
		{
			if (Active || player.whoAmI != Main.myPlayer || player.dead || !(MercyMode.IsSingleplayer || Net.BattleNet.Online))
				return false;
			var config = Terraria.ModLoader.ModContent.GetInstance<MercyConfig>();
			if (config != null && (!config.TurnBasedBattles || !config.BattlesWithEnemies))
				return false;
			if (Instance.lastEndTick != 0 && Main.GameUpdateCount - Instance.lastEndTick < GraceTicks || Instance.enterGrace > 0)
				return false;
			if (MercyMode.AnyBossAlive())
				return false;
			return Instance.StartBoulder(p);
		}

		private bool StartBoulder(Projectile p)
		{
			NPC proxy = Main.npc[Main.maxNPCs];
			proxy.SetDefaults(Terraria.ID.NPCID.TargetDummy);
			proxy.whoAmI = Main.maxNPCs;
			proxy.active = true;
			proxy.friendly = false;
			proxy.realLife = -1;
			proxy.boss = false;
			proxy.width = p.width;
			proxy.height = p.height;
			proxy.position = p.position;
			proxy.velocity = Vector2.Zero;
			proxy.defense = 0;
			// About three and a half good FIGHT turns with the weapon in hand
			WeaponOption w = CurrentWeapon();
			proxy.lifeMax = Math.Max(60, (int)((w?.PerfectTurn ?? 30) * 3.5f));
			proxy.life = proxy.lifeMax;
			boulderEnc = new Encounters.BoulderEncounter { Npc = proxy, ProjType = p.type };
			proxy.damage = boulderEnc.Damage;
			// The boulder itself is in the battle now (another player's game keeps its own copy of a trap's boulder)
			p.active = false;
			Start(proxy, "boulder", new List<NPC> { proxy });
			return true;
		}

		/// <summary>The boulder's battle is over: broken, it leaves some stone; either way its stand-in goes.</summary>
		private void EndBoulder(bool lost)
		{
			if (boulderEnc == null)
				return;
			NPC proxy = Main.npc[Main.maxNPCs];
			if (!lost && !boulderEnc.SparedIt && proxy.life <= 0)
				Player.QuickSpawnItem(Player.GetSource_FromThis(), Terraria.ID.ItemID.StoneBlock, 10);
			proxy.active = false;
			boulderEnc = null;
		}

		// ================================================================== cutscenes

		/// <summary>A few seconds of the hero and the enemy moving about on the battle screen (an ACT's little scene).</summary>
		public sealed class Cutscene
		{
			public int Length = 90;
			/// <summary>Offsets by tick: the hero's feet and the enemy's centre.</summary>
			public Func<int, Vector2> Hero, Enemy;
			/// <summary>Extra turn on the enemy, by tick.</summary>
			public Func<int, float> EnemySpin;
			/// <summary>Sounds, dust and shakes, by tick.</summary>
			public Action<BattleSystem, int> OnTick;
		}

		private Cutscene cutscene;
		private int cutsceneTick;
		private Vector2 cutHero, cutEnemy;
		private float cutSpin;

		/// <summary>Plays a little scene on the battle screen while the ACT's lines show.</summary>
		public void PlayCutscene(Cutscene c)
		{
			cutscene = c;
			cutsceneTick = 0;
		}

		private void UpdateCutscene()
		{
			if (cutscene == null)
			{
				cutHero = cutEnemy = Vector2.Zero;
				cutSpin = 0f;
				return;
			}
			int t = cutsceneTick++;
			cutHero = cutscene.Hero?.Invoke(t) ?? Vector2.Zero;
			cutEnemy = cutscene.Enemy?.Invoke(t) ?? Vector2.Zero;
			cutSpin = cutscene.EnemySpin?.Invoke(t) ?? 0f;
			cutscene.OnTick?.Invoke(this, t);
			if (cutsceneTick >= cutscene.Length)
				cutscene = null;
		}

		/// <summary>Dust kicked up on the battle screen (for cutscenes).</summary>
		public void Dust(Vector2 at, Color color, int count = 6) => Sparks.Burst(this, at, count, color, 2f);

		/// <summary>Where the hero's feet and the enemy are drawn right now (for cutscenes aiming at them).</summary>
		public Vector2 HeroSpot => HeroFeet;
		public Vector2 EnemySpot => encounter?.ScreenCenter ?? Vector2.Zero;
	}
}
