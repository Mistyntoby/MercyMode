using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace MercyMode.Battle
{
	/// <summary>
	/// <c>/mmbattle npc &lt;boss&gt; -test</c>: for one battle the player gets what a typical player has when they reach
	/// that boss (the same stage <see cref="BattleSystem.BossStage"/> tunes the boss's damage against): max HP, an armour
	/// set, a weapon of the time and five healing potions. Their own things come back when the battle ends; the backup is
	/// saved with the character too, so a crash or an autosave mid-test never loses them.
	/// </summary>
	public class TestLoadoutPlayer : ModPlayer
	{
		private Item[] inventory, armor;
		private int crystals, fruit, life, lifeMax;

		public bool Testing => inventory != null;

		/// <summary>
		/// What a typical player brings to each boss (melee): weapon, armour (head, body, legs), healing potion and max
		/// HP, from the Terraria Wiki's boss strategy guides and class setups (Eye of Cthulhu: 200 HP and Gold or
		/// Platinum armour recommended; Queen Bee and Skeletron 300 HP; Deerclops 260-280 HP with Starfury or Blade of
		/// Grass; Molten for the Wall of Flesh; Titanium/Adamantite for the mechanical bosses...). The battle's balance
		/// is worked out from these very items (<see cref="BattleSystem.BossStage"/>), so the kit is the player the boss
		/// is tuned against.
		/// </summary>
		public static (int weapon, int head, int body, int legs, int potion, int hp) Kit(int boss) => boss switch
		{
			NPCID.KingSlime => (ItemID.GoldBroadsword, ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves, ItemID.LesserHealingPotion, 140),
			NPCID.EyeofCthulhu => (ItemID.PlatinumBroadsword, ItemID.PlatinumHelmet, ItemID.PlatinumChainmail, ItemID.PlatinumGreaves, ItemID.LesserHealingPotion, 200),
			NPCID.BrainofCthulhu or NPCID.EaterofWorldsHead or NPCID.EaterofWorldsBody or NPCID.EaterofWorldsTail
				=> (ItemID.LightsBane, ItemID.PlatinumHelmet, ItemID.PlatinumChainmail, ItemID.PlatinumGreaves, ItemID.LesserHealingPotion, 240),
			NPCID.QueenBee => (ItemID.BladeofGrass, ItemID.ShadowHelmet, ItemID.ShadowScalemail, ItemID.ShadowGreaves, ItemID.HealingPotion, 300),
			NPCID.Deerclops => (ItemID.Starfury, ItemID.PlatinumHelmet, ItemID.PlatinumChainmail, ItemID.PlatinumGreaves, ItemID.HealingPotion, 280),
			NPCID.SkeletronHead => (ItemID.BladeofGrass, ItemID.ShadowHelmet, ItemID.ShadowScalemail, ItemID.ShadowGreaves, ItemID.HealingPotion, 300),
			NPCID.WallofFlesh => (ItemID.FieryGreatsword, ItemID.MoltenHelmet, ItemID.MoltenBreastplate, ItemID.MoltenGreaves, ItemID.HealingPotion, 400),
			NPCID.QueenSlimeBoss or NPCID.Retinazer or NPCID.Spazmatism or NPCID.TheDestroyer or NPCID.SkeletronPrime
				=> (ItemID.TitaniumSword, ItemID.TitaniumMask, ItemID.TitaniumBreastplate, ItemID.TitaniumLeggings, ItemID.HealingPotion, 400),
			NPCID.Plantera => (ItemID.TrueExcalibur, ItemID.HallowedMask, ItemID.HallowedPlateMail, ItemID.HallowedGreaves, ItemID.GreaterHealingPotion, 480),
			NPCID.Golem or NPCID.GolemHead => (ItemID.TerraBlade, ItemID.TurtleHelmet, ItemID.TurtleScaleMail, ItemID.TurtleLeggings, ItemID.GreaterHealingPotion, 500),
			NPCID.DukeFishron or NPCID.HallowBoss or NPCID.CultistBoss
				=> (ItemID.TerraBlade, ItemID.BeetleHelmet, ItemID.BeetleScaleMail, ItemID.BeetleLeggings, ItemID.GreaterHealingPotion, 500),
			NPCID.MoonLordCore or NPCID.MoonLordHand or NPCID.MoonLordHead
				=> (ItemID.InfluxWaver, ItemID.SolarFlareHelmet, ItemID.SolarFlareBreastplate, ItemID.SolarFlareLeggings, ItemID.GreaterHealingPotion, 500),
			_ => Main.hardMode
				? (ItemID.TitaniumSword, ItemID.TitaniumMask, ItemID.TitaniumBreastplate, ItemID.TitaniumLeggings, ItemID.HealingPotion, 400)
				: (ItemID.LightsBane, ItemID.PlatinumHelmet, ItemID.PlatinumChainmail, ItemID.PlatinumGreaves, ItemID.HealingPotion, 240),
		};

		/// <summary>Puts on the typical kit for <paramref name="boss"/>, backing up the player's own things first.</summary>
		public string Apply(int boss)
		{
			if (!Testing)
			{
				inventory = Player.inventory.Select(i => i.Clone()).ToArray();
				armor = Player.armor.Select(i => i.Clone()).ToArray();
				crystals = Player.ConsumedLifeCrystals;
				fruit = Player.ConsumedLifeFruit;
				life = Player.statLife;
				lifeMax = Player.statLifeMax;
			}
			var (weapon, head, body, legs, potion, _) = Kit(boss);
			var (_, hp, defense) = BattleSystem.BossStage(boss);
			// Max HP: 100, plus 20 a Life Crystal (up to 15), plus 5 a Life Fruit
			int wantHp = (int)hp;
			Player.ConsumedLifeCrystals = Math.Clamp((wantHp - 100) / 20, 0, Player.LifeCrystalMax);
			Player.ConsumedLifeFruit = Math.Clamp((wantHp - 400) / 5, 0, Player.LifeFruitMax);
			for (int i = 0; i < Player.inventory.Length; i++)
				Player.inventory[i] = new Item();
			for (int i = 0; i < Player.armor.Length; i++)
				Player.armor[i] = new Item();
			Player.armor[0].SetDefaults(head);
			Player.armor[1].SetDefaults(body);
			Player.armor[2].SetDefaults(legs);
			Player.inventory[0].SetDefaults(weapon);
			Player.inventory[1].SetDefaults(potion);
			Player.inventory[1].stack = 5;
			Player.selectedItem = 0;
			Player.statLifeMax = 100 + Player.ConsumedLifeCrystals * 20 + Player.ConsumedLifeFruit * 5;
			Player.statLife = Player.statLifeMax;
			Player.ClearBuff(BuffID.PotionSickness);
			Player.potionDelay = 0;
			SyncGear();
			return $"{Lang.GetItemNameValue(weapon)}, {Lang.GetItemNameValue(head).Split(' ')[0]} armour, 5 {Lang.GetItemNameValue(potion)}, {Player.statLifeMax} HP (typical: {hp:0} HP, {defense:0} defense)";
		}

		/// <summary>Gives the player's own things back, if a test kit is on.</summary>
		public void Restore()
		{
			if (!Testing)
				return;
			for (int i = 0; i < Player.inventory.Length && i < inventory.Length; i++)
				Player.inventory[i] = inventory[i];
			for (int i = 0; i < Player.armor.Length && i < armor.Length; i++)
				Player.armor[i] = armor[i];
			Player.ConsumedLifeCrystals = crystals;
			Player.ConsumedLifeFruit = fruit;
			Player.statLifeMax = lifeMax > 0 ? lifeMax : 100 + crystals * 20 + fruit * 5;
			Player.statLife = Math.Clamp(life, 1, Player.statLifeMax);
			inventory = armor = null;
			SyncGear();
			if (Player.whoAmI == Main.myPlayer)
				Main.NewText("* Your own gear is back.", MercyMode.Gray);
		}

		/// <summary>
		/// Multiplayer: tells everyone the gear changed (worn armour shows on the character and counts for its
		/// defense right away; singleplayer needs nothing, the armour slots are what's worn).
		/// </summary>
		private void SyncGear()
		{
			if (Main.netMode != NetmodeID.MultiplayerClient || Player.whoAmI != Main.myPlayer)
				return;
			for (int i = 0; i < Player.inventory.Length; i++)
				NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, Player.whoAmI, PlayerItemSlotID.Inventory0 + i);
			for (int i = 0; i < Player.armor.Length; i++)
				NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, Player.whoAmI, PlayerItemSlotID.Armor0 + i);
			NetMessage.SendData(MessageID.PlayerLifeMana, -1, -1, null, Player.whoAmI);
		}

		// The backup goes into the save while a test is on, so it survives a crash or an autosave mid-battle
		public override void SaveData(TagCompound tag)
		{
			if (!Testing)
				return;
			tag["testInventory"] = inventory.Select(ItemIO.Save).ToList();
			tag["testArmor"] = armor.Select(ItemIO.Save).ToList();
			tag["testCrystals"] = crystals;
			tag["testFruit"] = fruit;
			tag["testLife"] = life;
			tag["testLifeMax"] = lifeMax;
		}

		public override void LoadData(TagCompound tag)
		{
			if (!tag.ContainsKey("testInventory"))
				return;
			inventory = tag.GetList<TagCompound>("testInventory").Select(ItemIO.Load).ToArray();
			armor = tag.GetList<TagCompound>("testArmor").Select(ItemIO.Load).ToArray();
			crystals = tag.GetInt("testCrystals");
			fruit = tag.GetInt("testFruit");
			life = tag.GetInt("testLife");
			lifeMax = tag.GetInt("testLifeMax");
		}

		// Loaded with a test kit still on (the game closed mid-test): put their own things back
		public override void OnEnterWorld() => Restore();
	}
}
