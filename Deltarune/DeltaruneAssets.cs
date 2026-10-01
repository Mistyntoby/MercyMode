using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.ModLoader;

namespace MercyMode.Deltarune
{
	/// <summary>A sprite from data.win turned into textures. Origin is in sprite pixels, like GameMaker's.</summary>
	public sealed class DrSprite
	{
		public Texture2D[] Frames;
		public Vector2 Origin;
		public int Width => Frames[0].Width;
		public int Height => Frames[0].Height;
		public Texture2D Frame(int i) => Frames[((i % Frames.Length) + Frames.Length) % Frames.Length];
	}

	public sealed class DrFont
	{
		public Texture2D Texture;
		public Dictionary<char, RawGlyph> Glyphs;
		public int LineHeight;
	}

	/// <summary>
	/// Pulls real Deltarune assets out of the player's own install at runtime.
	/// Nothing from Deltarune ships with the mod, so this only works if you own the game.
	/// </summary>
	public class DeltaruneAssets : ModSystem
	{
		public enum LoadState { NotStarted, Loading, Ready, NotFound, Failed, Disabled }

		public static LoadState State = LoadState.NotStarted;
		public static string StatusMessage = "";
		public static string LoadedFrom = "";
		public static string DumpPath = "";

		public static Texture2D Soul;
		public static SoundEffect BattleMusic;
		private static readonly Dictionary<string, SoundEffect> sounds = new();
		private static readonly Dictionary<string, float> soundVolumes = new();
		private static readonly Dictionary<string, DrSprite> sprites = new();
		private static readonly Dictionary<string, DrFont> fonts = new();

		// Role -> sprite/sound names to try, in order. Exact names get confirmed by the asset dump.
		private static readonly string[] SoulNames = { "spr_heart", "spr_heart_centered", "spr_soul" };
		public static readonly Dictionary<string, string[]> SoundRoles = new()
		{
			["graze"] = new[] { "snd_graze" },
			["spare"] = new[] { "snd_spare" },
			["heal"] = new[] { "snd_power", "snd_heal_c", "snd_heal" },
			["act"] = new[] { "snd_select", "snd_menumove" },
			["error"] = new[] { "snd_error", "snd_cantselect" },
			["hurt"] = new[] { "snd_hurt1" },
			["menumove"] = new[] { "snd_menumove" },
			["select"] = new[] { "snd_select" },
			["cantselect"] = new[] { "snd_cantselect", "snd_error" },
			["damage"] = new[] { "snd_damage" },
			["slash"] = new[] { "snd_laz_c" },
			["crit"] = new[] { "snd_criticalswing" },
			["battleenter"] = new[] { "snd_battleenter" },
			["weaponpull"] = new[] { "snd_weaponpull_fast", "snd_weaponpull" },
			["attack"] = new[] { "snd_heartshot_dr_b", "snd_heartshot_dr" },
			["text"] = new[] { "snd_text" },
			["item"] = new[] { "snd_item" },
			["boost"] = new[] { "snd_boost" },
			["mercyadd"] = new[] { "snd_mercyadd" },
			// a SOUL changing mode: the bell (Undertale's blue-SOUL ding; chapter 5 has it)
			["soulchange"] = new[] { "snd_bell", "snd_bell_bc" },
			// the yellow SOUL (chapter 2)
			["chargeshot"] = new[] { "snd_chargeshot_charge" },
			["chargefire"] = new[] { "snd_chargeshot_fire" },
			// attack patterns
			["bulletappear"] = new[] { "snd_spearappear" },
			["bulletfire"] = new[] { "snd_spearrise" },
			["impact"] = new[] { "snd_impact", "snd_screenshake" },
			["explosion"] = new[] { "snd_badexplosion", "snd_explosion", "snd_bomb" },
			// game over: the SOUL cracks, then shatters
			["soulcrack"] = new[] { "snd_break1" },
			["soulshatter"] = new[] { "snd_break2" },
		};

