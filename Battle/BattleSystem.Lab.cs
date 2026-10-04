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
		internal bool LabFirstStrike => firstStrike;
		internal Encounter LabTarget => targetEnemy?.E;
		internal List<(Encounter E, bool Out)> LabEnemies => enemies.Select(e => (e.E, e.Out)).ToList();
		internal List<string> LabBubbles => enemies.Where(e => e.Living).Select(e => e.Bubble).ToList();
		internal int LabWeaponCount => weaponOptions.Count;
		internal Vector2 LabSoul => soul;
		internal int LabShieldDir => shieldDir;
		internal int LabYellowShots => yellowShots.Count;
		internal int LabBlocks, LabBroken;
		/// <summary>The enemy list as shown: (name, guarded) per row.</summary>
		internal List<(string Name, bool Locked)> LabRows => TargetRows().Select(r => (r.Part != null ? r.Enemy.E.PartName(r.Part) : r.Enemy.E.Name, r.Locked)).ToList();
		internal string LabDeath => deathReason == null ? "?" : $"{deathReason.GetDeathText(Player.name)} ({deathDamage:0} damage)";
		/// <summary>The attack this enemy turn is running (unwrapped).</summary>
		internal EnemyAttack LabAttack => attack is OwnedAttack o ? o.Inner : attack;
		/// <summary>When set, every enemy turn runs this attack instead (lab tests of one pattern).</summary>
		internal static Func<EnemyAttack> LabForcedAttack;
	}
}
