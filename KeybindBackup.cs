using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Terraria;
using Terraria.GameInput;
using Terraria.ModLoader;

namespace MercyMode
{
	/// <summary>
	/// Keeps Mercy Mode's key bindings through updates. tModLoader remembers them by name (never rename a keybind:
	/// that alone loses it); this also keeps a copy next to the saves and, the first time a new version runs, puts
	/// back any binding that came back as its default while the copy says the player had changed it.
	/// </summary>
	public class KeybindBackup : ModSystem
	{
		private sealed class Backup
		{
			public string Version = "";
			public Dictionary<string, Dictionary<string, List<string>>> Binds = new();
		}

		private static string FilePath => Path.Combine(Main.SavePath, "MercyMode", "keybinds.json");
		private static readonly InputMode[] Modes = { InputMode.Keyboard, InputMode.KeyboardUI };
		private static bool restored;
		private static string lastSaved;

		/// <summary>Every Mercy Mode keybind with its default key.</summary>
		private static IEnumerable<(ModKeybind bind, string def)> Binds()
		{
			if (MercyMode.JoinBattleKey != null)
				yield return (MercyMode.JoinBattleKey, "J");
			if (MercyMode.BuffsKey != null)
				yield return (MercyMode.BuffsKey, "B");
			if (MercyMode.BattleKeys != null)
				foreach (var (key, bind) in MercyMode.BattleKeys)
					yield return (bind, key.ToString());
		}

		private static readonly System.Reflection.PropertyInfo FullNameProp =
			typeof(ModKeybind).GetProperty("FullName", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

		/// <summary>The name tModLoader files the binding under ("MercyMode/BattleConfirm").</summary>
		private static string KeyOf(ModKeybind bind) => FullNameProp?.GetValue(bind) as string ?? bind.ToString();

		private static List<string> Assigned(ModKeybind bind, InputMode mode)
		{
			var status = PlayerInput.CurrentProfile?.InputModes[mode]?.KeyStatus;
			return status != null && status.TryGetValue(KeyOf(bind), out List<string> keys) ? keys : null;
		}

		private static string Version => ModContent.GetInstance<MercyMode>().Version.ToString();

		public override void PostUpdateEverything()
		{
			if (Main.dedServ)
				return;
			try
			{
				if (!restored)
				{
					restored = true;
					Restore();
				}
				// Now and then (and only when something changed), the copy is brought up to date
				if (Main.GameUpdateCount % 300 == 0)
					Save();
			}
			catch (Exception e)
			{
				Mod.Logger.Warn("Keybind backup: " + e.Message);
			}
		}

		public override void OnWorldUnload()
		{
			if (Main.dedServ)
				return;
			try
			{
				Save();
			}
			catch (Exception e)
			{
				Mod.Logger.Warn("Keybind backup: " + e.Message);
			}
		}

		private static void Save()
		{
			var backup = new Backup { Version = Version };
			foreach (var (bind, _) in Binds())
			{
				var modes = new Dictionary<string, List<string>>();
				foreach (InputMode mode in Modes)
					if (Assigned(bind, mode) is List<string> keys)
						modes[mode.ToString()] = keys.ToList();
				backup.Binds[KeyOf(bind)] = modes;
			}
			string json = JsonConvert.SerializeObject(backup, Formatting.Indented);
			if (json == lastSaved)
				return;
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
			File.WriteAllText(FilePath, json);
			lastSaved = json;
		}

		private static void Restore()
		{
			if (!File.Exists(FilePath))
				return;
			var backup = JsonConvert.DeserializeObject<Backup>(File.ReadAllText(FilePath));
			// Same version: whatever is set now is what the player chose (a reset to defaults included)
			if (backup == null || backup.Version == Version)
				return;
			bool changed = false;
			foreach (var (bind, def) in Binds())
			{
				if (!backup.Binds.TryGetValue(KeyOf(bind), out var saved))
					continue;
				foreach (InputMode mode in Modes)
				{
					var now = Assigned(bind, mode);
					if (!saved.TryGetValue(mode.ToString(), out List<string> was) || was == null)
						continue;
					bool nowDefault = now == null || now.Count == 0 || now.Count == 1 && now[0] == def;
					bool wasCustom = !(was.Count == 0 || was.Count == 1 && was[0] == def);
					if (!nowDefault || !wasCustom)
						continue;
					var status = PlayerInput.CurrentProfile.InputModes[mode].KeyStatus;
					status[KeyOf(bind)] = was.ToList();
					changed = true;
				}
			}
			if (changed)
			{
				PlayerInput.Save();
				ModContent.GetInstance<MercyMode>().Logger.Info("Put back Mercy Mode key bindings from before the update.");
			}
		}
	}
}
