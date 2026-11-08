# Universal Cooldown UI — a **Bloodcraft fix** for V Rising 1.1.x

**This is a fix for Bloodcraft, not a standalone gameplay mod.** It repairs the cooldown and
stat integration that broke when V Rising moved from 1.1.12 to 1.1.15, and adds the missing
UI for it.

| | |
|---|---|
| **Fixes** | Bloodcraft **1.13.26** (built for V Rising 1.1.12) |
| **Game versions** | **V Rising 1.1.x** — developed and verified on **1.1.15**; should work on other 1.1 builds but is untested there |
| **Requires** | BepInExPack V Rising (IL2CPP). The server part needs the Bloodcraft source tree |

> **What was broken:** in 1.1.12 Bloodcraft wrote cooldown recovery into the ability bar. In
> 1.1.15 the game recalculates ability cooldowns from `AbilityBar_Shared` / vampire attributes
> and silently overwrites mod values, so cooldown recovery (CDR) did nothing and tooltips kept
> showing vanilla numbers. This project fixes both the server calculation and the client UI.

---

## What it does

### Server side (Bloodcraft patch)

| File | Purpose |
|---|---|
| `Systems/CooldownRateService.cs` | Applies the real cooldown reduction to every ability entity of the player (`AbilityCooldownData.Cooldown = vanilla * (1 - rate)`). Idempotent: values are always derived from the prefab, so nothing stacks when the player swaps weapons. Also writes the client sync file. |
| `Systems/BuffsTabService.cs` | Collects what Bloodcraft gives this player — permanent buffs, class synergies, blood legacy stats, weapon expertise — and writes it for the client, one file per player. |
| `Systems/StatsDiagnostics.cs` | Optional (`Enabled = false`) one-line stat dump for tuning a server. |

Two tiny hooks are added to Bloodcraft (see [`docs/SERVER.md`](docs/SERVER.md)):

- `Patches/ScriptSpawnServerPatch.cs` — after Bloodcraft applies its stat buffs, call
  `CooldownRateService.ApplyToPlayer(...)` and `BuffsTabService.Write(...)`.
- `Patches/StatChangeSystemPatch.cs` — `BuffsTabService.Tick()` so the data also refreshes
  after feeding / class changes, not only on weapon swap.

### Client side (BepInEx plugin)

- **Ability tooltips** — the cooldown number is reduced by the server rate
  (`Перезарядка: 8 сек` → `6,9 сек`).
- **Weapon tooltips** — a blue block with the Bloodcraft bonuses of the current weapon
  (leech, crit, attack speed, …).
- **Blood tab** — a real fifth tab in the character window, right after *Attributes*:
  - **General** — permanent Bloodcraft buffs (shroud, prestige buffs);
  - **Class: …** — the class synergies (which stats the class raises and by how much);
  - **Blood: …** — blood legacy bonuses of the current blood type;
  - **Weapon: …** — expertise bonuses of the weapon in hand.
- **Localization** — English and Russian, auto-detected from the system language.

---

## Repository layout

```
client/UniversalCooldownUI/   BepInEx IL2CPP plugin (C#)
server/Bloodcraft/            files added to the Bloodcraft source tree
docs/                         installation, protocol, troubleshooting
thunderstore/                 ready-to-upload Thunderstore package files
```

---

## Requirements

Repository: <https://github.com/Voits1/UniversalCooldownUI>
Thunderstore: <https://thunderstore.io/c/v-rising/p/Voits1/UniversalCooldownUI/>


| Component | Version |
|---|---|
| V Rising (client + dedicated server) | **1.1.15** — tested; other 1.1.x builds are expected to work |
| Bloodcraft | **1.13.26** — the source the patch is written against |
| BepInExPack V Rising | IL2CPP, 6.0.0-be.XXX or newer |
| .NET SDK | 8 for Bloodcraft, 6 for the client plugin |

The client mod alone does not depend on Bloodcraft being installed on your machine: it only
reads text files. The server part does — it lives inside Bloodcraft.

---

## Install — client

1. Install BepInExPack V Rising in your mod manager profile.
2. Drop `UniversalCooldownUI.dll` into `BepInEx/plugins/UniversalCooldownUI/`.
   (Or install the Thunderstore package.)
