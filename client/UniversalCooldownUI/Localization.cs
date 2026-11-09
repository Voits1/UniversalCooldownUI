using System;
using System.Collections.Generic;
using System.Globalization;

namespace UniversalCooldownUI
{
    /// <summary>
    /// Client side localization (English / Russian).
    /// The server sends locale neutral keys (enum names + numbers), everything the
    /// player reads is produced here.
    /// </summary>
    internal static class Localization
    {
        internal enum Lang
        {
            English,
            Russian
        }

        internal static Lang Current = Lang.English;

        internal static void Init(string configured)
        {
            string value = (configured ?? "auto").Trim().ToLowerInvariant();

            switch (value)
            {
                case "en":
                case "english":
                    Current = Lang.English;
                    return;
                case "ru":
                case "russian":
                    Current = Lang.Russian;
                    return;
            }

            // auto: follow the game/system UI language
            try
            {
                string two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                Current = two == "ru" ? Lang.Russian : Lang.English;
            }
            catch
            {
                Current = Lang.English;
            }
        }

        static bool Ru => Current == Lang.Russian;

        // ---------- window / tabs ----------

        internal static string TabTitle => "Blood";

        internal static string SectionTitle(string kind, string arg)
        {
            switch (kind)
            {
                case "general":
                    return Ru ? "Общие" : "General";
                case "class":
                    return string.Format(Ru ? "Класс: {0} (синергии)" : "Class: {0} (synergies)", ClassName(arg));
                case "blood":
                    return string.Format(Ru ? "Кровь: {0}" : "Blood: {0}", BloodName(arg));
                case "weapon":
                    return string.Format(Ru ? "Оружие: {0}" : "Weapon: {0}", WeaponName(arg));
                default:
                    return kind;
            }
        }

        internal static string EmptyHint(string kind)
        {
            switch (kind)
            {
                case "general":
                    return Ru ? "Постоянных баффов нет" : "No permanent buffs";
                case "class":
                    return Ru ? "Класс не выбран" : "No class selected";
                case "blood":
                    return Ru ? "Бонусы крови не выбраны" : "No blood bonuses selected";
                case "weapon":
                    return Ru ? "Опыт оружия не выбран" : "No weapon stats selected";
                default:
                    return Ru ? "Нет данных" : "No data";
            }
        }

        internal static string Loading => Ru ? "Данные загружаются…" : "Loading server data…";
        internal static string LoadingHint => Ru ? "подождите пару секунд" : "a few seconds";

        internal static string Shroud => Ru ? "Плащ крови" : "Blood shroud";
        internal static string ShroudOn => Ru ? "включён" : "enabled";
        internal static string PrestigeName(string prefab) => Humanize(prefab);
        internal static string PrestigeLevel(int level) => Ru ? $"ур. {level}" : $"lvl {level}";

        // ---------- values ----------

        /// <summary>Stats the game shows in percent; everything else is a flat number.</summary>
        internal static bool IsPercent(string key)
        {
            switch (key)
            {
                case "MaxHealth":
                case "PhysicalPower":
                case "SpellPower":
                    return false;
                default:
                    return true;
            }
        }

        internal static string FormatStat(string key, float value)
        {
            if (IsPercent(key))
            {
                return "+" + (value * 100f).ToString("0.#", NumberFormat) + "%";
            }


            return "+" + value.ToString("0.#", NumberFormat);
        }

        internal static string FormatMultiplier(float value)
        {
            return "×" + value.ToString("0.##", NumberFormat);
        }

        static CultureInfo NumberFormat => Ru ? CultureInfo.GetCultureInfo("ru-RU") : CultureInfo.InvariantCulture;

        // ---------- stat names ----------

        internal static string Stat(string key)
        {
            if (Ru) return StatRu.TryGetValue(key, out string ru) ? ru : Humanize(key);
            return StatEn.TryGetValue(key, out string en) ? en : Humanize(key);
        }

