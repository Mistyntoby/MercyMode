using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using MercyMode.Battle;
using MercyMode.Battle.Encounters;
using MercyMode.Battle.Net;
using Phase = MercyMode.Battle.BattleSystem.Phase;

namespace MercyMode.Lab
{
	/// <summary>
	/// The headless lab: on a dedicated server started with MERCYMODE_LAB set (tools/lab/lab.sh), the server turns
	/// itself into a single-player game with a stand-in player, plays scripted battles by pressing keys, checks what
	/// happens and writes a report, then quits. It never runs in a normal game. Drawing and sound aren't covered
	/// (the server has neither); everything else in a battle runs for real.
	/// </summary>
	public class LabSystem : ModSystem
	{
		private static MethodInfo doUpdate;

		private static readonly string Wanted = Environment.GetEnvironmentVariable("MERCYMODE_LAB");
		public static bool Enabled => Main.dedServ && !string.IsNullOrWhiteSpace(Wanted);

		private static readonly HashSet<Keys> down = new();
		private readonly Stack<IEnumerator> stack = new();
		private readonly List<string> report = new();
		private readonly List<(string name, Func<IEnumerable> run)> queue = new();
		private string current;
		private int currentTicks, passed, failed, updates;
		private bool setUp;
		private Phase lastPhase;
		private const int ScenarioTimeout = 60 * 60 * 20;

		private static BattleSystem B => BattleSystem.Instance;

		/// <summary>Full health, in the world and in the battle's own copy of it.</summary>
		private static void Heal()
		{
			P.statLife = P.statLifeMax2;
			if (BattleSystem.Active)
				B.SetBattleLife(P.statLife);
		}
		private static Player P => Main.player[0];

		public override void Load()
		{
			if (!Enabled)
				return;
			Main.OnTickForThirdPartySoftwareOnly += ServerTick;
			// The server drops every player without a network client each tick; the lab's player has none
			On_Netplay.UpdateConnectedClients += _ => { };
			// The stand-in player has no save file: a death mustn't try to write one (it crashes the server)
			On_Player.SavePlayer += (_, _, _) => { };
			StandInTextures();
		}

		public override void Unload()
		{
			Main.OnTickForThirdPartySoftwareOnly -= ServerTick;
		}

