# Server side: patching Bloodcraft

The server part is not a standalone plugin — it lives inside **Bloodcraft** (the mod that
owns the stats). This document explains exactly what to add.

Tested against **Bloodcraft 1.13.26**. If you use another version, the hook points below are
the same, only line numbers change.

---

## 1. Copy the files

```
server/Bloodcraft/Systems/CooldownRateService.cs   ->  <Bloodcraft>/Systems/CooldownRateService.cs
server/Bloodcraft/Systems/BuffsTabService.cs       ->  <Bloodcraft>/Systems/BuffsTabService.cs
server/Bloodcraft/Systems/StatsDiagnostics.cs      ->  <Bloodcraft>/Systems/StatsDiagnostics.cs
```

Nothing else in `Systems/` needs to change.

---

## 2. Hook the cooldown + Blood tab data

File: `<Bloodcraft>/Patches/ScriptSpawnServerPatch.cs`

At the top of the file add the namespace:

```csharp
using Bloodcraft.Systems;
```

Then in `ApplyPlayerBonusStats(Entity buffEntity, Entity playerCharacter)` — right after
Bloodcraft updated blood and weapon stats — add the two calls:

```csharp
static void ApplyPlayerBonusStats(Entity buffEntity, Entity playerCharacter)
{
    ulong steamId = playerCharacter.GetSteamId();

    // ... original body: SyncToUserBuffer, BloodManager.UpdateBloodStats, WeaponManager.UpdateWeaponStats

    ApplyCooldownRecoveryRates(buffEntity, playerCharacter, steamId);   // <- add
    BuffsTabService.Write(playerCharacter, steamId);                    // <- add
}
```

And add the helper method to the same class (it collects the cooldown/power stats from the
weapon buff and hands them to `CooldownRateService`):

```csharp
/// <summary>
/// Collects cooldown / power stats from the player's weapon buff and applies them:
/// the real cooldown rate goes through CooldownRateService (the game overwrites the
/// ability bar values), the rest is written to the sync files used by the client mod.
/// </summary>
static void ApplyCooldownRecoveryRates(Entity buffEntity, Entity playerCharacter, ulong steamId)
{
    try
    {
        if (!buffEntity.TryGetBuffer<ModifyUnitStatBuff_DOTS>(out var buffer)) return;

        float spell = 0f, weapon = 0f, ult = 0f, physPower = 0f, spellPower = 0f;
        var extra = new System.Text.StringBuilder();

        for (int i = 0; i < buffer.Length; i++)
        {
            ModifyUnitStatBuff_DOTS statBuff = buffer[i];

            if (statBuff.StatType == UnitStatType.SpellCooldownRecoveryRate) spell += statBuff.Value;
            else if (statBuff.StatType == UnitStatType.WeaponCooldownRecoveryRate) weapon += statBuff.Value;
            else if (statBuff.StatType == UnitStatType.UltimateCooldownRecoveryRate) ult += statBuff.Value;
            else if (statBuff.StatType == UnitStatType.PhysicalPower) physPower += statBuff.Value;
            else if (statBuff.StatType == UnitStatType.SpellPower) spellPower += statBuff.Value;
            else if (statBuff.Value > 0f) extra.Append(statBuff.StatType).Append(':').Append(statBuff.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
        }

        CooldownRateService.ApplyToPlayer(playerCharacter, spell, weapon, ult, physPower, spellPower, extra.ToString());
        StatsDiagnostics.Log(playerCharacter);
    }
    catch (Exception e)
    {
        Core.Log.LogWarning($"[ApplyPlayerBonusStats] cooldown rates: {e.Message}");
    }
}
```

## 3. Refresh the tab data periodically

File: `<Bloodcraft>/Patches/StatChangeSystemPatch.cs`

```csharp
using Bloodcraft.Systems;

// inside OnUpdatePrefix, next to the other periodic work
BuffsTabService.Tick();
```

That is enough: blood changes when feeding, class changes by command, weapon changes on
equip — the tab should not wait for the next weapon-buff refresh.

---

## 4. Build

```powershell
dotnet build Bloodcraft.csproj -c Release
```

Copy `bin/Release/net6.0/Bloodcraft.dll` over the installed one, **stop the server first**
(the file is locked while it runs), then start it with `start_server.bat`.

---

## 5. Verify

Server log, after a player enters the world:

```
[BuffsTab] first data for <steamId>
[CooldownRate] base cooldowns updated: N abilities, spell=14 % weapon=21 % ultimate=14 %
```

Files in `BepInEx/config`:

```
universalcooldown_rates.txt
universalcooldown_buffs_<steamId>.txt
universalcooldown_buffs.txt
```

If `[BuffsTab]` lines never appear, Bloodcraft's stat application never reached the hook —
check that `ApplyPlayerBonusStats` still exists in your Bloodcraft version and that the two
calls are inside it.

---

## Notes

- Do **not** re-add the old experiments: cloning `BonusStatsBuff` entities stacks cooldowns and
  can corrupt the world; `default(ModifyUnitStats).Apply(...)` crashes the client on weapon
  swap; Harmony patches on the Burst `ModifyUnitStatBuffSystem` break the trampoline and the
  server will not start.
- `CooldownRateService` never writes into `AbilityBar_Shared`; it modifies
  `AbilityCooldownData` on the ability entities, which is what both the client tooltips and the
  server cooldown timer read.
