using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.Utilities;
using MercyMode.Battle.Net;
using MercyMode.Deltarune;
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
		/// <summary>How many of each ally's hit numbers are up this turn (they stack).</summary>
		private readonly Dictionary<int, int> allyHitsShown = new();
		/// <summary>When each ally first showed up on this screen (0: there from the start, gliding in with us).</summary>
		private readonly Dictionary<int, uint> allySeen = new();
		/// <summary>Each ally's spot (0 above, 1 below), kept so nobody jumps when someone else leaves.</summary>
		private readonly Dictionary<int, int> allySlot = new();
		/// <summary>Where each ally was last drawn, for walking off from there.</summary>
		private readonly Dictionary<int, Vector2> allyLastFeet = new();
		/// <summary>Allies who left mid-battle, walking off to the left: when they turned around.</summary>
		private readonly Dictionary<int, uint> allyWalkingOff = new();
		/// <summary>Allies who left as the battle ended: still drawn, gliding back out with us.</summary>
		private readonly HashSet<int> allyStaying = new();
		/// <summary>spr_dodgeheart turned white, so it can take each player's colour.</summary>
		private static Texture2D whiteHeart;
		private static Texture2D whiteHeartFrom;

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
			allySeen.Clear();
			allySlot.Clear();
			allyLastFeet.Clear();
			allyWalkingOff.Clear();
			allyStaying.Clear();
		}

		/// <summary>An ally left the party: they walk off, or (we're already gliding out) come along.</summary>
		internal void OnAllyLeft(int who)
		{
			if (phase == Phase.None || who < 0 || who >= Main.maxPlayers)
				return;
			// Already gliding out ourselves: they come along. Otherwise (left the game, won and moved on before us,
			// /mmbattle end) they turn around and walk off
			if (phase is Phase.Outro)
				allyStaying.Add(who);
			else if (!Main.player[who].dead && allyLastFeet.ContainsKey(who))
				allyWalkingOff[who] = Main.GameUpdateCount;
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
			// Alone: the server answers straight away; nothing to say for that moment
			if (!BattleNet.Allies.Any(p => !BattleNet.Joining.Contains(p.whoAmI)))
				return "";
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
		internal void OnNetTurnOf(List<int> players)
		{
			actingSince = Main.GameUpdateCount;
			allyHitsShown.Clear();
			if (players.Contains(Main.myPlayer))
			{
				// Won already (someone acted before us and finished it): the picked action is dropped, no FIGHT bar
				// against nobody
				if (battleOver || LivingEnemies.Count == 0 || phase != Phase.Waiting)
					pendingAction = null;
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
			waitingShowsOthers = true;
			string names = string.Join(" and ", players.Select(i => Main.player[i].name));
			SetText(BattleNet.FightingNow.Count > 0 ? $"* {names} attack!" : $"* {names}'s turn!");
		}

		/// <summary>The rows of FIGHT bars stack down from the first one, one per player fighting this step.</summary>
		private const float FightRowSpacing = 38f;
		private float BarY => FightBarY + Math.Max(0, BattleNet.FightingNow.IndexOf(Main.myPlayer)) * FightRowSpacing;

		/// <summary>
		/// The other players fighting at the same time: their bars under (or over) ours, in their colour, flashing as their
		/// hits land. Their bolts aren't sent over the network (it would lag), only their hits.
		/// </summary>
		private void DrawAllyFightRows()
		{
			for (int i = 0; i < BattleNet.FightingNow.Count; i++)
			{
				int who = BattleNet.FightingNow[i];
				if (who == Main.myPlayer || who < 0 || who >= Main.maxPlayers || !Main.player[who].active)
					continue;
				Player p = Main.player[who];
				Color c = PartyColors.Of(p);
				float x = FightBarX, y = FightBarY + i * FightRowSpacing;
				// Built like our own bar: head, Z, a double border and the see-through press spot, all in their colour
				DrawAllyHead(p, new Vector2(x + 19, y + 20), 1f);
				DrDraw.Text("Z", x + 50, y + 4, c);
				DrDraw.Rect(x + 79, y + 1, FightBoxWidth + 1, 35, Color.Black * 0.6f);
				DrDraw.Outline(x + 78, y, FightBoxWidth + 3, 37, c);
				DrDraw.Outline(x + 79, y + 1, FightBoxWidth + 1, 35, c * 0.6f);
				// The see-through press spot, like ours
				if (!DrDraw.Sprite("spr_pressspot", 0, x + 80, y, c, 1f, 0f, 0.55f))
					DrDraw.Rect(x + 80, y, 10, 38, c * 0.35f);
				if (BattleNet.AllyHitTick.TryGetValue(who, out uint hit) && Main.GameUpdateCount - hit < 20)
				{
					float t = (Main.GameUpdateCount - hit) / 20f;
					Vector2 sc = new(1f + t * 2f, 1f + t * 0.5f);
					// Their hit flashes like ours (obj_burstbolt: grows and fades)
					Color flash = Color.Lerp(c, Color.White, 0.5f);
					if (!DrDraw.Sprite("spr_attackspot", 0, x + 80 - 5 * (sc.X - 1), y - 19 * (sc.Y - 1), flash, sc, 0f, 1f - t))
						DrDraw.Rect(x + 80 - 5 * (sc.X - 1), y - 19 * (sc.Y - 1), 10 * sc.X, 38 * sc.Y, flash * (1f - t));
				}
			}
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

		private int lastSentHp = -1, lastSentHpMax = -1;

		/// <summary>The others see this SOUL in the box (and our HP, whenever it changes or once a second).</summary>
		private void SendSoul()
		{
			if (BattleNet.InParty && (Player.statLife != lastSentHp || Player.statLifeMax2 != lastSentHpMax || time % 60 == 0))
			{
				lastSentHp = Player.statLife;
				lastSentHpMax = Player.statLifeMax2;
				BattleNet.SendHp(lastSentHp, lastSentHpMax);
			}
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
		internal void OnNetHit(int attacker, NPC npc, int damage, bool crit)
		{
			BattleEnemy e = enemies.FirstOrDefault(x => x.E.Npc == npc || x.E.Members().Contains(npc));
			if (e == null || phase == Phase.None)
				return;
			WithEnemy(e, () =>
			{
				// In the attacker's colour, the way ours are in ours
				Color theirs = Color.Lerp(PartyColors.Of(Main.player[attacker]), Color.White, 0.5f);
				// Each player's numbers get their own column beside ours, stacking up hit by hit, so a MISS and a hit
				// at the same moment don't land on top of each other
				int column = 1 + Math.Max(0, BattleNet.Allies.Select(a => a.whoAmI).ToList().IndexOf(attacker));
				int stacked = allyHitsShown.TryGetValue(attacker, out int k) ? k : 0;
				allyHitsShown[attacker] = stacked + 1;
				EnemyNumber(damage, crit ? HeroCritColor : theirs, damage > 0 ? -1 : DamageNumber.MissFrame,
					yOffset: -18f * stacked, at: PartSpot(npc) + new Vector2(52f * column, 10f));
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
			// Won: everyone cheers with the player
			if (battleOver && heroPose == HeroPose.Victory)
				return (HeroPose.Victory, heroTimer);
			bool hitRecently = BattleNet.AllyHitTick.TryGetValue(p.whoAmI, out uint hit) && Main.GameUpdateCount - hit < 30;
			if (hitRecently)
				return (HeroPose.Attack, (Main.GameUpdateCount - hit) / (float)TicksPerFrame);
			if (!BattleNet.ReadyFaces.TryGetValue(p.whoAmI, out int face))
				return (HeroPose.Idle, 0f);
			bool acting = BattleNet.ActingNow.Contains(p.whoAmI);
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

		/// <summary>
		/// How far an ally is along their glide from their spot in the world to the battle screen: with this player's own
		/// glide (intro and outro), or their own if they joined later.
		/// </summary>
		private float AllyFly(Player p)
		{
			if (!allySeen.TryGetValue(p.whoAmI, out uint seen))
				allySeen[p.whoAmI] = seen = phase == Phase.Intro ? 0u : Main.GameUpdateCount;
			float own = 1f;
			if (seen != 0)
			{
				float t = MathHelper.Clamp((Main.GameUpdateCount - seen) / (float)GlideTicks, 0f, 1f);
				own = 1f - (float)Math.Pow(1f - t, 3);
			}
			return Math.Min(FlyProgress(), own);
		}

		/// <summary>The spot an ally stands in: the first free one, kept for as long as they're around.</summary>
		private int SlotOf(int who)
		{
			if (allySlot.TryGetValue(who, out int slot))
				return slot;
			for (slot = 0; slot < AllyFeet.Length; slot++)
				if (!allySlot.ContainsValue(slot))
					break;
			return allySlot[who] = slot;
		}

		/// <summary>Walking pace off the screen, in battle pixels per tick.</summary>
		private const float WalkOffSpeed = 2.2f;

		private void DrawAllies(SpriteBatch sb, Matrix m)
		{
			if (!BattleNet.InParty && allyStaying.Count == 0 && allyWalkingOff.Count == 0)
				return;
			var drawn = BattleNet.Allies.Select(p => p.whoAmI).Concat(allyStaying).Distinct().ToList();
			// One walking off isn't drawn standing too
			foreach (int who in drawn)
			{
				Player p = Main.player[who];
				if (!p.active || p.dead || allyWalkingOff.ContainsKey(who))
					continue;
				int slot = SlotOf(who);
				if (slot >= AllyFeet.Length)
					continue;
				Vector2 spot = AllyFeet[slot];
				// Like the player: from where they stand in the world to their spot, growing to battle size, lit by the
				// world at the start of the glide and full bright once there
				float fly = AllyFly(p);
				Vector2 feet = Vector2.Lerp(WorldToBattle(p.Bottom), spot, fly);
				float scale = MathHelper.Lerp(WorldPixelScale(), HeroScale, fly);
				var (pose, timer) = AllyPose(p);
				float bob = pose switch
				{
					HeroPose.Idle => (float)Math.Round(Math.Sin((time + slot * 25) / 20f)),
					HeroPose.Act => -(float)Math.Sin(Math.Min(1f, timer / 14f) * Math.PI) * 10f,
					HeroPose.Victory => -(float)Math.Abs(Math.Sin(Math.Min(1f, timer / 27f) * Math.PI * 2)) * 8f,
					_ => 0f,
				};
				Color light = fly >= 1f ? Color.White : Color.Lerp(Lighting.GetColor(p.Center.ToTileCoordinates()), Color.White, fly);
				// Watchers stand a little see-through until they jump in
				float shadow = BattleNet.Joining.Contains(who) ? 0.5f : 0f;
				allyLastFeet[who] = spot;
				HeroLight = light;
				DrawPlayerPose(sb, m, p, feet + new Vector2(0f, bob * fly), scale, fly >= 1f ? pose : HeroPose.Idle, timer, shadow, ally: true);
				HeroLight = Color.White;
			}

			// Left mid-battle: they turn around and walk off the left side of the screen
			foreach (var (who, since) in allyWalkingOff.ToList())
			{
				Player p = Main.player[who];
				float t = Main.GameUpdateCount - since;
				Vector2 feet = allyLastFeet[who] - new Vector2(Math.Max(0f, t - 10f) * WalkOffSpeed, 0f);
				// Left the game: Terraria keeps their character around, so they can still walk off
				if (feet.X < -60f)
				{
					allyWalkingOff.Remove(who);
					allySlot.Remove(who);
					continue;
				}
				// A beat to turn around, then Terraria's walk cycle (frames 7-19)
				int walk = t < 10f ? -1 : 7 + (int)((t - 10f) / 4f) % 13;
				// Fades with the battle screen if it closes while they're still walking
				float fade = MathHelper.Clamp(screenFade, 0f, 1f);
				if (fade <= 0.02f)
					continue;
				DrawPlayerPose(sb, m, p, feet, HeroScale, HeroPose.Idle, 0f, 1f - fade, ally: true, facing: -1, walkFrame: walk);
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
				float frameAlpha = BattleNet.ActingNow.Contains(who) ? 1f : 0.55f;
				DrDraw.Outline(r.X, r.Y - 2, r.Width, 36, color * frameAlpha, 2);

				// The command they picked, like ours; otherwise their own head (see-through while they're watching)
				if (!(BattleNet.ReadyFaces.TryGetValue(who, out int face) && face > 0 && DrDraw.Sprite("spr_headkris", face, r.X + 3, r.Y + 4, Color.White)))
					DrawAllyHead(p, new Vector2(r.X + 16, r.Y + 15), watching ? 0.5f : 1f);

				const float nameScale = 0.68f, nameRoom = 64f;
				string name = (p.active ? p.name : "?").ToUpperInvariant();
				float scale = MathHelper.Clamp(nameRoom / Math.Max(1f, DrDraw.Measure(name, DrDraw.BigFont)), 0.4f, nameScale);
				while (name.Length > 1 && DrDraw.Measure(name, DrDraw.BigFont) * scale > nameRoom)
					name = name.Substring(0, name.Length - 1);
				DrDraw.Text(name, r.X + 40, r.Y + 7, p.dead ? Color.Gray : Color.White, DrDraw.BigFont, scale);

				const int labelX = 112, barX = 130, barWidth = 28, barY = 10, barHeight = 12;
				// Their HP as their own battle has it (Terraria's sync of other players' HP drifts with guessed regen)
				var (life, lifeMax) = BattleNet.AllyHp.TryGetValue(who, out var hpNow) ? hpNow : (p.statLife, p.statLifeMax2);
				float ratio = lifeMax > 0 ? MathHelper.Clamp(life / (float)lifeMax, 0f, 1f) : 0f;
				DrDraw.Text("HP", r.X + labelX - 4, r.Y + barY - 3, Color.White, DrDraw.SmallFont);
				DrDraw.Rect(r.X + barX, r.Y + barY, barWidth, barHeight, new Color(128, 0, 0));
				DrDraw.Rect(r.X + barX, r.Y + barY, (float)Math.Ceiling(ratio * barWidth), barHeight, color);
				string hp = $"{Math.Max(0, life)}/{lifeMax}";
				float room = 208 - (barX + barWidth + 3);
				float numberScale = Math.Min(1f, room / Math.Max(1f, DrDraw.Measure(hp, DrDraw.SmallFont)));
				DrDraw.Text(hp, r.X + barX + barWidth + 3, r.Y + barY + barHeight / 2f - DrDraw.LineHeight(DrDraw.SmallFont) * numberScale / 2f,
					ratio <= 0.25f ? new Color(255, 255, 0) : Color.White, DrDraw.SmallFont, numberScale);
			}
		}

		/// <summary>The other players' head portraits (Terraria renders heads into their own textures).</summary>
		private readonly Dictionary<int, RightFacingHead> allyHeads = new();

		private void DrawAllyHead(Player p, Vector2 center, float alpha)
		{
			if (!allyHeads.TryGetValue(p.whoAmI, out var head))
			{
				head = new RightFacingHead();
				allyHeads[p.whoAmI] = head;
				Main.ContentThatNeedsRenderTargets.Add(head);
			}
			head.Use(p);
			head.UseColor(PartyColors.Of(p));
			head.Request();
			Color tint = Color.Lerp(PartyColors.Of(p), Color.White, 0.45f);
			if (head.IsReady)
				DrDraw.Sb.Draw(head.GetTarget(), center, null, tint * alpha, 0f, new Vector2(42f), 0.82f, SpriteEffects.None, 0f);
			else
				DrDraw.HeartShapeAt(center.X - 6f, center.Y - 10f, 17, PartyColors.Of(p) * alpha);
		}

		/// <summary>Lets go of the head textures (the battle ended).</summary>
		private void ReleaseAllyHeads()
		{
			foreach (var head in allyHeads.Values)
			{
				Main.ContentThatNeedsRenderTargets.Remove(head);
				head.GetTarget()?.Dispose();
				head.Reset();
			}
			allyHeads.Clear();
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
				// The same SOUL sprite as ours, in their colour (a plain heart without Deltarune's)
				if (WhiteHeart() is Texture2D white)
					DrDraw.Sb.Draw(white, shown, null, c * 0.9f, 0f, DeltaruneAssets.Sprite("spr_dodgeheart").Origin, 1f, SpriteEffects.None, 0f);
				else
					DrDraw.HeartShapeAt(shown.X + 2, shown.Y + 2, 16, c * 0.9f);
			}
		}
	

		/// <summary>
		/// spr_dodgeheart with its red turned to white (shading kept), so tinting it gives a SOUL in any colour.
		/// </summary>
		private static Texture2D WhiteHeart()
		{
			if (DeltaruneAssets.Sprite("spr_dodgeheart") is not DrSprite s || Main.dedServ)
				return null;
			Texture2D src = s.Frame(0);
			if (whiteHeart != null && whiteHeartFrom == src)
				return whiteHeart;
			var pixels = new Color[src.Width * src.Height];
			src.GetData(pixels);
			for (int i = 0; i < pixels.Length; i++)
			{
				Color c = pixels[i];
				// Premultiplied: the brightest channel against alpha is how light the pixel is
				byte v = Math.Max(c.R, Math.Max(c.G, c.B));
				pixels[i] = new Color(v, v, v, c.A);
			}
			whiteHeart?.Dispose();
			whiteHeart = new Texture2D(Main.graphics.GraphicsDevice, src.Width, src.Height);
			whiteHeart.SetData(pixels);
			whiteHeartFrom = src;
			return whiteHeart;
		}
	}

	/// <summary>A head portrait always rendered facing right, whichever way the player faces in the world.</summary>
	public sealed class RightFacingHead : PlayerHeadDrawRenderTargetContent
	{
		private Player who;

		public void Use(Player p)
		{
			who = p;
			UsePlayer(p);
		}

		protected override void HandleUseReqest(Microsoft.Xna.Framework.Graphics.GraphicsDevice device, SpriteBatch spriteBatch)
		{
			if (who == null)
			{
				base.HandleUseReqest(device, spriteBatch);
				return;
			}
			int dir = who.direction;
			who.direction = 1;
			try
			{
				base.HandleUseReqest(device, spriteBatch);
			}
			finally
			{
				who.direction = dir;
			}
		}
	}
}
