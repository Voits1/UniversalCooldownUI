using System;
using ProjectM;
using Unity.Entities;

namespace Bloodcraft.Systems;

/// <summary>
/// Optional diagnostics: writes the final stat values the player really has after
/// Bloodcraft bonuses were applied (one log line per refresh).
///
/// Enable it from <see cref="Enabled"/> while tuning a server; keep it off in production,
/// it logs on every weapon buff refresh.
/// </summary>
internal static class StatsDiagnostics
{
    public const bool Enabled = false;

    public static void Log(Entity player)
    {
        if (!Enabled) return;

        try
        {
            string line = "[StatsDiag]";

            if (player.TryGetComponent(out Health health))
            {
                line += $" HP={health.MaxHealth._Value:F1}";
            }

            if (player.TryGetComponent(out UnitStats stats))
            {
                line += $" PP={stats.PhysicalPower._Value:F1} SP={stats.SpellPower._Value:F1}";
                line += $" DR={stats.DamageReduction._Value:F3} PR={stats.PhysicalResistance._Value:F3} SR={stats.SpellResistance._Value:F3}";
                line += $" HR={stats.HealingReceived._Value:F3} RBD={stats.ReducedBloodDrain._Value:F3}";
                line += $" CCR={stats.CCReduction._Value}";
            }

            if (player.TryGetComponent(out AbilityBar_Shared bar))
            {
                line += $" PAS={bar.PrimaryAttackSpeed._Value:F3} AAS={bar.AbilityAttackSpeed._Value:F3}";
                line += $" CDR(s/w/u)={bar.SpellCooldownRecoveryRate._Value:F3}/{bar.WeaponCooldownRecoveryRate._Value:F3}/{bar.UltimateCooldownRecoveryRate._Value:F3}";
            }

            if (player.TryGetComponent(out Movement movement))
            {
                line += $" MS={movement.Speed._Value:F2}";
            }

            Core.Log.LogInfo(line);
        }
        catch (Exception e)
        {
            Core.Log.LogWarning("[StatsDiag] " + e.Message);
        }
    }
}
