using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace FriendCompass.Windows;

public unsafe class OverlayWindow : Window
{
    private readonly Plugin plugin;

    private static readonly Vector4 ColorMain = new(0.35f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 ColorWarn = new(0.95f, 0.75f, 0.3f, 1f);

    public OverlayWindow(Plugin plugin)
        : base("FriendCompassOverlay", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoBackground
            | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav)
    {
        this.plugin = plugin;
        IsOpen = false;
    }

    public override void Draw()
    {
        // 可见性由 Plugin.RefreshTracked 统一管理（WindowSystem 只绘制 IsOpen 的窗口）
        var config = plugin.Configuration;
        var target = plugin.TrackedFriend;
        var character = plugin.TrackedCharacter;
        var localPlayer = plugin.ObjectTable.LocalPlayer;

        if (target == null || character == null || localPlayer == null)
            return;

        var anythingVisible = config.ShowOverlay || config.ShowWorldMarker || config.ShowMapMarker;
        if (!anythingVisible)
            return;

        if (config.ShowOverlay)
            DrawCompass(character, localPlayer);

        var drawList = ImGui.GetForegroundDrawList();

        if (config.ShowWorldMarker)
            DrawWorldMarker(drawList, character!, target!);

        if (config.ShowMapMarker)
            DrawMapMarker(drawList, character!, localPlayer);
    }

    // ---------- 悬浮窗（方向箭头 + 同图好友距离） ----------

    // 鲜艳的罗盘配色
    private static readonly Vector4 ColorArrow = new(1.0f, 0.55f, 0.05f, 1f);   // 亮橙
    private static readonly Vector4 ColorArrowOutline = new(0f, 0f, 0f, 0.9f);   // 黑描边
    private static readonly Vector4 ColorRing = new(1.0f, 0.85f, 0.1f, 1f);      // 亮黄

    private void DrawCompass(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter target,
        Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter localPlayer)
    {
        // 用镜头水平偏航角（Camera.DirH，与角色朝向同一角度约定）计算方位：
        // 屏幕上方对应镜头朝向，因此屏幕角 = 世界方位角 - 镜头偏航 - 90°
        // （ImGui 中 0 弧度指向 +X，-90° 指向上方）。比屏幕坐标投影稳定，
        // 不受悬浮窗位置与好友是否在视锥内的影响。
        var delta = target.Position - localPlayer.Position;
        var bearing = MathF.Atan2(delta.X, delta.Z);
        var cameraManager = CameraManager.Instance();
        if (cameraManager == null || cameraManager->Camera == null)
            return;
        var angle = bearing - cameraManager->Camera->DirH - MathF.PI / 2f;

        var center = ImGui.GetWindowPos() + ImGui.GetWindowSize() / 2f;
        var drawList = ImGui.GetWindowDrawList();
        var colArrow = ImGui.ColorConvertFloat4ToU32(ColorArrow);
        var colOutline = ImGui.ColorConvertFloat4ToU32(ColorArrowOutline);
        var colRing = ImGui.ColorConvertFloat4ToU32(ColorRing);

        // 加大箭头：黑色描边打底 + 亮橙填充，保证任何背景下醒目
        DrawArrow(drawList, center + new Vector2(0, -14f), angle, 34f, colOutline);
        DrawArrow(drawList, center + new Vector2(0, -14f), angle, 28f, colArrow);
        drawList.AddCircle(center, 32f, colRing, 0, 2.5f);

        var dist = Vector3.Distance(localPlayer.Position, target.Position);
        var inst = plugin.InstanceSwitcher.CurrentInstance();
        var text = inst > 0
            ? $"{target.Name.TextValue}  {dist:F0} 米 · 分流{inst}"
            : $"{target.Name.TextValue}  {dist:F0} 米";
        var textSize = ImGui.CalcTextSize(text);
        drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize() + 2f,
            center + new Vector2(-textSize.X / 2f + 1f, 24f), colOutline, text);
        drawList.AddText(ImGui.GetFont(), ImGui.GetFontSize() + 2f,
            center + new Vector2(-textSize.X / 2f, 23f), colArrow, text);
    }