		/// <summary>Sprites the battle screen uses. Missing ones fall back to simple shapes.</summary>
		public static readonly string[] SpriteNames =
		{
			"spr_heart", "spr_dodgeheart", "spr_battlebg_0",
			"spr_btfight", "spr_btact", "spr_btitem", "spr_btspare", "spr_btdefend",
			"spr_pressfront", "spr_pressspot", "spr_attackspot", "spr_attack_cut1",
			"spr_tensionbar", "spr_tensionfilling", "spr_tensionmarker", "spr_tplogo",
			"spr_grazeappear", "spr_hpname", "spr_numbersfontbig", "spr_numbersfontbig_gold",
			"spr_ponman_eyebullet", "spr_smallbullet", "spr_healsparkle", "spr_sparestar",
			"bg_battleback1", "spr_battlemsg", "spr_heartoutline", "spr_heartoutline2", "spr_sparestar_anim", "spr_lightfairy",
			"spr_heartbreak", "spr_heartshards", "spr_headkris",
			"spr_yellowheart", "spr_yheart_shot", "spr_yheart_bigshot",
		};

		public static readonly string[] FontNames = { "fnt_mainbig", "fnt_main", "fnt_small" };

		/// <summary>
		/// Coloured SOULs and other sprites whose names vary by chapter. Each takes the first exact name any chapter has,
		/// else a name matching the pattern (kept strict: a loose keyword search picked spr_bhero_shield, a party member's
		/// shield, for the green SOUL's). Chapter 5 has spr_yellowheart and spr_purpleheart; no blue or green heart, bone or
		/// SOUL shield (2026-10-01 asset list), so those may come from another chapter or stay drawn by the mod.
		/// </summary>
		public static readonly (string key, string[] exact, string pattern)[] SearchedSprites =
		{
			("soul_blue", new[] { "spr_blueheart", "spr_heart_blue", "spr_blueheart_centered" }, @"^spr_(blue_?heart|heart_?blue|blue_?soul|soul_?blue)$"),
			("soul_green", new[] { "spr_greenheart", "spr_heart_green", "spr_greenheart_centered" }, @"^spr_(green_?heart|heart_?green|green_?soul|soul_?green)$"),
			("soul_purple", new[] { "spr_purpleheart", "spr_heart_purple" }, @"^spr_(purple_?heart|heart_?purple|purple_?soul|soul_?purple)$"),
			("soul_yellow", new[] { "spr_yellowheart", "spr_heart_yellow" }, @"^spr_(yellow_?heart|heart_?yellow)$"),
			("bone", new[] { "spr_bone", "spr_s_bone", "spr_bonebullet", "spr_bone_bullet", "spr_papyrus_bone", "spr_bullet_bone" }, @"^spr_(bullet_?)?bone(_?bullet)?(_v|_vertical|_h|_horizontal)?$"),
			("shield", new[] { "spr_greenshield", "spr_heart_shield", "spr_soulshield", "spr_shield_soul", "spr_greenheart_shield" }, @"^spr_.*(heart|soul|green).*shield.*$|^spr_.*shield.*(heart|soul|green).*$"),
		};

		/// <summary>Which Deltarune sprite each coloured SOUL (and searched sprite) came from (for /drassets).</summary>
		public static readonly Dictionary<string, string> SoulModeSources = new();

		public override void PostSetupContent()
		{
			if (!Main.dedServ)
				StartLoading();
		}

		public override void Unload()
		{
			var textures = new List<Texture2D>();
			if (Soul != null)
				textures.Add(Soul);
			foreach (var s in sprites.Values)
				textures.AddRange(s.Frames);
			foreach (var f in fonts.Values)
				textures.Add(f.Texture);
			var effects = sounds.Values.ToList();
			if (BattleMusic != null)
				effects.Add(BattleMusic);
			Main.QueueMainThreadAction(() =>
			{
				foreach (var t in textures)
					t.Dispose();
				foreach (var s in effects)
					s.Dispose();
			});
			Soul = null;
			BattleMusic = null;
			sounds.Clear();
			soundVolumes.Clear();
			sprites.Clear();
			fonts.Clear();
			State = LoadState.NotStarted;
		}

		public static void StartLoading()
		{
			if (State == LoadState.Loading)
				return;

			var config = ModContent.GetInstance<MercyConfig>();
			if (config == null || !config.UseDeltaruneAssets)
			{
				State = LoadState.Disabled;
				StatusMessage = "Using Deltarune assets is turned off in the mod config.";
				return;
			}

			State = LoadState.Loading;
			StatusMessage = "Looking for Deltarune...";
			string folderOverride = config.DeltaruneFolder;
			int chapterPref = config.Chapter;

			Task.Run(() =>
			{
				try
				{
					LoadFromInstall(folderOverride, chapterPref);
				}
				catch (Exception e)
				{
					State = LoadState.Failed;
					StatusMessage = "Failed to read Deltarune: " + e.Message;
					ModContent.GetInstance<MercyMode>().Logger.Error("Deltarune asset load failed", e);
				}
			});
		}

