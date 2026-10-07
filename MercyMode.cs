using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework.Input;
using Terraria.GameInput;
using Terraria;
using Terraria.Chat;
using Terraria.Localization;
using Terraria.ID;
using Terraria.ModLoader;

namespace MercyMode
{
	public class MercyMode : Mod
	{
		public static ModKeybind JoinBattleKey;
		/// <summary>Shows or hides the buff list in the battle's panel.</summary>
		public static ModKeybind BuffsKey;
		/// <summary>Battle keys by the Deltarune key they stand for (Z confirm, X back/slow, arrows).</summary>
		public static Dictionary<Keys, ModKeybind> BattleKeys;

		/// <summary>
		/// The keys bound to a keybind, from both the gameplay and the inventory/UI controls (Terraria keeps them apart,
		/// and a key bound on one side only would otherwise work only half the time).
		/// </summary>
		public static List<string> AssignedKeys(ModKeybind bind)
		{
			var keys = new List<string>();
			if (bind == null || Main.dedServ)
				return keys;
			foreach (InputMode mode in new[] { InputMode.Keyboard, InputMode.KeyboardUI })
				foreach (string k in bind.GetAssignedKeys(mode))
					if (!keys.Contains(k))
						keys.Add(k);
			return keys;
		}

		/// <summary>
		/// The real keys for one of the battle's Deltarune keys, as bound in Controls. Unbound (or the headless lab,
		/// which presses the Deltarune keys itself): the key as written.
		/// </summary>
		public static IEnumerable<Keys> BoundKeys(Keys deltaruneKey)
		{
			if (BattleKeys == null || !BattleKeys.TryGetValue(deltaruneKey, out ModKeybind bind) || Main.dedServ)
				return new[] { deltaruneKey };
			var list = new List<Keys>();
			foreach (string k in AssignedKeys(bind))
				if (Enum.TryParse(k, out Keys key))
					list.Add(key);
			// Only mouse buttons bound: no key at all (not the default letter, which would still work)
			if (list.Count == 0 && BoundMouse(deltaruneKey).Count > 0)
				return list;
			return list.Count > 0 ? list : new[] { deltaruneKey };
		}

		/// <summary>The mouse buttons (1 left, 2 right, 3 middle, 4-5 side) bound to one of the battle's keys.</summary>
		public static List<int> BoundMouse(Keys deltaruneKey)
		{
			var list = new List<int>();
			if (BattleKeys == null || !BattleKeys.TryGetValue(deltaruneKey, out ModKeybind bind) || Main.dedServ)
				return list;
			foreach (string k in AssignedKeys(bind))
				if (k.StartsWith("Mouse") && int.TryParse(k.Substring(5), out int n) && n >= 1 && n <= 5)
					list.Add(n);
			return list;
		}

		/// <summary>Whether a mouse button (1-5) is down now, or was last tick.</summary>
		public static bool MouseDown(int button, bool old = false)
		{
			var s = old ? Terraria.GameInput.PlayerInput.MouseInfoOld : Terraria.GameInput.PlayerInput.MouseInfo;
			var state = button switch
			{
				1 => s.LeftButton,
				2 => s.RightButton,
				3 => s.MiddleButton,
				4 => s.XButton1,
				_ => s.XButton2,
			};
			return state == Microsoft.Xna.Framework.Input.ButtonState.Pressed;
		}

		/// <summary>Every keybind of ours with its default key, so a blank one can be given it back.</summary>
		private static readonly List<(ModKeybind Bind, string Key)> keyDefaults = new();

		private ModKeybind Key(string name, string key)
		{
			ModKeybind bind = KeybindLoader.RegisterKeybind(this, name, key);
			keyDefaults.Add((bind, key));
			return bind;
		}

