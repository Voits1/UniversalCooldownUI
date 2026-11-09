using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using ProjectM.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UniversalCooldownUI
{
    /// <summary>
    /// Captures the live <see cref="InventorySubMenu"/> (the character window) and drives
    /// the Blood tab every frame. With Debug/ProbeMenu enabled it also dumps the window
    /// hierarchy into the log (useful after a game update).
    /// </summary>
    internal static class MenuProbe
    {
        internal static InventorySubMenu Menu;

        static bool _dumpedMenu;
        static bool _dumpedContent;
        static int _tick;
        static readonly StringBuilder Sb = new();

        internal static void Install(Harmony harmony)
        {
            try
            {
                Type mapper = AccessTools.TypeByName("ProjectM.UI.InventorySubMenuMapper");
                MethodInfo initUI = mapper == null ? null : AccessTools.Method(mapper, "InitializeUI");

                if (initUI != null)
                {
                    harmony.Patch(initUI, postfix: new HarmonyMethod(
                        typeof(MenuProbe).GetMethod(nameof(OnInitializeUI), BindingFlags.Static | BindingFlags.NonPublic)));
                    Plugin.L.LogInfo("[Probe] InitializeUI hooked");
                }
                else
                {
                    Plugin.L.LogWarning("[Probe] InventorySubMenuMapper.InitializeUI not found");
                }

                Type canvasSystem = AccessTools.TypeByName("ProjectM.UI.UICanvasSystem");
                MethodInfo hide = canvasSystem == null ? null : AccessTools.Method(canvasSystem, "UpdateHideIfDisabled");

                if (hide != null)
                {
                    harmony.Patch(hide, postfix: new HarmonyMethod(
                        typeof(MenuProbe).GetMethod(nameof(OnCanvasUpdate), BindingFlags.Static | BindingFlags.NonPublic)));
                    Plugin.L.LogInfo("[Probe] canvas update hooked");
                }
                else
                {
                    Plugin.L.LogWarning("[Probe] UICanvasSystem.UpdateHideIfDisabled not found");
                }
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[Probe] install: " + e);
            }
        }

        static void OnInitializeUI(InventorySubMenu menu)
        {
            bool changed = Menu != menu;

            if (Menu == null)
            {
                Plugin.L.LogInfo($"[Probe] character window found: '{menu.gameObject.name}'");
            }
            else if (changed)
            {
                // reconnect (or a world reload): the old menu and its clones are gone,
                // everything has to be rebuilt against the new one
                Plugin.L.LogInfo("[Probe] character window re-created - rebinding");
            }

            Menu = menu;

            if (changed)
            {
                ChatSync.ResetSession();
                BuffsTab.Rebind(menu);
            }
            else
            {
                BuffsTab.Bind(menu);
            }
        }

        static void OnCanvasUpdate(UICanvasSystem __instance)
        {
            try
            {
                BuffsTab.Tick(__instance == null ? null : __instance.World);
                if (Menu == null) return;

                if (++_tick % 15 != 0) return;
                if (!Plugin.Probe.Value) return;

                if (!_dumpedMenu)
                {
                    _dumpedMenu = true;
                    DumpMenu();
                }

                if (Menu.gameObject.activeInHierarchy && !_dumpedContent)
                {
                    _dumpedContent = true;
                    DumpContents();
                }
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("[Probe] poll: " + e.Message);
            }
        }

        static void DumpMenu()
        {
            Sb.Clear();
            Sb.AppendLine("[Probe] ===== MENU =====");
            Sb.AppendLine($"[Probe] tabButtons={Menu.TabButtons?.Length} tabs={Menu.Tabs?.Length}");

            var buttons = Menu.TabButtons;
            if (buttons != null)
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    if (buttons[i] == null) continue;
                    var label = buttons[i].GetComponentInChildren<TMP_Text>(true);
                    Sb.AppendLine($"[Probe]   tab[{i}] '{buttons[i].gameObject.name}' label='{Trunc(label?.text)}'");
                }
            }

            var tabs = Menu.Tabs;
            if (tabs != null)
            {
                for (int i = 0; i < tabs.Length; i++)
                {
                    if (tabs[i] == null) continue;
                    Sb.AppendLine($"[Probe]   content[{i}] '{tabs[i].gameObject.name}' active={tabs[i].gameObject.activeSelf}");
                }
            }

            if (buttons != null && buttons.Length > 0 && buttons[0] != null)
            {
                Sb.AppendLine("[Probe] --- tab strip ---");
                DumpTree(buttons[0].transform.parent, 0, 80);
            }

            Sb.AppendLine("[Probe] ===== END =====");
            Plugin.L.LogInfo(Sb.ToString());
        }

        static void DumpContents()
        {
            var tabs = Menu.Tabs;
            if (tabs == null) return;

            for (int i = 0; i < tabs.Length; i++)
            {
                if (tabs[i] == null) continue;

                Sb.Clear();
                Sb.AppendLine($"[Probe] ===== CONTENT {i} '{tabs[i].gameObject.name}' =====");
                DumpTree(tabs[i], 0, 400);
                Sb.AppendLine($"[Probe] ===== CONTENT {i} END =====");
                Plugin.L.LogInfo(Sb.ToString());
            }
        }

        static string Trunc(string s)
        {
            if (s == null) return "<null>";
            s = s.Replace("\r", " ").Replace("\n", "\\n");
            return s.Length > 90 ? s.Substring(0, 90) + "..." : s;
        }

        static void DumpTree(Transform t, int depth, int budget)
        {
            if (t == null || budget <= 0 || Sb.Length > 90000) return;

            string extra = "";
            var rt = t.GetComponent<RectTransform>();
            if (rt != null)
            {
                extra = $" size={rt.rect.width:0}x{rt.rect.height:0}";
                if (t.GetComponent<HorizontalLayoutGroup>() != null) extra += " [HLG]";
                if (t.GetComponent<VerticalLayoutGroup>() != null) extra += " [VLG]";
                if (t.GetComponent<GridLayoutGroup>() != null) extra += " [GLG]";
            }

            var img = t.GetComponent<Image>();
            if (img != null) extra += $" [Image {(img.sprite == null ? "-" : img.sprite.name)}]";

            string text = "";
            var tmp = t.GetComponent<TMP_Text>();
            if (tmp != null) text = $" text='{Trunc(tmp.text)}'";

            Sb.AppendLine($"[Probe] {new string(' ', Math.Min(depth * 2, 40))}- {t.gameObject.name} act={t.gameObject.activeSelf}{extra}{text}");

            int n = t.childCount;
            for (int i = 0; i < n && i < 40; i++) DumpTree(t.GetChild(i), depth + 1, budget - 1);
        }
    }
}