3. Start the game once — `BepInEx/config/universalcooldown.ui.cfg` is created.

Without the server part the plugin still works: tooltips use the fallback rates from the
config, and the Blood tab shows a "waiting for server data" hint.

## Install — server

1. Take the **Bloodcraft 1.13.26** source tree that matches your game version.
2. Copy `server/Bloodcraft/Systems/*.cs` into `Bloodcraft/Systems/`.
3. Apply the two hooks described in [`docs/SERVER.md`](docs/SERVER.md) (a few lines each).
4. Build and replace `BepInEx/plugins/<Bloodcraft folder>/Bloodcraft.dll`.
5. Restart the server (`start_server.bat` — V Rising servers will not accept clients otherwise).

The server writes two kinds of files into `BepInEx/config`:

- `universalcooldown_rates.txt` — cooldown rates and weapon stats for tooltips;
- `universalcooldown_buffs_<steamId>.txt` — the Blood tab content for that player
  (plus `universalcooldown_buffs.txt` for older clients).

---

## Configuration (client)

`BepInEx/config/universalcooldown.ui.cfg`

| Key | Default | Meaning |
|---|---|---|
| `General/Enabled` | `true` | Master switch (tooltips + tab). |
| `General/Language` | `auto` | `auto`, `en` or `ru`. |
| `Tab/BloodTab` | `true` | Add the Blood tab to the character window. |
| `Rates/Spell`, `Rates/Weapon`, `Rates/Ultimate` | `0.14` / `0.21` / `0.14` | Fallback rates when the server file is missing. |
| `Sync/RatesFile` | *(empty)* | Path to the server rates file. Empty = `BepInEx/config/universalcooldown_rates.txt`. The Blood tab data is read from the same folder. |
| `Debug/ProbeMenu` | `false` | Dump the character window hierarchy into the log (after a game update). |

If the server runs on another machine, point `Sync/RatesFile` at a synced copy of the file.

---

## How the pieces talk

The server and the client never share memory — they share text files. See
[`docs/PROTOCOL.md`](docs/PROTOCOL.md) for the exact formats. The client sends the server
locale-neutral keys (enum names + numbers) and renders English or Russian itself.

---

## Compatibility

- Works together with **Raphael** and **Eclipse**; the tab is added to the vanilla window
  instead of replacing anything.
- The tab is registered inside the game's own tab system (`InventorySubMenu.TabButtons` /
  `Tabs`), so highlighting and switching stay native.
- Only the character window is touched; HUD, inventory and other windows are not modified.

## Known limitations

- Bloodcraft 1.13.26 has **no class passive buffs** — upstream commented
  `ClassBuffMap` / `InitializeClassPassiveBuffs` out. The "Class" section therefore shows
  the class **synergies** (which stats it raises), not a list of passive abilities.
- The Blood tab needs the server patch. Without it the tab shows a hint.
- Values update every few seconds (the server rewrites the file only when something changes).

---

## Build from source

Client (Windows, BepInEx installed):

```powershell
dotnet build client/UniversalCooldownUI/UniversalCooldownUI.csproj -c Release `
  -p:BepInExRoot="C:\path\to\profile\BepInEx"
```

The project references the game's interop assemblies from that folder, nothing is copied
to the output.

Server: copy the files into the Bloodcraft tree, apply the hooks, then

```powershell
dotnet build Bloodcraft.csproj -c Release
```

## Publishing (Thunderstore)

The listing is linked to this repository in two ways:

- the Thunderstore package metadata (`website_url`) points here;
- `.github/workflows/publish.yml` uploads a packaged release to Thunderstore automatically.

To enable the automatic upload, add the repository secret `THUNDERSTORE_TOKEN` (a Thunderstore
service-account token of the team) and publish a GitHub release with the package zip attached
(build it with `build-package.ps1`). The workflow then pushes the version to the `v-rising`
community.

## License

MIT — see [LICENSE](LICENSE).

Bloodcraft itself is a separate project by **zfolmt** (MIT); this repository contains only the
files we add to it and a short instruction how to hook them in. V Rising is a game by
Stunlock Studios; no game assets are shipped here.
