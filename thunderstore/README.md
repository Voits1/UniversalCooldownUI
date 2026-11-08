# Universal Cooldown UI

Client side companion for **Bloodcraft** on **V Rising 1.1.x**.

Bloodcraft 1.13.26 was built for 1.1.12. In 1.1.15 the game recalculates ability cooldowns
from the ability bar, so cooldown recovery does nothing and tooltips keep showing vanilla
numbers. This mod fixes the display side and adds a proper in-game window for the stats.

## What you get

- **Ability tooltips** — the cooldown number is reduced by the server rate.
- **Weapon tooltips** — a blue block with the Bloodcraft bonuses of the weapon in hand
  (leech, crit, attack speed, …).
- **Blood tab** — a native fifth tab in the character window, right after *Attributes*:
  - **General** — permanent Bloodcraft buffs (shroud, prestige buffs);
  - **Class** — the class synergies;
  - **Blood** — legacy bonuses of the current blood type;
  - **Weapon** — expertise bonuses of the weapon in hand.
- **English and Russian** UI, auto-detected from the system language.

## Requires the server part

The tab is filled from files written by a patched Bloodcraft on the server
(`universalcooldown_rates.txt` and `universalcooldown_buffs_<steamId>.txt`). Ask your server
admin to install the server patch — without it the mod still reduces tooltip cooldowns using
the fallback rates from its config, and the tab shows a hint.

## Configuration

`BepInEx/config/universalcooldown.ui.cfg`

- `General/Enabled` — master switch;
- `General/Language` — `auto`, `en`, `ru`;
- `Tab/BloodTab` — add or hide the Blood tab;
- `Rates/Spell|Weapon|Ultimate` — fallback rates if the server file is missing;
- `Sync/RatesFile` — path to the server rates file (empty = `BepInEx/config/universalcooldown_rates.txt`).

## Compatibility

Works next to Raphael and Eclipse; only the character window is touched. Server patch source
and full documentation: GitHub repository.