		/// <summary>
		/// Gives any of our keybinds that has no key at all its default back. tModLoader only applies a default the first
		/// time it sees a keybind; after that the saved (possibly empty) binding wins, and an empty Join Battle key or
		/// battle key leaves the mod unusable. Runs as the game finishes loading and on entering a world.
		/// </summary>
		public static void RestoreBlankKeybinds()
		{
			if (Main.dedServ)
				return;
			bool changed = false;
			foreach (var (bind, key) in keyDefaults)
			{
				try
				{
					List<string> keys = bind.GetAssignedKeys(Terraria.GameInput.InputMode.Keyboard);
					if (keys != null && keys.Count == 0)
					{
						keys.Add(key);
						changed = true;
					}
				}
				catch (KeyNotFoundException)
				{
					// Not in the profile yet: tModLoader will use the default itself
				}
			}
			if (changed)
			{
				try
				{
					Terraria.GameInput.PlayerInput.Save();
				}
				catch (System.Exception e)
				{
					ModContent.GetInstance<MercyMode>().Logger.Warn("Couldn't save the restored keybinds: " + e.Message);
				}
			}
		}

		public override void Load()
		{
			keyDefaults.Clear();
			JoinBattleKey = Key("JoinBattle", "J");
			BuffsKey = Key("BattleBuffs", "B");
			// The battle screen's keys, rebindable in Settings > Controls
			BattleKeys = new Dictionary<Keys, ModKeybind>
			{
				[Keys.Z] = Key("BattleConfirm", "Z"),
				[Keys.X] = Key("BattleBack", "X"),
				[Keys.Up] = Key("BattleUp", "Up"),
				[Keys.Down] = Key("BattleDown", "Down"),
				[Keys.Left] = Key("BattleLeft", "Left"),
				[Keys.Right] = Key("BattleRight", "Right"),
			};
		}

		public override void Unload()
		{
			JoinBattleKey = null;
			BuffsKey = null;
			BattleKeys = null;
			keyDefaults.Clear();
		}

		// Chat colors, Deltarune-ish
		public static readonly Color TextWhite = new Color(255, 255, 255);
		public static readonly Color MercyYellow = new Color(255, 255, 64);
		public static readonly Color TPOrange = new Color(255, 160, 64);
		public static readonly Color Gray = new Color(160, 160, 160);

		public static void Say(string text, Color color)
		{
			// A multiplayer server (a spare goes through it) tells everyone in chat
			if (Battle.Net.BattleNet.IsServer)
				ChatHelper.BroadcastChatMessage(NetworkText.FromLiteral(text), color);
			else
				Main.NewText(text, color);
		}

		/// <summary>
		/// A malformed packet (a bad index from a modified client or server) is logged and dropped, so another player
		/// can't crash this game by sending one.
		/// </summary>
		public override void HandlePacket(BinaryReader reader, int whoAmI)
		{
			try
			{
				Battle.Net.BattleNet.Handle(reader, whoAmI);
			}
			catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException or EndOfStreamException
				or InvalidDataException or KeyNotFoundException or NullReferenceException)
			{
				Logger.Warn($"Dropped a malformed packet from {(Main.netMode == NetmodeID.Server ? "player " + whoAmI : "the server")}: {e.GetType().Name}");
			}
		}

		/// <summary>Worm segments and similar point at a "head" through realLife. Mercy lives on the head.</summary>
		public static NPC Root(NPC npc)
		{
			if (npc.realLife >= 0 && npc.realLife < Main.maxNPCs && Main.npc[npc.realLife].active)
				return Main.npc[npc.realLife];
			return npc;
		}

		public static bool AnyBossAlive()
		{
			foreach (NPC npc in Main.ActiveNPCs)
				if (npc.boss)
					return true;
			return false;
		}

		public static bool IsSingleplayer => Main.netMode == NetmodeID.SinglePlayer || Lab.LabSystem.Enabled;
	}

	/// <summary>Puts back any battle key that ended up with no key at all (see <see cref="MercyMode.RestoreBlankKeybinds"/>).</summary>
	public class KeybindRestore : ModSystem
	{
		public override void PostSetupContent() => MercyMode.RestoreBlankKeybinds();
		public override void OnWorldLoad() => MercyMode.RestoreBlankKeybinds();
	}
}
