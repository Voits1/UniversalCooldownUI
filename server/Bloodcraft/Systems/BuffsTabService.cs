using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using Bloodcraft.Interfaces;
using Bloodcraft.Services;
using Bloodcraft.Systems.Expertise;
using Bloodcraft.Systems.Legacies;
using Bloodcraft.Utilities;
using ProjectM;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using static Bloodcraft.Systems.Leveling.ClassManager;
using static Bloodcraft.Utilities.Misc.PlayerBoolsManager;
using BloodType = Bloodcraft.Interfaces.BloodType;
using WeaponType = Bloodcraft.Interfaces.WeaponType;

namespace Bloodcraft.Systems;

/// <summary>
/// Writes the data behind the client "Blood" tab (character window).
///
/// One file per player: BepInEx/config/universalcooldown_buffs_&lt;steamId&gt;.txt
/// plus a generic universalcooldown_buffs.txt kept for older clients.
///
/// Line format (v2, locale neutral — the client localizes it):
///   V|2
///   S|general
///   R|shroud
///   R|prestige|&lt;prefab name&gt;|&lt;level&gt;
///   S|class|&lt;PlayerClass enum&gt;
///   R|synergy|&lt;stat enum&gt;|&lt;multiplier&gt;
///   S|blood|&lt;BloodType enum&gt;
///   R|stat|&lt;stat enum&gt;|&lt;value&gt;
///   S|weapon|&lt;WeaponType enum&gt;
///   R|stat|&lt;stat enum&gt;|&lt;value&gt;
///
/// Numbers are written with '.' as decimal separator (invariant culture).
/// </summary>
internal static class BuffsTabService
{
    public const string FilePrefix = "universalcooldown_buffs";

    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    static readonly Dictionary<ulong, string> LastWritten = new();

    /// <summary>Builds the tab payload (same text that is written to the files).</summary>
    public static string BuildText(Entity player, ulong steamId)
    {
        try
        {
            StringBuilder sb = new();
            sb.Append("V|2\n");

            // service section: cooldown rates for the client tooltips (players on other
            // machines cannot read the rates file, only chat reaches them)
            sb.Append("S|meta\n");
            sb.Append("R|rates|")
              .Append(ConfigService.SpellCooldownRecoveryRate.ToString("0.####", Invariant)).Append('|')
              .Append(ConfigService.WeaponCooldownRecoveryRate.ToString("0.####", Invariant)).Append('|')
              .Append(ConfigService.UltimateCooldownRecoveryRate.ToString("0.####", Invariant)).Append('\n');

            AppendGeneral(sb, steamId);
            AppendClass(sb, steamId);
            AppendBlood(sb, player, steamId);
            AppendWeapon(sb, player, steamId);

            return sb.ToString();
        }
        catch (Exception e)
        {
            Core.Log.LogWarning("[BuffsTab] build: " + e.Message);
            return string.Empty;
        }
    }

    /// <summary>Called whenever the player's weapon buff is (re)applied.</summary>
    public static void Write(Entity player, ulong steamId)
    {
        try
        {
            string text = BuildText(player, steamId);
            if (string.IsNullOrEmpty(text)) return;

            if (LastWritten.TryGetValue(steamId, out string previous) && text == previous) return;
            bool first = !LastWritten.ContainsKey(steamId);
            LastWritten[steamId] = text;

            string path = Path.Combine(Paths.ConfigPath, $"{FilePrefix}_{steamId}.txt");
            File.WriteAllText(path, text, new UTF8Encoding(false));

            string legacy = Path.Combine(Paths.ConfigPath, $"{FilePrefix}.txt");
            File.WriteAllText(legacy, text, new UTF8Encoding(false));

            if (first) Core.Log.LogInfo($"[BuffsTab] first data for {steamId}");

            // push straight to the client: remote clients cannot read this file, and waiting
            // for the next poll made weapon/blood changes show up a minute late
            PushToClient(player, text);
        }
        catch (Exception e)
        {
            Core.Log.LogWarning("[BuffsTab] write: " + e.Message);
        }
    }

    const int ChunkSize = 200;
    const string ChatPrefix = "UCUI";

    /// <summary>Sends the payload to the client over chat (it hides these lines).</summary>
    static void PushToClient(Entity player, string payload)
    {
        try
        {
            Entity userEntity = player.GetUserEntity();
            if (!userEntity.Exists()) return;

            string flat = payload.Replace('\n', '~');
            int total = (flat.Length + ChunkSize - 1) / ChunkSize;
            if (total <= 0) total = 1;

            for (int i = 0; i < total; i++)
            {
                int length = Math.Min(ChunkSize, flat.Length - i * ChunkSize);
                string part = length > 0 ? flat.Substring(i * ChunkSize, length) : string.Empty;

                LocalizationService.SendToClient(player, userEntity, $"{ChatPrefix}|{i + 1}|{total}|{part}");
            }
        }
        catch (Exception e)
        {
            Core.Log.LogWarning("[BuffsTab] push: " + e.Message);
        }
    }

    static DateTime _lastScan = DateTime.MinValue;

    /// <summary>
    /// Periodic refresh (blood changes when feeding, class via command, weapon via
    /// equipment), so the tab does not have to wait for the next weapon buff spawn.
    /// </summary>
    public static void Tick()
    {
        if ((DateTime.UtcNow - _lastScan).TotalSeconds < 3) return;
        _lastScan = DateTime.UtcNow;

        try
        {
            EntityQuery query = Core.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerCharacter>());
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);

            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity player = entities[i];

