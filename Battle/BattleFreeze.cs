using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace MercyMode.Battle
{
	/// <summary>Starts battles on contact or hit, and holds every NPC still while one is open.</summary>
	public class BattleFreezeNPC : GlobalNPC
	{
		public override bool PreAI(NPC npc)
		{
			if (!BattleSystem.Active)
				return true;
			// AI is skipped, but Terraria still adds velocity to position afterwards
			npc.velocity = Vector2.Zero;
			return false;
		}

		public override bool CheckActive(NPC npc) => !BattleSystem.Active;

		public override bool CanHitPlayer(NPC npc, Player target, ref int cooldownSlot)
		{
			if (BattleSystem.Active)
				return false;
			// Terraria asks this for every hostile NPC every tick, before checking that the hitboxes touch
			if (npc.Hitbox.Intersects(target.Hitbox) && BattleSystem.CanStart(npc, target))
			{
				BattleSystem.TryStart(npc, target, "touch");
				return false;
			}
			return true;
		}

		// During a battle only FIGHT hurts enemies (SimpleStrikeNPC skips these): no leftover swing or shot in the world
		public override bool? CanBeHitByItem(NPC npc, Player player, Item item) => BattleSystem.Active ? false : null;

		public override bool? CanBeHitByProjectile(NPC npc, Projectile projectile) => BattleSystem.Active ? false : null;

		public override void OnHitByItem(NPC npc, Player player, Item item, NPC.HitInfo hit, int damageDone)
			=> BattleSystem.TryStart(npc, player, "hit by " + item.Name);

		public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone)
		{
			// Town NPCs' shots (the Guide's arrows) also belong to the local player in singleplayer; skip them
			if (!projectile.npcProj && projectile.owner >= 0 && projectile.owner < Main.maxPlayers)
				BattleSystem.TryStart(npc, Main.player[projectile.owner], "hit by " + projectile.Name);
		}

		public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
			// The battle screen draws this one (gliding in and out of its world spot); the world copy stays hidden
			=> BattleSystem.DrawingEnemy || !BattleSystem.IsBattleSprite(npc);

		public override void DrawEffects(NPC npc, ref Color drawColor)
		{
			// On the battle screen: full-bright, or the world's light while gliding in or out
			if (BattleSystem.DrawingEnemy)
				drawColor = BattleSystem.Tint(Color.White, BattleSystem.EnemyLight);
		}

		public override void EditSpawnRate(Player player, ref int spawnRate, ref int maxSpawns)
		{
			if (BattleSystem.Active)
				maxSpawns = 0;
		}
	}

	public class BattleFreezeProjectile : GlobalProjectile
	{
		public override bool PreAI(Projectile projectile)
		{
			if (!BattleSystem.Active)
				return true;
			projectile.velocity = Vector2.Zero;
			projectile.timeLeft++; // don't expire while frozen
			return false;
		}

		public override bool CanHitPlayer(Projectile projectile, Player target) => !BattleSystem.Active;
	}

	/// <summary>Locks the player in place during a battle. Only the battle itself can hurt them.</summary>
	public class BattlePlayer : ModPlayer
	{
		public override void SetControls()
		{
			if (!BattleSystem.Active || Player.whoAmI != Main.myPlayer)
				return;
			Player.controlLeft = Player.controlRight = Player.controlUp = Player.controlDown = false;
			Player.controlJump = Player.controlUseItem = Player.controlUseTile = Player.controlThrow = false;
			Player.controlHook = Player.controlMount = Player.controlQuickHeal = Player.controlQuickMana = false;
			Player.controlSmart = Player.controlTorch = Player.controlInv = Player.controlMap = false;
		}

		public override bool CanUseItem(Item item) => !BattleSystem.Active;

		public override void HideDrawLayers(PlayerDrawSet drawInfo)
		{
			// The battle screen draws the character (gliding in and out of this spot); hide the world copy until it lands
			// headOnlyRender: the nameplate portrait and map icons draw through here too; keep those
			if (!BattleSystem.Active || BattleSystem.DrawingHero || drawInfo.headOnlyRender || drawInfo.drawPlayer.whoAmI != Main.myPlayer)
				return;
			foreach (PlayerDrawLayer layer in PlayerDrawLayerLoader.Layers)
				layer.Hide();
		}

		public override bool CanBeHitByNPC(NPC npc, ref int cooldownSlot) => !BattleSystem.Active;

		public override bool CanBeHitByProjectile(Projectile proj) => !BattleSystem.Active;

		public override bool ImmuneTo(PlayerDeathReason damageSource, int cooldownCounter, bool dodgeable)
			=> BattleSystem.Active && !BattleSystem.HurtingPlayer;

		public override bool PreKill(double damage, int hitDirection, bool pvp, ref bool playSound, ref bool genDust, ref PlayerDeathReason damageSource)
		{
			// A lethal hit in a battle: the SOUL breaks on the battle screen first, then the battle kills the player
			if (!BattleSystem.Active || Player.whoAmI != Main.myPlayer)
				return true;
			BattleSystem.Instance.RequestSoulDeath(damageSource, damage);
			return false;
		}

		public override void ModifyDrawInfo(ref PlayerDrawSet drawInfo)
		{
			if (!BattleSystem.DrawingHero)
				return;
			// The battle screen isn't in the world: use the character's own colours, not the light at that spot
			Player p = drawInfo.drawPlayer;
			drawInfo.colorHair = p.GetHairColor(useLighting: false);
			drawInfo.colorEyeWhites = Color.White;
			drawInfo.colorEyes = p.eyeColor;
			drawInfo.colorHead = p.skinColor;
			drawInfo.colorBodySkin = p.skinColor;
			drawInfo.colorLegs = p.skinColor;
			drawInfo.colorShirt = p.shirtColor;
			drawInfo.colorUnderShirt = p.underShirtColor;
			drawInfo.colorPants = p.pantsColor;
			drawInfo.colorShoes = p.shoeColor;
			drawInfo.colorArmorHead = Color.White;
			drawInfo.colorArmorBody = Color.White;
			drawInfo.colorArmorLegs = Color.White;
			drawInfo.colorMount = Color.White;
			drawInfo.colorDisplayDollSkin = Color.White;
			drawInfo.floatingTubeColor = Color.White;

			// While gliding to or from the world, match the world's lighting at the player's spot
			Color light = BattleSystem.HeroLight;
			if (light != Color.White)
			{
				drawInfo.colorHair = BattleSystem.Tint(drawInfo.colorHair, light);
				drawInfo.colorEyeWhites = BattleSystem.Tint(drawInfo.colorEyeWhites, light);
				drawInfo.colorEyes = BattleSystem.Tint(drawInfo.colorEyes, light);
				drawInfo.colorHead = BattleSystem.Tint(drawInfo.colorHead, light);
				drawInfo.colorBodySkin = BattleSystem.Tint(drawInfo.colorBodySkin, light);
				drawInfo.colorLegs = BattleSystem.Tint(drawInfo.colorLegs, light);
				drawInfo.colorShirt = BattleSystem.Tint(drawInfo.colorShirt, light);
				drawInfo.colorUnderShirt = BattleSystem.Tint(drawInfo.colorUnderShirt, light);
				drawInfo.colorPants = BattleSystem.Tint(drawInfo.colorPants, light);
				drawInfo.colorShoes = BattleSystem.Tint(drawInfo.colorShoes, light);
				drawInfo.colorArmorHead = BattleSystem.Tint(drawInfo.colorArmorHead, light);
				drawInfo.colorArmorBody = BattleSystem.Tint(drawInfo.colorArmorBody, light);
				drawInfo.colorArmorLegs = BattleSystem.Tint(drawInfo.colorArmorLegs, light);
				drawInfo.colorMount = BattleSystem.Tint(drawInfo.colorMount, light);
				drawInfo.colorDisplayDollSkin = BattleSystem.Tint(drawInfo.colorDisplayDollSkin, light);
				drawInfo.floatingTubeColor = BattleSystem.Tint(drawInfo.floatingTubeColor, light);
			}

			// Afterimages are drawn with a "shadow" amount; keep them see-through like Terraria does
			if (drawInfo.shadow > 0f)
			{
				float a = 1f - drawInfo.shadow;
				drawInfo.colorHair *= a;
				drawInfo.colorEyeWhites *= a;
				drawInfo.colorEyes *= a;
				drawInfo.colorHead *= a;
				drawInfo.colorBodySkin *= a;
				drawInfo.colorLegs *= a;
				drawInfo.colorShirt *= a;
				drawInfo.colorUnderShirt *= a;
				drawInfo.colorPants *= a;
				drawInfo.colorShoes *= a;
				drawInfo.colorArmorHead *= a;
				drawInfo.colorArmorBody *= a;
				drawInfo.colorArmorLegs *= a;
				drawInfo.colorMount *= a;
				drawInfo.colorDisplayDollSkin *= a;
				drawInfo.floatingTubeColor *= a;
			}
		}

		public override void ModifyHurt(ref Player.HurtModifiers modifiers)
		{
			// DEFEND: tdamage = ceil(2 * tdamage / 3)
			if (BattleSystem.HurtingPlayer && BattleSystem.Defending)
				modifiers.FinalDamage *= BattleConstants.DefendDamageMult;
		}
	}

	/// <summary>Silences Terraria's music while Rude Buster plays.</summary>
	/// <summary>
	/// Silences Terraria's music while Rude Buster plays. In boss battles it stays off, so Terraria picks the boss's
	/// own track as usual (vanilla, modded, Otherworldly).
	/// </summary>
	public class BattleMusicScene : ModSceneEffect
	{
		public override int Music => 0;
		public override SceneEffectPriority Priority => SceneEffectPriority.BossHigh;
		public override bool IsSceneEffectActive(Player player) => BattleSystem.SilenceTerrariaMusic;
	}
}