		/// <summary>
		/// Chosen chapter first, then the newest chapter down to chapter 1, and the launcher's data.win last
		/// (it only holds the chapter select screen).
		/// </summary>
		public static List<(int chapter, string path)> OrderDataFiles(List<(int chapter, string path)> files, int chapterPref)
		{
			return files
				.OrderByDescending(d => chapterPref > 0 && d.chapter == chapterPref)
				.ThenByDescending(d => d.chapter > 0)
				.ThenByDescending(d => d.chapter)
				.ToList();
		}

		private static void LoadFromInstall(string folderOverride, int chapterPref)
		{
			var log = ModContent.GetInstance<MercyMode>().Logger;

			string install = !string.IsNullOrWhiteSpace(folderOverride) && Directory.Exists(folderOverride)
				? folderOverride
				: InstallFinder.FindDeltarune();

			if (install == null)
			{
				State = LoadState.NotFound;
				StatusMessage = "Couldn't find Deltarune. Set the folder in the Mercy Mode config if it's installed somewhere unusual.";
				return;
			}

			DumpPath = "";
			SoulModeSources.Clear();
			var dataFiles = InstallFinder.FindDataFiles(install);
			if (dataFiles.Count == 0)
			{
				State = LoadState.NotFound;
				StatusMessage = $"Found {install} but no data.win inside it.";
				return;
			}

			RawFrame soulFrame = null;
			var rawSounds = new Dictionary<string, RawSound>();
			var rawSprites = new Dictionary<string, RawSprite>();
			var rawFonts = new Dictionary<string, RawFont>();
			var usedChapters = new List<int>();

			// Take each asset from the first data file that has it, so a sprite missing from one chapter
			// can still come from another.
			foreach (var (chapter, path) in OrderDataFiles(dataFiles, chapterPref))
			{
				StatusMessage = $"Reading chapter {chapter}...";
				using var data = new DataWin(path);
				int before = rawSounds.Count + rawSprites.Count + rawFonts.Count + (soulFrame != null ? 1 : 0);

				if (soulFrame == null)
				{
					foreach (string n in SoulNames)
					{
						RawSprite s = data.ReadSprite(n);
						if (s != null && s.Frames.Count > 0)
						{
							soulFrame = s.Frames[0];
							break;
						}
					}
				}

				foreach (var (role, names) in SoundRoles)
				{
					if (rawSounds.ContainsKey(role))
						continue;
					foreach (string n in names)
					{
						RawSound s = TryRead(() => data.ReadSound(n), n, log);
						if (s != null)
						{
							rawSounds[role] = s;
							break;
						}
					}
				}

				foreach (string n in SpriteNames)
				{
					if (rawSprites.ContainsKey(n))
						continue;
					RawSprite s = TryRead(() => data.ReadSprite(n), n, log);
					if (s != null && s.Frames.Count > 0)
						rawSprites[n] = s;
				}

				foreach (var (key, exact, pattern) in SearchedSprites)
				{
					if (rawSprites.ContainsKey(key))
						continue;
					string best = exact.FirstOrDefault(data.Sprites.ContainsKey) ?? data.Sprites.Keys
						.Where(n => System.Text.RegularExpressions.Regex.IsMatch(n, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
						.OrderBy(n => n.Length).ThenBy(n => n).FirstOrDefault();
					if (best == null)
						continue;
					RawSprite s = TryRead(() => data.ReadSprite(best), best, log);
					if (s != null && s.Frames.Count > 0)
					{
						rawSprites[key] = s;
						SoulModeSources[key] = $"{best} (chapter {chapter})";
					}
				}

				foreach (string n in FontNames)
				{
					if (rawFonts.ContainsKey(n))
						continue;
					RawFont f = TryRead(() => data.ReadFont(n), n, log);
					if (f != null && f.Glyphs.Count > 0)
						rawFonts[n] = f;
				}

				// A name list per chapter (the newest one is the one /drassets mentions)
				if (chapter >= 0)
				{
					string dump = WriteDump(data, chapter);
					if (DumpPath == "" && chapter > 0)
						DumpPath = dump;
				}
				data.ClearPageCache();

				if (rawSounds.Count + rawSprites.Count + rawFonts.Count + (soulFrame != null ? 1 : 0) > before)
					usedChapters.Add(chapter);

				if (soulFrame != null && rawSounds.Count == SoundRoles.Count && SpriteNames.All(rawSprites.ContainsKey)
					&& SearchedSprites.All(x => rawSprites.ContainsKey(x.key)) && rawFonts.Count == FontNames.Length)
					break;
			}

			if (soulFrame == null)
			{
				State = LoadState.Failed;
				StatusMessage = "Found Deltarune but none of its data files had the SOUL sprite. Check the asset dump for the right names.";
				return;
			}

			// Rude Buster, streamed from the shared mus folder
			(byte[] pcm, int rate, int channels)? music = null;
			string musicPath = Path.Combine(install, "mus", "battle.ogg");
			if (File.Exists(musicPath))
			{
				try
				{
					StatusMessage = "Decoding battle music...";
					music = AudioDecoder.DecodeOgg(File.ReadAllBytes(musicPath));
				}
				catch (Exception e)
				{
					log.Warn($"Couldn't decode battle music: {e.Message}");
				}
			}

			LoadedFrom = $"chapter {string.Join(", ", usedChapters)} ({install})";

			// GPU and audio resources have to be created on the main thread
			Main.QueueMainThreadAction(() =>
			{
				Soul = MakeTexture(soulFrame);
				foreach (var (role, raw) in rawSounds)
				{
					try
					{
						sounds[role] = AudioDecoder.ToSoundEffect(raw.Data);
						soundVolumes[role] = MathHelper.Clamp(raw.Volume, 0f, 1f);
					}
					catch (Exception e)
					{
						log.Warn($"Couldn't decode {raw.Name}: {e.Message}");
					}
				}
				foreach (var (name, raw) in rawSprites)
				{
					sprites[name] = new DrSprite
					{
						Frames = raw.Frames.Select(MakeTexture).ToArray(),
						Origin = new Vector2(raw.OriginX, raw.OriginY),
					};
				}
				foreach (var (name, raw) in rawFonts)
				{
					fonts[name] = new DrFont
					{
						Texture = MakeTexture(raw.Texture),
						Glyphs = raw.Glyphs,
						LineHeight = raw.Glyphs.Values.Max(g => g.Height),
					};
				}
				if (music is { } m)
					BattleMusic = new SoundEffect(m.pcm, m.rate, m.channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);

				State = LoadState.Ready;
				StatusMessage = $"Loaded SOUL, {sounds.Count}/{SoundRoles.Count} sounds, {sprites.Count}/{SpriteNames.Length} sprites, " +
					$"{fonts.Count}/{FontNames.Length} fonts{(BattleMusic != null ? ", battle music" : "")} from {LoadedFrom}";
				log.Info(StatusMessage);
				var missing = SpriteNames.Where(n => !sprites.ContainsKey(n)).Concat(FontNames.Where(n => !fonts.ContainsKey(n)))
					.Concat(SoundRoles.Keys.Where(r => !sounds.ContainsKey(r)).Select(r => "sound:" + r)).ToList();
				if (missing.Count > 0)
					log.Info("Missing Deltarune assets (using fallbacks): " + string.Join(", ", missing));
				log.Info("Coloured SOULs: " + (SoulModeSources.Count == 0 ? "none found (recoloured red SOUL)"
					: string.Join(", ", SoulModeSources.Select(kv => $"{kv.Key} = {kv.Value}"))));
			});
		}

		private static T TryRead<T>(Func<T> read, string name, log4net.ILog log) where T : class
		{
			try
			{
				return read();
			}
			catch (Exception e)
			{
				log.Warn($"Couldn't read {name}: {e.Message}");
				return null;
			}
		}

		private static Texture2D MakeTexture(RawFrame frame)
		{
			// SpriteBatch expects premultiplied alpha
			byte[] px = (byte[])frame.Rgba.Clone();
			for (int i = 0; i < px.Length; i += 4)
			{
				int a = px[i + 3];
				px[i] = (byte)(px[i] * a / 255);
				px[i + 1] = (byte)(px[i + 1] * a / 255);
				px[i + 2] = (byte)(px[i + 2] * a / 255);
			}
			var tex = new Texture2D(Main.graphics.GraphicsDevice, frame.Width, frame.Height);
			tex.SetData(px);
			return tex;
		}

		/// <summary>Writes every sprite and sound name to a text file so we can find the exact asset names.</summary>
		private static string WriteDump(DataWin data, int chapter)
		{
			try
			{
				string path = Path.Combine(Main.SavePath, $"MercyMode_deltarune_ch{chapter}_assets.txt");
				var sb = new StringBuilder();
				sb.AppendLine($"# Asset names from {data.Path}");
				sb.AppendLine($"# {data.Sprites.Count} sprites, {data.Sounds.Count} sounds, {data.Fonts.Count} fonts");
				sb.AppendLine("\n## Sounds");
				foreach (string n in data.Sounds.Keys.OrderBy(n => n))
					sb.AppendLine(n);
				sb.AppendLine("\n## Fonts");
				foreach (string n in data.Fonts.Keys.OrderBy(n => n))
					sb.AppendLine(n);
				sb.AppendLine("\n## Sprites");
				foreach (string n in data.Sprites.Keys.OrderBy(n => n))
					sb.AppendLine(n);
				File.WriteAllText(path, sb.ToString());
				return path;
			}
			catch
			{
				return "";
			}
		}

		private sealed class FadingSound
		{
			public SoundEffectInstance Instance;
			public ReLogic.Utilities.SlotId Slot;
			public float Volume;
			public int Age, Hold, Fade;
		}
		private static readonly List<FadingSound> fadingSounds = new();

		/// <summary>
		/// Like <see cref="Play"/>, but the sound is cut short: it plays at full volume for <paramref name="hold"/> ticks,
		/// then fades out over <paramref name="fade"/> ticks (long booms like explosions).
		/// </summary>
		public static void PlayFading(string role, SoundStyle fallback, float gain, int hold, int fade)
		{
			MercyConfig config = ModContent.GetInstance<MercyConfig>();
			MercySoundRedirect redirect = SoundRedirectFor(role, config);
			if (redirect == MercySoundRedirect.Silent)
				return;
			float roleGain = RoleGain(role) * gain;
			if (redirect == MercySoundRedirect.Deltarune && sounds.TryGetValue(role, out var effect))
			{
				float vol = MathHelper.Clamp(Main.soundVolume * config.BattleSoundVolume * soundVolumes.GetValueOrDefault(role, 1f) * roleGain, 0f, 1f);
				if (vol <= 0f)
					return;
				SoundEffectInstance instance = effect.CreateInstance();
				instance.Volume = vol;
				instance.Play();
				fadingSounds.Add(new FadingSound { Instance = instance, Volume = vol, Hold = hold, Fade = Math.Max(1, fade) });
				return;
			}
			SoundStyle style = redirect == MercySoundRedirect.Deltarune ? fallback : RedirectedStyle(redirect);
			var slot = SoundEngine.PlaySound(style with { Volume = style.Volume * config.BattleSoundVolume * roleGain });
			fadingSounds.Add(new FadingSound { Slot = slot, Volume = 1f, Hold = hold, Fade = Math.Max(1, fade) });
		}

		public override void PostUpdateEverything()
		{
			for (int i = fadingSounds.Count - 1; i >= 0; i--)
			{
				FadingSound s = fadingSounds[i];
				s.Age++;
				float t = s.Age <= s.Hold ? 1f : 1f - (s.Age - s.Hold) / (float)s.Fade;
				if (s.Instance != null)
				{
					if (t <= 0f || s.Instance.State == SoundState.Stopped)
					{
						s.Instance.Stop();
						s.Instance.Dispose();
						fadingSounds.RemoveAt(i);
						continue;
					}
					s.Instance.Volume = s.Volume * t;
				}
				else
				{
					// A Terraria sound: its ActiveSound.Volume is a multiplier on top of the style's volume
					if (!SoundEngine.TryGetActiveSound(s.Slot, out var active) || t <= 0f)
					{
						active?.Stop();
						fadingSounds.RemoveAt(i);
						continue;
					}
					active.Volume = t;
				}
			}
		}

		/// <summary>Plays the real Deltarune sound for a role if we have it, otherwise the vanilla fallback.</summary>
		public static void Play(string role, SoundStyle fallback, Vector2? position = null)
		{
			MercyConfig config = ModContent.GetInstance<MercyConfig>();
			MercySoundRedirect redirect = SoundRedirectFor(role, config);
			float roleGain = RoleGain(role);
			if (redirect == MercySoundRedirect.Silent)
				return;

			if (redirect != MercySoundRedirect.Deltarune)
			{
				SoundStyle redirected = RedirectedStyle(redirect);
				SoundEngine.PlaySound(redirected with { Volume = redirected.Volume * config.BattleSoundVolume * roleGain }, position);
				return;
			}

			if (sounds.TryGetValue(role, out var effect))
			{
				float vol = MathHelper.Clamp(Main.soundVolume * config.BattleSoundVolume * soundVolumes.GetValueOrDefault(role, 1f) * roleGain, 0f, 1f);
				if (vol > 0f)
					effect.Play(vol, 0f, 0f);
				return;
			}
			SoundEngine.PlaySound(fallback with { Volume = fallback.Volume * config.BattleSoundVolume * roleGain }, position);
		}

		/// <summary>Only plays if the real sound loaded. For effects that vanilla Terraria has no good match for.</summary>
		public static void PlayIfLoaded(string role)
		{
			MercyConfig config = ModContent.GetInstance<MercyConfig>();
			MercySoundRedirect redirect = SoundRedirectFor(role, config);
			float roleGain = RoleGain(role);
			if (redirect == MercySoundRedirect.Silent)
				return;
			if (redirect != MercySoundRedirect.Deltarune)
			{
				SoundStyle redirected = RedirectedStyle(redirect);
				SoundEngine.PlaySound(redirected with { Volume = redirected.Volume * config.BattleSoundVolume * roleGain });
				return;
			}

			if (sounds.TryGetValue(role, out var effect))
			{
				float vol = MathHelper.Clamp(Main.soundVolume * config.BattleSoundVolume * soundVolumes.GetValueOrDefault(role, 1f) * roleGain, 0f, 1f);
				if (vol > 0f)
					effect.Play(vol, 0f, 0f);
			}
		}

		/// <summary>
		/// Rude Buster is mastered louder than Terraria's music; this brings it down to sit with the boss tracks at the
		/// same Music volume. The config's Battle Music Volume still scales it.
		/// </summary>
		private const float RudeBusterGain = 0.45f;

		public static float BattleMusicVolume => MathHelper.Clamp(
			Main.musicVolume * ModContent.GetInstance<MercyConfig>().BattleMusicVolume * RudeBusterGain, 0f, 1f);

		private static MercySoundRedirect SoundRedirectFor(string role, MercyConfig config) => role switch
		{
			"menumove" or "select" or "cantselect" or "error" or "text" => config.MenuSoundRedirect,
			"hurt" or "damage" or "slash" or "crit" or "attack"
				or "bulletappear" or "bulletfire" or "impact" or "explosion" => config.BattleSoundRedirect,
			"act" or "heal" or "spare" or "item" or "boost" => config.ActionSoundRedirect,
			"weaponpull" => config.BattleStartSoundRedirect,
			"mercyadd" => config.MercyGainSoundRedirect,
			"graze" => config.GrazeSoundRedirect,
			"battleenter" or "weaponpull" => config.BattleStartSoundRedirect,
			_ => MercySoundRedirect.Deltarune,
		};

		// Keep frequent one-shots quieter than Terraria's ordinary effects while retaining each
		// GameMaker sound's own serialized volume from data.win.
		private static float RoleGain(string role) => role switch
		{
			"text" => 0.25f,
			"attack" => 0.4f,
			"menumove" or "select" => 0.75f,
			"spare" or "mercyadd" or "graze" => 0.8f,
			_ => 1f,
		};

		private static SoundStyle RedirectedStyle(MercySoundRedirect redirect) => redirect switch
		{
			MercySoundRedirect.MenuTick => Terraria.ID.SoundID.MenuTick,
			MercySoundRedirect.MenuOpen => Terraria.ID.SoundID.MenuOpen,
			MercySoundRedirect.MenuClose => Terraria.ID.SoundID.MenuClose,
			MercySoundRedirect.PlayerHit => Terraria.ID.SoundID.PlayerHit,
			MercySoundRedirect.NpcHit => Terraria.ID.SoundID.NPCHit1,
			MercySoundRedirect.NpcDeath => Terraria.ID.SoundID.NPCDeath1,
			MercySoundRedirect.Roar => Terraria.ID.SoundID.Roar,
			MercySoundRedirect.Item1 => Terraria.ID.SoundID.Item1,
			MercySoundRedirect.Item4 => Terraria.ID.SoundID.Item4,
			_ => Terraria.ID.SoundID.MenuTick,
		};

		public static int LoadedSoundCount => sounds.Count;

		public static bool HasSound(string role) => sounds.ContainsKey(role);

		/// <summary>Null when the sprite didn't load; callers draw a fallback shape.</summary>
		public static DrSprite Sprite(string name) => sprites.GetValueOrDefault(name);

		public static DrFont Font(string name) => fonts.GetValueOrDefault(name);

		public static int LoadedSpriteCount => sprites.Count;
		public static int LoadedFontCount => fonts.Count;
	}

	public static class InstallFinder
	{
		public static string FindDeltarune()
		{
			foreach (string lib in SteamLibraries())
			{
				foreach (string name in new[] { "DELTARUNE", "Deltarune", "deltarune" })
				{
					string p = Path.Combine(lib, "steamapps", "common", name);
					if (Directory.Exists(p))
						return p;
				}
			}
			return null;
		}

		private static IEnumerable<string> SteamLibraries()
		{
			var roots = new List<string>();
			string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

			if (OperatingSystem.IsWindows())
			{
				try
				{
					if (Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string reg)
						roots.Add(reg.Replace('/', '\\'));
				}
				catch { }
				roots.Add(@"C:\Program Files (x86)\Steam");
				roots.Add(@"C:\Program Files\Steam");
			}
			else if (OperatingSystem.IsMacOS())
			{
				roots.Add(Path.Combine(home, "Library", "Application Support", "Steam"));
			}
			else
			{
				roots.Add(Path.Combine(home, ".steam", "steam"));
				roots.Add(Path.Combine(home, ".local", "share", "Steam"));
			}

			var libs = new List<string>();
			foreach (string root in roots.Where(Directory.Exists))
			{
				libs.Add(root);
				string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
				if (!File.Exists(vdf))
					continue;
				foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"(.+?)\""))
					libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
			}
			return libs.Distinct(StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>Every GameMaker data file in the install, tagged with its chapter (0 = launcher / unknown).</summary>
		public static List<(int chapter, string path)> FindDataFiles(string install)
		{
			var result = new List<(int, string)>();
			string[] names = { "data.win", "game.unx", "game.ios", "game.droid" };
			foreach (string file in Directory.EnumerateFiles(install, "*", SearchOption.AllDirectories))
			{
				if (!names.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
					continue;
				string rel = Path.GetRelativePath(install, file);
				Match m = Regex.Match(rel, @"chapter\s*_?(\d+)", RegexOptions.IgnoreCase);
				int chapter = m.Success ? int.Parse(m.Groups[1].Value) : 0;
				result.Add((chapter, file));
			}
			return result;
		}
	}

	public static class AudioDecoder
	{
		public static SoundEffect ToSoundEffect(byte[] data)
		{
			if (data.Length > 4 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F')
			{
				using var ms = new MemoryStream(data);
				return SoundEffect.FromStream(ms);
			}

			if (data.Length > 4 && data[0] == 'O' && data[1] == 'g' && data[2] == 'g' && data[3] == 'S')
			{
				var (pcm, rate, channels) = DecodeOgg(data);
				return new SoundEffect(pcm, rate, channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
			}

			throw new InvalidDataException("Unknown audio format");
		}

		/// <summary>OGG Vorbis to 16-bit PCM. Safe to call off the main thread.</summary>
		public static (byte[] pcm, int rate, int channels) DecodeOgg(byte[] data)
		{
			using var ms = new MemoryStream(data);
			using var vorbis = new NVorbis.VorbisReader(ms, false);
			int channels = vorbis.Channels;
			using var output = new MemoryStream();
			float[] buffer = new float[4096 * channels];
			byte[] bytes = new byte[buffer.Length * 2];
			int read;
			while ((read = vorbis.ReadSamples(buffer, 0, buffer.Length)) > 0)
			{
				for (int i = 0; i < read; i++)
				{
					short s = (short)(MathHelper.Clamp(buffer[i], -1f, 1f) * short.MaxValue);
					bytes[i * 2] = (byte)s;
					bytes[i * 2 + 1] = (byte)(s >> 8);
				}
				output.Write(bytes, 0, read * 2);
			}
			return (output.ToArray(), vorbis.SampleRate, channels);
		}
	}
}
