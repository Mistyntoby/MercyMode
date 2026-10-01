using Microsoft.Xna.Framework;
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

		public override void OnHitByItem(NPC npc, Player player, Item item, NPC.HitInfo hit, int damageDone)
			=> BattleSystem.TryStart(npc, player, "hit by " + item.Name);

		public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone)
		{
			// Town NPCs' shots (the Guide's arrows) also belong to the local player in singleplayer; skip them
			if (!projectile.npcProj && projectile.owner >= 0 && projectile.owner < Main.maxPlayers)
				BattleSystem.TryStart(npc, Main.player[projectile.owner], "hit by " + projectile.Name);
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

		public override bool CanBeHitByNPC(NPC npc, ref int cooldownSlot) => !BattleSystem.Active;

		public override bool CanBeHitByProjectile(Projectile proj) => !BattleSystem.Active;

		public override bool ImmuneTo(PlayerDeathReason damageSource, int cooldownCounter, bool dodgeable)
			=> BattleSystem.Active && !BattleSystem.HurtingPlayer;

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
		}

		public override void ModifyHurt(ref Player.HurtModifiers modifiers)
		{
			// DEFEND: tdamage = ceil(2 * tdamage / 3)
			if (BattleSystem.HurtingPlayer && BattleSystem.Defending)
				modifiers.FinalDamage *= BattleConstants.DefendDamageMult;
		}
	}

	/// <summary>Silences Terraria's music while Rude Buster plays.</summary>
	public class BattleMusicScene : ModSceneEffect
	{
		public override int Music => Deltarune.DeltaruneAssets.BattleMusic != null ? 0 : -1;
		public override SceneEffectPriority Priority => SceneEffectPriority.BossHigh;
		public override bool IsSceneEffectActive(Player player) => BattleSystem.Active;
	}
}
