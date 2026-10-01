using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.ModLoader;
using MercyMode.Deltarune;

namespace MercyMode
{
	public class MercyPlayer : ModPlayer
	{
		public const float HealPrayerCost = 32f;
		public const float TPPerGraze = 2.5f;
		public const int GrazeRange = 40; // pixels around your hitbox that count as a graze

		/// <summary>0 to 100.</summary>
		public float TP;

		/// <summary>Ticks left where the soul flashes after a graze.</summary>
		public int GrazeFlash;

		private int actCooldown;
		private bool warnedMultiplayer;

		// key = projectile identity (or 100000 + NPC index), value = tick when it can graze again
		private readonly Dictionary<int, uint> grazeCooldowns = new();

		public override void OnRespawn()
		{
			TP = 0;
			grazeCooldowns.Clear();
		}

		public override void PostUpdate()
		{
			if (Player.whoAmI != Main.myPlayer)
				return;

			if (actCooldown > 0)
				actCooldown--;
			if (GrazeFlash > 0)
				GrazeFlash--;

			// The battle screen has its own grazing; frozen bullets in the world shouldn't feed TP
			if (Player.dead || Player.immune || Battle.BattleSystem.Active)
				return;

			CheckGrazes();

			// Clean up old entries once a second so the dictionary doesn't grow forever
			if (Main.GameUpdateCount % 60 == 0)
			{
				var expired = new List<int>();
				foreach (var kv in grazeCooldowns)
					if (kv.Value < Main.GameUpdateCount)
						expired.Add(kv.Key);
				foreach (int k in expired)
					grazeCooldowns.Remove(k);
			}
		}

		private void CheckGrazes()
		{
			Rectangle hitbox = Player.Hitbox;
			Rectangle grazeBox = hitbox;
			grazeBox.Inflate(GrazeRange, GrazeRange);

			foreach (Projectile proj in Main.ActiveProjectiles)
			{
				if (!proj.hostile || proj.damage <= 0)
					continue;
				TryGraze(proj.identity, proj.Hitbox, hitbox, grazeBox, proj.Center);
			}

			foreach (NPC npc in Main.ActiveNPCs)
			{
				if (npc.friendly || npc.damage <= 0 || npc.dontTakeDamage && !npc.boss)
					continue;
				TryGraze(100000 + npc.whoAmI, npc.Hitbox, hitbox, grazeBox, npc.Center);
			}
		}

		private void TryGraze(int key, Rectangle threat, Rectangle hitbox, Rectangle grazeBox, Vector2 threatCenter)
		{
			if (!threat.Intersects(grazeBox) || threat.Intersects(hitbox))
				return;
			if (grazeCooldowns.TryGetValue(key, out uint readyAt) && readyAt > Main.GameUpdateCount)
				return;

			grazeCooldowns[key] = (uint)Main.GameUpdateCount + 30;
			TP = Math.Min(100f, TP + TPPerGraze);
			GrazeFlash = 12;
			DeltaruneAssets.PlayIfLoaded("graze");

			Vector2 between = Vector2.Lerp(Player.Center, threatCenter, 0.5f);
			for (int i = 0; i < 4; i++)
			{
				Dust d = Dust.NewDustPerfect(between, DustID.Torch, Main.rand.NextVector2Circular(2f, 2f));
				d.noGravity = true;
			}
		}

		public override void ProcessTriggers(TriggersSet triggersSet)
		{
			if (Battle.BattleSystem.Active)
				return;
			if (MercyMode.ActKey.JustPressed)
				DoAct();
			if (MercyMode.SpareKey.JustPressed)
				DoSpare();
			if (MercyMode.HealPrayerKey.JustPressed)
				DoHealPrayer();
		}

		private bool CheckMultiplayer()
		{
			if (MercyMode.IsSingleplayer)
				return true;
			if (!warnedMultiplayer)
			{
				MercyMode.Say("* ACT, SPARE and Heal Prayer outside battles only work in singleplayer for now (battles work in multiplayer).", MercyMode.Gray);
				warnedMultiplayer = true;
			}
			return false;
		}

		private void DoAct()
		{
			if (!CheckMultiplayer() || Player.dead || actCooldown > 0)
				return;

			NPC boss = MercyMode.FindTargetBoss(Player);
			if (boss == null)
			{
				MercyMode.Say("* But nobody came.", MercyMode.Gray);
				actCooldown = 30;
				return;
			}

			var g = boss.GetGlobalNPC<MercyGlobalNPC>();
			actCooldown = 90;

			if (g.RecentlyHurt)
			{
				MercyMode.Say($"* {boss.FullName} is too angry to listen. Stop hitting it first.", MercyMode.Gray);
				DeltaruneAssets.Play("error", SoundID.MenuClose, Player.Center);
				return;
			}

			if (g.Mercy >= 100f)
			{
				MercyMode.Say($"* {boss.FullName} is ready to be spared.", MercyMode.MercyYellow);
				return;
			}

			MercyMode.Say(ActLines.Get(boss.type, g.ActCount), MercyMode.TextWhite);
			g.ActCount++;

			int gain = Main.rand.Next(14, 23);
			g.Mercy = Math.Min(100f, g.Mercy + gain);
			CombatText.NewText(boss.Hitbox, MercyMode.MercyYellow, $"+{gain}% MERCY");
			DeltaruneAssets.Play("mercyadd", SoundID.MenuTick, Player.Center);

			if (g.Mercy >= 100f)
				MercyMode.Say($"* {boss.FullName} doesn't want to fight anymore.", MercyMode.MercyYellow);
		}

		private void DoSpare()
		{
			if (!CheckMultiplayer() || Player.dead)
				return;

			NPC boss = MercyMode.FindTargetBoss(Player);
			if (boss == null)
				return;

			var g = boss.GetGlobalNPC<MercyGlobalNPC>();
			if (g.Mercy < 100f)
			{
				MercyMode.Say($"* But {boss.FullName} wasn't ready to be spared.", MercyMode.Gray);
				DeltaruneAssets.Play("error", SoundID.MenuClose, Player.Center);
				return;
			}

			MercyGlobalNPC.Spare(boss);
		}

		private void DoHealPrayer()
		{
			if (!CheckMultiplayer() || Player.dead)
				return;

			if (TP < HealPrayerCost)
			{
				MercyMode.Say($"* Not enough TP. ({(int)TP}% / {(int)HealPrayerCost}%)", MercyMode.Gray);
				DeltaruneAssets.Play("error", SoundID.MenuClose, Player.Center);
				return;
			}

			TP -= HealPrayerCost;
			int amount = Math.Max(20, Player.statLifeMax2 / 4);
			Player.Heal(amount);
			DeltaruneAssets.Play("heal", SoundID.Item4, Player.Center);
			for (int i = 0; i < 20; i++)
			{
				Dust d = Dust.NewDustDirect(Player.position, Player.width, Player.height, DustID.HealingPlus);
				d.velocity.Y -= 2f;
			}
		}
	}
}
