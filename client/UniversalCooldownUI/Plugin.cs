using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using TMPro;

namespace UniversalCooldownUI
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BasePlugin
    {
        public const string PluginGuid = "universalcooldown.ui";
        public const string PluginName = "Universal Cooldown UI";
        public const string PluginVersion = "1.0.2";

        internal static ManualLogSource L;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Language;
        internal static ConfigEntry<bool> BloodTab;
        internal static ConfigEntry<bool> Probe;
        internal static ConfigEntry<float> SpellRate, WeaponRate, UltimateRate;
        internal static ConfigEntry<string> RatesFile;

        public override void Load()
        {
            L = Log;

            Enabled = Config.Bind("General", "Enabled", true, "Show Bloodcraft bonuses in tooltips and the character window.");
            Language = Config.Bind("General", "Language", "auto", "UI language: auto, en or ru.");
            BloodTab = Config.Bind("Tab", "BloodTab", true, "Add the 'Blood' tab to the character window.");

            SpellRate = Config.Bind("Rates", "Spell", 0.14f, "Spell cooldown rate fallback (used when the server file is missing).");
            WeaponRate = Config.Bind("Rates", "Weapon", 0.21f, "Weapon cooldown rate fallback.");
            UltimateRate = Config.Bind("Rates", "Ultimate", 0.14f, "Ultimate cooldown rate fallback.");

            RatesFile = Config.Bind("Sync", "RatesFile", "",
                "Server file with cooldown rates (universalcooldown_rates.txt). Empty = BepInEx/config/<file>. " +
                "The Blood tab data files are read from the same folder.");

            Probe = Config.Bind("Debug", "ProbeMenu", false, "Dump the character window hierarchy into the log.");

            Localization.Init(Language.Value);

            Harmony harmony = new("local.universalcooldownui");

            MethodInfo prop = typeof(TooltipPatch).GetMethod(nameof(TooltipPatch.ApplyFromProperty), BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo textMethod = typeof(TooltipPatch).GetMethod(nameof(TooltipPatch.ApplyFromText), BindingFlags.Static | BindingFlags.NonPublic);

            try
            {
                MethodInfo setter = typeof(TMP_Text).GetProperty("text")?.GetSetMethod(true);
                if (setter != null) harmony.Patch(setter, postfix: new HarmonyMethod(prop));
            }
            catch (Exception e)
            {
                L.LogWarning("text setter patch failed: " + e.Message);
            }

            int patched = 0;
            foreach (MethodInfo method in typeof(TMP_Text).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name != "SetText") continue;
                try { harmony.Patch(method, postfix: new HarmonyMethod(textMethod)); patched++; } catch { }
            }

            MenuProbe.Install(harmony);

            L.LogInfo($"UniversalCooldownUI loaded (lang={Localization.Current}), SetText patched: {patched}");
        }

        // ---------- server rates ----------

        internal static float ServerSpell = -1f, ServerWeapon = -1f, ServerUltimate = -1f;
        internal static float ServerPhysPower, ServerSpellPower;
        internal static Dictionary<string, float> ServerExtraStats = new();
        static DateTime _lastRead = DateTime.MinValue;

        /// <summary>Configured rates file, or BepInEx/config/universalcooldown_rates.txt.</summary>
        internal static string RatesPath()
        {
            string configured = RatesFile.Value;
            if (!string.IsNullOrWhiteSpace(configured)) return configured;

            return Path.Combine(Paths.ConfigPath, "universalcooldown_rates.txt");
        }

        /// <summary>Folder that holds the server files (rates + Blood tab data).</summary>
        internal static string SyncDirectory()
        {
            try
            {
                string dir = Path.GetDirectoryName(RatesPath());
                if (!string.IsNullOrEmpty(dir)) return dir;
            }
            catch { }

            return Paths.ConfigPath;
        }

        internal static void TryReadServerRates()
        {
            if ((DateTime.UtcNow - _lastRead).TotalSeconds < 3) return;
            _lastRead = DateTime.UtcNow;

            try
            {
                string path = RatesPath();
                if (!File.Exists(path)) return;

                string[] parts = File.ReadAllText(path).Trim().Split('|');
                if (parts.Length < 3) return;

                if (float.TryParse(parts[0].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float s)
                    && float.TryParse(parts[1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float w)
                    && float.TryParse(parts[2].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float u))
                {
                    ServerSpell = s;
                    ServerWeapon = w;
                    ServerUltimate = u;
                }

                if (parts.Length >= 5)
                {
                    if (float.TryParse(parts[3].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float pp)) ServerPhysPower = pp;
                    if (float.TryParse(parts[4].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float sp)) ServerSpellPower = sp;
                }

                if (parts.Length >= 6 && !string.IsNullOrWhiteSpace(parts[5]))
                {
                    Dictionary<string, float> parsed = new();

                    foreach (string entry in parts[5].Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        int idx = entry.LastIndexOf(':');
                        if (idx <= 0) continue;

                        if (float.TryParse(entry.Substring(idx + 1).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                        {
                            parsed[entry.Substring(0, idx)] = value;
                        }
                    }

                    ServerExtraStats = parsed;
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Rewrites cooldown numbers in ability tooltips and appends the Bloodcraft bonus
    /// block to weapon tooltips.
    /// </summary>
    internal static class TooltipPatch
    {
        static readonly Regex CooldownRegex = new(@"(?<num>\d+(?:[.,]\d+)?)(?<tail>\s*(?:сек|sec|s)\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        [ThreadStatic] static bool _busy;

        internal static void ApplyFromProperty(TMP_Text __instance, string value) => Apply(__instance, value);
        internal static void ApplyFromText(TMP_Text __instance) => Apply(__instance, __instance?.text);

        static void Apply(TMP_Text __instance, string value)
        {
            if (_busy || !Plugin.Enabled.Value || string.IsNullOrEmpty(value)) return;
            if (value.Contains("#8fd6ff")) return;

            string lowered = value.ToLowerInvariant();
            bool isCooldown = lowered.Contains("перезарядк") || lowered.Contains("cooldown") || lowered.Contains("recharge");
            bool isWeapon = lowered.Contains("атрибуты") && (lowered.Contains("прочность") || lowered.Contains("durability"))
                            || lowered.Contains("attributes") && lowered.Contains("durability");

            if (!isCooldown && !isWeapon) return;

            Plugin.TryReadServerRates();

            float rate = Plugin.ServerSpell >= 0f ? Plugin.ServerSpell : Plugin.SpellRate.Value;

            if (lowered.Contains("ульт") || lowered.Contains("ultimate")) rate = Plugin.ServerUltimate >= 0f ? Plugin.ServerUltimate : Plugin.UltimateRate.Value;
            else if (lowered.Contains("оруж") || lowered.Contains("weapon") || lowered.Contains("основная атака")) rate = Plugin.ServerWeapon >= 0f ? Plugin.ServerWeapon : Plugin.WeaponRate.Value;

            string result = value;

            if (isCooldown && rate > 0.0005f)
            {
                result = CooldownRegex.Replace(result, match =>
                {
                    if (!float.TryParse(match.Groups["num"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
                        return match.Value;

                    // recovery rate, not linear reduction: 100% rate = twice as fast
                    float reduced = number / (1f + rate);

                    string formatted = reduced >= 10f ? reduced.ToString("0.#") : reduced.ToString("0.##");
                    return formatted + match.Groups["tail"].Value;
                });
            }

            if (isWeapon && Plugin.ServerExtraStats.Count > 0)
            {
                StringBuilder bonus = new("\n<color=#8fd6ff>");
                int shown = 0;

                foreach (KeyValuePair<string, float> pair in Plugin.ServerExtraStats)
                {
                    if (pair.Value <= 0f) continue;

                    if (shown > 0) bonus.Append('\n');
                    bonus.Append(Localization.Stat(pair.Key)).Append(": ").Append(Localization.FormatStat(pair.Key, pair.Value));
                    shown++;

                    if (shown >= 6) break;
                }

                bonus.Append("</color>");
                if (shown > 0) result += bonus.ToString();
            }

            if (result == value) return;

            try
            {
                _busy = true;
                __instance.text = result;
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
