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

		/// <summary>What a typical player brings to each boss: weapon, armour (head, body, legs), healing potion.</summary>
		private static (int weapon, int head, int body, int legs, int potion) Kit(int boss) => boss switch
		{
			NPCID.KingSlime => (ItemID.GoldBroadsword, ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves, ItemID.LesserHealingPotion),
			NPCID.EyeofCthulhu => (ItemID.PlatinumBroadsword, ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves, ItemID.LesserHealingPotion),
			NPCID.BrainofCthulhu or NPCID.EaterofWorldsHead or NPCID.EaterofWorldsBody or NPCID.EaterofWorldsTail
				=> (ItemID.LightsBane, ItemID.SilverHelmet, ItemID.SilverChainmail, ItemID.SilverGreaves, ItemID.LesserHealingPotion),
			NPCID.QueenBee or NPCID.Deerclops => (ItemID.BloodButcherer, ItemID.GoldHelmet, ItemID.GoldChainmail, ItemID.GoldGreaves, ItemID.HealingPotion),
			NPCID.SkeletronHead => (ItemID.BladeofGrass, ItemID.ShadowHelmet, ItemID.ShadowScalemail, ItemID.ShadowGreaves, ItemID.HealingPotion),
			NPCID.WallofFlesh => (ItemID.FieryGreatsword, ItemID.MoltenHelmet, ItemID.MoltenBreastplate, ItemID.MoltenGreaves, ItemID.HealingPotion),
			NPCID.QueenSlimeBoss or NPCID.Retinazer or NPCID.Spazmatism or NPCID.TheDestroyer or NPCID.SkeletronPrime
				=> (ItemID.AdamantiteSword, ItemID.AdamantiteHelmet, ItemID.AdamantiteBreastplate, ItemID.AdamantiteLeggings, ItemID.HealingPotion),
			NPCID.Plantera => (ItemID.TrueExcalibur, ItemID.HallowedMask, ItemID.HallowedPlateMail, ItemID.HallowedGreaves, ItemID.GreaterHealingPotion),
			NPCID.Golem or NPCID.GolemHead => (ItemID.TerraBlade, ItemID.TurtleHelmet, ItemID.TurtleScaleMail, ItemID.TurtleLeggings, ItemID.GreaterHealingPotion),
			NPCID.DukeFishron or NPCID.HallowBoss or NPCID.CultistBoss
				=> (ItemID.InfluxWaver, ItemID.TurtleHelmet, ItemID.TurtleScaleMail, ItemID.TurtleLeggings, ItemID.GreaterHealingPotion),
			NPCID.MoonLordCore or NPCID.MoonLordHand or NPCID.MoonLordHead
				=> (ItemID.InfluxWaver, ItemID.SolarFlareHelmet, ItemID.SolarFlareBreastplate, ItemID.SolarFlareLeggings, ItemID.GreaterHealingPotion),
			_ => Main.hardMode
				? (ItemID.AdamantiteSword, ItemID.AdamantiteHelmet, ItemID.AdamantiteBreastplate, ItemID.AdamantiteLeggings, ItemID.HealingPotion)
				: (ItemID.BloodButcherer, ItemID.GoldHelmet, ItemID.GoldChainmail, ItemID.GoldGreaves, ItemID.HealingPotion),
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
			var (weapon, head, body, legs, potion) = Kit(boss);
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
			if (Player.whoAmI == Main.myPlayer)
				Main.NewText("* Your own gear is back.", MercyMode.Gray);
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
