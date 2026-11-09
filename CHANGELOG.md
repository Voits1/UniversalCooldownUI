# Changelog

## 1.1.0 — works for players on other machines

- **Chat channel**: the client asks the server with `.ucui d` and receives the tab data in
  machine readable chat lines it hides from the chat window. Remote players no longer sit on
  "loading server data" forever — files alone only worked when the client shared the server's
  disk.
- **Cooldown rates over chat**: a `meta` section carries spell/weapon/ultimate rates, so
  tooltips are correct on remote clients too (they used to fall back to local config values).
- The client polls the rates every two minutes and refreshes the tab when it opens.
- "Loading" state is a single full width line instead of a row that overlapped its hint.
- New server file: `server/Bloodcraft/Commands/UniversalCooldownCommands.cs` (the command).
## 1.0.2 — balance fix

- Cooldown recovery is now applied as a **recovery rate**: `cooldown = base / (1 + rate)`.
  The previous `base * (1 - rate)` collapsed cooldowns to zero on high values (100% rate gave
  0 s instead of half). Same formula on the server and in the tooltip.
- Documented that the stat caps come from Bloodcraft's own config and should stay at the
  Bloodcraft defaults (`0.1` spell / `0.1` weapon / `0.2` ultimate). Raising them to `1`
  multiplied every bonus by ten and broke the balance.
- No changes to the tab layout.
## 1.0.1 — metadata fix

- Thunderstore manifest: correct `website_url` (repository link).
- No code changes; the 1.0.0 files are identical.
## 1.0.0 — first public release

Everything below was built from scratch during one long debugging session against a live
V Rising 1.1.15 dedicated server with Bloodcraft 1.13.26 (built for 1.1.12).

### Cooldown recovery actually works (server)

- Diagnosed why Bloodcraft's CDR did nothing on 1.1.15: the game recalculates ability
  cooldowns from `AbilityBar_Shared` / vampire attributes and overwrites the modifiers.
- `CooldownRateService`: applies `vanilla * (1 - rate)` directly to `AbilityCooldownData` of
  every ability owned by the player, always deriving the base value from the prefab, so the
  result is idempotent and never stacks when weapons are swapped.
- Disabled and removed the earlier failed experiments (buff clones, `AbilityBar_Shared`
  writes, Harmony on Burst systems) — they stacked cooldowns and crashed the server.

### Client tooltips (client)

- Patches `TMP_Text.text` and every `TMP_Text.SetText` overload (attribute patches fail on
  1.1.15 with "Undefined target method").
- Rewrites the cooldown number in ability tooltips by the server rate, picking
  spell / weapon / ultimate rate from the tooltip text.
- Appends a blue `#8fd6ff` block with the weapon's Bloodcraft bonuses to weapon tooltips.

### Sync between server and client

- `universalcooldown_rates.txt`: cooldown rates + power + extra weapon stats.
- `universalcooldown_buffs_<steamId>.txt`: locale-neutral data for the character window,
  one file per player, plus a generic file for older clients.
- Client resolves its own Steam ID from the client ECS (`LocalUser`) and reads its file.

### Blood tab in the character window (client)

- A real fifth tab inside the vanilla character window, right after *Attributes*.
- Registered in the game's own tab arrays (`InventorySubMenu.TabButtons` / `Tabs`), so the
  game handles selection and highlighting natively (a manually drawn highlight looked wrong
  because the selected tab style lives inside the game's tab system, not in the button tint).
- Sections: **General** (shroud, prestige buffs), **Class** (synergies), **Blood** (legacy of
  the current blood type), **Weapon** (expertise of the weapon in hand).
- Body is a clone of the attributes tab: vanilla headers, dividers and row backgrounds.
- The game rewrites texts of its own UI elements every frame, so the tab keeps a list of
  “enforced” texts and restores them a few times per second.
- Attributes rows are built by the game only when that tab is shown, so the plugin warms the
  templates up once while the window is still hidden (switch to attributes, wait, switch back).

### Localization

- Server sends enum keys and numbers, the client renders English or Russian
  (`General/Language = auto|en|ru`, auto = system language).
- Old (v1) server files with pre-formatted Russian text are still rendered as-is.

### Performance

- One `Tick` per frame (the canvas hook fires several times per frame).
- File polling by `LastWriteTime` instead of reading the file every second.
- Text enforcement throttled; templates cached.
- Fixed the freeze/hitch that the first version caused.

### Housekeeping

- Client config moved to the `universalcooldown.ui` GUID.
- Thunderstore package files included (`thunderstore/`).
- Optional `StatsDiagnostics` (disabled by default) for server tuning.
