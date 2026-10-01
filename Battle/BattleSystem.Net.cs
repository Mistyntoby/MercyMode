using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Utilities;
using MercyMode.Battle.Net;
using static MercyMode.Battle.BattleConstants;

namespace MercyMode.Battle
{
	/// <summary>
	/// The battle screen's side of multiplayer (<see cref="BattleNet"/>): actions are picked first and carried out one
	/// player at a time, everyone shares the bullet box, and the others are drawn in their colours.
	/// </summary>
	public partial class BattleSystem
	{
		/// <summary>The server started the bullet box: this round's enemy turn may begin.</summary>
		private bool enemyTurnGranted;
		/// <summary>A bullet box the server started while this player was still busy (gliding in, mid-action).</summary>
		private bool enemyTurnQueued;
		/// <summary>The action this player picked, run when the server says it's their turn.</summary>
		private Action pendingAction;
		/// <summary>This player is carrying out their action (its text goes to the others).</summary>
		private bool executing;
		/// <summary>Joined a battle already going: watching until the next bullet box.</summary>
		private bool spectating;
		/// <summary>The shared seed for this bullet box, so every screen gets the same attack.</summary>
		private UnifiedRandom netRand;
		private int netRound;
		/// <summary>The waiting text shows something from the others (their turn, their lines) instead of who we wait on.</summary>
		private bool waitingShowsOthers;
		private uint actingSince;
		private readonly Dictionary<int, Vector2> allySoulDrawn = new();

		/// <summary>This player's colour: everything that's Kris-cyan in Deltarune.</summary>
		private static Color KrisCyan => PartyColors.Of(Main.LocalPlayer);
		/// <summary>The colour of damage the hero deals: merge_color(their colour, c_white, 0.5). Crits are gold.</summary>
		private static Color HeroDamageColor => Color.Lerp(KrisCyan, Color.White, 0.5f);
		private static readonly Color HeroCritColor = new(255, 220, 64);

		private void ResetNet()
		{
			enemyTurnGranted = false;
			enemyTurnQueued = false;
			pendingAction = null;
			executing = false;
			spectating = false;
			netRand = null;
			waitingShowsOthers = false;
			allySoulDrawn.Clear();
		}

		/// <summary>
		/// An action picked from the menu. Alone (or in singleplayer) it happens straight away; in a party it waits for
		/// everyone to pick, then for this player's turn.
		/// </summary>
		private void Commit(int face, Action run)
		{
			if (!BattleNet.InParty)
			{
				run();
				return;
			}
			faceAction = face;
			pendingAction = run;
			SetHeroPose(face switch
			{
				FaceFight => HeroPose.AttackReady,
				FaceItem => HeroPose.ItemReady,
				FaceDefend => HeroPose.Defend,
				_ => HeroPose.ActReady,
			});
			BattleNet.SendReady(face);
			EnterWaiting();
		}

		private void EnterWaiting()
		{
			waitingShowsOthers = false;
			SetPhase(Phase.Waiting);
			text = WaitingText();
			textShown = text.Length;
		}

		private string WaitingText()
		{
			if (spectating)
				return "* You're watching for now.\n* You'll jump in at the next attack!";
			var names = BattleNet.WaitingOn.ToList();
			return names.Count == 0 ? "* Waiting for the others..." : $"* Waiting for {string.Join(" and ", names)} to choose...";
		}

		private void UpdateWaiting()
		{
			if (!waitingShowsOthers)
			{
				string t = WaitingText();
				if (t != text)
				{
					text = t;
					textShown = text.Length;
				}
			}
		}

		/// <summary>The server: this player carries out their action now (or someone else does, and we watch).</summary>
		internal void OnNetTurnOf(int player)
		{
			actingSince = Main.GameUpdateCount;
			if (player == Main.myPlayer)
			{
				if (pendingAction == null)
				{
					// Nothing to do (this player hadn't picked when the wait timed out)
					BattleNet.SendActionDone();
					return;
				}
				Action run = pendingAction;
				pendingAction = null;
				executing = true;
				// An earlier action may have finished off the target
				RetargetIfNeeded();
				run();
				return;
			}
			if (phase != Phase.Waiting)
				return;
			Player p = Main.player[player];
			waitingShowsOthers = true;
			SetText($"* {p.name}'s turn!");
		}

		/// <summary>A line from the acting player's text box.</summary>
		internal void OnNetText(int player, string line)
		{
			if (phase != Phase.Waiting)
				return;
			waitingShowsOthers = true;
			SetText(line);
		}

		/// <summary>The server: everyone has acted, into the bullet box (with this seed and round, the same for all).</summary>
		internal void OnNetEnemyTurn(int seed, int round)
		{
			netRand = new UnifiedRandom(seed);
			netRound = round;
			spectating = false;
			pendingAction = null;
			if (phase is Phase.Waiting or Phase.Menu or Phase.WeaponSelect or Phase.EnemySelect or Phase.ActSelect or Phase.ItemSelect)
			{
				// Still choosing when the wait ran out: no action this round
				executing = false;
				enemyTurnGranted = true;
				StartEnemyTurn();
				return;
			}
			// Gliding in, or mid-action: as soon as that's done
			enemyTurnQueued = true;
		}

