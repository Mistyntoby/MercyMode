using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;

namespace MercyMode.Battle
{
	/// <summary>One entry in the ACT menu.</summary>
	public class ActOption
	{
		public string Name;
		/// <summary>Short text shown on the right while the act is highlighted.</summary>
		public string Description = "";
		/// <summary>TP cost in Mercy Mode's 0-100 TP. 0 for normal acts.</summary>
		public float TPCost;
		/// <summary>Runs the act and returns the lines to show. Each string is one text box.</summary>
		public Func<BattleSystem, List<string>> Run;
	}

	/// <summary>
	/// An enemy turn: spawns bullets while the turn timer runs. Update is called once per tick with the tick
	/// count since the box finished growing.
	/// </summary>
	public abstract class EnemyAttack
	{
		/// <summary>Turn length in ticks (global.turntimer).</summary>
		public int Duration = BattleConstants.DefaultEnemyTurnTicks;
		public abstract void Update(BattleSystem battle, int tick);
	}

	/// <summary>Per-boss battle content: name, acts, text and bullet patterns.</summary>
	public abstract class BossBattle
	{
		public NPC Npc;
		public int Turn;
		/// <summary>How many times each act has been used, by name.</summary>
		public readonly Dictionary<string, int> ActUses = new();

		/// <summary>Upper-case name like Deltarune shows it.</summary>
		public abstract string Name { get; }
		public abstract string EncounterText { get; }
		/// <summary>The line shown in the text box at the start of a player turn.</summary>
		public abstract string FlavorText();
		public abstract List<ActOption> Acts(BattleSystem battle);
		public abstract EnemyAttack NextAttack(BattleSystem battle);

		/// <summary>Where the enemy is drawn on the battle screen (its centre).</summary>
		public virtual Vector2 DrawCenter => new(500, 190);
		/// <summary>Scale for the boss's Terraria sprite on the battle screen.</summary>
		public virtual float DrawScale => 1f;
		/// <summary>Extra rotation for the boss sprite on the battle screen.</summary>
		public virtual float DrawRotation(int time) => 0f;

		/// <summary>Adds MERCY, halving it each time the same act is repeated. Returns what was actually gained.</summary>
		protected float GainMercy(string actName, float amount)
		{
			ActUses.TryGetValue(actName, out int uses);
			ActUses[actName] = uses + 1;
			float gain = amount / (1 << Math.Min(uses, 4));
			var g = Npc.GetGlobalNPC<MercyGlobalNPC>();
			float before = g.Mercy;
			g.Mercy = Math.Min(100f, g.Mercy + gain);
			return g.Mercy - before;
		}

		/// <summary>The line Deltarune adds when an enemy becomes spareable.</summary>
		protected string SpareableLine => $"* {Name} doesn't want to fight anymore.";

		public float Mercy => Npc.GetGlobalNPC<MercyGlobalNPC>().Mercy;

		/// <summary>Bosses with a battle. Everything else keeps the real-time fight.</summary>
		public static BossBattle Create(NPC npc)
		{
			BossBattle b = npc.type switch
			{
				NPCID.EyeofCthulhu => new Bosses.EyeOfCthulhuBattle(),
				_ => null,
			};
			if (b != null)
				b.Npc = npc;
			return b;
		}

		public static bool HasBattle(NPC npc) => npc.type == NPCID.EyeofCthulhu;
	}
}
