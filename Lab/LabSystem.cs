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
		private const int ScenarioTimeout = 60 * 240;

		private static BattleSystem B => BattleSystem.Instance;
		private static Player P => Main.player[0];

		public override void Load()
		{
			if (!Enabled)
				return;
			Main.OnTickForThirdPartySoftwareOnly += ServerTick;
			// The server drops every player without a network client each tick; the lab's player has none
			On_Netplay.UpdateConnectedClients += _ => { };
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
				("boss-kill", () => BossKill(NPCID.EyeofCthulhu)),
				("boss-kill-king-slime", () => BossKill(NPCID.KingSlime)),
				("boss-spare", BossSpare),
				("no-world-hits", NoWorldHits),
				("multi-hit-spills-over", MultiHitSpillsOver),
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
			// Last one first, to check the cursor wraps and the target follows it
			yield return FightAndKill(2);
			Check(Living().Count == 2, $"expected 2 left, {Living().Count} living");
			yield return Until(() => B.LabPhase != Phase.FightResult, "the end of the FIGHT", skipText: false);
			Check(B.LabPhase is Phase.EnemyIntro or Phase.EnemyTurn, $"expected the enemy turn right after the kill, got {B.LabPhase}");
			yield return WatchEnemyTurn(2);
			yield return Menu();
			yield return FightAndKill(0);
			Check(Living().Count == 1, "expected 1 left");
			yield return WatchEnemyTurn(1);
			yield return Menu();
			yield return FightAndKill(0);
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
			Check(B.LabPhase is Phase.EnemyIntro or Phase.EnemyTurn, $"expected the enemy turn right after the kill, got {B.LabPhase}");
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
			// Mixed ending: defeat one, spare the other
			yield return FightAndKill(0);
			yield return Menu();
			Living()[0].Mercy = 100f;
			yield return Spare(0);
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

		private IEnumerable BossSpare()
		{
			yield return StartWith(NPCID.EyeofCthulhu);
			yield return Menu();
			B.LabTarget.Mercy = 100f;
			NPC.downedBoss1 = false;
			bool before = NPC.downedBoss1;
			yield return Spare(0);
			yield return WaitForEnd();
			Check(NPC.downedBoss1, "sparing the Eye didn't count it as beaten");
			Check(!Main.npc.Any(n => n.active && n.type == NPCID.EyeofCthulhu), "the Eye is still around after the spare");
			Log($"  downedBoss1 {before} -> {NPC.downedBoss1}");
		}

		/// <summary>A swing or shot from before the battle (or still in the world) never hurts the enemy during it.</summary>
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
