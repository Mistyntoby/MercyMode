using System.Collections.Generic;
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
	}
}