                    ulong steamId;
                    try { steamId = player.GetSteamId(); }
                    catch { continue; }

                    Write(player, steamId);
                }
            }
            finally
            {
                entities.Dispose();
            }
        }
        catch (Exception e)
        {
            Core.Log.LogWarning("[BuffsTab] tick: " + e.Message);
        }
    }

    static void AppendGeneral(StringBuilder sb, ulong steamId)
    {
        sb.Append("S|general\n");

        try
        {
            if (GetPlayerBool(steamId, SHROUD_KEY)) sb.Append("R|shroud\n");
        }
        catch { }

        try
        {
            if (ConfigService.PrestigeSystem && steamId.TryGetPlayerPrestiges(out var prestigeData)
                && prestigeData.TryGetValue(PrestigeType.Experience, out int prestigeLevel) && prestigeLevel > 0)
            {
                List<PrefabGUID> buffs = Patches.UpdateBuffsBufferDestroyPatch.PrestigeBuffs;

                for (int i = 0; i < buffs.Count && i < prestigeLevel; i++)
                {
                    PrefabGUID guid = buffs[i];
                    if (guid.GuidHash == 0) continue;

                    sb.Append("R|prestige|").Append(CleanPrefabName(guid)).Append('|').Append(i + 1).Append('\n');
                }
            }
        }
        catch { }
    }

    static void AppendClass(StringBuilder sb, ulong steamId)
    {
        try
        {
            if (!steamId.HasClass(out PlayerClass? playerClass) || !playerClass.HasValue)
            {
                sb.Append("S|class|none\n");
                return;
            }

            sb.Append("S|class|").Append(playerClass.Value.ToString()).Append('\n');

            float mult = ConfigService.SynergyMultiplier;
            string multText = mult.ToString("0.##", Invariant);

            if (ClassWeaponStatSynergies.TryGetValue(playerClass.Value, out List<WeaponManager.WeaponStats.WeaponStatType> weaponStats))
            {
                foreach (var stat in weaponStats)
                {
                    sb.Append("R|synergy|").Append(stat.ToString()).Append('|').Append(multText).Append('\n');
                }
            }

            if (ClassBloodStatSynergies.TryGetValue(playerClass.Value, out List<BloodManager.BloodStats.BloodStatType> bloodStats))
            {
                foreach (var stat in bloodStats)
                {
                    sb.Append("R|synergy|").Append(stat.ToString()).Append('|').Append(multText).Append('\n');
                }
            }
        }
        catch (Exception e)
        {
            Core.Log.LogWarning("[BuffsTab] class: " + e.Message);
        }
    }

    static void AppendBlood(StringBuilder sb, Entity player, ulong steamId)
    {
        try
        {
            BloodType bloodType = BloodManager.GetCurrentBloodType(player.GetBlood());
            sb.Append("S|blood|").Append(bloodType.ToString()).Append('\n');

            IBloodLegacy handler = BloodLegacyFactory.GetBloodHandler(bloodType);

            if (handler == null || !steamId.TryGetPlayerBloodStats(out var bloodStats)
                || !bloodStats.TryGetValue(bloodType, out List<BloodManager.BloodStats.BloodStatType> chosen)) return;

            foreach (var stat in chosen)
            {
                if (!BloodManager.TryGetScaledModifyUnitLegacyStat(handler, player, steamId, bloodType, stat, out float value, out _)) continue;
                if (Math.Abs(value) < 0.0001f) continue;

                sb.Append("R|stat|").Append(stat.ToString()).Append('|').Append(value.ToString("0.####", Invariant)).Append('\n');
            }
        }
        catch (Exception e)
        {
            Core.Log.LogWarning("[BuffsTab] blood: " + e.Message);
        }
    }

    static void AppendWeapon(StringBuilder sb, Entity player, ulong steamId)
    {
        try
        {
            WeaponType weaponType = WeaponManager.GetCurrentWeaponType(player);
            sb.Append("S|weapon|").Append(weaponType.ToString()).Append('\n');

            IWeaponExpertise handler = WeaponExpertiseFactory.GetExpertise(weaponType);

            if (handler == null || !steamId.TryGetPlayerWeaponStats(out var weaponStats)
                || !weaponStats.TryGetValue(weaponType, out List<WeaponManager.WeaponStats.WeaponStatType> chosen)) return;

            foreach (var stat in chosen)
            {
                if (!WeaponManager.TryGetScaledModifyUnitExpertiseStat(handler, player, steamId, weaponType, stat, out float value, out _)) continue;
                if (Math.Abs(value) < 0.0001f) continue;

                sb.Append("R|stat|").Append(stat.ToString()).Append('|').Append(value.ToString("0.####", Invariant)).Append('\n');
            }
        }
        catch (Exception e)
        {
            Core.Log.LogWarning("[BuffsTab] weapon: " + e.Message);
        }
    }

    static string CleanPrefabName(PrefabGUID guid)
    {
        string name;
        try { name = guid.GetPrefabName(); }
        catch { return guid.GuidHash.ToString(Invariant); }

        if (string.IsNullOrEmpty(name)) return guid.GuidHash.ToString(Invariant);

        int idx = name.IndexOf("Prefab", StringComparison.Ordinal);
        if (idx > 0) name = name[..idx];

        return name.Replace("_Buff", string.Empty).Replace("Buff_", string.Empty).Replace('_', ' ').Trim();
    }
}