		/// <summary>Runs with the bullet box's shared random numbers, so every screen spawns the same attack.</summary>
		private T WithNetRand<T>(Func<T> f)
		{
			if (!BattleNet.InParty || netRand == null)
				return f();
			UnifiedRandom saved = Main.rand;
			Main.rand = netRand;
			try
			{
				return f();
			}
			finally
			{
				Main.rand = saved;
			}
		}

		private void WithNetRand(Action f) => WithNetRand(() =>
		{
			f();
			return 0;
		});

		/// <summary>The others see this SOUL in the box.</summary>
		private void SendSoul()
		{
			if (BattleNet.InParty && phase is Phase.EnemyIntro or Phase.EnemyTurn && time % 2 == 0)
				BattleNet.SendSoul(soul, (byte)soulMode);
		}

		/// <summary>Leaves the battle on this client only (multiplayer /mmbattle end).</summary>
		public void Leave()
		{
			if (Active)
				End();
		}

		/// <summary>An enemy's MERCY as the server has it, after anyone's ACT.</summary>
		internal void OnNetMercy(NPC npc, float mercy)
		{
			foreach (BattleEnemy e in enemies)
				if (e.E.Npc == npc)
					e.E.SetMercyQuiet(mercy);
		}

		/// <summary>Another party member spared this enemy.</summary>
		internal void OnNetSpared(NPC npc)
		{
			BattleEnemy e = enemies.FirstOrDefault(x => !x.Out && (x.E.Npc == npc || x.E.Members().Contains(npc)));
			if (e == null)
				return;
			e.Out = true;
			WithEnemy(e, PlayEnemySpared);
		}

		/// <summary>Another party member's FIGHT hit landed: show it here too.</summary>
		internal void OnNetHit(NPC npc, int damage, bool crit)
		{
			BattleEnemy e = enemies.FirstOrDefault(x => x.E.Npc == npc || x.E.Members().Contains(npc));
			if (e == null || phase == Phase.None)
				return;
			WithEnemy(e, () =>
			{
				EnemyNumber(damage, crit ? HeroCritColor : HeroDamageColor, damage > 0 ? -1 : DamageNumber.MissFrame, at: PartSpot(npc));
				enemyShake = 18;
			});
			Sfx("damage");
		}

		// ---- drawing the party ----

		/// <summary>Party boxes sit side by side across the panel (one centred, three filling it), like Deltarune's.</summary>
		private static int PartyBoxX(int index) => (int)Math.Round(320f - BattleNet.PartySize * 213f / 2f + index * 213f + 0.5f);

		/// <summary>Where the other party members stand: one above-behind, one below-behind the player.</summary>
		private static readonly Vector2[] AllyFeet = { new(70, 146), new(70, 292) };

		/// <summary>What an ally is doing: carrying out their action, ready with one, or idle.</summary>
		private (HeroPose Pose, float Timer) AllyPose(Player p)
		{
			float since = (Main.GameUpdateCount - actingSince) / (float)TicksPerFrame;
			bool hitRecently = BattleNet.AllyHitTick.TryGetValue(p.whoAmI, out uint hit) && Main.GameUpdateCount - hit < 30;
			if (hitRecently)
				return (HeroPose.Attack, (Main.GameUpdateCount - hit) / (float)TicksPerFrame);
			if (!BattleNet.ReadyFaces.TryGetValue(p.whoAmI, out int face))
				return (HeroPose.Idle, 0f);
			bool acting = BattleNet.Acting == p.whoAmI;
			return face switch
			{
				FaceFight => (HeroPose.AttackReady, 0f),
				FaceDefend => (HeroPose.Defend, 0f),
				FaceItem => (acting ? HeroPose.Item : HeroPose.ItemReady, since),
				_ => (acting ? HeroPose.Act : HeroPose.ActReady, since),
			};
		}

		/// <summary>An ally's weapon, for their poses: what they're holding, if it's a weapon.</summary>
		private static Item AllyWeapon(Player p)
		{
			Item held = p.HeldItem;
			return held != null && !held.IsAir && held.damage > 0 && !held.consumable ? held : null;
		}

