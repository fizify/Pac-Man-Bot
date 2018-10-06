﻿using PacManBot.Extensions;

namespace PacManBot.Games.Concrete.Rpg.Armors
{
    public class Clothes : Armor
    {
        public override string Name => "Clothes";
        public override string Description => "You were born with them on.";
        public override string EffectsDesc => "No effects.";
    }


    public class NoobArmor : Armor
    {
        public override string Name => "Chainmail";
        public override string Description => "Some needed basic protection.";
        public override string EffectsDesc => "+1 Defense\n+2% crit chance";

        public override int LevelGet => 7;

        public override void EquipEffects(RpgPlayer player)
        {
            player.Defense += 1;
            player.CritChance += 0.02;
        }
    }
}
