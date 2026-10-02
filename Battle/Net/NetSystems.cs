using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.UI;

namespace MercyMode.Battle.Net
{
	/// <summary>Each player's colour: their battle UI on their own screen, and their box and SOUL on everyone else's.</summary>
	public static class PartyColors
	{
		/// <summary>The config's choices, in order (Automatic first).</summary>
		public static readonly Color[] Choices =
		{
			new(0, 255, 255), // Automatic placeholder (unused)
			new(0, 255, 255), // Cyan, Kris's
			new(255, 80, 220), // Magenta, Susie's
			new(80, 255, 80), // Green, Ralsei's
			new(255, 230, 40), // Yellow, Noelle's
			new(255, 150, 40), // Orange
			new(90, 130, 255), // Blue
			new(255, 255, 255), // White
		};

		/// <summary>Automatic: by the slot the player joined the server in (the first player is Kris cyan).</summary>
		private static readonly int[] AutoOrder = { 1, 2, 3, 4, 5, 6, 7 };

		public static Color Of(Player p)
		{
			int choice = p?.GetModPlayer<BattleNetPlayer>().ColorChoice ?? 0;
			if (choice <= 0 || choice >= Choices.Length)
				choice = AutoOrder[(p?.whoAmI ?? 0) % AutoOrder.Length];
			return Choices[choice];
		}
	}

	public class BattleNetPlayer : ModPlayer
	{
		/// <summary>The config's <see cref="PartyColorChoice"/> (0 = automatic). Synced to everyone.</summary>
		public byte ColorChoice;

		public override void PreUpdate()
		{
			if (Player.whoAmI == Main.myPlayer && !Main.dedServ)
				ColorChoice = (byte)(ModContent.GetInstance<MercyConfig>()?.PartyColor ?? PartyColorChoice.Automatic);
		}

		public override void SyncPlayer(int toWho, int fromWho, bool newPlayer) => BattleNet.SendColor(Player.whoAmI, ColorChoice, toWho, fromWho);

		public override void CopyClientState(ModPlayer targetCopy) => ((BattleNetPlayer)targetCopy).ColorChoice = ColorChoice;

		public override void SendClientChanges(ModPlayer clientPlayer)
		{
			if (((BattleNetPlayer)clientPlayer).ColorChoice != ColorChoice)
				SyncPlayer(-1, Main.myPlayer, false);
		}

		public override void PlayerDisconnect()
		{
			if (Main.netMode == NetmodeID.Server)
				BattleNet.ServerDisconnect(Player.whoAmI);
		}
	}

	/// <summary>
	/// What a battle looks like from outside: the fighters swing at the frozen enemies when their FIGHT lands, and the
	/// enemies' attacks fly at them as harmless sparks during the bullet box. None of it can hurt anyone.
	/// </summary>
	public static class WorldVisuals
	{
		public static void Swing(Player p, NPC target)
		{
			if (Main.dedServ || !p.active || p.whoAmI == Main.myPlayer)
				return;
			if (target.active)
				p.direction = target.Center.X >= p.Center.X ? 1 : -1;
			Item held = p.HeldItem;
			if (held != null && !held.IsAir && held.damage > 0)
			{
				// Only this client's picture of them: a remote player's swing never hits anything here
				p.itemAnimationMax = p.itemAnimation = Math.Max(12, held.useAnimation);
				p.itemTime = p.itemAnimation;
			}
			if (target.active)
				for (int i = 0; i < 8; i++)
				{
					Dust d = Dust.NewDustDirect(target.position, target.width, target.height, DustID.RainbowMk2, 0f, 0f, 0, PartyColors.Of(p), 1.3f);
					d.noGravity = true;
					d.velocity *= 2f;
				}
		}

		/// <summary>The bullet box seen from outside: sparks from the enemies at the fighters.</summary>
		public static void Update()
		{
			foreach (var (id, wb) in BattleNet.WorldBattles)
			{
				if (id == BattleNet.MyBattle || wb.Stage != BattleNet.Stage.EnemyTurn || Main.GameUpdateCount - wb.StageTick > 60 * 15)
					continue;
				if ((Main.GameUpdateCount + (uint)id) % 7 != 0)
					continue;
				var npcs = BattleNet.NpcsOf(id).ToList();
				var fighters = wb.Players.Where(i => i >= 0 && i < Main.maxPlayers && Main.player[i].active && !Main.player[i].dead).Select(i => Main.player[i]).ToList();
				if (npcs.Count == 0 || fighters.Count == 0)
					continue;
				NPC from = Main.rand.Next(npcs);
				Player at = Main.rand.Next(fighters);
				if (from.DistanceSQ(Main.LocalPlayer.Center) > 2000f * 2000f)
					continue;
				Vector2 dir = Vector2.Normalize(at.Center - from.Center + Main.rand.NextVector2Circular(30f, 30f));
				for (int i = 0; i < 3; i++)
				{
					Dust d = Dust.NewDustPerfect(from.Center, DustID.RainbowMk2, dir * (7f + i), 0, Color.White, 1.4f - i * 0.2f);
					d.noGravity = true;
					d.fadeIn = 0.4f;
				}
			}
		}
	}

