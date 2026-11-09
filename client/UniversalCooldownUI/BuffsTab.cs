using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ProjectM.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace UniversalCooldownUI
{
    /// <summary>
    /// The fifth "Blood" tab inside the vanilla character window.
    ///
    /// The tab is registered inside the game's own tab system (InventorySubMenu.TabButtons /
    /// Tabs), so the game itself handles selection, highlighting and content visibility.
    /// The tab body is a clone of the attributes tab, refilled with data written by the
    /// server (see server/Bloodcraft/Systems/BuffsTabService.cs).
    /// </summary>
    internal static class BuffsTab
    {
        const string DataFilePrefix = "universalcooldown_buffs";

        static readonly Color TextColor = new(1f, 1f, 1f, 0.92f);
        static Color _green = new(0.42f, 0.85f, 0.33f);

        static InventorySubMenu _menu;
        static GameObject _root;
        static Transform _content;
        static CanvasGroup _windowGroup;

        static SimpleStunButton _button;
        static TMP_Text _buttonLabel;
        static RectTransform _buttonRect;
        static Sprite _normalSprite;
        static Sprite _selectedSprite;
        static Color _normalColor = Color.white;
        static Color _selectedColor = Color.white;
        static Image _highlight;

        static GameObject _headerTemplate;
        static GameObject _dividerTemplate;
        static GameObject _rowTemplate;

        static readonly List<SimpleStunButton> VanillaButtons = new();
        static readonly List<(TMP_Text Text, string Value)> Enforced = new();
        static List<Section> _lastSections = new();
        static List<Section> _remoteSections = new();
        static float _lastDataTime = -999f;

        static bool _built;
        static bool _active;
        static bool _nativeTab;
        static int _tabIndex = -1;
        static int _lastTab = -99;
        static int _lastClickFrame = -1;
        static int _layoutFrames;
        static string _signature = string.Empty;
        static float _nextRefresh;
        static int _tickFrame = -1;
        static float _nextEnforce;
        static DateTime _fileStamp = DateTime.MinValue;
        static ulong _localId;
        static float _nextIdTry;

        // template warm-up (attributes rows are built by the game on demand)
        static int _warmStage;
        static int _warmFrame;
        static int _warmPrevTab;
        static int _warmRetryAt;

        internal static void Bind(InventorySubMenu menu)
        {
            if (menu != null) _menu = menu;
        }

        /// <summary>New character window (reconnect): drop every cached reference and rebuild.</summary>
        internal static void Rebind(InventorySubMenu menu)
        {
            try { CleanupExisting(menu); } catch (Exception e) { Plugin.L.LogWarning("[BuffsTab] cleanup: " + e.Message); }

            ResetState();
            _remoteSections.Clear();
            _lastSections.Clear();
            _signature = string.Empty;
            _fileStamp = DateTime.MinValue;
            _lastDataTime = -999f;
            _warmStage = 0;
            _warmRetryAt = 0;

            _menu = menu;
        }

        // ---------- per frame ----------

        internal static void Tick(Unity.Entities.World world)
        {
            try
            {
                if (_menu == null) return;
                if (!Plugin.BloodTab.Value) return;

                ChatSync.SetWorld(world);
                ChatSync.AutoPoll();
                if (_localId == 0 && world != null) TryResolveLocalId(world);

                int frame = Time.frameCount;
                if (frame == _tickFrame) return;
                _tickFrame = frame;

                if (_built && (_root == null || _button == null)) ResetState();

                bool visible = _menu.gameObject.activeInHierarchy;
                if (visible && !_built) TryBuild();
                if (!_built) return;

                WarmUpTemplates();

                if (visible) PollClicks();

                RefreshFromServer();
                ApplyNativeState();
                EnforceTexts();

                if (_active && _layoutFrames > 0)
                {
                    _layoutFrames--;
                    RebuildLayout();
                }
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[BuffsTab] tick: " + e.Message);
            }
        }

        static void ApplyNativeState()
        {
            int tab = -1;
            try { tab = _menu.CurrentTab; } catch { }
            _lastTab = tab;

            if (!_nativeTab || _tabIndex < 0) return;

            bool nativeActive = tab == _tabIndex;

            if (nativeActive && !_active)
            {
                _active = true;
                Rebuild(ReadServerSections());
                _layoutFrames = 3;
                if (Time.time - _lastDataTime > 20f) ChatSync.Request();
                LogLayout();
                Plugin.L.LogInfo($"[BuffsTab] opened by the game (tab={_tabIndex})");
            }
            else if (!nativeActive && _active)
            {
                _active = false;
            }
        }

        static void RefreshFromServer()
        {
            if (!_active || Time.time < _nextRefresh) return;
            _nextRefresh = Time.time + 1f;

            DateTime stamp = DateTime.MinValue;
            string path = DataFileFor(_localId);

            try { if (File.Exists(path)) stamp = File.GetLastWriteTimeUtc(path); } catch { }

            if (stamp != _fileStamp)
            {
                _fileStamp = stamp;
                List<Section> fresh = ReadServerSections();
                if (Signature(fresh) != _signature) Rebuild(fresh);
            }
            else if (Enforced.Count == 0)
            {
                // rows could not be built yet (templates were not ready) - retry
                Rebuild(ReadServerSections());
            }

            if (_remoteSections.Count == 0 || Time.time - _lastDataTime > 10f) ChatSync.Request();
        }

        static void EnforceTexts()
        {
            if (!_active || Enforced.Count == 0 || Time.time < _nextEnforce) return;
            _nextEnforce = Time.time + 0.25f;

            for (int i = 0; i < Enforced.Count; i++)
            {
                var item = Enforced[i];
                if (item.Text == null) continue;
                if (item.Text.text != item.Value) item.Text.text = item.Value;
            }
        }

        static void ResetState()
        {
            _built = false;
            _active = false;
            _nativeTab = false;
            _tabIndex = -1;
            _built = false;
            _root = null;
            _content = null;
            _button = null;
            _buttonLabel = null;
            _buttonRect = null;
            _highlight = null;
            _windowGroup = null;
            _headerTemplate = null;
            _dividerTemplate = null;
            _rowTemplate = null;
            VanillaButtons.Clear();
            Enforced.Clear();
        }

        // ---------- build ----------

        static void TryBuild()
        {
            // a duplicate can exist after a partial rebuild - always start from a clean strip
            try { CleanupExisting(_menu); } catch { }

            var buttons = _menu.TabButtons;
            var tabs = _menu.Tabs;
            if (buttons == null || buttons.Length == 0 || tabs == null || tabs.Length == 0) return;

            int last = buttons.Length - 1;
            SimpleStunButton templateButton = buttons[last];
            Transform strip = templateButton.transform.parent;

            int selected = 0;
            try { selected = Mathf.Clamp(_menu.CurrentTab, 0, last); } catch { }

            var selectedImage = buttons[selected].GetComponent<Image>();
            if (selectedImage != null) _selectedSprite = selectedImage.sprite;

            for (int i = 0; i < buttons.Length; i++)
            {
                var img = buttons[i] == null ? null : buttons[i].GetComponent<Image>();
                if (img == null || img.sprite == null) continue;
                if (_selectedSprite != null && img.sprite != _selectedSprite)
                {
                    _normalSprite = img.sprite;
                    break;
                }
            }

            if (_selectedSprite == null) _selectedSprite = _normalSprite;
            if (_normalSprite == null) _normalSprite = _selectedSprite;

            var lastLabel = templateButton.GetComponentInChildren<TMP_Text>(true);
            if (lastLabel != null) _selectedColor = lastLabel.color;

            var firstLabel = buttons[0].GetComponentInChildren<TMP_Text>(true);
            _normalColor = firstLabel != null && firstLabel.color != _selectedColor ? firstLabel.color : _selectedColor;

            var green = FindGreenValue(tabs[last]);
            if (green.HasValue) _green = green.Value;

            // --- tab button ---
            GameObject buttonGo = UnityEngine.Object.Instantiate(templateButton.gameObject, strip);
            buttonGo.name = "BloodTabButton";
            buttonGo.SetActive(true);

            _button = buttonGo.GetComponent<SimpleStunButton>();
            _buttonRect = buttonGo.GetComponent<RectTransform>();
            _buttonLabel = buttonGo.GetComponentInChildren<TMP_Text>(true);

            if (_buttonLabel != null)
            {
                _buttonLabel.text = Localization.TabTitle;
                _buttonLabel.enableWordWrapping = false;
                _buttonLabel.enableAutoSizing = true;
                _buttonLabel.fontSizeMin = 9f;
                _buttonLabel.fontSizeMax = 20f;
                _buttonLabel.overflowMode = TextOverflowModes.Overflow;
                _buttonLabel.color = _normalColor;
            }

            buttonGo.transform.SetAsLastSibling();
            _highlight = CreateHighlight(buttonGo.transform);

            try { ((UnityEvent)_button.onClick).RemoveAllListeners(); } catch { }

            for (int i = 0; i < buttons.Length; i++) VanillaButtons.Add(buttons[i]);

            ShrinkTabStrip(strip, buttons);

            // --- tab body (clone of the attributes tab) ---
            GameObject tabTemplate = tabs[last].gameObject;
            _root = UnityEngine.Object.Instantiate(tabTemplate, tabTemplate.transform.parent);
            _root.name = "BloodTab";
            _root.SetActive(false);

            _windowGroup = tabTemplate.transform.parent == null ? null : tabTemplate.transform.parent.GetComponent<CanvasGroup>();

            AddBackground(_root.transform);

            _content = FindDeep(_root.transform, "AttributesTabContent");
            if (_content == null)
            {
                Plugin.L.LogWarning("[BuffsTab] AttributesTabContent not found - the tab clone was not built");
                return;
            }

            ClearContent();
            RegisterAsNativeTab(buttons, tabs);

            _built = true;
            Plugin.L.LogInfo($"[BuffsTab] tab built (buttons={buttons.Length}, native={_nativeTab})");

            Rebuild(ReadServerSections());
        }

        /// <summary>
        /// Adds the tab to the game arrays so the game highlights and switches it natively.
        /// </summary>
        static void RegisterAsNativeTab(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<SimpleStunButton> buttons,
                                        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<RectTransform> tabs)
        {
            try
            {
                int count = buttons.Length;
                _tabIndex = count;

                var newButtons = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<SimpleStunButton>(count + 1);
                for (int i = 0; i < count; i++) newButtons[i] = buttons[i];
                newButtons[count] = _button;

                var newTabs = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<RectTransform>(count + 1);
                for (int i = 0; i < tabs.Length; i++) newTabs[i] = tabs[i];
                newTabs[count] = _root.GetComponent<RectTransform>();

                _menu.TabButtons = newButtons;
                _menu.Tabs = newTabs;

                _nativeTab = true;
                Plugin.L.LogInfo($"[BuffsTab] tab registered with the game as index {_tabIndex} (total {count + 1})");
            }
            catch (Exception e)
            {
                _nativeTab = false;
                _tabIndex = -1;
            _built = false;
                Plugin.L.LogWarning("[BuffsTab] native registration failed, falling back to overlay: " + e.Message);
            }
        }

        static void ShrinkTabStrip(Transform strip, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<SimpleStunButton> tabButtons)
        {
            if (strip == null || tabButtons == null) return;

            int count = tabButtons.Length + 1;
            var stripRect = strip.GetComponent<RectTransform>();
            var hlg = strip.GetComponent<HorizontalLayoutGroup>();
            float spacing = hlg != null ? hlg.spacing : 0f;
            float width = stripRect != null ? (stripRect.rect.width - spacing * (count - 1)) / count : 110f;
            if (width < 84f) width = 84f;

            var targets = new List<Transform>();

            for (int i = 0; i < tabButtons.Length; i++)
            {
                if (tabButtons[i] != null) targets.Add(tabButtons[i].transform);
            }

            if (_button != null) targets.Add(_button.transform);

            foreach (Transform child in targets)
            {
                if (child == null) continue;

                var le = child.GetComponent<LayoutElement>();
                if (le == null) le = child.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = width;
                le.flexibleWidth = 0f;

                var label = child.GetComponentInChildren<TMP_Text>(true);
                if (label == null) continue;

                label.enableAutoSizing = true;
                label.fontSizeMin = 9f;
                label.fontSizeMax = 20f;
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Overflow;
            }
        }

        /// <summary>Red frame shown behind our tab label when the overlay fallback is used.</summary>
        static Image CreateHighlight(Transform parent)
        {
            try
            {
                GameObject go = new("BloodTabHighlight");
                go.transform.SetParent(parent, false);
                go.transform.SetAsFirstSibling();

                var rect = go.AddComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(3f, 4f);
                rect.offsetMax = new Vector2(-3f, -3f);

                var img = go.AddComponent<Image>();
                img.sprite = null;
                img.type = Image.Type.Simple;
                img.color = new Color(0.62f, 0.10f, 0.12f, 1f);
                img.raycastTarget = false;

                go.SetActive(false);
                return img;
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[BuffsTab] highlight: " + e.Message);
                return null;
            }
        }

        static void AddBackground(Transform parent)
        {
            try
            {
                GameObject bg = new("BloodTabBackground");
                bg.transform.SetParent(parent, false);
                bg.transform.SetAsFirstSibling();

                var rect = bg.AddComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                var img = bg.AddComponent<Image>();
                img.sprite = null;
                img.type = Image.Type.Simple;
                img.color = new Color(0.043f, 0.043f, 0.055f, 1f);
                img.raycastTarget = false;
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[BuffsTab] background: " + e.Message);
            }
        }

        // ---------- input ----------

        static void PollClicks()
        {
            try
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

                int frame = Time.frameCount;
                if (frame == _lastClickFrame) return;

                Vector2 pos = mouse.position.ReadValue();
                Camera cam = null;
                var canvas = _button != null ? _button.GetComponentInParent<Canvas>() : null;
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) cam = canvas.worldCamera;

                if (_buttonRect != null && RectTransformUtility.RectangleContainsScreenPoint(_buttonRect, pos, cam))
                {
                    _lastClickFrame = frame;

                    if (_nativeTab)
                    {
                        try { _menu.CurrentTab = _tabIndex; }
                        catch (Exception e)
                        {
                            Plugin.L.LogWarning("[BuffsTab] native switch failed: " + e.Message);
                            _nativeTab = false;
                        }
                    }
                    else if (!_active)
                    {
                        ActivateOverlay();
                    }

                    return;
                }

                for (int i = 0; i < VanillaButtons.Count; i++)
                {
                    var button = VanillaButtons[i];
                    if (button == null) continue;

                    var rect = button.GetComponent<RectTransform>();
                    if (rect == null) continue;
                    if (!RectTransformUtility.RectangleContainsScreenPoint(rect, pos, cam)) continue;

                    _lastClickFrame = frame;
                    if (!_nativeTab && _active) DeactivateOverlay();

                    return;
                }
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[BuffsTab] clicks: " + e.Message);
            }
        }

        // ---------- overlay fallback ----------

        static void ActivateOverlay()
        {
            try
            {
                if (!_built || _root == null) return;

                Rebuild(ReadServerSections());
                _root.SetActive(true);
                _root.transform.SetAsLastSibling();
                _active = true;
                _layoutFrames = 3;

                if (_highlight != null) _highlight.gameObject.SetActive(true);
                if (_buttonLabel != null) _buttonLabel.color = Color.white;

                LogLayout();
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[BuffsTab] activate: " + e.Message);
            }
        }

        static void DeactivateOverlay()
        {
            try
            {
                if (_root != null) _root.SetActive(false);
                _active = false;

                if (_highlight != null) _highlight.gameObject.SetActive(false);
                if (_buttonLabel != null) _buttonLabel.color = _normalColor;
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[BuffsTab] deactivate: " + e.Message);
            }
        }

        // ---------- templates ----------

        /// <summary>
        /// The game builds the attributes rows only when that tab has been shown once.
        /// While the window is still hidden we briefly switch it to attributes (index 3),
        /// let the game populate the rows, and switch back. Invisible to the player.
        /// </summary>
        static void WarmUpTemplates()
        {
            try
            {
                if (_warmStage == 2 || _active) return;

                if (_warmStage == 0)
                {
                    if (FindRowTemplate() != null)
                    {
                        _warmStage = 2;
                        return;
                    }

                    if (Time.frameCount < _warmRetryAt) return;

                    _warmPrevTab = 0;
                    try { _warmPrevTab = _menu.CurrentTab; } catch { }

                    try { _menu.CurrentTab = 3; }
                    catch { _warmStage = 2; return; }

                    _warmFrame = Time.frameCount;
                    _warmStage = 1;
                    return;
                }

                if (_warmStage == 1 && Time.frameCount > _warmFrame + 4)
                {
                    try { _menu.CurrentTab = _warmPrevTab; } catch { }
                    _warmStage = 2;

                    _rowTemplate = FindRowTemplate();
                    _headerTemplate = FindTemplate("AttributesHeader");
                    _dividerTemplate = FindTemplate("HDiv");

                    Plugin.L.LogInfo($"[BuffsTab] templates warmed up: row={(_rowTemplate == null ? "null" : _rowTemplate.name)}");

                    if (_rowTemplate == null)
                    {
                        _warmStage = 0;
                        _warmRetryAt = Time.frameCount + 90;
                    }
                }
            }
            catch (Exception e)
            {
                _warmStage = 2;
                Plugin.L.LogWarning("[BuffsTab] warmup: " + e.Message);
            }
        }

        static GameObject FindRowTemplate()
        {
            var sections = _menu.AttributeSections;
            if (sections == null) return null;

            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < sections.Count; i++)
                {
                    var section = sections[i];
                    if (section == null) continue;

                    Transform container = FindDeep(section.transform, "AttributesContainer");
                    if (container == null) continue;

                    for (int k = 0; k < container.childCount; k++)
                    {
                        Transform child = container.GetChild(k);
                        if (!child.gameObject.activeSelf) continue;

                        bool isClone = child.name.EndsWith("(Clone)");
                        if (pass == 0 && (!isClone || !HasFilledText(child))) continue;
                        if (pass == 1 && !isClone) continue;

                        return child.gameObject;
                    }
                }
            }

            return null;
        }

        static bool HasFilledText(Transform row)
        {
            foreach (var text in row.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == null) continue;

                string s = text.text;
                if (string.IsNullOrEmpty(s)) continue;
                if (s.StartsWith("{")) continue;
                if (s == "Weapon Level") continue;

                return true;
            }

            return false;
        }

        static GameObject FindTemplate(string name)
        {
            var sections = _menu.AttributeSections;
            if (sections != null)
            {
                for (int i = 0; i < sections.Count; i++)
                {
                    if (sections[i] == null) continue;
                    Transform t = FindDeep(sections[i].transform, name);
                    if (t != null) return t.gameObject;
                }
            }

            var tabs = _menu.Tabs;
            if (tabs == null) return null;

            for (int i = 0; i < tabs.Length; i++)
            {
                if (tabs[i] == null) continue;
                Transform t = FindDeep(tabs[i], name);
                if (t != null) return t.gameObject;
            }

            return null;
        }

        // ---------- data ----------

        internal sealed class Section
        {
            public string Kind = "";
            public string Title = "";
            public readonly List<KeyValuePair<string, string>> Rows = new();
        }

        static string DataFileFor(ulong id)
        {
            string dir = Plugin.SyncDirectory();

            if (id != 0)
            {
                string personal = Path.Combine(dir, $"{DataFilePrefix}_{id}.txt");
                if (File.Exists(personal)) return personal;
            }

            return Path.Combine(dir, $"{DataFilePrefix}.txt");
        }

        static void TryResolveLocalId(Unity.Entities.World world)
        {
            if (Time.time < _nextIdTry) return;
            _nextIdTry = Time.time + 5f;

            try
            {
                var em = world.EntityManager;
                var query = em.CreateEntityQuery(Unity.Entities.ComponentType.ReadOnly<ProjectM.Network.LocalUser>());
                var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);

                try
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        var entity = entities[i];
                        if (!em.HasComponent<ProjectM.Network.User>(entity)) continue;

                        var user = em.GetComponentData<ProjectM.Network.User>(entity);
                        if (user.PlatformId == 0) continue;

                        _localId = user.PlatformId;
                        _fileStamp = DateTime.MinValue;
                        Plugin.L.LogInfo($"[BuffsTab] local steam id resolved");
                        break;
                    }
                }
                finally
                {
                    entities.Dispose();
                    query.Dispose();
                }
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[BuffsTab] local id: " + e.Message);
            }
        }

        /// <summary>
        /// Removes tab buttons / tab bodies we created earlier (duplicates left by a previous
        /// build or by a reconnect) and shrinks the game arrays back to the vanilla entries.
        /// </summary>
        static void CleanupExisting(InventorySubMenu menu)
        {
            if (menu == null) return;

            var buttons = menu.TabButtons;
            var tabs = menu.Tabs;

            // destroy our tab bodies
            if (tabs != null)
            {
                for (int i = 0; i < tabs.Length; i++)
                {
                    var rect = tabs[i];
                    if (rect == null || rect.gameObject == null) continue;
                    if (rect.gameObject.name != "BloodTab") continue;

                    rect.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(rect.gameObject);
                }
            }

            // destroy our buttons (by name, in the strip)
            Transform strip = null;
            if (buttons != null)
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    if (buttons[i] == null) continue;
                    strip = buttons[i].transform.parent;
                    break;
                }
            }

            if (strip != null)
            {
                for (int i = strip.childCount - 1; i >= 0; i--)
                {
                    Transform child = strip.GetChild(i);
                    if (child == null) continue;
                    if (child.name != "BloodTabButton") continue;

                    child.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }

            // rebuild the arrays without our entries
            menu.TabButtons = WithoutOurButtons(buttons);
            menu.Tabs = WithoutOurTabs(tabs);
        }

        static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<SimpleStunButton> WithoutOurButtons(
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<SimpleStunButton> buttons)
        {
            if (buttons == null) return null;

            int keep = 0;
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null && buttons[i].gameObject.name != "BloodTabButton") keep++;
            }

            var result = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<SimpleStunButton>(keep);
            int index = 0;

            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] == null || buttons[i].gameObject.name == "BloodTabButton") continue;
                result[index++] = buttons[i];
            }

            return result;
        }

        static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<RectTransform> WithoutOurTabs(
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<RectTransform> tabs)
        {
            if (tabs == null) return null;

            int keep = 0;
            for (int i = 0; i < tabs.Length; i++)
            {
                if (tabs[i] != null && tabs[i].gameObject.name != "BloodTab") keep++;
            }

            var result = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<RectTransform>(keep);
            int index = 0;

            for (int i = 0; i < tabs.Length; i++)
            {
                if (tabs[i] == null || tabs[i].gameObject.name == "BloodTab") continue;
                result[index++] = tabs[i];
            }

            return result;
        }

        /// <summary>Data received from the server over chat.</summary>
        internal static void OnRemotePayload(string payload)
        {
            try
            {
                List<Section> sections = ParseSections(payload);
                if (sections.Count == 0) return;

                _remoteSections = sections;
                _lastSections = sections;
                _lastDataTime = Time.time;

                if (_active) Rebuild(sections);

                Plugin.L.LogInfo($"[BuffsTab] data received over chat ({sections.Count} sections)");
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[BuffsTab] remote data: " + e.Message);
            }
        }

        static List<Section> ReadServerSections()
        {
            List<Section> fromFile = new();

            try
            {
                string path = DataFileFor(_localId);
                if (File.Exists(path)) fromFile = ParseSections(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[BuffsTab] read: " + e.Message);
            }

            if (fromFile.Count > 0) return fromFile;
            return _remoteSections;
        }

        static List<Section> ParseSections(string text)
        {
            List<Section> result = new();

            try
            {
                Section current = null;

                foreach (string raw in (text ?? string.Empty).Split('\n'))
                {
                    string line = raw.TrimEnd();
                    if (line.Length == 0) continue;

                    string[] parts = line.Split('|');

                    if (parts[0] == "V") continue;

                    if (parts[0] == "S")
                    {
                        current = new Section();

                        string kind = parts.Length >= 2 ? parts[1] : string.Empty;
                        string arg = parts.Length >= 3 ? parts[2] : string.Empty;

                        if (kind == "meta")
                        {
                            // service section: parsed, never displayed
                            current = null;
                            continue;
                        }

                        if (IsKnownKind(kind))
                        {
                            current.Kind = kind;
                            current.Title = Localization.SectionTitle(kind, arg);
                        }
                        else if (parts.Length == 2)
                        {
                            current.Title = parts[1];   // legacy: already localized by an old server
                        }

                        result.Add(current);
                        continue;
                    }

                    if (parts[0] != "R") continue;

                    if (parts.Length >= 2 && parts[1] == "rates")
                    {
                        ApplyRates(parts);
                        continue;
                    }

                    if (current == null) continue;

                    AddRow(current, parts);
                }
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[BuffsTab] parse: " + e.Message);
            }

            for (int i = 0; i < result.Count; i++)
            {
                if (result[i].Rows.Count > 0) continue;
                result[i].Rows.Add(new KeyValuePair<string, string>(Localization.EmptyHint(result[i].Kind), "—"));
            }

            return result;
        }

        static bool IsKnownKind(string kind)
        {
            return kind == "general" || kind == "class" || kind == "blood" || kind == "weapon";
        }

        static void ApplyRates(string[] parts)
        {
            if (parts.Length < 5) return;

            if (float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value)) Plugin.ServerSpell = value;
            if (float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)) Plugin.ServerWeapon = value;
            if (float.TryParse(parts[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value)) Plugin.ServerUltimate = value;
        }

        static void AddRow(Section section, string[] parts)
        {
            if (parts.Length < 2) return;

            string kind = parts[1];


            if (kind == "shroud" && parts.Length == 2)
            {
                section.Rows.Add(new KeyValuePair<string, string>(Localization.Shroud, Localization.ShroudOn));
                return;
            }

            if (kind == "prestige" && parts.Length >= 4)
            {
                if (int.TryParse(parts[3], out int level))
                {
                    section.Rows.Add(new KeyValuePair<string, string>(Localization.PrestigeName(parts[2]), Localization.PrestigeLevel(level)));
                }

                return;
            }

            if (kind == "synergy" && parts.Length >= 4)
            {
                if (float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float multiplier))
                {
                    section.Rows.Add(new KeyValuePair<string, string>(Localization.Stat(parts[2]), Localization.FormatMultiplier(multiplier)));
                }

                return;
            }

            if (kind == "stat" && parts.Length >= 4)
            {
                if (float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
                {
                    section.Rows.Add(new KeyValuePair<string, string>(Localization.Stat(parts[2]), Localization.FormatStat(parts[2], value)));
                }

                return;
            }

            // legacy: "R|<name>|<value>"
            if (parts.Length >= 3)
            {
                section.Rows.Add(new KeyValuePair<string, string>(parts[1], parts[2]));
            }
        }

        static string Signature(List<Section> sections)
        {
            var sb = new StringBuilder();

            foreach (var section in sections)
            {
                sb.Append(section.Title).Append('|');
                foreach (var row in section.Rows) sb.Append(row.Key).Append('=').Append(row.Value).Append(';');
                sb.Append('\n');
            }

            return sb.ToString();
        }

        // ---------- rendering ----------

        static void ClearContent()
        {
            if (_content == null) return;

            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                var child = _content.GetChild(i).gameObject;
                child.SetActive(false);         // remove from layout immediately, Destroy is deferred
                UnityEngine.Object.Destroy(child);
            }
        }

        static void Rebuild(List<Section> sections)
        {
            if (_content == null) return;

            ClearContent();
            Enforced.Clear();

            _rowTemplate = _rowTemplate != null ? _rowTemplate : FindRowTemplate();
            _headerTemplate = _headerTemplate != null ? _headerTemplate : FindTemplate("AttributesHeader");
            _dividerTemplate = _dividerTemplate != null ? _dividerTemplate : FindTemplate("HDiv");

            if (sections.Count == 0 && _lastSections.Count > 0) sections = _lastSections;
            else if (sections.Count > 0) _lastSections = sections;

            if (sections.Count == 0)
            {
                // one full width line: header-less section, value empty
                sections = new List<Section>
                {
                    new()
                    {
                        Kind = "",
                        Title = "",
                        Rows = { new KeyValuePair<string, string>(Localization.Loading, "") }
                    }
                };
            }

            foreach (Section section in sections) AddSection(section);

            _signature = Signature(sections);
            RebuildLayout();
        }

        static void AddSection(Section section)
        {
            bool singleLine = section.Rows.Count == 1 && string.IsNullOrEmpty(section.Rows[0].Value);

            GameObject sectionGo = BuildSection(section.Title, out Transform container, singleLine);
            if (sectionGo == null) return;

            if (singleLine)
            {
                AddPlainText(sectionGo.transform, section.Rows[0].Key);
                return;
            }

            foreach (var row in section.Rows) AddRowView(container, row.Key, row.Value);
        }

        /// <summary>One centered line of text, used for the "loading" state.</summary>
        static void AddPlainText(Transform parent, string text)
        {
            try
            {
                GameObject go = new("BuffsPlainText");
                go.transform.SetParent(parent, false);
                go.AddComponent<RectTransform>();

                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.text = text;
                tmp.fontSize = 18f;
                tmp.color = TextColor;
                tmp.alignment = TextAlignmentOptions.Left;
                tmp.enableWordWrapping = true;
                tmp.overflowMode = TextOverflowModes.Overflow;

                Enforced.Add((tmp, text));
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[BuffsTab] plain text: " + e.Message);
            }
        }

        static GameObject BuildSection(string title, out Transform container, bool withoutGrid = false)
        {
            container = null;

            GameObject section = new("BuffsSection");
            section.transform.SetParent(_content, false);

            var rect = section.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);

            var vlg = section.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.padding = new RectOffset { left = 12, right = 12, top = 10, bottom = 10 };
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            if (_headerTemplate != null && !string.IsNullOrEmpty(title))
            {
                GameObject header = UnityEngine.Object.Instantiate(_headerTemplate, section.transform);
                header.SetActive(true);
                header.name = "BuffsHeader";

                StripInteractive(header);

                var tmp = header.GetComponent<TMP_Text>();
                if (tmp == null) tmp = header.GetComponentInChildren<TMP_Text>(true);

                if (tmp != null)
                {
                    tmp.text = title;
                    tmp.enableWordWrapping = false;
                    Enforced.Add((tmp, title));
                }
            }

            if (_dividerTemplate != null)
            {
                GameObject divider = UnityEngine.Object.Instantiate(_dividerTemplate, section.transform);
                divider.SetActive(true);
                divider.name = "BuffsDivider";
            }

            if (withoutGrid) return section;

            GameObject gridGo = new("BuffsGrid");
            gridGo.transform.SetParent(section.transform, false);
            gridGo.AddComponent<RectTransform>();

            var glg = gridGo.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(280f, 40f);
            glg.spacing = new Vector2(10f, 4f);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 2;
            glg.childAlignment = TextAnchor.MiddleLeft;

            var csf = gridGo.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            container = gridGo.transform;
            return section;
        }

        static void AddRowView(Transform container, string name, string value)
        {
            GameObject template = _rowTemplate != null ? _rowTemplate : FindRowTemplate();
            if (template == null || container == null) return;

            GameObject row = UnityEngine.Object.Instantiate(template, container);
            row.name = "BuffRow";
            row.SetActive(true);

            StripInteractive(row);

            var texts = new List<TMP_Text>();
            foreach (var text in row.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text != null) texts.Add(text);
            }

            TMP_Text nameText = null;
            TMP_Text valueText = null;

            for (int i = 0; i < texts.Count; i++)
            {
                string goName = texts[i].gameObject.name;
                if (nameText == null && goName.StartsWith("AttributeName")) nameText = texts[i];
                else if (valueText == null && goName.StartsWith("AttributeValue")) valueText = texts[i];
            }

            if (nameText == null && texts.Count > 0) nameText = texts[0];
            if (valueText == null && texts.Count > 1) valueText = texts[1];

            for (int i = 0; i < texts.Count; i++)
            {
                if (texts[i] == nameText || texts[i] == valueText) continue;
                UnityEngine.Object.Destroy(texts[i].gameObject);
            }

            LayoutRow(nameText, valueText);

            if (nameText != null)
            {
                nameText.text = name;
                nameText.color = TextColor;
                nameText.enableWordWrapping = false;
                nameText.enableAutoSizing = true;
                nameText.fontSizeMin = 9f;
                nameText.fontSizeMax = 15f;
                nameText.overflowMode = TextOverflowModes.Ellipsis;
                nameText.alignment = TextAlignmentOptions.Left;
                Enforced.Add((nameText, name));
            }

            if (valueText != null)
            {
                valueText.text = value;
                valueText.color = value.StartsWith("+") ? _green : TextColor;
                valueText.enableWordWrapping = false;
                valueText.enableAutoSizing = true;
                valueText.fontSizeMin = 10f;
                valueText.fontSizeMax = 16f;
                valueText.alignment = TextAlignmentOptions.Right;
                Enforced.Add((valueText, value));
            }

            var bar = FindDeep(row.transform, "ProgressBar");
            if (bar != null) UnityEngine.Object.Destroy(bar.gameObject);

            var focus = FindDeep(row.transform, "BorderFocus");
            if (focus != null) UnityEngine.Object.Destroy(focus.gameObject);
        }

        static void LayoutRow(TMP_Text nameText, TMP_Text valueText)
        {
            try
            {
                if (nameText != null && nameText.transform.parent != null)
                {
                    var holder = nameText.transform.parent;
                    var hlg = holder.GetComponent<HorizontalLayoutGroup>();

                    if (hlg != null)
                    {
                        hlg.childControlWidth = true;
                        hlg.childControlHeight = true;
                        hlg.childForceExpandWidth = false;
                        hlg.childAlignment = TextAnchor.MiddleLeft;
                        hlg.spacing = 4f;
                    }
                }

                ApplyLayout(nameText, 1f, -1f, 40f);
                ApplyLayout(valueText, 0f, 72f, -1f);
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[BuffsTab] row layout: " + e.Message);
            }
        }

        static void ApplyLayout(TMP_Text text, float flexible, float preferred, float min)
        {
            if (text == null) return;

            var le = text.GetComponent<LayoutElement>();
            if (le == null) le = text.gameObject.AddComponent<LayoutElement>();

            le.flexibleWidth = flexible;
            if (preferred > 0f) le.preferredWidth = preferred;
            if (min > 0f) le.minWidth = min;
        }

        /// <summary>Removes game components that would rewrite texts or handle clicks on the clone.</summary>
        static void StripInteractive(GameObject go)
        {
            try
            {
                foreach (var comp in go.GetComponentsInChildren<Component>(true))
                {
                    if (comp == null) continue;

                    string typeName = comp.GetIl2CppType().Name;
                    if (typeName == "CharacterAttributeEntry" || typeName == "SimpleStunButton" ||
                        typeName == "Tooltip" || typeName == "LocalizedText")
                    {
                        UnityEngine.Object.Destroy(comp);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[BuffsTab] strip: " + e.Message);
            }
        }

        static void RebuildLayout()
        {
            try
            {
                var rect = _content as RectTransform;
                if (rect == null) return;

                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                if (rect.parent != null) LayoutRebuilder.MarkLayoutForRebuild(rect.parent as RectTransform);
            }
            catch { }
        }

        static void LogLayout()
        {
            try
            {
                if (_content == null) return;

                var sb = new StringBuilder("[BuffsTab] layout:");

                for (int i = 0; i < _content.childCount; i++)
                {
                    Transform section = _content.GetChild(i);
                    if (section == null || !section.gameObject.activeSelf) continue;

                    var rect = section.GetComponent<RectTransform>();

                    int rows = 0;
                    var grid = FindDeep(section, "BuffsGrid");
                    if (grid != null) rows = grid.childCount;

                    sb.Append($" [{(rect != null ? rect.rect.width : -1):0}x{(rect != null ? rect.rect.height : -1):0} rows={rows}]");
                }

                sb.Append($" tab={(_buttonRect != null ? _buttonRect.rect.width : -1):0}px");
                Plugin.L.LogInfo(sb.ToString());
            }
            catch { }
        }

        static Color? FindGreenValue(RectTransform tab)
        {
            try
            {
                if (tab == null) return null;

                foreach (var text in tab.GetComponentsInChildren<TMP_Text>(false))
                {
                    if (text == null) continue;

                    string s = text.text;
                    if (string.IsNullOrEmpty(s) || !s.Contains("+")) continue;

                    Color c = text.color;
                    if (c.g > c.r && c.g > c.b) return c;
                }
            }
            catch { }

            return null;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }

            return null;
        }
    }
}
