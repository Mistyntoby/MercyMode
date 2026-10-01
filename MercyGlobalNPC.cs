using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace MercyMode
{
	public class MercyGlobalNPC : GlobalNPC
	{
		public override bool InstancePerEntity => true;

		/// <summary>0 to 100. Only meaningful on a boss's root NPC.</summary>
		public float Mercy;

		/// <summary>Game tick of the last time a player hurt this boss. Angry bosses won't listen to ACTs.</summary>
		public uint LastHurtTick;

		/// <summary>How many ACTs have landed. Used to walk through the flavor lines in order.</summary>
		public int ActCount;

		public bool RecentlyHurt => Main.GameUpdateCount - LastHurtTick < 120; // 2 seconds

		public override void OnHitByItem(NPC npc, Player player, Item item, NPC.HitInfo hit, int damageDone)
			=> LoseTrust(npc);

		public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone)
			=> LoseTrust(npc);

		private static void LoseTrust(NPC npc)
		{
			NPC root = MercyMode.Root(npc);
			if (!root.boss)
				return;
			var g = root.GetGlobalNPC<MercyGlobalNPC>();
			g.LastHurtTick = (uint)Main.GameUpdateCount;
			g.Mercy = Math.Max(0f, g.Mercy - 1.5f);
		}

		public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
		{
			// Not on the battle screen: the battle has its own MERCY display
			if (Battle.BattleSystem.DrawingEnemy)
				return;
			if (!npc.boss || npc.realLife >= 0 && npc.realLife != npc.whoAmI)
				return;

			const int width = 90;
			const int height = 10;
			Vector2 center = npc.Top - screenPos - new Vector2(0, 34);
			var back = new Rectangle((int)(center.X - width / 2f), (int)center.Y, width, height);
			var fill = new Rectangle(back.X + 2, back.Y + 2, (int)((width - 4) * (Mercy / 100f)), height - 4);

			Texture2D px = TextureAssets.MagicPixel.Value;
			spriteBatch.Draw(px, new Rectangle(back.X - 1, back.Y - 1, back.Width + 2, back.Height + 2), Color.White);
			spriteBatch.Draw(px, back, new Color(40, 0, 60));
			spriteBatch.Draw(px, fill, MercyMode.MercyYellow);

			bool ready = Mercy >= 100f;
			string label = ready ? "SPARE!" : $"MERCY {(int)Mercy}%";
			Color labelColor = ready ? MercyMode.MercyYellow : Color.White;
			Utils.DrawBorderString(spriteBatch, label, center - new Vector2(0, 4), labelColor, 0.8f, 0.5f, 1f);
		}

		/// <summary>End the fight peacefully. The boss still drops its loot and counts as defeated.</summary>
		private static bool EncounterRegistryIsBoss(NPC npc) => Battle.EncounterRegistry.IsBossFight(npc);

		public static void Spare(NPC root)
		{
			Deltarune.DeltaruneAssets.Play("spare", SoundID.Item4, root.Center);
			CombatText.NewText(root.Hitbox, MercyMode.MercyYellow, "SPARED", dramatic: true);
			MercyMode.Say($"* {root.FullName} was spared!", MercyMode.MercyYellow);

			// Sparkle burst on the boss and every segment attached to it
			foreach (NPC npc in Main.ActiveNPCs)
			{
				if (npc.whoAmI != root.whoAmI && npc.realLife != root.whoAmI)
					continue;
				for (int i = 0; i < 25; i++)
				{
					Dust d = Dust.NewDustDirect(npc.position, npc.width, npc.height, DustID.GoldFlame);
					d.velocity *= 2.5f;
					d.noGravity = true;
					d.scale = 1.6f;
				}
			}

			// Loot, downed flags, boss messages, hardmode for Wall of Flesh, etc.
			if (EncounterRegistryIsBoss(root))
			{
				root.NPCLoot();
			}
			else
			{
				// A regular enemy goes through Terraria's own death, so it counts toward an invasion's or event's
				// progress (goblins, pirates, Pumpkin Moon waves...); quietly, the spare has its own sound
				var sound = root.DeathSound;
				root.DeathSound = null;
				root.life = 0;
				try
				{
					root.checkDead();
				}
				finally
				{
					root.DeathSound = sound;
				}
			}

			foreach (NPC npc in Main.ActiveNPCs)
			{
				if (npc.whoAmI == root.whoAmI || npc.realLife == root.whoAmI)
				{
					npc.active = false;
					npc.life = 0;
				}
			}
		}
	}
}
