﻿using System;
using System.Runtime.Serialization;
using PacManBot.Extensions;
using PacManBot.Games.Concrete.Rpg.Buffs;

namespace PacManBot.Games.Concrete.Rpg.Enemies
{
    public class Skeleton : Enemy
    {
        public override string Name => "Skeleton";
        public override string Description => "Only the first of many.";
        public override int Level => 3;
        public override int ExpYield => 3;
        public override int BaseDamage => 5;
        public override int BaseDefense => 1;
        public override double BaseCritChance => 0.2;

        public override void SetStats()
        {
            MaxLife = 26;
            DamageType = DamageType.Pierce;
            DamageResistance[DamageType.Pierce] = 0.2;
        }
    }


    public class Skeleton2 : Enemy
    {
        public override string Name => "Skellington";
        public override string Description => "A skeleton's flamboyant brother.";
        public override int Level => 8;
        public override int ExpYield => 7;
        public override int BaseDamage => 10;
        public override int BaseDefense => 2;
        public override double BaseCritChance => 0.2;

        public override void SetStats()
        {
            MaxLife = 42;
            DamageType = DamageType.Cutting;
            DamageResistance[DamageType.Pierce] = 0.2;
        }
    }


    public class Skeleton3 : Enemy
    {
        public override string Name => "Scarington";
        public override string Description => "😱💀";
        public override int Level => 11;
        public override int ExpYield => 7;
        public override int BaseDamage => 20;
        public override int BaseDefense => -3;
        public override double BaseCritChance => 0.1;

        public override void SetStats()
        {
            MaxLife = 27;
            DamageType = DamageType.Pierce;
            DamageResistance[DamageType.Pierce] = -0.2;
        }
    }


    public class Skeleton4 : Enemy
    {
        public override string Name => "Swoleton";
        public override string Description => "Milk makes your bones strong!";
        public override int Level => 15;
        public override int ExpYield => 9;
        public override int BaseDamage => 15;
        public override int BaseDefense => 1;
        public override double BaseCritChance => 0.02;

        [DataMember] private bool milk = false;

        public override void SetStats()
        {
            MaxLife = 60;
            DamageType = DamageType.Blunt;
            DamageResistance[DamageType.Blunt] = 0.3;
        }

        public override string Attack(Entity target)
        {
            string msg = "";
            if (Life < MaxLife / 2 && !milk)
            {
                milk = true;
                msg = $"{Name} drank some milk and became stronger!\n";
                Life += MaxLife / 2;
                MaxLife += MaxLife / 2;
            }

            if (milk)
            {
                Damage += 4;
                Defense += 4;
            }

            return msg + base.Attack(target);
        }
    }


    public class SkeletonKing : Enemy
    {
        public override string Name => "Skeleton King";
        public override string Description => "He's actually just a count,\nbut don't tell him.";
        public override int Level => 20;
        public override int ExpYield => 10;
        public override int BaseDamage => 26;
        public override int BaseDefense => 2;
        public override double BaseCritChance => 0.01;

        public override void SetStats()
        {
            MaxLife = 100;
            DamageType = DamageType.Blunt;
            DamageResistance[DamageType.Magic] = 0.15;
        }

        public override string Attack(Entity target)
        {
            string msg = "";
            if (Program.Random.OneIn(4))
            {
                msg = $"{target} is overwhelmed!";
                target.AddBuff<Burn>(2);
                target.AddBuff<Vulnerable>(2);
                target.AddBuff<Wet>(2);
            }
            return base.Attack(target) + msg;
        }
    }


    public class Skeleton5 : Enemy
    {
        public override string Name => "Swingeton";
        public override string Description => "Its dance is unpredictable.";
        public override int Level => 30;
        public override int ExpYield => 16;
        public override int BaseDamage => 999;
        public override int BaseDefense => 6;
        public override double BaseCritChance => 0;

        public override void SetStats()
        {
            MaxLife = 74;
            DamageType = DamageType.Pierce;
            DamageResistance[DamageType.Pierce] = 0.1;
        }

        public override string Attack(Entity target)
        {
            Damage = Program.Random.Next(10, 45);
            string msg = base.Attack(target);
            Damage = BaseDamage;
            return msg;
        }
    }


    public class Skeleton6 : Enemy
    {
        public override string Name => "Skeleton Jones";
        public override string Description => "A fierce, dead pirate.";
        public override int Level => 40;
        public override int ExpYield => 20;
        public override int BaseDamage => 30;
        public override int BaseDefense => 8;
        public override double BaseCritChance => 0.05;

        public override void SetStats()
        {
            MaxLife = 120;
            DamageType = DamageType.Cutting;
            MagicType = MagicType.Water;
            MagicResistance[MagicType.Water] = 0.25;
        }

        public override string Attack(Entity target)
        {
            string msg = "";
            if (!HasBuff<Fury>() && Program.Random.OneIn(3))
            {
                AddBuff<Fury>(3);
                msg = $"{this} is going berzerk!\n";
            }
            return msg + base.Attack(target);
        }
    }


    public class SkeletonLich : Enemy
    {
        public override string Name => "Skeleton Lich";
        public override string Description => "Evil dark lord of bones.";
        public override int Level => 55;
        public override int ExpYield => 30;
        public override int BaseDamage => 70;
        public override int BaseDefense => 15;
        public override double BaseCritChance => 0.05;

        [DataMember] private int summonCounter = 1;
        [DataMember] private int summons = 0;

        public override void SetStats()
        {
            MaxLife = 300;
            DamageType = DamageType.Magic;
            DamageResistance[DamageType.Magic] = 0.2;
        }

        public override string Attack(Entity target)
        {
            string message = base.Attack(target);

            if (++summonCounter % 3 == 0)
            {
                int heal = 30;
                MaxLife += heal;
                Life += heal;
                if (summons++ == 0) message += $"\nThe Lich summons skeletons to aid him! +{heal} HP";
                else message += $"\nThe Lich summons more skeletons! +{heal} HP";
            }
            if (summons > 0)
            {
                int dmg = (15 * summons * Program.Random.NextDouble(0.8, 1.2)).Ceiling();
                target.Life -= dmg;
                message += $"\nThe Lich's army attacks for {dmg} HP!\n";
            }

            return message;
        }
    }
}