        static readonly Dictionary<string, string> StatEn = new()
        {
            ["MaxHealth"] = "Max health",
            ["MovementSpeed"] = "Movement speed",
            ["PrimaryAttackSpeed"] = "Attack speed",
            ["PhysicalLifeLeech"] = "Physical life leech",
            ["SpellLifeLeech"] = "Spell life leech",
            ["PrimaryLifeLeech"] = "Primary attack leech",
            ["PhysicalPower"] = "Physical power",
            ["SpellPower"] = "Spell power",
            ["PhysicalCritChance"] = "Physical crit chance",
            ["PhysicalCritDamage"] = "Physical crit damage",
            ["SpellCritChance"] = "Spell crit chance",
            ["SpellCritDamage"] = "Spell crit damage",
            ["HealingReceived"] = "Healing received",
            ["DamageReduction"] = "Damage reduction",
            ["PhysicalResistance"] = "Physical resistance",
            ["SpellResistance"] = "Spell resistance",
            ["ResourceYield"] = "Resource yield",
            ["ReducedBloodDrain"] = "Blood drain reduction",
            ["SpellCooldownRecoveryRate"] = "Spell cooldown rate",
            ["WeaponCooldownRecoveryRate"] = "Weapon cooldown rate",
            ["UltimateCooldownRecoveryRate"] = "Ultimate cooldown rate",
            ["MinionDamage"] = "Minion damage",
            ["AbilityAttackSpeed"] = "Ability attack speed",
            ["CorruptionDamageReduction"] = "Corruption resistance"
        };

        static readonly Dictionary<string, string> StatRu = new()
        {
            ["MaxHealth"] = "Макс. здоровье",
            ["MovementSpeed"] = "Скорость движения",
            ["PrimaryAttackSpeed"] = "Скорость атаки",
            ["PhysicalLifeLeech"] = "Вампиризм (физ.)",
            ["SpellLifeLeech"] = "Вампиризм (маг.)",
            ["PrimaryLifeLeech"] = "Вампиризм осн. атаки",
            ["PhysicalPower"] = "Физическая сила",
            ["SpellPower"] = "Сила магии",
            ["PhysicalCritChance"] = "Шанс крит. удара (физ.)",
            ["PhysicalCritDamage"] = "Крит. урон (физ.)",
            ["SpellCritChance"] = "Шанс крит. удара (маг.)",
            ["SpellCritDamage"] = "Крит. урон (маг.)",
            ["HealingReceived"] = "Исцеление",
            ["DamageReduction"] = "Снижение урона",
            ["PhysicalResistance"] = "Сопротивление физ.",
            ["SpellResistance"] = "Сопротивление маг.",
            ["ResourceYield"] = "Добыча",
            ["ReducedBloodDrain"] = "Расход крови",
            ["SpellCooldownRecoveryRate"] = "КД заклинаний",
            ["WeaponCooldownRecoveryRate"] = "КД оружия",
            ["UltimateCooldownRecoveryRate"] = "КД ультимейта",
            ["MinionDamage"] = "Урон приспешников",
            ["AbilityAttackSpeed"] = "Скорость способностей",
            ["CorruptionDamageReduction"] = "Сопротивление порче"
        };

        // ---------- classes / blood / weapons ----------

        internal static string ClassName(string key)
        {
            if (string.IsNullOrEmpty(key) || key == "none") return Ru ? "не выбран" : "none";

            var map = Ru ? ClassRu : ClassEn;
            return map.TryGetValue(key, out string name) ? name : Humanize(key);
        }

        static readonly Dictionary<string, string> ClassEn = new()
        {
            ["BloodKnight"] = "Blood Knight",
            ["DemonHunter"] = "Demon Hunter",
            ["VampireLord"] = "Vampire Lord",
            ["ShadowBlade"] = "Shadow Blade",
            ["ArcaneSorcerer"] = "Arcane Sorcerer",
            ["DeathMage"] = "Death Mage"
        };

