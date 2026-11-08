using System;
using System.IO;
using System.Text;
using BepInEx;
using ProjectM;
using ProjectM.Scripting;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;

namespace Bloodcraft.Systems;

/// <summary>
/// Applies Bloodcraft cooldown recovery to the player's real ability cooldowns.
///
/// V Rising 1.1 recalculates ability cooldowns from the ability bar / vampire attributes,
/// so writing the modifiers into <c>AbilityBar_Shared</c> is not enough: the game keeps
/// overwriting them. Instead the base cooldown stored in <c>AbilityCooldownData</c> on each
/// of the player's ability entities is set to <c>vanilla * (1 - rate)</c>, which the client
/// tooltips and the server use. It is idempotent (values already in place are not touched)
/// and never stacks, because the vanilla value is always read from the prefab.
/// </summary>
internal static class CooldownRateService
{
    static EntityManager EntityManager => Core.EntityManager;

    // ---------------------------------------------------------------------
    // Public entry point
    // ---------------------------------------------------------------------

    public static void ApplyToPlayer(Entity player, float spell, float weapon, float ultimate,
                                     float physPower = 0f, float spellPower = 0f, string extraStats = "")
    {
        try
        {
            WriteSyncFile(spell, weapon, ultimate, physPower, spellPower, extraStats);

            EntityQuery query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<AbilityCooldownData>());
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);

            int changed = 0;

            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity ability = entities[i];

                    if (!ability.TryGetComponent(out EntityOwner owner) || owner.Owner != player) continue;
                    if (!ability.TryGetComponent(out PrefabGUID guid)) continue;
                    if (!Core.SystemService.PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(guid, out Entity prefab)) continue;
                    if (!prefab.TryGetComponent(out AbilityCooldownData prefabData)) continue;

                    float vanilla = prefabData.Cooldown._Value;
                    if (vanilla <= 0f) continue;

                    float rate = RateFor(prefabName: guid.GetPrefabName(), spell: spell, weapon: weapon, ultimate: ultimate);
                    if (rate < 0f) rate = 0f;

                    AbilityCooldownData data = EntityManager.GetComponentData<AbilityCooldownData>(ability);
                    float target = vanilla * (1f - rate);

                    if (Math.Abs(data.Cooldown._Value - target) < 0.001f) continue;

                    data.Cooldown._Value = target;
                    EntityManager.SetComponentData(ability, data);
                    changed++;
                }
            }
            finally
            {
                entities.Dispose();
            }

            if (changed > 0)
            {
                Core.Log.LogInfo($"[CooldownRate] base cooldowns updated: {changed} abilities, spell={spell:P0} weapon={weapon:P0} ultimate={ultimate:P0}");
            }
        }
        catch (Exception e)
        {
            Core.Log.LogWarning("[CooldownRate] " + e.Message);
        }
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    static float RateFor(string prefabName, float spell, float weapon, float ultimate)
    {
        if (string.IsNullOrEmpty(prefabName)) return spell;

        if (prefabName.Contains("Ultimate", StringComparison.OrdinalIgnoreCase)) return ultimate;
        if (prefabName.Contains("Weapon", StringComparison.OrdinalIgnoreCase)) return weapon;

        return spell;
    }

    /// <summary>
    /// Writes the values the client uses for tooltips and the Blood tab:
    /// <c>spell|weapon|ultimate|physPower|spellPower|extraStats</c>
    /// (extraStats = "StatType:value;..." in invariant culture).
    /// </summary>
    static void WriteSyncFile(float spell, float weapon, float ultimate, float physPower, float spellPower, string extraStats)
    {
        try
        {
            string path = Path.Combine(Paths.ConfigPath, "universalcooldown_rates.txt");
            string text = string.Join("|",
                spell.ToString(System.Globalization.CultureInfo.InvariantCulture),
                weapon.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ultimate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                physPower.ToString(System.Globalization.CultureInfo.InvariantCulture),
                spellPower.ToString(System.Globalization.CultureInfo.InvariantCulture),
                extraStats ?? string.Empty);

            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            Core.Log.LogWarning("[CooldownRate] sync file: " + e.Message);
        }
    }
}
