# Changelog

## 1.1.5 — smaller quality of life pass

- `Sync/PollSeconds` (default 20) controls the fallback polling interval; `0` relies on server
  pushes only.
- Class section is titled with `(synergies)` so the `×1.5` values are not mistaken for passives.
- The server writes the rates to a per player file (`universalcooldown_rates_<steamId>.txt`) and
  the client prefers it; the shared file stays for compatibility.
- README: known limitations section (EN/RU and on Thunderstore).
## 1.1.4 — no duplicate tabs, readable movement speed

- Fixed duplicated "Blood" tabs: the game reports the same character window through a new
  wrapper, which was treated as a new window on every call. The window is now recognised by its
  native pointer, and any tab created earlier is destroyed and removed from the game arrays
  before a rebuild, so duplicates cannot pile up (they used to squeeze the whole tab strip).
- Movement speed is shown as a percentage (`+2,3%`) instead of a rounded `+0,0`.
## 1.1.3 — the tab survives a reconnect

- Reconnecting to the server destroyed the character window, and the mod kept using the old
  (destroyed) references — the Blood tab silently disappeared until the game was restarted.
  Now a new character window rebinds everything: tab, cached rows and the chat session.
- Chat session state (local user/character entities) resets on reconnect, so data resumes.
## 1.1.2 — updates without waiting

- The client polls the server every 20 seconds (and every ~8 seconds while the tab is open),
  so weapon/blood changes show up quickly even if a server push is missed.
- The server logs every push (`[BuffsTab] pushed N chunk(s) to <steamId>`) to make delivery
  easy to verify.
## 1.1.1 — live updates and a section header fix

- The server now **pushes** the data to the client as soon as it changes (weapon swap, blood,
  class, level), instead of waiting for the next client poll — the weapon section updates
  immediately.
- Fixed: the "General" section was rendered with its raw key (`general`) and an empty-state
  row, because a two-field `S|general` line was mistaken for the legacy format.
- The chat channel also carries the tooltip rates on every push.
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
