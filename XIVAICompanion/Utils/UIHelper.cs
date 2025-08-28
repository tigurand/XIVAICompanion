using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Plugin;
using System.Numerics;
using System.Runtime.InteropServices;

namespace XIVAICompanion.Utils
{
    internal static class UIHelper
    {
        public class HeaderIconOptions
        {
            public string Tooltip { get; set; } = string.Empty;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct ImGuiWindow
        {
            [FieldOffset(0xC)] public ImGuiWindowFlags Flags;
            [FieldOffset(0xD5)] public byte HasCloseButton;
        }

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        private static extern nint igGetCurrentWindow();

        private static unsafe ImGuiWindow* GetCurrentWindow() => (ImGuiWindow*)igGetCurrentWindow();
        private static unsafe ImGuiWindowFlags GetCurrentWindowFlags() => GetCurrentWindow()->Flags;
        private static unsafe bool CurrentWindowHasCloseButton() => GetCurrentWindow()->HasCloseButton != 0;

        private static uint _headerLastWindowID = 0;
        private static ulong _headerLastFrame = 0;
        private static uint _headerCurrentPos = 0;
        private static float _headerImGuiButtonWidth = 0;

        public static unsafe bool AddHeaderIcon(IDalamudPluginInterface pluginInterface, string id, FontAwesomeIcon icon, out bool pressed, HeaderIconOptions? options = null)
        {
            pressed = false;
            if (ImGui.IsWindowCollapsed()) return false;

            var scale = ImGuiHelpers.GlobalScale;
            var currentID = ImGui.GetID(0);
            if (currentID != _headerLastWindowID || _headerLastFrame != pluginInterface.UiBuilder.FrameCount)
            {
                _headerLastWindowID = currentID;
                _headerLastFrame = pluginInterface.UiBuilder.FrameCount;
                _headerCurrentPos = 0;
                _headerImGuiButtonWidth = 0f;

                if (CurrentWindowHasCloseButton())
                    _headerImGuiButtonWidth += 17 * scale;
                if (!GetCurrentWindowFlags().HasFlag(ImGuiWindowFlags.NoCollapse))
                    _headerImGuiButtonWidth += 17 * scale;
            }

            var prevCursorPos = ImGui.GetCursorPos();
            var buttonSize = new Vector2(20 * scale);

            var buttonPos = new Vector2(ImGui.GetWindowWidth() - buttonSize.X - _headerImGuiButtonWidth - 4 * scale - 30 * _headerCurrentPos++ * scale - ImGui.GetStyle().FramePadding.X * 2, ImGui.GetScrollY() + 1);

            ImGui.SetCursorPos(buttonPos);
            var drawList = ImGui.GetWindowDrawList();
            drawList.PushClipRectFullScreen();

            ImGui.InvisibleButton(id, buttonSize);
            var itemMin = ImGui.GetItemRectMin();
            var itemMax = ImGui.GetItemRectMax();
            var halfSize = ImGui.GetItemRectSize() / 2;
            var center = itemMin + halfSize;

            if (ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(itemMin, itemMax, false))
            {
                if (options != null && !string.IsNullOrEmpty(options.Tooltip))
                {
                    ImGui.SetTooltip(options.Tooltip);
                }

                drawList.AddCircleFilled(center, halfSize.X, ImGui.GetColorU32(ImGui.IsMouseDown(ImGuiMouseButton.Left) ? ImGuiCol.ButtonActive : ImGuiCol.ButtonHovered));
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    pressed = true;
                }
            }

            ImGui.SetCursorPos(buttonPos);

            ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
            var iconString = icon.ToIconString();
            drawList.AddText(Dalamud.Interface.UiBuilder.IconFont, ImGui.GetFontSize(), itemMin + halfSize - ImGui.CalcTextSize(iconString) / 2, 0xFFFFFFFF, iconString);
            ImGui.PopFont();

            ImGui.PopClipRect();
            ImGui.SetCursorPos(prevCursorPos);

            return pressed;
        }
    }
}