		/// <summary>
		/// A dedicated server only updates the world while a client is connected. The lab has none, so once the world
		/// is up it plays single-player and runs the game's update itself, once per server tick.
		/// </summary>
		private void ServerTick()
		{
			if (!Main.dedServ || Main.gameMenu && !setUp)
				return;
			if (!setUp)
			{
				setUp = true;
				Setup();
			}
			// Faster than real time when asked (MERCYMODE_LAB_SPEED game ticks per server tick): nothing here waits on
			// a clock, so the battles play out the same, just sooner
			for (int i = 0; i < Speed; i++)
			{
				// The lab is the keyboard: keys for this tick, then the next step of the script, then the game's update
				Main.oldKeyState = Main.keyState;
				Main.keyState = new KeyboardState(down.ToArray());
				Main.hasFocus = true;
				Main.gameMenu = false;
				// A 1080p screen centred on the player, for the battle's world-to-screen math
				Main.screenWidth = 1920;
				Main.screenHeight = 1080;
				Main.screenPosition = P.Center - new Vector2(960f, 540f);
				P.statLifeMax = 500;
				Step();
				try
				{
					// Main.DoUpdate is what Main.Update runs, minus the catch that would hide a crash from the lab
					doUpdate ??= typeof(Main).GetMethod("DoUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
					doUpdate.Invoke(Main.instance, new object[] { new GameTime() });
					updates++;
				}
				catch (Exception e)
				{
					Log("CRASH in the game update: " + (e is TargetInvocationException t ? t.InnerException : e));
					failed++;
					Finish();
				}
			}
		}

		private static readonly int Speed = Math.Clamp(int.TryParse(Environment.GetEnvironmentVariable("MERCYMODE_LAB_SPEED"), out int sp) ? sp : 1, 1, 32);

		/// <summary>
		/// The server has no textures, but battle code reads sprite sizes (bullet hitboxes, frames). Every texture
		/// becomes a 48x48 stand-in with no pixels: Asset.Value returns DefaultValue for anything not loaded, and the
		/// Load* calls that would fetch real art do nothing.
		/// </summary>
		private static void StandInTextures()
		{
			var tex = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
			GC.SuppressFinalize(tex);
			foreach (string f in new[] { "<Width>k__BackingField", "<Height>k__BackingField" })
				typeof(Texture2D).GetField(f, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(tex, 48);
			Asset<Texture2D>.DefaultValue = tex;
			On_Main.LoadNPC += (_, _, _) => { };
			On_Main.LoadProjectile += (_, _, _) => { };
			On_Main.LoadItem += (_, _, _) => { };
			On_Main.LoadGore += (_, _, _) => { };
		}

		/// <summary>Fills empty texture slots (run once the arrays have their final, modded size).</summary>
		private static void FillTextureSlots()
		{
			foreach (Asset<Texture2D>[] set in new[] { TextureAssets.Npc, TextureAssets.Projectile, TextureAssets.Item, TextureAssets.Gore, TextureAssets.Extra })
				for (int i = 0; i < set.Length; i++)
					set[i] ??= Asset<Texture2D>.Empty;
		}

		// ================================================================== runner

		private void Setup()
		{
			// Become a single-player game with one player standing at spawn
			FillTextureSlots();
			// The server keeps running as a server (none of the client's lighting, clouds and art), with player 0 as
			// the local player; MercyMode.IsSingleplayer counts the lab as single-player
			Main.myPlayer = 0;
			var p = new Player { name = "Lab", whoAmI = 0 };
			Main.player[0] = p;
			p.active = true;
			p.statLifeMax = 500;
			p.statLifeMax2 = 500;
			p.statLife = 500;
			p.position = new Vector2(Main.spawnTileX * 16f, (Main.spawnTileY - 3) * 16f);
			p.inventory[0].SetDefaults(ItemID.CopperBroadsword);
			p.inventory[1].SetDefaults(ItemID.LesserHealingPotion);
			p.inventory[1].stack = 5;
			p.selectedItem = 0;

			var all = new List<(string, Func<IEnumerable>)>
			{
				("squad-fight", SquadFight),
				("goblin-squad-spare", GoblinSquadSpare),
				("army-enrage", ArmyEnrage),
				("act-second-target", ActSecondTarget),
				("boss-fights-alone", BossAlone),
				("single-enemy", SingleEnemy),
				("worm-chains", WormChains),
				("parts-skeletron", PartsSkeletron),
				("parts-twins", PartsTwins),
				("parts-golem", PartsGolem),
				("soul-blue", SoulBlue),
				("soul-green", SoulGreen),
				("soul-purple", SoulPurple),
				("soul-yellow", SoulYellow),
				("desperation", Desperation),
				("attacks-enemies", AttacksEnemies),
				("attacks-signatures", AttacksSignatures),
				("attacks-armies", AttacksArmies),
				("attacks-bosses", AttacksBosses),
				("boss-kill", () => BossKill(NPCID.EyeofCthulhu)),
				("boss-kill-king-slime", () => BossKill(NPCID.KingSlime)),
				("boss-spare", BossSpare),
				("no-world-hits", NoWorldHits),
				("multi-hit-spills-over", MultiHitSpillsOver),
				("mp-server", MultiplayerServer),
				("frames", Frames),
				("eye-rips", EyeRips),
				("first-strike", FirstStrike),
				("everyone-talks", EveryoneTalks),
				("twins-summoned", TwinsSummoned),
				("twins-change", TwinsChange),
				("lose-despawns", LoseDespawns),
				("balance", Balance),
				("test-kits", TestKits),
			};
			string want = Wanted.Trim().ToLowerInvariant();
			foreach (var s in all)
				if (want == "all" || want.Split(',').Select(w => w.Trim()).Contains(s.Item1))
					queue.Add(s);
			Log($"Lab: {queue.Count} scenario(s): {string.Join(", ", queue.Select(q => q.name))}");
		}

		private void Step()
		{
			if (stack.Count == 0)
			{
				if (current != null)
				{
					Pass();
				}
				if (queue.Count == 0)
				{
					Finish();
					return;
				}
				var (name, run) = queue[0];
				queue.RemoveAt(0);
				current = name;
				currentTicks = 0;
				Log($"--- {name}");
				stack.Push(Scenario(run).GetEnumerator());
			}
			currentTicks++;
			if (B.LabPhase != lastPhase)
			{
				lastPhase = B.LabPhase;
				Log($"    [{currentTicks / 60f:0.0}s] {lastPhase}" + (lastPhase is Phase.Message or Phase.Intro or Phase.Menu ? $" \"{B.LabText.Replace("\n", " / ")}\"" : ""));
			}
			try
			{
				if (currentTicks > ScenarioTimeout)
					throw new LabFail($"timed out (phase {B.LabPhase}, {updates} game updates)");
				// Run until something waits a tick (yield return null)
				while (stack.Count > 0)
				{
					IEnumerator top = stack.Peek();
					if (!top.MoveNext())
					{
						stack.Pop();
						continue;
					}
					if (top.Current is IEnumerable nested)
					{
						stack.Push(nested.GetEnumerator());
						continue;
					}
					break;
				}
			}
			catch (Exception e)
			{
				string why = e is LabFail ? e.Message : e.ToString();
				Log($"FAIL {current}: {why}");
				failed++;
				stack.Clear();
				current = null;
				down.Clear();
			}
		}

		private void Pass()
		{
			Log($"PASS {current} ({currentTicks / 60f:0.0}s)");
			passed++;
			current = null;
		}

		private void Finish()
		{
			Log($"Lab done: {passed} passed, {failed} failed");
			string dir = Environment.GetEnvironmentVariable("MERCYMODE_LAB_OUT");
			if (!string.IsNullOrEmpty(dir))
				File.WriteAllLines(Path.Combine(dir, "lab-results.txt"), report);
			Environment.Exit(failed == 0 ? 0 : 1);
		}

		/// <summary>No battle, no enemies, full health: each scenario starts clean (a failed one can leave a battle going).</summary>
		private static IEnumerable Scenario(Func<IEnumerable> run)
		{
			down.Clear();
			foreach (NPC n in Main.npc)
				if (n.active && !n.townNPC)
					n.active = false;
			yield return Until(() => !BattleSystem.Active, "the last battle ending", 60 * 20);
			P.statLife = P.statLifeMax;
			P.dead = false;
			Main.invasionType = 0;
			Main.invasionSize = 0;
			yield return run();
		}

		/// <summary>Presses through text boxes until the phase is one of the given ones, returning every line shown.</summary>
		private static IEnumerable ReadUntil(List<string> seen, params Phase[] stop)
		{
			int t = 0;
			while (!stop.Contains(B.LabPhase))
			{
				Check(++t < 60 * 30, $"never reached {string.Join("/", stop)} (phase {B.LabPhase})");
				if (B.LabPhase == Phase.Message && (seen.Count == 0 || seen[^1] != B.LabText))
					seen.Add(B.LabText);
				if (B.LabPhase == Phase.Message)
				{
					down.Add(t % 4 < 2 ? Keys.X : Keys.Z);
					yield return null;
					down.Clear();
				}
				yield return null;
			}
		}

		private void Log(string line)
		{
			// Never flood the disk
			if (report.Count > 5000)
				Environment.Exit(3);
			report.Add(line);
			Console.WriteLine("[LAB] " + line);
			ModContent.GetInstance<MercyMode>().Logger.Info("[LAB] " + line);
		}

		private sealed class LabFail : Exception
		{
			public LabFail(string message) : base(message) { }
		}

		private static void Check(bool ok, string what)
		{
			if (!ok)
				throw new LabFail(what);
		}

		// ================================================================== building blocks

		private static IEnumerable Wait(int ticks)
		{
			for (int i = 0; i < ticks; i++)
				yield return null;
		}

		private static IEnumerable Press(Keys k)
		{
			down.Add(k);
			yield return null;
			down.Remove(k);
			yield return null;
		}

		/// <summary>Waits for a condition, pressing through text boxes on the way (X fills the text, Z moves on).</summary>
		private static IEnumerable Until(Func<bool> done, string what, int timeout = 60 * 60, bool skipText = true)
		{
			int t = 0;
			while (!done())
			{
				if (++t > timeout)
					throw new LabFail($"never reached: {what} (phase {B.LabPhase}, text \"{B.LabText}\")");
				// Boss bullets are sized for a geared player of their stage; the lab's player has no armour, so scenarios
				// that aren't about damage keep it topped up while waiting through enemy turns (the damage checks run
				// their own loops)
				if (BattleSystem.Active && B.LabPhase == Phase.EnemyTurn)
					Heal();
				if (skipText && B.LabPhase == Phase.Message)
				{
					down.Add(t % 4 < 2 ? Keys.X : Keys.Z);
					yield return null;
					down.Clear();
				}
				yield return null;
			}
		}

		private IEnumerable Menu() => Until(() => B.LabPhase == Phase.Menu || B.LabPhase == Phase.None, "the player's turn");

		/// <summary>FIGHT=0, ACT=1, ITEM=2, SPARE=3, DEFEND=4.</summary>
		private static IEnumerable Choose(int choice)
		{
			int guard = 0;
			while (B.LabMenuChoice != choice)
			{
				Check(++guard < 10, "couldn't move the menu cursor");
				foreach (object o in Press(Keys.Right))
					yield return o;
			}
			foreach (object o in Press(Keys.Z))
				yield return o;
		}

		/// <summary>In the enemy list, moves the cursor to the n-th living enemy and confirms.</summary>
		private static IEnumerable PickEnemy(int index)
		{
			yield return Until(() => B.LabPhase == Phase.EnemySelect, "the enemy list", skipText: false);
			int guard = 0;
			while (B.LabListIndex != index)
			{
				Check(++guard < 10, $"couldn't move the enemy cursor to {index}");
				yield return Press(Keys.Down);
			}
			Encounter want = Living()[index];
			Check(B.LabTarget == want, $"target is {B.LabTarget?.Name}, cursor is on {want.Name}");
			yield return Press(Keys.Z);
		}

		private static List<Encounter> Living() => B.LabEnemies.Where(e => !e.Out && e.E.Alive).Select(e => e.E).ToList();

		/// <summary>FIGHT the n-th enemy with the first weapon, after dropping it to 1 HP so any hit kills.</summary>
		private IEnumerable FightAndKill(int index)
		{
			Encounter target = Living()[index];
			foreach (NPC m in target.Members())
				m.life = 1;
			yield return Choose(0);
			yield return Until(() => B.LabPhase == Phase.WeaponSelect, "the weapon list", skipText: false);
			Check(B.LabWeaponCount > 0, "no weapons listed");
			yield return Press(Keys.Z);
			yield return PickEnemy(index);
			yield return Until(() => B.LabPhase == Phase.FightBar, "the FIGHT bar", skipText: false);
			// Mash Z until the bar is done
			int t = 0;
			while (B.LabPhase == Phase.FightBar)
			{
				Check(++t < 60 * 20, "the FIGHT bar never finished");
				down.Add(Keys.Z);
				yield return null;
				down.Remove(Keys.Z);
				yield return null;
			}
			Check(!target.Alive, $"{target.Name} survived the hit with 1 HP");
		}

		private IEnumerable Spare(int index)
		{
			yield return Choose(3);
			yield return PickEnemy(index);
		}

		/// <summary>Spawns enemies around the player and starts the battle with the first, like /mmbattle group.</summary>
		private static IEnumerable StartWith(params int[] types)
		{
			NPC first = null;
			for (int i = 0; i < types.Length; i++)
			{
				int idx = NPC.NewNPC(P.GetSource_FromThis(), (int)P.Center.X + 160 + i * 70, (int)P.Center.Y - 40 - (i % 2) * 50, types[i]);
				first ??= Main.npc[idx];
			}
			BattleSystem.QueueStart(first, 20);
			int t = 0;
			while (!BattleSystem.Active)
			{
				if (++t > 60 * 5)
				{
					NPC root = EncounterRegistry.ResolveRoot(first);
					throw new LabFail($"the battle never started: npc active {first.active} life {first.life}, player dead {P.dead} active {P.active} " +
						$"whoAmI {P.whoAmI}/{Main.myPlayer}, single {MercyMode.IsSingleplayer}, eligible {EncounterRegistry.Eligible(root)}, " +
						$"canStart {BattleSystem.CanStart(root, P, true)}");
				}
				yield return null;
			}
		}

		private IEnumerable WaitForEnd() => Until(() => !BattleSystem.Active, "the battle ending", 60 * 30);

		/// <summary>Watches one enemy turn: who attacks and whether bullets know who fired them.</summary>
		private IEnumerable WatchEnemyTurn(int expectedAttackers)
		{
			yield return Until(() => B.LabPhase is Phase.EnemyTurn or Phase.Outro or Phase.None, "the enemy turn");
			if (B.LabPhase != Phase.EnemyTurn)
				yield break;
			var owners = new HashSet<Encounter>();
			int t = 0;
			while (B.LabPhase == Phase.EnemyTurn && t++ < 60 * 20)
			{
				foreach (Bullet b in B.Bullets)
					if (b.Owner != null)
						owners.Add(b.Owner);
				yield return null;
			}
			Log($"  enemy turn: bullets from {owners.Count} enemies ({string.Join(", ", owners.Select(o => o.Name))}), HP {P.statLife}/{P.statLifeMax2}");
			Check(owners.Count >= 1, "no bullet had an owner");
			Check(owners.Count <= expectedAttackers, $"{owners.Count} enemies attacked, expected at most {expectedAttackers}");
			Check(owners.All(o => Living().Contains(o)), "a spared or defeated enemy attacked");
		}

		// ================================================================== scenarios

		private IEnumerable SquadFight()
		{
			yield return StartWith(NPCID.Zombie, NPCID.Zombie, NPCID.Zombie);
			var all = B.LabEnemies;
			Log($"  opened with {all.Count} enemies: \"{B.LabText}\"");
			Check(all.Count == 3, $"expected 3 enemies, got {all.Count}");
			Check(all.Select(e => e.E.Slot).Distinct().Count() == 3 && all.All(e => e.E.Slot != null), "enemies don't have their own spots");

			yield return Menu();
			// Last one first, to check the cursor wraps and the target follows it. Leftover hits may take out more.
			yield return FightAndKill(2);
			int left = Living().Count;
			Check(left <= 2, $"expected at most 2 left, {left} living");
			if (left > 0)
			{
				yield return Until(() => B.LabPhase != Phase.FightResult, "the end of the FIGHT", skipText: false);
				Check(B.LabPhase is Phase.EnemyTalk or Phase.EnemyIntro or Phase.EnemyTurn, $"expected the enemy turn right after the kill, got {B.LabPhase}");
				yield return WatchEnemyTurn(left);
			}
			// Keep fighting until everyone is down, each turn's attackers matching who's left
			int guard = 0;
			while (BattleSystem.Active && Living().Count > 0)
			{
				Check(++guard < 5, "too many turns");
				yield return Menu();
				if (!BattleSystem.Active || B.LabPhase != Phase.Menu)
					break;
				yield return FightAndKill(0);
				if (Living().Count > 0)
					yield return WatchEnemyTurn(Living().Count);
			}
			yield return WaitForEnd();
			Check(Main.npc.Count(n => n.active && n.type == NPCID.Zombie) == 0, "zombies still around after the battle");
		}

		private IEnumerable GoblinSquadSpare()
		{
			// A goblin invasion in progress: spares should count toward it like kills
			Main.invasionType = InvasionID.GoblinArmy;
			Main.invasionSize = Main.invasionSizeStart = 80;
			yield return StartWith(NPCID.GoblinPeon, NPCID.GoblinThief, NPCID.GoblinSorcerer);
			var all = B.LabEnemies;
			Log($"  opened with {all.Count}: \"{B.LabText}\"");
			Check(all.Count == 3, $"expected 3 goblins, got {all.Count}");
			Check(all.All(e => e.E is ArmyEnemy a && a.Kind == ArmyKind.Goblins), "not all goblins are army enemies");

			yield return Menu();
			Encounter first = Living()[0];
			first.Mercy = 100f;
			float otherBefore = Living()[1].Mercy;
			yield return Spare(0);
			var seen = new List<string>();
			yield return ReadUntil(seen, Phase.EnemyIntro, Phase.EnemyTurn, Phase.Outro);
			Log($"  after the spare: {string.Join(" | ", seen.Select(l => l.Replace("\n", " ")))}");
			Check(seen.Any(l => l.Contains("other goblins")), "no squad line after the spare");
			Check(!Living().Contains(first), "the spared goblin is still fighting");
			Check(Living().Count == 2, $"expected 2 left, {Living().Count} living");
			Log($"  squad MERCY {otherBefore:0} -> {Living()[0].Mercy:0} after the spare; invasion {Main.invasionSize}/{Main.invasionSizeStart}");
			Check(Living()[0].Mercy >= otherBefore + ArmyEnemy.MoraleOnSpare - 0.1f, "squad MERCY didn't rise after the spare");
			Check(Main.invasionSize < 80, "the spare didn't count toward the invasion");

			yield return WatchEnemyTurn(2);
			yield return Menu();
			foreach (Encounter e in Living())
				e.Mercy = 100f;
			yield return Spare(1);
			yield return Menu();
			Check(Living().Count == 1, "expected 1 goblin left");
			yield return Spare(0);
			yield return WaitForEnd();
			Log($"  invasion {Main.invasionSize}/{Main.invasionSizeStart} after sparing all three");
			Check(Main.invasionSize <= 80 - 3, "not every spare counted toward the invasion");
		}

		private IEnumerable ArmyEnrage()
		{
			yield return StartWith(NPCID.PirateDeckhand, NPCID.PirateCorsair);
			Check(B.LabEnemies.Count == 2, "expected 2 pirates");
			yield return Menu();
			yield return FightAndKill(0);
			Check(Living().Count == 1 && Living()[0] is EnemyEncounter { Enraged: true }, "the other pirate didn't get enraged");
			// No "was defeated" box while others remain: straight to the bullet box
			yield return Until(() => B.LabPhase != Phase.FightResult, "the end of the FIGHT", skipText: false);
			Check(B.LabPhase is Phase.EnemyTalk or Phase.EnemyIntro or Phase.EnemyTurn, $"expected the enemy turn right after the kill, got {B.LabPhase}");
			yield return WatchEnemyTurn(1);
			yield return Menu();
			yield return FightAndKill(0);
			yield return WaitForEnd();
		}

		private IEnumerable ActSecondTarget()
		{
			yield return StartWith(NPCID.BlueSlime, NPCID.BlueSlime);
			Check(B.LabEnemies.Count == 2, "expected 2 slimes");
			yield return Menu();
			Encounter a = Living()[0], b = Living()[1];
			float aBefore = a.Mercy, bBefore = b.Mercy;
			yield return Choose(1);
			yield return PickEnemy(1);
			yield return Until(() => B.LabPhase == Phase.ActSelect, "the ACT list", skipText: false);
			// The last act is usually the MERCY one; Check is first
			yield return Press(Keys.Right);
			yield return Press(Keys.Z);
			yield return Until(() => B.LabPhase != Phase.ActSelect, "the act running", skipText: false);
			Log($"  ACT on the second slime: MERCY {aBefore:0}/{bBefore:0} -> {a.Mercy:0}/{b.Mercy:0}");
			Check(a.Mercy == aBefore, "the act changed the wrong slime");
			yield return WatchEnemyTurn(2);
			yield return Menu();
			// Mixed ending: spare the one we acted on, defeat the other
			b.Mercy = 100f;
			yield return Spare(Living().IndexOf(b));
			yield return Menu();
			Check(Living().Count == 1 && Living()[0] == a, "the spare took the wrong slime");
			yield return FightAndKill(0);
			yield return WaitForEnd();
		}

		private IEnumerable BossAlone()
		{
			for (int i = 0; i < 2; i++)
				NPC.NewNPC(P.GetSource_FromThis(), (int)P.Center.X - 120 - i * 60, (int)P.Center.Y - 20, NPCID.Zombie);
			yield return StartWith(NPCID.EyeofCthulhu);
			Check(B.LabEnemies.Count == 1, $"the boss brought {B.LabEnemies.Count - 1} zombies along");
			yield return Menu();
			yield return Choose(4); // DEFEND
			yield return WatchEnemyTurn(1);
			yield return Menu();
			foreach (NPC m in B.LabTarget.Members().ToList())
				m.active = false;
			yield return WaitForEnd();
		}

		private IEnumerable BossKill(int type)
		{
			yield return StartWith(type);
			Check(B.LabEnemies.Count == 1 && B.LabTarget.Slot == null, "a boss should fight alone, in its own spot");
			yield return Menu();
			yield return FightAndKill(0);
			yield return Until(() => B.LabPhase == Phase.Message, "the win message", skipText: false);
			Check(B.LabText.Contains("YOU WON"), $"expected the win text, got \"{B.LabText}\"");
			yield return WaitForEnd();
		}

		/// <summary>Bosses can't be spared: MERCY stays at 0, and SPARE just says it won't back down.</summary>
		private IEnumerable BossSpare()
		{
			yield return StartWith(NPCID.EyeofCthulhu);
			yield return Menu();
			B.LabTarget.Mercy = 100f;
			Check(B.LabTarget.Mercy == 0f, $"a boss got MERCY ({B.LabTarget.Mercy})");
			yield return Spare(0);
			yield return Until(() => B.LabPhase == Phase.Message, "the spare message", skipText: false);
			Check(B.LabText.Contains("won't back down"), $"expected the boss to refuse, got \"{B.LabText}\"");
			Check(Main.npc.Any(n => n.active && n.type == NPCID.EyeofCthulhu), "the Eye was spared anyway");
			yield return Menu();
			yield return FightAndKill(0);
			yield return WaitForEnd();
		}

		/// <summary>A swing or shot from before the battle (or still in the world) never hurts the enemy during it.</summary>
		/// <summary>
		/// The multiplayer server's bookkeeping, run directly (the lab has no game clients): who gets pulled into a
		/// battle, which NPCs freeze, the ready barrier and its timeout, shared MERCY, spares and leaving.
		/// </summary>
		private IEnumerable MultiplayerServer()
		{
			BattleNet.Reset();
			BattleNet.LabCapture = true;
			BattleNet.LabSent.Clear();
			var fakes = new[] { 1, 2, 3, 4 };
			try
			{
				// Players 1, 2 and 4 stand nearby, 3 far away
				foreach (int i in fakes)
				{
					var f = new Player { name = "Ally" + i, whoAmI = i };
					Main.player[i] = f;
					f.active = true;
					f.statLifeMax = f.statLifeMax2 = f.statLife = 100;
					f.position = P.position + new Vector2(i == 3 ? 4000f : 40f * i, 0f);
				}
				NPC Spawn(int x) => Main.npc[NPC.NewNPC(P.GetSource_FromThis(), (int)P.Center.X + x, (int)P.Center.Y - 40, NPCID.Zombie)];
				NPC z1 = Spawn(200), z2 = Spawn(-200), z3 = Spawn(260);
				z1.velocity = new Vector2(3f, 0f);
				z2.velocity = new Vector2(-2f, 1f);

				int id = BattleNet.ServerStartBattle(0, new List<NPC> { z1, z3 });
				var party = BattleNet.LabPlayers(id);
				Log($"  battle {id}: party {string.Join(",", party)}; sent {string.Join(" ", BattleNet.LabSent)}");
				Check(id > 0, "no battle was made");
				Check(party.SequenceEqual(new[] { 0, 1, 2 }), $"party {string.Join(",", party)}, expected 0,1,2 (3 is far, 4 is past the limit)");
				Check(BattleNet.IsFrozen(z1) && BattleNet.IsFrozen(z3), "the battle's enemies aren't frozen");
				Check(!BattleNet.IsFrozen(z2), "an enemy outside the battle froze");
				Check(z1.velocity == Vector2.Zero, "a frozen enemy kept moving");
				Check(new[] { 0, 1, 2 }.All(i => BattleNet.LabSent.Contains($"JoinBattle>{i}")), "not every party member was told to join");
				Check(BattleNet.LabSent.Contains("Frozen>all") && BattleNet.LabSent.Contains("BattleState>all"), "everyone wasn't told about the battle");
				Check(BattleNet.InBattle(1) && !BattleNet.InBattle(3), "InBattle is wrong");
				Check(BattleNet.ServerStartBattle(4, new List<NPC> { z1 }) == -1, "a second battle took a frozen enemy");
				BattleNet.ServerJoin(4, z1);
				Check(!BattleNet.InBattle(4), "a fourth player joined a full party");

				int Sent(string m) => BattleNet.LabSent.Count(x => x.StartsWith(m + ">"));
				// Nobody acts until all three have picked
				BattleNet.LabSent.Clear();
				BattleNet.ServerReady(0, id, 1);
				BattleNet.ServerReady(1, id, 6);
				Check(Sent("TurnOf") == 0, "someone acted before everyone had picked");
				BattleNet.ServerReady(2, id, 3);
				// Player 0 picked FIGHT, 1 ACT, 2 ITEM: the ACT and the ITEM go first, one at a time, then the FIGHT
				Check(BattleNet.LabStage(id) == BattleNet.Stage.Acting && BattleNet.LabStep(id).SequenceEqual(new[] { 1 }), $"step {string.Join(",", BattleNet.LabStep(id))}, expected the ACT (1) first");
				Check(Sent("TurnOf") == 3, "the turn wasn't announced to all three");
				BattleNet.ServerActionDone(0, id);
				Check(BattleNet.LabStep(id).SequenceEqual(new[] { 1 }), "player 0 ended player 1's turn");
				BattleNet.ServerActionDone(1, id);
				Check(BattleNet.LabStep(id).SequenceEqual(new[] { 2 }), "the ITEM (2) isn't next");
				BattleNet.ServerActionDone(2, id);
				Check(BattleNet.LabStep(id).SequenceEqual(new[] { 0 }), "the FIGHT (0) isn't last");
				Check(Sent("BeginEnemyTurn") == 0, "the bullet box opened before the last action");
				BattleNet.ServerActionDone(0, id);
				Check(BattleNet.LabStage(id) == BattleNet.Stage.EnemyTurn && Sent("BeginEnemyTurn") == 3, "the bullet box didn't open for all three");

				// Someone leaves; a newcomer presses the join key: they watch, then jump in at the next bullet box
				BattleNet.ServerLeave(2, id);
				BattleNet.LabSent.Clear();
				BattleNet.ServerJoin(4, z1);
				Check(BattleNet.LabPending(id).SequenceEqual(new[] { 4 }) && BattleNet.LabSent.Contains("JoinBattle>4"), "the newcomer wasn't queued");
				BattleNet.ServerReady(4, id, 1);
				BattleNet.ServerReady(0, id, 1);
				BattleNet.ServerReady(1, id, 1);
				Check(BattleNet.LabCurrent(id) == 0, "a watcher held up (or joined) the round");
				BattleNet.ServerActionDone(0, id);
				BattleNet.ServerActionDone(1, id);
				Check(BattleNet.LabPlayers(id).SequenceEqual(new[] { 0, 1, 4 }) && BattleNet.LabPending(id).Count == 0, "the newcomer didn't jump in at the bullet box");
				Check(BattleNet.LabSent.Contains("BeginEnemyTurn>4"), "the newcomer isn't in the bullet box");

				// Everyone picked FIGHT: they all fight at once; the step ends when every one of them is done
				BattleNet.ServerReady(0, id, 1);
				BattleNet.ServerReady(1, id, 1);
				BattleNet.ServerReady(4, id, 1);
				Check(BattleNet.LabStep(id).SequenceEqual(new[] { 0, 1, 4 }), $"step {string.Join(",", BattleNet.LabStep(id))}, expected all three fighting together");
				BattleNet.ServerActionDone(1, id);
				Check(BattleNet.LabStage(id) == BattleNet.Stage.Acting, "the FIGHT step ended before everyone finished");
				// One of them leaves mid-FIGHT, another never finishes: the step still ends (timeout)
				BattleNet.ServerLeave(0, id);
				Check(BattleNet.LabStage(id) == BattleNet.Stage.Acting, "the FIGHT step ended with player 4 still fighting");
				for (int t = 0; t <= BattleNet.ActTimeoutTicks + 1; t++)
					BattleNet.ServerUpdate();
				Check(BattleNet.LabStage(id) == BattleNet.Stage.EnemyTurn, "a stuck FIGHT never timed out");
				// One AFK player: the others go ahead after the wait
				BattleNet.ServerReady(1, id, 1);
				for (int t = 0; t <= BattleNet.ChooseTimeoutTicks + 1; t++)
					BattleNet.ServerUpdate();
				Check(BattleNet.LabStage(id) == BattleNet.Stage.Acting && BattleNet.LabCurrent(id) == 1, "the wait for an AFK player never timed out");
				BattleNet.ServerActionDone(1, id);

				// MERCY adds up across the party, capped at 100
				BattleNet.ServerAddMercy(z1, 40f);
				BattleNet.ServerAddMercy(z1, 40f);
				Check(Math.Abs(z1.GetGlobalNPC<MercyGlobalNPC>().Mercy - 80f) < 0.01f, $"MERCY {z1.GetGlobalNPC<MercyGlobalNPC>().Mercy}, expected 80");
				BattleNet.ServerAddMercy(z1, 40f);
				Check(z1.GetGlobalNPC<MercyGlobalNPC>().Mercy == 100f, "MERCY went past 100");

				// A spare goes through the server: the enemy leaves, the others hear about it (not the one who spared)
				BattleNet.LabSent.Clear();
				BattleNet.ServerSpare(0, z1);
				Check(!z1.active, "the spared enemy is still there");
				Check(BattleNet.LabSent.Contains("Spared>1") && !BattleNet.LabSent.Contains("Spared>0"), $"spare sent {string.Join(" ", BattleNet.LabSent)}");
				Check(z1.playerInteraction[1] && z1.playerInteraction[4], "the party doesn't count for the boss bags");

				// Only a battle's own enemies can be killed through it
				BattleNet.ServerKill(0, new List<NPC> { z2 });
				Check(z2.active, "KillMembers killed an enemy outside the battle");
				BattleNet.ServerKill(0, new List<NPC> { z3 });
				Check(!z3.active, "KillMembers didn't kill a battle member");

				// Every enemy gone: the battle counts as won, nobody can join it, it stops showing as a battle
				BattleNet.ServerUpdate();
				Check(BattleNet.LabStage(id) == BattleNet.Stage.Over, $"stage {BattleNet.LabStage(id)} after every enemy went, expected Over");
				BattleNet.ServerJoin(2, z1);
				Check(!BattleNet.InBattle(2), "someone joined a battle that was already won");

				// Everyone leaves: the battle closes and its enemies are let go
				BattleNet.LabSent.Clear();
				BattleNet.ServerLeave(1, id);
				BattleNet.ServerDisconnect(4);
				Check(BattleNet.LabBattleCount == 0, "the battle stayed open with nobody in it");
				Check(BattleNet.LabSent.Contains("Unfrozen>all"), "clients weren't told the battle ended");

				// The whole party gone mid-battle (died or left): its enemies despawn, they aren't let go or killed
				int id2 = BattleNet.ServerStartBattle(3, new List<NPC> { z2 });
				// Player 3 is far from everyone: only close players come along, even if others stand by the enemy
				Check(BattleNet.LabPlayers(id2).SequenceEqual(new[] { 3 }), $"party {string.Join(",", BattleNet.LabPlayers(id2))}: players far from the starter were pulled in");
				Check(z2.velocity == Vector2.Zero && BattleNet.IsFrozen(z2), "z2 didn't freeze");
				BattleNet.ServerLeave(3, id2);
				Check(!z2.active, "z2 stayed in the world after its party left mid-battle");
				Check(!BattleNet.IsFrozen(z2), "z2 stayed frozen");
				Log("  server bookkeeping ok");
			}
			finally
			{
				foreach (int i in fakes)
					Main.player[i].active = false;
				BattleNet.LabCapture = false;
				BattleNet.Reset();
			}
			yield break;
		}

		private IEnumerable NoWorldHits()
		{
			// Mid-swing and with an arrow already flying when the battle starts
			P.itemAnimation = P.itemAnimationMax = 30;
			int arrow = Projectile.NewProjectile(P.GetSource_FromThis(), P.Center + new Vector2(300f, 0f), Vector2.Zero, ProjectileID.WoodenArrowFriendly, 20, 0f, 0);
			yield return StartWith(NPCID.Zombie);
			Check(P.itemAnimation == 0, $"the swing kept going into the battle ({P.itemAnimation})");
			Check(!Main.projectile[arrow].active, "the arrow from before the battle is still flying");
			NPC z = B.LabTarget.Npc;
			int life = z.life;
			// A friendly shot sitting right on the enemy for a second
			int shot = Projectile.NewProjectile(P.GetSource_FromThis(), z.Center, Vector2.Zero, ProjectileID.WoodenArrowFriendly, 20, 0f, 0);
			yield return Wait(60);
			Main.projectile[shot].active = false;
			Check(z.life == life, $"the enemy took damage outside FIGHT ({life} -> {z.life})");
			yield return Menu();
			yield return FightAndKill(0);
			yield return WaitForEnd();
		}

		/// <summary>A fast weapon's later hits move on to the next enemy once its target is down.</summary>
		private IEnumerable MultiHitSpillsOver()
		{
			Item saved = P.inventory[0];
			P.inventory[0] = new Item(ItemID.CopperShortsword);
			try
			{
				yield return StartWith(NPCID.Zombie, NPCID.Zombie, NPCID.Zombie);
				yield return Menu();
				foreach (Encounter e in Living())
					foreach (NPC m in e.Members())
						m.life = 1;
				yield return FightAndKill(0);
				yield return Until(() => B.LabPhase != Phase.FightResult, "the end of the FIGHT", skipText: false);
				int down = B.LabEnemies.Count(e => !e.E.Alive);
				Log($"  one FIGHT with a Copper Shortsword took down {down} of 3");
				Check(down >= 2, "the later hits didn't carry on to the next enemy");
				foreach (NPC n in Main.npc)
					if (n.active && n.type == NPCID.Zombie)
						n.active = false;
				yield return WaitForEnd();
			}
			finally
			{
				P.inventory[0] = saved;
			}
		}

		// ================================================================== attack sweeps

		private static int lastStartFailed;

		/// <summary>Like StartWith, but a battle that never starts is reported (some bosses can't spawn here) instead of failing.</summary>
		private IEnumerable TryStartWith(params int[] types)
		{
			lastStartFailed = 0;
			NPC first = null;
			for (int i = 0; i < types.Length; i++)
			{
				int idx = NPC.NewNPC(P.GetSource_FromThis(), (int)P.Center.X + 160 + i * 70, (int)P.Center.Y - 40 - (i % 2) * 50, types[i]);
				first ??= Main.npc[idx];
			}
			BattleSystem.QueueStart(first, 20);
			int t = 0;
			while (!BattleSystem.Active)
			{
				if (++t > 60 * 8)
				{
					lastStartFailed = 1;
					yield break;
				}
				yield return null;
			}
		}

		/// <summary>
		/// Plays one enemy's turns 0..count-1 (DEFEND each time, healed every tick) and checks every attack runs:
		/// spawns something, doesn't crash, and hands the turn back.
		/// </summary>
		private IEnumerable SweepAttacks(string label, int count, params int[] types)
		{
			yield return TryStartWith(types);
			if (lastStartFailed != 0)
			{
				Log($"  SKIP {label}: the battle didn't start here");
				foreach (NPC n in Main.npc)
					if (n.active && !n.townNPC)
						n.active = false;
				yield break;
			}
			Encounter e = B.LabTarget;
			var seen = new List<string>();
			for (int i = 0; i < count; i++)
			{
				yield return Menu();
				if (!BattleSystem.Active)
					break;
				e.Turn = i;
				Heal();
				// Keep the boss away from its desperation turn: this sweeps its normal attacks
				e.DesperationUsed = true;
				yield return Choose(4);
				yield return Until(() => B.LabPhase is Phase.EnemyTurn or Phase.None or Phase.Outro, "the enemy turn");
				string name = B.LabAttack is Combo ? "Combo" : B.LabAttack?.GetType().Name ?? "?";
				SoulMode mode = B.SoulMode;
				int most = 0, ticks = 0;
				while (B.LabPhase == Phase.EnemyTurn)
				{
					Heal();
					most = Math.Max(most, B.Bullets.Count);
					ticks++;
					Check(ticks < 60 * 40, $"{label} attack {i} ({name}) never ended");
					yield return null;
				}
				Check(B.LabPhase != Phase.Death, $"{label} attack {i} killed the player at full health: {B.LabDeath}");
				seen.Add($"{i}:{name}{(mode != SoulMode.Red ? "/" + mode : "")}({most})");
				Check(most > 0, $"{label} attack {i} ({name}) spawned nothing");
			}
			Log($"  {label} [{e.GetType().Name}]: {string.Join(" ", seen)}");
			foreach (NPC n in Main.npc)
				if (n.active && !n.townNPC)
					n.active = false;
			yield return Until(() => !BattleSystem.Active, "the battle ending", 60 * 30);
		}

		private IEnumerable AttacksEnemies()
		{
			var families = new (string, int)[]
			{
				("slime", NPCID.BlueSlime), ("fighter", NPCID.Zombie), ("flier", NPCID.DemonEye), ("caster", NPCID.DarkCaster),
				("worm", NPCID.GiantWormHead), ("water", NPCID.Piranha), ("spider", NPCID.Herpling), ("mimic", NPCID.Mimic),
				("charger", NPCID.Unicorn), ("spirit", NPCID.CursedSkull), ("blade", NPCID.EnchantedSword), ("snapper", NPCID.Antlion),
				("generic", NPCID.Harpy),
			};
			foreach (var (label, type) in families)
				yield return SweepAttacks(label, 7, type);
		}

		/// <summary>Enemies with their own attack (EnemySignatures): turn 0 is theirs.</summary>
		private IEnumerable AttacksSignatures()
		{
			var types = new (string, int)[]
			{
				("skeleton", NPCID.Skeleton), ("angry bones", NPCID.AngryBones), ("cave bat", NPCID.CaveBat), ("hellbat", NPCID.Hellbat),
				("hornet", NPCID.Hornet), ("eater of souls", NPCID.EaterofSouls), ("face monster", NPCID.FaceMonster), ("ice slime", NPCID.IceSlime),
				("fire imp", NPCID.FireImp), ("lava slime", NPCID.LavaSlime), ("jellyfish", NPCID.BlueJellyfish), ("shark", NPCID.Shark),
				("possessed armor", NPCID.PossessedArmor), ("wraith", NPCID.Wraith), ("pixie", NPCID.Pixie), ("mummy", NPCID.Mummy),
				("werewolf", NPCID.Werewolf), ("meteor head", NPCID.MeteorHead), ("granite", NPCID.GraniteFlyer), ("bone serpent", NPCID.BoneSerpentHead),
				("tim", NPCID.Tim), ("ghost", NPCID.Ghost), ("demon", NPCID.Demon), ("crab", NPCID.Crab), ("vulture", NPCID.Vulture),
				("bee", NPCID.Bee),
			};
			foreach (var (label, type) in types)
				yield return SweepAttacks(label, 1, type);
		}

		private IEnumerable AttacksArmies()
		{
			var soldiers = new (string, int)[]
			{
				("goblin peon", NPCID.GoblinPeon), ("goblin archer", NPCID.GoblinArcher), ("goblin sorcerer", NPCID.GoblinSorcerer),
				("pirate", NPCID.PirateDeckhand), ("frost legion", NPCID.SnowmanGangsta), ("martian", NPCID.GrayGrunt),
				("pumpkin moon", NPCID.Scarecrow1), ("frost moon", NPCID.ZombieElf), ("old one's army", NPCID.DD2GoblinT1),
				("eclipse", NPCID.SwampThing),
			};
			foreach (var (label, type) in soldiers)
				yield return SweepAttacks(label, 6, type);
		}

		private IEnumerable AttacksBosses()
		{
			var bosses = new (string, int[])[]
			{
				("eye of cthulhu", new int[] { NPCID.EyeofCthulhu }), ("king slime", new int[] { NPCID.KingSlime }),
				("eater of worlds", new int[] { NPCID.EaterofWorldsHead }), ("brain of cthulhu", new int[] { NPCID.BrainofCthulhu }),
				("queen bee", new int[] { NPCID.QueenBee }), ("skeletron", new int[] { NPCID.SkeletronHead }), ("deerclops", new int[] { NPCID.Deerclops }),
				("wall of flesh", new int[] { NPCID.WallofFlesh }), ("queen slime", new int[] { NPCID.QueenSlimeBoss }),
				("twins", new int[] { NPCID.Retinazer, NPCID.Spazmatism }), ("destroyer", new int[] { NPCID.TheDestroyer }),
				("skeletron prime", new int[] { NPCID.SkeletronPrime }), ("plantera", new int[] { NPCID.Plantera }), ("golem", new int[] { NPCID.Golem }),
				("duke fishron", new int[] { NPCID.DukeFishron }), ("empress", new int[] { NPCID.HallowBoss }), ("cultist", new int[] { NPCID.CultistBoss }),
				("moon lord", new int[] { NPCID.MoonLordCore }),
			};
			foreach (var (label, types) in bosses)
				yield return SweepAttacks(label, 9, types);
		}

		/// <summary>Worms are drawn as a chain of segments from the head to the tail, not just their head.</summary>
		private IEnumerable WormChains()
		{
			foreach (var (label, type, tail) in new (string, int, int)[]
			{
				("destroyer", NPCID.TheDestroyer, NPCID.TheDestroyerTail),
				("eater of worlds", NPCID.EaterofWorldsHead, NPCID.EaterofWorldsTail),
				("giant worm", NPCID.GiantWormHead, NPCID.GiantWormTail),
			})
			{
				yield return StartWith(type);
				yield return Menu();
				var chain = B.LabTarget.DrawParts().ToList();
				string kinds = string.Join(" ", chain.Select(n => n.type == type ? "H" : n.type == tail ? "T" : "b"));
				Log($"  {label}: {chain.Count} segments drawn: {kinds}");
				Check(chain.Count >= 5, $"{label}: only {chain.Count} segments in the chain");
				Check(chain[0].type == type && chain[^1].type == tail, $"{label}: the chain doesn't run head to tail ({kinds})");
				foreach (NPC m in B.LabTarget.Members().ToList())
					m.active = false;
				yield return Until(() => !BattleSystem.Active, "the battle ending", 60 * 30);
			}
		}

		// ================================================================== breakable bosses

		/// <summary>FIGHT the part named in the enemy list, after dropping it to 1 HP so the hit breaks it.</summary>
		private IEnumerable FightPart(string part, bool expectLocked = false)
		{
			yield return Choose(0);
			yield return Until(() => B.LabPhase == Phase.WeaponSelect, "the weapon list", skipText: false);
			yield return Press(Keys.Z);
			yield return Until(() => B.LabPhase == Phase.EnemySelect, "the enemy list", skipText: false);
			var rows = B.LabRows;
			int want = rows.FindIndex(r => r.Name == part);
			Check(want >= 0, $"no {part} in the list: {string.Join(", ", rows.Select(r => r.Name + (r.Locked ? " (guarded)" : "")))}");
			int guard = 0;
			while (B.LabListIndex != want)
			{
				Check(++guard < 12, $"couldn't move the cursor to {part}");
				yield return Press(Keys.Down);
			}
			NPC chosen = B.LabTarget.ChosenPart;
			Check(chosen != null && B.LabTarget.PartName(chosen) == part, $"the cursor is on {part} but the chosen part is {(chosen == null ? "none" : B.LabTarget.PartName(chosen))}");
			if (expectLocked)
			{
				yield return Press(Keys.Z);
				yield return Wait(4);
				Check(B.LabPhase == Phase.EnemySelect, $"a guarded {part} could be picked");
				yield return Press(Keys.X);
				yield return Press(Keys.X);
				yield return Until(() => B.LabPhase == Phase.Menu, "back to the menu", skipText: false);
				yield break;
			}
			chosen.life = 1;
			yield return Press(Keys.Z);
			yield return Until(() => B.LabPhase == Phase.FightBar, "the FIGHT bar", skipText: false);
			int t = 0;
			while (B.LabPhase == Phase.FightBar)
			{
				Check(++t < 60 * 20, "the FIGHT bar never finished");
				down.Add(Keys.Z);
				yield return null;
				down.Remove(Keys.Z);
				yield return null;
			}
			Check(!chosen.active || chosen.life <= 0 || !B.LabTarget.Members().Contains(chosen), $"{part} survived the hit with 1 HP");
		}

		private IEnumerable PartsSkeletron()
		{
			yield return StartWith(NPCID.SkeletronHead);
			yield return Menu();
			yield return Choose(0);
			yield return Until(() => B.LabPhase == Phase.WeaponSelect, "the weapon list", skipText: false);
			yield return Press(Keys.Z);
			yield return Until(() => B.LabPhase == Phase.EnemySelect, "the enemy list", skipText: false);
			var rows = B.LabRows.Select(r => r.Name).ToList();
			Log($"  rows: {string.Join(", ", rows)}");
			Check(rows.SequenceEqual(new[] { "SKELETRON", "LEFT HAND", "RIGHT HAND" }), "expected the head and both hands");
			yield return Press(Keys.X);
			yield return Press(Keys.X);
			yield return Until(() => B.LabPhase == Phase.Menu, "back to the menu", skipText: false);

			yield return FightPart("LEFT HAND");
			Check(BattleSystem.Active && B.LabTarget.Alive, "breaking a hand ended the fight");
			yield return Until(() => B.LabPhase is Phase.EnemyTurn, "the enemy turn after the hand broke");
			yield return Menu();
			yield return FightPart("SKELETRON");
			yield return WaitForEnd();
			Check(!Main.npc.Any(n => n.active && n.type == NPCID.SkeletronHand), "a hand outlived the head");
		}

		private IEnumerable PartsTwins()
		{
			yield return StartWith(NPCID.Retinazer, NPCID.Spazmatism);
			yield return Menu();
			// They spawn on one spot: posed for the battle screen they sit apart, both looking left at the party
			var twins = B.LabTarget.DrawParts().ToList();
			Check(twins.Count == 2, $"{twins.Count} twins drawn, not 2");
			Vector2[] saved = twins.Select(n => n.position).ToArray();
			float[] savedRot = twins.Select(n => n.rotation).ToArray();
			B.LabTarget.PoseForBattle(twins, twins[0], 0, 0f);
			float apart = twins.Count == 2 ? Vector2.Distance(twins[0].Center, twins[1].Center) : 0f;
			bool lookLeft = twins.All(n => Math.Abs(MathHelper.WrapAngle(n.rotation - MathHelper.PiOver2)) < 0.3f);
			for (int i = 0; i < twins.Count; i++)
			{
				twins[i].position = saved[i];
				twins[i].rotation = savedRot[i];
			}
			Log($"  posed {apart:0} px apart, looking left: {lookLeft}");
			Check(apart > 100f, $"the twins are drawn only {apart:0} px apart");
			Check(lookLeft, "a twin doesn't look at the party");
			yield return FightPart("RETINAZER");
			Check(BattleSystem.Active && B.LabTarget.Alive, "breaking one twin ended the fight");
			yield return Until(() => B.LabPhase is Phase.EnemyTurn, "the enemy turn");
			string name = B.LabAttack?.GetType().Name;
			Log($"  Spazmatism alone attacks with {name}");
			Check(name is "Sprinkler" or "LaneDash" or "Forecast", $"the lone Spazmatism used {name}, one of Retinazer's attacks");
			yield return Menu();
			yield return FightPart("SPAZMATISM");
			yield return WaitForEnd();
		}

		private IEnumerable PartsGolem()
		{
			yield return StartWith(NPCID.Golem);
			yield return Menu();
			yield return FightPart("GOLEM", expectLocked: true);
			yield return FightPart("HEAD");
			yield return Until(() => B.LabPhase is Phase.EnemyTurn, "the enemy turn");
			yield return Menu();
			yield return FightPart("GOLEM");
			yield return WaitForEnd();
		}

		// ================================================================== SOUL modes

		/// <summary>Starts a zombie battle whose enemy turns all run one attack, and goes to the first enemy turn.</summary>
		private IEnumerable ForcedTurn(Func<EnemyAttack> attack)
		{
			BattleSystem.LabForcedAttack = attack;
			yield return StartWith(NPCID.Zombie);
			yield return Menu();
			yield return Choose(4);
			yield return Until(() => B.LabPhase == Phase.EnemyTurn, "the enemy turn");
		}

		private IEnumerable EndForced()
		{
			BattleSystem.LabForcedAttack = null;
			foreach (NPC n in Main.npc)
				if (n.active && !n.townNPC)
					n.active = false;
			yield return Until(() => !BattleSystem.Active, "the battle ending", 60 * 30);
		}

		private IEnumerable SoulBlue()
		{
			try
			{
				yield return ForcedTurn(() => new BoneWalls(40));
				Check(B.SoulMode == SoulMode.Blue, $"mode is {B.SoulMode}");
				float floor = B.Box.Bottom - BattleConstants.BoxClampHigh;
				yield return Wait(30);
				Check(Math.Abs(B.LabSoul.Y - floor) < 1f, $"the blue SOUL isn't on the floor ({B.LabSoul.Y} vs {floor})");
				down.Add(Keys.Up);
				yield return Wait(18);
				float peak = B.LabSoul.Y;
				down.Remove(Keys.Up);
				Check(peak < floor - 30f, $"the jump only got to {floor - peak:0} px");
				yield return Wait(70);
				Check(Math.Abs(B.LabSoul.Y - floor) < 1f, "the blue SOUL didn't land again");
				// A tap of Up jumps lower than holding it
				down.Add(Keys.Up);
				yield return Wait(2);
				down.Remove(Keys.Up);
				float lowPeak = floor;
				for (int i = 0; i < 40; i++)
				{
					lowPeak = Math.Min(lowPeak, B.LabSoul.Y);
					yield return null;
				}
				Log($"  held jump {floor - peak:0} px, tapped jump {floor - lowPeak:0} px");
				Check(floor - lowPeak < floor - peak, "a tap jumped as high as a hold");
			}
			finally
			{
				BattleSystem.LabForcedAttack = null;
			}
			yield return EndForced();
		}

		private IEnumerable SoulGreen()
		{
			try
			{
				yield return ForcedTurn(() => new ShieldSpears((p, v) => Shots.Ball(p, v, Color.White), 18) { TricksterEvery = 0 });
				Check(B.SoulMode == SoulMode.Green, $"mode is {B.SoulMode}");
				Vector2 centre = B.LabSoul + new Vector2(BattleConstants.SoulSize / 2f);
				Check(Vector2.Distance(centre, B.Box.Center.ToVector2()) < 2f, "the green SOUL isn't in the middle");
				yield return Press(Keys.Right);
				Check(B.LabShieldDir == 1, $"shield faces {B.LabShieldDir}, expected right");
				B.LabBlocks = 0;
				int hp = P.statLife;
				// Turn the shield to the nearest spear, like a player would
				while (B.LabPhase == Phase.EnemyTurn)
				{
					Bullet near = B.Bullets.Where(b => b.Harmful && !b.Waiting).OrderBy(b => Vector2.DistanceSquared(b.Position, centre)).FirstOrDefault();
					down.Clear();
					if (near != null)
					{
						Vector2 d = near.Position - centre;
						down.Add(Math.Abs(d.X) > Math.Abs(d.Y) ? (d.X > 0 ? Keys.Right : Keys.Left) : (d.Y > 0 ? Keys.Down : Keys.Up));
					}
					yield return null;
					down.Clear();
					yield return null;
				}
				Log($"  blocked {B.LabBlocks} spears, HP {hp} -> {P.statLife}");
				Check(B.LabBlocks >= 5, "the shield blocked almost nothing");
			}
			finally
			{
				BattleSystem.LabForcedAttack = null;
			}
			yield return EndForced();
		}

		private IEnumerable SoulPurple()
		{
			try
			{
				yield return ForcedTurn(() => new StringRunners((p, v) => Shots.Ball(p, v, Color.White), 40));
				Check(B.SoulMode == SoulMode.Purple, $"mode is {B.SoulMode}");
				float OnString(int i) => B.PurpleStringY(i) - BattleConstants.SoulSize / 2f;
				yield return Wait(20);
				Check(Math.Abs(B.LabSoul.Y - OnString(1)) < 1f, "the purple SOUL isn't on the middle string");
				yield return Press(Keys.Down);
				yield return Wait(12);
				Check(Math.Abs(B.LabSoul.Y - OnString(2)) < 1f, "Down didn't move it to the bottom string");
				yield return Press(Keys.Down);
				yield return Wait(12);
				Check(Math.Abs(B.LabSoul.Y - OnString(2)) < 1f, "it left the strings");
				yield return Press(Keys.Up);
				yield return Wait(4);
				yield return Press(Keys.Up);
				yield return Wait(12);
				Check(Math.Abs(B.LabSoul.Y - OnString(0)) < 1f, "Up didn't move it to the top string");
				float x0 = B.LabSoul.X;
				down.Add(Keys.Left);
				yield return Wait(10);
				down.Clear();
				Check(B.LabSoul.X < x0 - 5f, "it can't slide along the string");
			}
			finally
			{
				BattleSystem.LabForcedAttack = null;
			}
			yield return EndForced();
		}

		private IEnumerable SoulYellow()
		{
			try
			{
				yield return ForcedTurn(() => new Gunships((p, v) => Shots.Ball(p, v, Color.Gray, 1f, 2.4f), (p, v) => Shots.Ball(p, v, Color.Red), 40) { Toughness = 3 });
				Check(B.SoulMode == SoulMode.Yellow, $"mode is {B.SoulMode}");
				B.LabBroken = 0;
				int bigShots = 0, t = 0;
				// Line up with the nearest ship and tap Z; every so often hold for a big shot
				while (B.LabPhase == Phase.EnemyTurn)
				{
					t++;
					Bullet ship = B.Bullets.Where(b => b.Toughness > 0 && !b.Dead).OrderBy(b => b.Position.X).FirstOrDefault();
					down.Remove(Keys.Up);
					down.Remove(Keys.Down);
					if (ship != null)
					{
						float dy = ship.Position.Y - (B.LabSoul.Y + 10f);
						if (Math.Abs(dy) > 3f)
							down.Add(dy > 0 ? Keys.Down : Keys.Up);
					}
					bool charging = t % 160 >= 100;
					if (charging)
						down.Add(Keys.Z);
					else if (t % 160 == 0)
						bigShots++;
					else if (t % 8 == 0)
						down.Add(Keys.Z);
					else
						down.Remove(Keys.Z);
					yield return null;
				}
				down.Clear();
				Log($"  broke {B.LabBroken} bullets");
				Check(B.LabBroken >= 2, "the yellow SOUL's shots broke nothing");
			}
			finally
			{
				BattleSystem.LabForcedAttack = null;
			}
			yield return EndForced();
		}

		private IEnumerable Desperation()
		{
			yield return StartWith(NPCID.EyeofCthulhu);
			yield return Menu();
			Encounter eoc = B.LabTarget;
			foreach (NPC m in eoc.Members())
				m.life = (int)(m.lifeMax * 0.2f);
			yield return Choose(4);
			yield return Until(() => B.LabPhase == Phase.Message || B.LabPhase == Phase.EnemyIntro, "the turn after DEFEND", skipText: false);
			Check(B.LabText.Contains("everything"), $"no desperation line, got \"{B.LabText}\"");
			yield return Until(() => B.LabPhase == Phase.EnemyTurn, "the all-out turn");
			Check(eoc.DesperationUsed, "the all-out turn wasn't used");
			Log($"  all-out turn: {B.LabAttack?.GetType().Name}");
			int most = 0;
			while (B.LabPhase == Phase.EnemyTurn)
			{
				Heal();
				most = Math.Max(most, B.Bullets.Count);
				yield return null;
			}
			Check(most > 0, "the all-out turn spawned nothing");
			// It only happens once
			yield return Menu();
			yield return Choose(4);
			yield return Until(() => B.LabPhase is Phase.EnemyIntro or Phase.EnemyTurn or Phase.Message, "the next turn", skipText: false);
			Check(B.LabPhase != Phase.Message || !B.LabText.Contains("everything"), "it went all out twice");
			foreach (NPC m in eoc.Members().ToList())
				m.active = false;
			yield return Until(() => !BattleSystem.Active, "the battle ending", 60 * 30);
		}

		/// <summary>Each boss's -test kit: what it gives, how a typical fight with it adds up, and that the player's own gear comes back.</summary>
		private IEnumerable TestKits()
		{
			var kit = P.GetModPlayer<TestLoadoutPlayer>();
			int ownWeapon = P.inventory[0].type, ownMax = P.statLifeMax;
			var bosses = new (string, int)[]
			{
				("King Slime", NPCID.KingSlime), ("Eye of Cthulhu", NPCID.EyeofCthulhu), ("Eater of Worlds", NPCID.EaterofWorldsHead),
				("Brain of Cthulhu", NPCID.BrainofCthulhu), ("Queen Bee", NPCID.QueenBee), ("Skeletron", NPCID.SkeletronHead),
				("Deerclops", NPCID.Deerclops), ("Wall of Flesh", NPCID.WallofFlesh), ("Queen Slime", NPCID.QueenSlimeBoss),
				("The Twins", NPCID.Retinazer), ("The Destroyer", NPCID.TheDestroyer), ("Skeletron Prime", NPCID.SkeletronPrime),
				("Plantera", NPCID.Plantera), ("Golem", NPCID.Golem), ("Duke Fishron", NPCID.DukeFishron),
				("Empress of Light", NPCID.HallowBoss), ("Lunatic Cultist", NPCID.CultistBoss), ("Moon Lord", NPCID.MoonLordCore),
			};
			foreach (var (label, type) in bosses)
			{
				string given = kit.Apply(type);
				yield return Wait(2); // armour counts from the next update
				var sample = new NPC();
				sample.SetDefaults(type);
				var enc = EncounterRegistry.Create(sample);
				int weaponDamage = P.GetWeaponDamage(P.inventory[0]);
				// (The lab's player never runs Terraria's equipment update, so the armour's own defense is added up here)
				int armour = P.armor[0].defense + P.armor[1].defense + P.armor[2].defense;
				int raw = BattleSystem.BossBulletDamage(enc, 0.8f, armour);
				int taken = Math.Max(1, raw - armour / 2);
				float scale = BattleSystem.HitScale(enc);
				float turnsToWin = enc.LifeMax / Math.Max(1f, weaponDamage * 2f * scale);
				var (_, stageHp, stageDef) = BattleSystem.BossStage(type);
				Check(Math.Abs(armour - stageDef) <= 1, $"{label}'s kit armour gives {armour} defense, the balance assumes {stageDef}");
				Log($"  {label}: {given}; armour {armour} defense; ordinary bullet hits for {taken} ({stageHp / taken:0.0} hits to die); weapon {weaponDamage} a hit, ~{turnsToWin:0} turns to win with 2 good hits a turn");
				Check(P.statLifeMax > 100 || type == NPCID.KingSlime, $"{label}'s kit left max HP at {P.statLifeMax}");
				Check(P.inventory[0].type != ownWeapon, $"{label}'s kit didn't give a weapon");
			}
			kit.Restore();
			Check(P.inventory[0].type == ownWeapon && P.statLifeMax == ownMax, $"own gear not back: weapon {P.inventory[0].type}, max HP {P.statLifeMax}");

			// In a real battle: the kit goes on, and comes off when it ends
			kit.Apply(NPCID.EyeofCthulhu);
			yield return StartWith(NPCID.EyeofCthulhu);
			Check(kit.Testing && P.inventory[0].type == ItemID.PlatinumBroadsword, "the Eye's kit isn't on in the battle");
			yield return Menu();
			foreach (NPC m in B.LabTarget.Members().ToList())
				m.active = false;
			yield return WaitForEnd();
			Check(!kit.Testing && P.inventory[0].type == ownWeapon, "the kit stayed on after the battle");
			Log($"  {BattleSystem.LastTestReport}");
			Check(BattleSystem.LastTestReport?.Contains("turn") == true, "no turn and time report after the test run");
			P.statLife = 500;
		}

		/// <summary>Boss hits scale to a fair number of turns; potions bring on potion sickness; slime bullets keep their colour.</summary>
		private IEnumerable Balance()
		{
			// Sickness from outside doesn't follow you into the battle
			P.AddBuff(BuffID.PotionSickness, 3600);
			yield return StartWith(NPCID.EyeofCthulhu);
			Check(!P.HasBuff(BuffID.PotionSickness), "potion sickness from outside carried into the battle");
			yield return Menu();
			Encounter eye = B.LabTarget;
			float scale = BattleSystem.HitScale(eye);
			float par = BattleSystem.BossStage(NPCID.EyeofCthulhu).turnDamage;
			float turnsPar = eye.LifeMax / (par * scale);
			Log($"  Eye of Cthulhu: {eye.LifeMax} HP, hits x{scale:0.0}; the kit weapon's turn ({par:0}) wins in {turnsPar:0.0} turns");
			Check(turnsPar > 14f && turnsPar < 18f, $"the kit weapon takes {turnsPar:0.0} turns");

			// Boss bullets against a typical player for the stage: an ordinary one takes about a tenth of the bar
			foreach (var (type, label) in new[] { (NPCID.KingSlime, "King Slime"), (NPCID.EyeofCthulhu, "Eye of Cthulhu"), (NPCID.Plantera, "Plantera") })
			{
				var sample = new NPC();
				sample.SetDefaults(type);
				var enc = EncounterRegistry.Create(sample);
				var (_, hp, def) = BattleSystem.BossStage(type);
				int raw = BattleSystem.BossBulletDamage(enc, 0.8f, (int)def);
				float taken = raw - def * 0.5f;
				// Armour counts: the stage's armour takes the standard hit, no armour takes more, twice the armour less
				float bare = BattleSystem.BossBulletDamage(enc, 0.8f, 0), heavy = BattleSystem.BossBulletDamage(enc, 0.8f, (int)def * 2) - def;
				Log($"  {label}: ordinary bullet {taken:0} to a typical {hp:0} HP / {def:0} defense player ({taken / hp:P0}); no armour {bare:0}, double armour {heavy:0}");
				Check(taken / hp > 0.17f && taken / hp < 0.23f, $"{label}'s bullets take {taken / hp:P0} of a typical health bar");
				Check(bare > taken * 1.1f && heavy < taken * 0.9f, $"{label}: armour barely matters (none {bare}, typical {taken}, double {heavy})");
			}

			// A slime bullet is drawn blue, not grey
			Bullet slime = Shots.Npc(NPCID.BlueSlime, Vector2.Zero, Vector2.Zero, 1f, 1f, new Vector2(8, 8));
			Log($"  blue slime bullet colour {slime.Color}");
			Check(slime.Color.B > slime.Color.R + 60, $"slime bullet is {slime.Color}, not blue");

			// A potion, then ITEM is locked for a few turns
			P.inventory[1].SetDefaults(ItemID.LesserHealingPotion);
			P.inventory[1].stack = 5;
			P.statLife = 200;
			yield return Choose(2);
			yield return Until(() => B.LabPhase == Phase.ItemSelect, "the item list", skipText: false);
			yield return Press(Keys.Z);
			yield return Until(() => B.LabPhase is Phase.EnemyTalk or Phase.EnemyIntro or Phase.EnemyTurn, "the enemy turn after healing");
			yield return Menu();
			Log($"  potion sickness: {B.LabPotionSick} turns left");
			Check(B.LabPotionSick > 0, "using a potion didn't bring on potion sickness");
			Check(P.HasBuff(BuffID.PotionSickness), "potion sickness isn't in the effects list");
			// ITEM still opens; the potion in it can't be used while sick
			yield return Choose(2);
			yield return Until(() => B.LabPhase == Phase.ItemSelect, "the item list while sick", skipText: false);
			yield return Press(Keys.Z);
			yield return Wait(10);
			Check(B.LabPhase == Phase.ItemSelect, $"a potion was used while sick (phase {B.LabPhase})");
			yield return Press(Keys.X);
			yield return Wait(5);
			P.statLife = 500;
			foreach (NPC m in eye.Members().ToList())
				m.active = false;
			yield return WaitForEnd();
		}

		/// <summary>Losing the battle (the SOUL breaks) despawns its enemies: no kill, no loot, no downed flag.</summary>
		private IEnumerable LoseDespawns()
		{
			bool downedBefore = NPC.downedQueenBee;
			yield return StartWith(NPCID.QueenBee);
			yield return Menu();
			NPC bee = B.LabTarget.Npc;
			int items = Main.item.Count(i => i.active);
			B.RequestSoulDeath(Terraria.DataStructures.PlayerDeathReason.ByCustomReason(Terraria.Localization.NetworkText.FromLiteral("lab")), 9999);
			yield return Until(() => !BattleSystem.Active, "the battle ending after the SOUL broke", 60 * 10, skipText: false);
			Log($"  after losing: Queen Bee active {bee.active}, life {bee.life}, downed {NPC.downedQueenBee}, items {items} -> {Main.item.Count(i => i.active)}");
			Check(!bee.active, "the Queen Bee is still there after the player lost");
			Check(NPC.downedQueenBee == downedBefore, "losing counted as defeating the Queen Bee");
			Check(Main.item.Count(i => i.active) <= items, "losing dropped loot");
			// Back on our feet for the next scenario
			P.respawnTimer = 0;
			if (P.dead)
				P.Spawn(PlayerSpawnContext.ReviveFromDeath);
			P.statLife = 500;
			yield return Wait(30);
		}

		/// <summary>A twin below 40% spends a turn changing form (laser cannon / mouth), once; the other keeps its eye.</summary>
		private IEnumerable TwinsChange()
		{
			yield return StartWith(NPCID.Retinazer, NPCID.Spazmatism);
			yield return Menu();
			var twins = (Twins)B.LabTarget;
			NPC ret = twins.Members().First(n => n.type == NPCID.Retinazer);
			ret.life = (int)(ret.lifeMax * 0.35f);
			yield return Choose(4);
			yield return Until(() => B.LabPhase is Phase.EnemyTurn, "the enemy turn");
			string name = B.LabAttack?.GetType().Name;
			Log($"  Retinazer at 35%: {name}");
			Check(name == "ChangeForm", $"Retinazer attacked with {name} instead of changing form");
			yield return Menu();
			Check(twins.Changed(NPCID.Retinazer), "Retinazer didn't change form");
			Check(!twins.Changed(NPCID.Spazmatism), "Spazmatism changed form above 40%");
			yield return Choose(4);
			yield return Until(() => B.LabPhase is Phase.EnemyTurn, "the next enemy turn");
			Check(B.LabAttack?.GetType().Name != "ChangeForm", "Retinazer changed form twice");
			foreach (NPC m in twins.Members().ToList())
				m.active = false;
			yield return WaitForEnd();
		}

		/// <summary>The Twins as the Mechanical Eye summons them: both in the fight, both listed, drawn apart.</summary>
		private IEnumerable TwinsSummoned()
		{
			Main.dayTime = false;
			Main.time = 0;
			NPC.SpawnOnPlayer(P.whoAmI, NPCID.Retinazer);
			NPC.SpawnOnPlayer(P.whoAmI, NPCID.Spazmatism);
			yield return Wait(30);
			foreach (NPC n in Main.npc.Where(n => n.active && n.type is NPCID.Retinazer or NPCID.Spazmatism))
				Log($"  world: {n.FullName} #{n.whoAmI} life {n.life}/{n.lifeMax} realLife {n.realLife} at {n.Center - P.Center}");
			NPC ret = Main.npc.First(n => n.active && n.type == NPCID.Retinazer);
			BattleSystem.QueueStart(ret, 1, "touch");
			yield return Until(() => BattleSystem.Active, "the battle starting", 60 * 5);
			yield return Menu();
			foreach (NPC n in B.LabTarget.Members())
				Log($"  member: {n.FullName} #{n.whoAmI} life {n.life}/{n.lifeMax} active {n.active}");
			var rows = B.LabTarget.TargetParts();
			Log($"  target rows: {string.Join(", ", rows.Select(B.LabTarget.PartName))}");
			Check(rows.Count == 2, $"{rows.Count} twin(s) listed");
			foreach (NPC m in B.LabTarget.Members().ToList())
				m.active = false;
			yield return WaitForEnd();
		}

		/// <summary>Every living enemy says something before the box opens, the ones sitting the turn out and bosses too.</summary>
		private IEnumerable EveryoneTalks()
		{
			foreach (int[] group in new[] { new int[] { NPCID.BlueSlime, NPCID.BlueSlime, NPCID.BlueSlime }, new int[] { NPCID.EyeofCthulhu } })
			{
				yield return StartWith(group);
				yield return Menu();
				yield return Choose(4);
				yield return Until(() => B.LabPhase is Phase.EnemyTalk or Phase.EnemyIntro or Phase.EnemyTurn, "the enemy turn");
				List<string> said = B.LabBubbles;
				Log($"  {B.LabTarget.Name} x{said.Count}: {string.Join(" | ", said)}");
				Check(said.All(b => !string.IsNullOrEmpty(b)), $"{said.Count(b => string.IsNullOrEmpty(b))} of {said.Count} said nothing");
				Check(B.LabPhase == Phase.EnemyTalk, $"no talking before the box ({B.LabPhase})");
				foreach (NPC m in B.LabEnemies.SelectMany(e => e.E.Members()).ToList())
					m.active = false;
				yield return WaitForEnd();
			}
		}

		/// <summary>Hitting an enemy to start the battle lands a FIGHT hit before the panel comes up.</summary>
		private IEnumerable FirstStrike()
		{
			int idx = NPC.NewNPC(P.GetSource_FromThis(), (int)P.Center.X + 160, (int)P.Center.Y - 40, NPCID.Zombie);
			NPC z = Main.npc[idx];
			z.lifeMax = z.life = 500;
			BattleSystem.QueueStart(z, 20, "hit by lab");
			yield return Until(() => BattleSystem.Active, "the battle starting", 60 * 5);
			int before = z.life;
			bool sawLine = false;
			while (B.LabPhase == Phase.Intro)
			{
				sawLine |= B.LabFirstStrike;
				yield return null;
			}
			Log($"  first strike: {before} -> {z.life} HP, then {B.LabPhase}");
			Check(sawLine, "the battle didn't open with a first strike");
			Check(z.life < before, "the first strike did no damage");
			yield return Menu();
			foreach (NPC m in B.LabTarget.Members().ToList())
				m.active = false;
			yield return WaitForEnd();

			// A battle started any other way opens as usual
			yield return StartWith(NPCID.Zombie);
			Check(!B.LabFirstStrike, "a battle not started by a hit opened with a strike");
			yield return Menu();
			foreach (NPC m in B.LabTarget.Members().ToList())
				m.active = false;
			yield return WaitForEnd();
		}

		/// <summary>Below half HP the Eye spends a turn tearing open, then shows its mouth.</summary>
		private IEnumerable EyeRips()
		{
			yield return StartWith(NPCID.EyeofCthulhu);
			yield return Menu();
			Check(B.LabTarget.FrameOverride(0, 6) is < 3, "the Eye shows its mouth before tearing open");
			NPC eye = B.LabTarget.Npc;
			eye.life = (int)(eye.lifeMax * 0.45f);
			yield return Choose(4);
			yield return Until(() => B.LabPhase is Phase.EnemyTurn, "the enemy turn");
			string name = B.LabAttack?.GetType().Name;
			Log($"  turn at 45% HP: {name}");
			Check(name == "RipOpen", $"the Eye attacked with {name} instead of tearing open");
			Check(B.Bullets.All(b => !b.Harmful), "tearing open hurt the SOUL");
			yield return Menu();
			int? shown = B.LabTarget.FrameOverride(0, 6);
			Log($"  frame afterwards: {shown}");
			Check(shown is >= 3, "the Eye didn't switch to its mouth frames");
			yield return Choose(4);
			yield return Until(() => B.LabPhase is Phase.EnemyTurn, "the next enemy turn");
			Check(B.LabAttack?.GetType().Name != "RipOpen", "the Eye tore open twice");
			foreach (NPC m in B.LabTarget.Members().ToList())
				m.active = false;
			yield return WaitForEnd();
		}

		/// <summary>Frozen enemies keep animating: a live frozen flying fish, then every hostile type simulated.</summary>
		private IEnumerable Frames()
		{
			yield return StartWith(NPCID.FlyingFish);
			NPC fish = B.LabTarget.Npc;
			var seen = new HashSet<int>();
			for (int i = 0; i < 120; i++)
			{
				seen.Add(fish.frame.Y);
				yield return null;
			}
			Log($"  frozen flying fish: {seen.Count} frames ({string.Join(" ", seen)})");
			Check(seen.Count > 1, "the frozen flying fish stays on one frame");
			var still = new List<string>();
			for (int type = 1; type < NPCID.Count; type++)
			{
				var n = new NPC();
				try
				{
					n.SetDefaults(type);
					if (n.friendly || n.townNPC || n.boss || n.damage <= 0 || Main.npcFrameCount[type] < 2 || n.lifeMax <= 5)
						continue;
					n.whoAmI = 199;
					n.position = new Vector2(Main.spawnTileX * 16f, (Main.spawnTileY - 10) * 16f);
					n.direction = n.spriteDirection = -1;
					var frames = new HashSet<int>();
					for (int t = 0; t < 120; t++)
					{
						Frozen.Freeze(n);
						BattleFreezeNPC.AnimateInPlace(n, n.FindFrame);
						frames.Add(n.frame.Y);
					}
					if (frames.Count < 2)
						still.Add($"{type}:{Lang.GetNPCNameValue(type)}");
				}
				catch (Exception e)
				{
					still.Add($"{type}:(error {e.GetType().Name})");
				}
			}
			// How often a few common ones change frame, frozen (ticks per change)
			foreach (int type in new[] { NPCID.BlueSlime, NPCID.Zombie, NPCID.DemonEye, NPCID.FlyingFish, NPCID.Skeleton })
			{
				var n = new NPC();
				n.SetDefaults(type);
				n.whoAmI = 199;
				n.direction = n.spriteDirection = -1;
				int changes = 0, last = n.frame.Y;
				for (int t = 0; t < 240; t++)
				{
					Frozen.Freeze(n);
					BattleFreezeNPC.AnimateInPlace(n, n.FindFrame);
					if (n.frame.Y != last)
						changes++;
					last = n.frame.Y;
				}
				Log($"  {Lang.GetNPCNameValue(type)}: a frame change every {(changes > 0 ? 240f / changes : 0):0.0} ticks");
			}
			Log($"  {still.Count} hostile types stand still when frozen:");
			foreach (var chunk in still.Chunk(8))
				Log("    " + string.Join(", ", chunk));
			// Casters, closed mimics, turrets and boss parts really do hold one frame; 256 did before battle enemies
			// pretended to move while picking frames
			Check(still.Count < 60, $"{still.Count} enemy types stopped animating when frozen");
			foreach (NPC m in B.LabTarget.Members().ToList())
				m.active = false;
			yield return WaitForEnd();
		}

		/// <summary>What the battle freeze leaves an NPC with right before Terraria picks its frame.</summary>
		private static class Frozen
		{
			public static void Freeze(NPC n)
			{
				n.velocity = Vector2.Zero;
				if (!n.noGravity)
					n.velocity.Y += 0.3f;
			}
		}

		private IEnumerable SingleEnemy()
		{
			yield return StartWith(NPCID.DemonEye);
			Check(B.LabEnemies.Count == 1 && B.LabTarget.Slot == null, "a lone enemy should use its own spot");
			yield return Menu();
			yield return FightAndKill(0);
			yield return Until(() => B.LabPhase == Phase.Message, "the win message", skipText: false);
			Check(B.LabText.Contains("YOU WON"), $"expected the win text, got \"{B.LabText}\"");
			yield return WaitForEnd();
		}
	}
}