        static readonly Dictionary<string, string> ClassRu = new()
        {
            ["BloodKnight"] = "Кровавый рыцарь",
            ["DemonHunter"] = "Охотник на демонов",
            ["VampireLord"] = "Владыка вампиров",
            ["ShadowBlade"] = "Теневая грань",
            ["ArcaneSorcerer"] = "Тайный чародей",
            ["DeathMage"] = "Маг смерти"
        };

        internal static string BloodName(string key)
        {
            if (string.IsNullOrEmpty(key) || key == "none") return Ru ? "нет" : "none";

            var map = Ru ? BloodRu : BloodEn;
            return map.TryGetValue(key, out string name) ? name : Humanize(key);
        }

        static readonly Dictionary<string, string> BloodEn = new()
        {
            ["Worker"] = "Worker",
            ["Warrior"] = "Warrior",
            ["Scholar"] = "Scholar",
            ["Rogue"] = "Rogue",
            ["Mutant"] = "Mutant",
            ["VBlood"] = "V Blood",
            ["None"] = "None",
            ["GateBoss"] = "Gate boss",
            ["Draculin"] = "Draculin",
            ["Immortal"] = "Immortal",
            ["Creature"] = "Creature",
            ["Brute"] = "Brute",
            ["Corruption"] = "Corruption"
        };

        static readonly Dictionary<string, string> BloodRu = new()
        {
            ["Worker"] = "Рабочая",
            ["Warrior"] = "Воительская",
            ["Scholar"] = "Учёная",
            ["Rogue"] = "Разбойничья",
            ["Mutant"] = "Мутантская",
            ["VBlood"] = "Кровь V",
            ["None"] = "Нет",
            ["GateBoss"] = "Кровь стража",
            ["Draculin"] = "Дракулинская",
            ["Immortal"] = "Бессмертная",
            ["Creature"] = "Звериная",
            ["Brute"] = "Грубая",
            ["Corruption"] = "Порченая"
        };

        internal static string WeaponName(string key)
        {
            if (string.IsNullOrEmpty(key)) return Ru ? "нет" : "none";

            var map = Ru ? WeaponRu : WeaponEn;
            return map.TryGetValue(key, out string name) ? name : Humanize(key);
        }

        static readonly Dictionary<string, string> WeaponEn = new()
        {
            ["Sword"] = "Sword",
            ["Axe"] = "Axe",
            ["Mace"] = "Mace",
            ["Spear"] = "Spear",
            ["Crossbow"] = "Crossbow",
            ["GreatSword"] = "Greatsword",
            ["Slashers"] = "Slashers",
            ["Pistols"] = "Pistols",
            ["Reaper"] = "Reaper",
            ["Longbow"] = "Longbow",
            ["Whip"] = "Whip",
            ["Unarmed"] = "Unarmed",
            ["FishingPole"] = "Fishing pole",
            ["TwinBlades"] = "Twin blades",
            ["Daggers"] = "Daggers",
            ["Claws"] = "Claws"
        };

        static readonly Dictionary<string, string> WeaponRu = new()
        {
            ["Sword"] = "Меч",
            ["Axe"] = "Топор",
            ["Mace"] = "Булава",
            ["Spear"] = "Копьё",
            ["Crossbow"] = "Арбалет",
            ["GreatSword"] = "Двуручный меч",
            ["Slashers"] = "Клинки",
            ["Pistols"] = "Пистолеты",
            ["Reaper"] = "Коса",
            ["Longbow"] = "Длинный лук",
            ["Whip"] = "Хлыст",
            ["Unarmed"] = "Без оружия",
            ["FishingPole"] = "Удочка",
            ["TwinBlades"] = "Парные клинки",
            ["Daggers"] = "Кинжалы",
            ["Claws"] = "Когти"
        };

        /// <summary>Turns "PhysicalCritDamage" into "Physical Crit Damage".</summary>
        internal static string Humanize(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            var sb = new System.Text.StringBuilder(name.Length + 6);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) sb.Append(' ');
                sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
