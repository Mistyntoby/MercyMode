using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using System.Linq;

namespace MercyMode.Battle
{
	/// <summary>Read-only views of the battle's state for the headless lab (<see cref="MercyMode.Lab.LabSystem"/>).</summary>
	public partial class BattleSystem
	{
		internal Phase LabPhase => phase;
		internal int LabMenuChoice => (int)menuChoice;
		internal int LabListIndex => listIndex;
		internal string LabText => text;
		internal Encounter LabTarget => targetEnemy?.E;
		internal List<(Encounter E, bool Out)> LabEnemies => enemies.Select(e => (e.E, e.Out)).ToList();
		internal int LabWeaponCount => weaponOptions.Count;
		internal Vector2 LabSoul => soul;
		internal int LabShieldDir => shieldDir;
		internal int LabYellowShots => yellowShots.Count;
		internal int LabBlocks, LabBroken;
		internal string LabDeath => deathReason == null ? "?" : $"{deathReason.GetDeathText(Player.name)} ({deathDamage:0} damage)";
		/// <summary>The attack this enemy turn is running (unwrapped).</summary>
		internal EnemyAttack LabAttack => attack is OwnedAttack o ? o.Inner : attack;
		/// <summary>When set, every enemy turn runs this attack instead (lab tests of one pattern).</summary>
		internal static Func<EnemyAttack> LabForcedAttack;
	}
}
