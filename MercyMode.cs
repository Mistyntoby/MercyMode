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
			return list.Count > 0 ? list : new[] { deltaruneKey };
		}

		public override void Load()
		{
			JoinBattleKey = KeybindLoader.RegisterKeybind(this, "JoinBattle", "J");
			// The battle screen's keys, rebindable in Settings > Controls
			BattleKeys = new Dictionary<Keys, ModKeybind>
			{
				[Keys.Z] = KeybindLoader.RegisterKeybind(this, "BattleConfirm", "Z"),
				[Keys.X] = KeybindLoader.RegisterKeybind(this, "BattleBack", "X"),
				[Keys.Up] = KeybindLoader.RegisterKeybind(this, "BattleUp", "Up"),
				[Keys.Down] = KeybindLoader.RegisterKeybind(this, "BattleDown", "Down"),
				[Keys.Left] = KeybindLoader.RegisterKeybind(this, "BattleLeft", "Left"),
				[Keys.Right] = KeybindLoader.RegisterKeybind(this, "BattleRight", "Right"),
			};
		}

		public override void Unload()
		{
			JoinBattleKey = null;
			BattleKeys = null;
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

		public override void HandlePacket(BinaryReader reader, int whoAmI) => Battle.Net.BattleNet.Handle(reader, whoAmI);

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
}