		private void DrawAllies(SpriteBatch sb, Matrix m)
		{
			if (!BattleNet.InParty)
				return;
			float fly = FlyProgress();
			int slot = 0;
			foreach (Player p in BattleNet.Allies)
			{
				if (slot >= AllyFeet.Length)
					break;
				Vector2 feet = AllyFeet[slot++];
				if (p.dead)
					continue;
				var (pose, timer) = AllyPose(p);
				float bob = pose == HeroPose.Idle ? (float)Math.Round(Math.Sin((time + slot * 25) / 20f)) : 0f;
				if (pose == HeroPose.Act)
					bob = -(float)Math.Sin(Math.Min(1f, timer / 14f) * Math.PI) * 10f;
				// Watchers stand a little see-through until they jump in
				float shadow = Math.Max((1f - fly) * 0.9f, BattleNet.Joining.Contains(p.whoAmI) ? 0.5f : 0f);
				DrawPlayerPose(sb, m, p, feet + new Vector2(0f, bob), HeroScale, pose, timer, shadow, ally: true);
			}
		}

		private void DrawAllyBoxes(float top)
		{
			if (!BattleNet.InParty || BattleNet.PartySize < 2)
				return;
			for (int i = 0; i < BattleNet.Party.Count; i++)
			{
				int who = BattleNet.Party[i];
				if (who == Main.myPlayer || who < 0 || who >= Main.maxPlayers)
					continue;
				Player p = Main.player[who];
				Color color = PartyColors.Of(p);
				bool watching = BattleNet.Joining.Contains(who);
				var r = new Rectangle(PartyBoxX(i), (int)top, 212, 34);
				DrDraw.Rect(r.X, r.Y, r.Width, 34f, Color.Black);
				// Their colour frames their box; brighter while it's their turn
				float frameAlpha = BattleNet.Acting == who ? 1f : 0.55f;
				DrDraw.Outline(r.X, r.Y - 2, r.Width, 36, color * frameAlpha, 2);

				if (BattleNet.ReadyFaces.TryGetValue(who, out int face) && face > 0)
					DrDraw.Sprite("spr_headkris", face, r.X + 3, r.Y + 4, Color.White);
				else
					DrDraw.Text(watching ? "WAIT" : "...", r.X + 6, r.Y + 8, new Color(128, 128, 128), DrDraw.SmallFont, watching ? 0.7f : 1f);

				const float nameScale = 0.68f, nameRoom = 64f;
				string name = (p.active ? p.name : "?").ToUpperInvariant();
				float scale = MathHelper.Clamp(nameRoom / Math.Max(1f, DrDraw.Measure(name, DrDraw.BigFont)), 0.4f, nameScale);
				while (name.Length > 1 && DrDraw.Measure(name, DrDraw.BigFont) * scale > nameRoom)
					name = name.Substring(0, name.Length - 1);
				DrDraw.Text(name, r.X + 40, r.Y + 7, p.dead ? Color.Gray : Color.White, DrDraw.BigFont, scale);

				const int labelX = 112, barX = 130, barWidth = 28, barY = 10, barHeight = 12;
				float ratio = p.statLifeMax2 > 0 ? MathHelper.Clamp(p.statLife / (float)p.statLifeMax2, 0f, 1f) : 0f;
				DrDraw.Text("HP", r.X + labelX - 4, r.Y + barY - 3, Color.White, DrDraw.SmallFont);
				DrDraw.Rect(r.X + barX, r.Y + barY, barWidth, barHeight, new Color(128, 0, 0));
				DrDraw.Rect(r.X + barX, r.Y + barY, (float)Math.Ceiling(ratio * barWidth), barHeight, color);
				string hp = $"{Math.Max(0, p.statLife)}/{p.statLifeMax2}";
				float room = 208 - (barX + barWidth + 3);
				float numberScale = Math.Min(1f, room / Math.Max(1f, DrDraw.Measure(hp, DrDraw.SmallFont)));
				DrDraw.Text(hp, r.X + barX + barWidth + 3, r.Y + barY + barHeight / 2f - DrDraw.LineHeight(DrDraw.SmallFont) * numberScale / 2f,
					ratio <= 0.25f ? new Color(255, 255, 0) : Color.White, DrDraw.SmallFont, numberScale);
			}
		}

		/// <summary>The others' SOULs in the box, each in its player's colour.</summary>
		private void DrawAllySouls()
		{
			if (!BattleNet.InParty || phase is not (Phase.EnemyIntro or Phase.EnemyTurn))
				return;
			foreach (var (who, (pos, _, tick)) in BattleNet.AllySouls)
			{
				if (Main.GameUpdateCount - tick > 30 || who < 0 || who >= Main.maxPlayers || !Main.player[who].active || Main.player[who].dead)
					continue;
				// Eased toward the latest position the network brought, so it glides instead of hopping
				Vector2 shown = allySoulDrawn.TryGetValue(who, out Vector2 was) && Vector2.DistanceSquared(was, pos) < 120f * 120f
					? Vector2.Lerp(was, pos, 0.5f) : pos;
				allySoulDrawn[who] = shown;
				Color c = PartyColors.Of(Main.player[who]);
				DrDraw.HeartShapeAt(shown.X + 1, shown.Y + 1, 18, Color.Black * 0.6f);
				DrDraw.HeartShapeAt(shown.X + 2, shown.Y + 2, 16, c * 0.85f);
			}
		}
	}
}