    private static void DrawArrow(ImDrawListPtr drawList, Vector2 center, float angle, float size, uint col)
    {
        var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var perp = new Vector2(-dir.Y, dir.X);
        var tip = center + dir * size;
        var base1 = center - dir * (size * 0.62f) + perp * (size * 0.48f);
        var base2 = center - dir * (size * 0.62f) - perp * (size * 0.48f);
        drawList.AddTriangleFilled(tip, base1, base2, col);
    }

    // ---------- 好友头顶世界标记 ----------

    private void DrawWorldMarker(ImDrawListPtr drawList,
        Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter character, FriendSnapshot friend)
    {
        var gameGui = plugin.GameGui;

        if (!gameGui.WorldToScreen(character.Position, out var feet, out var behind) || behind)
            return;
        if (!gameGui.WorldToScreen(character.Position + new Vector3(0, 2.2f, 0), out var head, out _))
            return;

        var col = ImGui.ColorConvertFloat4ToU32(ColorMain);
        drawList.AddLine(feet, head, col, 2f);

        // 头顶菱形
        const float r = 6f;
        var up = new Vector2(0, r);
        var right = new Vector2(r, 0);
        drawList.AddQuadFilled(head + up, head + right, head - up, head - right, col);
        drawList.AddText(head + new Vector2(9f, -14f), col, friend.Name);
    }

    // ---------- 野外大地图（AreaMap）标记 ----------

    private void DrawMapMarker(ImDrawListPtr drawList,
        Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter character,
        Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter localPlayer)
    {
        var territoryId = plugin.ClientState.TerritoryType;
        if (territoryId == 0)
            return;

        var map = plugin.GetMapForTerritory((ushort)territoryId);
        if (map == null)
            return;

        var addon = plugin.GameGui.GetAddonByName("AreaMap");
        var addonPtr = addon.Address;
        if (addonPtr == nint.Zero)
            return;

        var unit = (AtkUnitBase*)addonPtr;
        if (!unit->IsVisible)
            return;

        var areaMap = (AddonAreaMap*)addonPtr;
        var map2d = areaMap->AreaMap;
        var comp = map2d.ComponentMap;
        if (comp == null)
            return;

        // 大地图标题与当前区域名一致时才绘制（防止浏览其他地图时错位）
        var titleNode = areaMap->TitleTextNode;
        if (titleNode != null)
        {
            var title = titleNode->NodeText.ToString();
            var zoneName = plugin.GetTerritoryName((ushort)territoryId);
            if (!title.Contains(zoneName, StringComparison.Ordinal))
                return;
        }

        // 世界坐标 → 地图纹理像素（0~2048）
        var scale = map.Value.SizeFactor / 100.0;
        Vector2 ToMapPx(Vector3 w) => new(
            (float)((w.X + map.Value.OffsetX) * scale),
            (float)((w.Z + map.Value.OffsetY) * scale));

        var playerPx = ToMapPx(localPlayer.Position);
        var friendPx = ToMapPx(character.Position);

        // 以游戏绘制的玩家标记为锚点，抵消平移/缩放/朝向误差
        var markerX = map2d.PlayerMarkerX + (friendPx.X - playerPx.X) * map2d.MapScale;
        var markerY = map2d.PlayerMarkerY + (friendPx.Y - playerPx.Y) * map2d.MapScale;

        var owner = comp->OwnerNode;
        if (owner == null)
            return;

        var screen = new Vector2(
            owner->ScreenX + markerX * owner->ScaleX,
            owner->ScreenY + markerY * owner->ScaleY);

        var col = ImGui.ColorConvertFloat4ToU32(ColorWarn);
        drawList.AddCircleFilled(screen, 7f, col);
        drawList.AddCircle(screen, 7f, ImGui.ColorConvertFloat4ToU32(ColorMain), 0, 2f);
        drawList.AddText(screen + new Vector2(10f, -8f),
            ImGui.ColorConvertFloat4ToU32(ColorMain), plugin.TrackedFriend?.Name ?? string.Empty);
    }
}
