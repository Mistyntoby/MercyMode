using System.Collections.Generic;
using System.Linq;
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
		/// <summary>
		/// Held still: in singleplayer everything is while a battle is open; in multiplayer only the enemies of a battle
		/// (the rest of the world keeps going for everyone else).
		/// </summary>
		public static bool Frozen(NPC npc) => MercyMode.IsSingleplayer ? BattleSystem.Active : Net.BattleNet.IsFrozen(npc);

		public override bool InstancePerEntity => true;

		/// <summary>Made unchaseable while frozen, so summons outside the battle don't dash at it forever.</summary>
		private bool unchased;

		public override bool PreAI(NPC npc)
		{
			if (!Frozen(npc))
			{
				if (unchased)
				{
					npc.chaseable = true;
					unchased = false;
				}
				return true;
			}
			if (npc.chaseable)
			{
				npc.chaseable = false;
				unchased = true;
			}
			// AI is skipped, but Terraria still adds velocity to position afterwards
			npc.velocity = Vector2.Zero;
			// Multiplayer, seen from outside: it faces the players fighting it
			if (!MercyMode.IsSingleplayer && Net.BattleNet.WorldBattles.TryGetValue(Net.BattleNet.BattleOf(npc), out var wb))
			{
				Player near = wb.Players.Where(i => i >= 0 && i < Main.maxPlayers && Main.player[i].active)
					.Select(i => Main.player[i]).OrderBy(pl => pl.DistanceSQ(npc.Center)).FirstOrDefault();
				if (near != null)
					npc.direction = npc.spriteDirection = near.Center.X < npc.Center.X ? -1 : 1;
			}
			return false;
		}

		public override bool CheckActive(NPC npc) => !Frozen(npc);

		// A battle's enemy and the rest of the world leave each other alone (town NPCs, other monsters)
		public override bool CanHitNPC(NPC npc, NPC target) => !Frozen(npc) && !Frozen(target);

		public override bool CanHitPlayer(NPC npc, Player target, ref int cooldownSlot)
		{
			if (BattleSystem.Active || Net.BattleNet.Online && Net.BattleNet.RequestPending)
				return false;
			// Multiplayer: another party's enemies are harmless (the join key brings you into their battle)
			if (Net.BattleNet.IsFrozen(npc))
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
		public override bool? CanBeHitByItem(NPC npc, Player player, Item item) => Frozen(npc) || BattleSystem.Active ? false : null;

		public override bool? CanBeHitByProjectile(NPC npc, Projectile projectile) => Frozen(npc) || BattleSystem.Active ? false : null;

		public override void OnHitByItem(NPC npc, Player player, Item item, NPC.HitInfo hit, int damageDone)
			=> BattleSystem.TryStart(npc, player, "hit by " + item.Name);

		public override void OnHitByProjectile(NPC npc, Projectile projectile, NPC.HitInfo hit, int damageDone)
		{
			// Town NPCs' shots (the Guide's arrows) also belong to the local player in singleplayer; skip them
			// Summons don't start battles: they roam, and dragged their owner into fights far away
			if (!projectile.npcProj && projectile.owner >= 0 && projectile.owner < Main.maxPlayers
				&& !BattleSystem.IsSummonOf(projectile, projectile.owner))
				BattleSystem.TryStart(npc, Main.player[projectile.owner], "hit by " + projectile.Name);
		}

		public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
			// The battle screen draws this one (gliding in and out of its world spot); the world copy stays hidden
			=> BattleSystem.DrawingEnemy || !BattleSystem.IsBattleSprite(npc);

		public override void DrawEffects(NPC npc, ref Color drawColor)
		{
			// On the battle screen: full-bright, or the world's light while gliding in or out
			if (BattleSystem.DrawingEnemy)
			{
				drawColor = BattleSystem.Tint(Color.White, BattleSystem.EnemyLight);
				// Picking a part: the others dim, so the one under the cursor stands out
				if (BattleSystem.FlashPart >= 0 && npc.whoAmI != BattleSystem.FlashPart)
					drawColor = BattleSystem.Tint(drawColor, new Color(110, 110, 120));
			}
		}

		public override void EditSpawnRate(Player player, ref int spawnRate, ref int maxSpawns)
		{
			// Multiplayer: the server spawns enemies, so it checks who's in a battle
			if (MercyMode.IsSingleplayer ? BattleSystem.Active : Net.BattleNet.InBattle(player.whoAmI))
				maxSpawns = 0;
		}
	}

	public class BattleFreezeProjectile : GlobalProjectile
	{
		public override bool PreAI(Projectile projectile)
		{
			// Our own minions and sentries stay out of it (in multiplayer too): they fight in the battle, after FIGHT
			// Summons of anyone in a battle hold still, on every screen and the server (not just the owner's)
			bool ownSummon = SummonInBattle(projectile);
			// Multiplayer: other projectiles belong to their owners and the server; the world keeps going
			if (!ownSummon && (!BattleSystem.Active || !MercyMode.IsSingleplayer))
				return true;
			projectile.velocity = Vector2.Zero;
			projectile.timeLeft++; // don't expire while frozen
			// Summons that start invisible and fade in from their AI (the Stardust Dragon) would stay invisible while
			// frozen, which is how one called in the battle shows up: fade them in here instead
			if (ownSummon && projectile.alpha > 0 && Terraria.ID.ContentSamples.ProjectilesByType.TryGetValue(projectile.type, out Projectile sample)
				&& sample.alpha >= 200)
				projectile.alpha = System.Math.Max(0, projectile.alpha - 20);
			// A fighter's following summons wait just behind them in the world (sentries stay where they were built)
			int type = projectile.type;
			bool dragonBody = type is Terraria.ID.ProjectileID.StardustDragon2 or Terraria.ID.ProjectileID.StardustDragon3 or Terraria.ID.ProjectileID.StardustDragon4;
			if (ownSummon && !projectile.sentry && type != Terraria.ID.ProjectileID.AbigailCounter && !dragonBody)
			{
				Player owner = Main.player[projectile.owner];
				Vector2 to = owner.Center + new Vector2(-owner.direction * 30f, -14f);
				Vector2 moved = to - projectile.Center;
				projectile.Center = to;
				// The Stardust Dragon's body and tail come along with its head, keeping its shape
				if (type == Terraria.ID.ProjectileID.StardustDragon1)
				{
					foreach (Projectile seg in Main.ActiveProjectiles)
						if (seg.owner == projectile.owner && seg.type is Terraria.ID.ProjectileID.StardustDragon2 or Terraria.ID.ProjectileID.StardustDragon3 or Terraria.ID.ProjectileID.StardustDragon4)
							seg.Center += moved;
				}
				else
				{
					projectile.direction = owner.direction;
					projectile.rotation = 0f; // upright while it waits, not tilted from flying
					projectile.spriteDirection = owner.direction * FacingRight(type);
				}
			}
			return false;
		}

		public override bool CanHitPlayer(Projectile projectile, Player target) => !BattleSystem.Active;

		/// <summary>
		/// Per summon kind, the spriteDirection that makes it face right. Sprites drawn facing left (the Imp) use -1.
		/// Learned from how each summon turns while it flies about in the world; a few known ones to start with.
		/// </summary>
		private static readonly Dictionary<int, int> facingVotes = new()
		{
			[Terraria.ID.ProjectileID.FlyingImp] = -50,
		};

		public static int FacingRight(int type) => facingVotes.TryGetValue(type, out int v) && v < 0 ? -1 : 1;

		public override void PostAI(Projectile projectile)
		{
			// Moving sideways at a fair speed, a summon faces where it goes: note which spriteDirection that was
			if (projectile.owner != Main.myPlayer || Main.netMode == Terraria.ID.NetmodeID.Server || System.Math.Abs(projectile.velocity.X) < 2f
				|| projectile.spriteDirection == 0 || !BattleSystem.IsSummonOf(projectile, projectile.owner) || SummonInBattle(projectile))
				return;
			int vote = projectile.spriteDirection * System.Math.Sign(projectile.velocity.X);
			facingVotes.TryGetValue(projectile.type, out int v);
			facingVotes[projectile.type] = System.Math.Clamp(v + vote, -60, 60);
		}

		/// <summary>A summon whose owner is in a battle (ours, or another player's as the server and the others know it).</summary>
		public static bool SummonInBattle(Projectile p)
		{
			if (p.owner < 0 || p.owner >= Main.maxPlayers || !BattleSystem.IsSummonOf(p, p.owner))
				return false;
			if (p.owner == Main.myPlayer && BattleSystem.Active)
				return true;
			if (Main.netMode == Terraria.ID.NetmodeID.Server)
				return Net.BattleNet.InBattle(p.owner);
			return Net.BattleNet.WorldBattles.Values.Any(b => b.Stage != Net.BattleNet.Stage.Over && b.Players.Contains(p.owner));
		}

		// Frozen minions can't hit anything in the world meanwhile (no kills heard in the background)
		public override bool? CanHitNPC(Projectile projectile, NPC target) =>
			SummonInBattle(projectile) ? false : null;

		// The battle screen draws them beside the player; the world copies stay hidden
		public override bool PreDraw(Projectile projectile, ref Color lightColor) =>
			BattleSystem.DrawingSummons || !(BattleSystem.Active && BattleSystem.IsSummonOf(projectile, Main.myPlayer));
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
			// The inventory key (Esc) opens the pause menu instead: BattleSystem.PostUpdateInput
			Player.controlSmart = Player.controlTorch = Player.controlMap = Player.controlInv = false;
		}

		public override bool CanUseItem(Item item) => !BattleSystem.Active;

		public override void PostUpdateEquips()
		{
			// In a battle, enemies in the world don't go after this player (the server decides targets in multiplayer)
			bool inBattle = Main.netMode == Terraria.ID.NetmodeID.Server ? Net.BattleNet.InBattle(Player.whoAmI)
				: Player.whoAmI == Main.myPlayer && BattleSystem.Active;
			if (inBattle)
				System.Array.Fill(Player.npcTypeNoAggro, true);
		}

		public override void HideDrawLayers(PlayerDrawSet drawInfo)
		{
			// The battle screen draws the character (gliding in and out of this spot); hide the world copy until it lands
			// headOnlyRender: the nameplate portrait and map icons draw through here too; keep those
			// Party members too (multiplayer): the battle screen glides them in from their spots as well
			int who = drawInfo.drawPlayer.whoAmI;
			if (!BattleSystem.Active || BattleSystem.DrawingHero || drawInfo.headOnlyRender || who != Main.myPlayer && !Net.BattleNet.Party.Contains(who))
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

	/// <summary>
	/// The battle screen isn't in the world: Terraria lights the held weapon by the tile at the spot it's drawn, which made
	/// weapons dark on the battle screen. While the battle draws a character, that light is the battle's own.
	/// </summary>
	public class BattleLighting : ModSystem
	{
		public override void Load()
		{
			Terraria.On_Lighting.GetColor_int_int += (orig, x, y) => BattleSystem.DrawingHero ? BattleSystem.HeroLight : orig(x, y);
		}
	}

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
