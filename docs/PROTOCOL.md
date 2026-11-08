# File protocol between the server and the client

The server (patched Bloodcraft) writes plain text files into `BepInEx/config`.
The client (BepInEx plugin) reads them. No sockets, no shared memory: files can be synced
between machines, and the game is unaffected if one side is missing.

All files are **UTF-8 without BOM**, one record per line.

---

## 1. `universalcooldown_rates.txt`

Used by tooltips. Updated every time Bloodcraft applies the player's weapon buff.

```
spell|weapon|ultimate|physPower|spellPower|extraStats
```

| Field | Meaning |
|---|---|
| `spell`, `weapon`, `ultimate` | Cooldown rates, `0.14` = 14%. |
| `physPower`, `spellPower` | Raw power values (informational). |
| `extraStats` | `StatType:value;` pairs of the other stats of the weapon buff, e.g. `PrimaryAttackSpeed:0.039;PhysicalLifeLeech:0.104;` |

Numbers are numbers; the client accepts both `.` and `,` as the decimal separator.

Example:

```
0.14|0.21|0.14|15.6|0|PrimaryAttackSpeed:0.039;PhysicalLifeLeech:0.104;
```

---

## 2. `universalcooldown_buffs_<steamId>.txt` (v2)

The Blood tab content. One file **per player**, because each player has their own class,
blood and weapon. `universalcooldown_buffs.txt` (without the id) is written as well, for
older clients; the current client prefers its own file and falls back to the generic one.

The file is locale neutral: it carries enum names and numbers only, the client renders them
in English or Russian.

### Format

```
V|2

S|general
R|shroud
R|prestige|<prefab name>|<level>

S|class|<PlayerClass>
R|synergy|<stat>|<multiplier>

S|blood|<BloodType>
R|stat|<stat>|<value>

S|weapon|<WeaponType>
R|stat|<stat>|<value>
```

| Record | Meaning |
|---|---|
| `V|2` | Format version. |
| `S|<kind>|<arg>` | Starts a section. `kind` is `general`, `class`, `blood` or `weapon`; `arg` is the class / blood / weapon enum name. |
| `R|shroud` | The Blood shroud buff is enabled. |
| `R|prestige|<name>|<level>` | A permanent prestige buff, available from that prestige level. |
| `R|synergy|<stat>|<multiplier>` | The class raises this stat's cap by a multiplier (e.g. `1.5`). |
| `R|stat|<stat>|<value>` | A concrete bonus; the client decides whether it is a percentage. |

`<stat>` is the game/Bloodcraft stat enum name (`MaxHealth`, `PhysicalPower`,
`WeaponCooldownRecoveryRate`, `MinimalDamage`, …). Unknown names are humanized
(`PhysicalCritDamage` → "Physical Crit Damage").

Section order is preserved; empty sections are drawn by the client with a localized hint
("No permanent buffs", "No class selected", …).

### Example

```
V|2
S|general
R|shroud
S|class|ShadowBlade
R|synergy|MovementSpeed|1.5
R|synergy|PrimaryAttackSpeed|1.5
R|synergy|PhysicalPower|1.5
S|blood|Rogue
R|stat|WeaponCooldownRecoveryRate|0.21
R|stat|UltimateCooldownRecoveryRate|0.14
S|weapon|Sword
R|stat|PhysicalPower|15.6
R|stat|PhysicalLifeLeech|0.104
```

### Legacy (v1)

Older servers wrote already formatted Russian text:

```
S|Общие
R|Постоянных баффов нет|—
S|Класс: Теневая грань
R|Скорость движения|усиление ×1,5
```

The client still renders this form verbatim, so an old server with a new client does not
break — it just is not translated.

---

## Which stats are percentages?

Decided by the client (`Localization.IsPercent`):

- percentage: attack speed, all leech, all crit chance/damage, healing received, damage
  reduction, resistances, resource yield, blood drain reduction, all cooldown rates,
  minion damage, ability attack speed, corruption resistance;
- flat: `MaxHealth`, `MovementSpeed` (shown with one decimal), `PhysicalPower`, `SpellPower`.