	/// <summary>Server bookkeeping each tick; on clients, the join prompt and the outside view of battles.</summary>
	public class BattleNetSystem : ModSystem
	{
		/// <summary>The battle the join prompt is for, and an enemy of it (-1: no prompt).</summary>
		private static int promptBattle = -1;
		private static NPC promptNpc;

		public override void PostUpdateEverything()
		{
			if (BattleNet.IsServer)
				BattleNet.ServerUpdate();
			if (!BattleNet.Online)
				return;
			WorldVisuals.Update();
			UpdatePrompt();
		}

		private static void UpdatePrompt()
		{
			promptBattle = -1;
			promptNpc = null;
			Player me = Main.LocalPlayer;
			if (BattleSystem.Active || me.dead || BattleNet.RequestPending)
				return;
			float best = BattleNet.PromptRange * BattleNet.PromptRange;
			foreach (var (id, wb) in BattleNet.WorldBattles)
			{
				if (wb.Players.Count >= BattleNet.MaxParty || wb.Players.Contains(me.whoAmI))
					continue;
				foreach (NPC n in BattleNet.NpcsOf(id))
				{
					float d = n.DistanceSQ(me.Center);
					if (d < best)
					{
						best = d;
						promptBattle = id;
						promptNpc = n;
					}
				}
			}
			if (promptNpc != null && JoinPressed())
				TryJoin();
		}

		/// <summary>Joins the battle the prompt is for (join key or /mmbattle join). False if there's none in reach.</summary>
		public static bool TryJoin()
		{
			if (promptNpc == null || !promptNpc.active)
				return false;
			if (ModContent.GetInstance<MercyConfig>()?.TurnBasedBattles == false)
			{
				MercyMode.Say("* Turn-based battles are off in the Mercy Mode config.", MercyMode.Gray);
				return true;
			}
			MercyMode.Say("* Joining the battle...", MercyMode.MercyYellow);
			BattleNet.RequestJoin(promptNpc);
			return true;
		}

		/// <summary>
		/// The keys bound to Join Battle: Terraria keeps separate bindings for gameplay and for the inventory/UI, and a
		/// key bound only on the UI side would otherwise work only with the inventory open.
		/// </summary>
		private static List<string> JoinKeys()
		{
			var keys = new List<string>();
			if (MercyMode.JoinBattleKey == null)
				return keys;
			foreach (InputMode mode in new[] { InputMode.Keyboard, InputMode.KeyboardUI })
				foreach (string k in MercyMode.JoinBattleKey.GetAssignedKeys(mode))
					if (!keys.Contains(k))
						keys.Add(k);
			return keys;
		}

		private static bool JoinPressed()
		{
			if (Main.drawingPlayerChat || Main.editSign || Main.editChest || Main.blockInput)
				return false;
			if (MercyMode.JoinBattleKey?.JustPressed == true)
				return true;
			foreach (string k in JoinKeys())
				if (Enum.TryParse(k, out Keys key) && Main.keyState.IsKeyDown(key) && !Main.oldKeyState.IsKeyDown(key))
					return true;
			return false;
		}

		public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
		{
			int at = layers.FindIndex(l => l.Name == "Vanilla: Mouse Text");
			if (at < 0)
				return;
			layers.Insert(at, new LegacyGameInterfaceLayer("MercyMode: Join Battle", () =>
			{
				if (BattleNet.Online && !BattleSystem.Active)
					DrawOutside(Main.spriteBatch);
				return true;
			}, InterfaceScaleType.UI));
		}

		private static void DrawOutside(SpriteBatch sb)
		{
			// Fighters are marked in the world, in their colour
			foreach (var (_, wb) in BattleNet.WorldBattles)
				foreach (int i in wb.Players)
				{
					if (i < 0 || i >= Main.maxPlayers || !Main.player[i].active || i == Main.myPlayer)
						continue;
					Player p = Main.player[i];
					Vector2 screen = (p.Top - Main.screenPosition) * Main.GameViewMatrix.Zoom / Main.UIScale + new Vector2(0f, -38f);
					Utils.DrawBorderString(sb, "IN BATTLE", screen, PartyColors.Of(p), 0.7f, 0.5f, 1f);
				}

			if (promptNpc == null)
				return;
			string key = JoinKeys().FirstOrDefault();
			string line = key != null ? $"Press {key} to join the battle!" : "Type /mmbattle join (or bind \"Join Battle\" in Controls) to join the battle!";
			var pos = new Vector2(Main.screenWidth / 2f / Main.UIScale, Main.screenHeight * 0.72f / Main.UIScale);
			float pulse = 0.85f + 0.15f * (float)Math.Sin(Main.GameUpdateCount / 10f);
			Utils.DrawBorderString(sb, line, pos, MercyMode.MercyYellow * pulse, 1.1f, 0.5f, 0.5f);
		}

		public override void OnWorldLoad() => BattleNet.Reset();

		public override void OnWorldUnload() => BattleNet.Reset();
	}
}
