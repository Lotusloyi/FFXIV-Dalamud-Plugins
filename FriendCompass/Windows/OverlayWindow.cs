using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
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

    // ---------- 悬浮罗盘（箭头 + 距离） ----------

    private void DrawCompass(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter target,
        Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter localPlayer)
    {
        var gameGui = plugin.GameGui;

        gameGui.WorldToScreen(localPlayer.Position, out var playerScreen, out _);
        var ok = gameGui.WorldToScreen(target.Position, out var targetScreen, out var behind);

        // 指向角度：以本地玩家的屏幕位置为原点
        var angle = 0f;
        if (ok)
        {
            var dx = targetScreen.X - playerScreen.X;
            var dy = targetScreen.Y - playerScreen.Y;
            angle = MathF.Atan2(dy, dx);
            if (behind)
                angle += MathF.PI;
        }

        var center = ImGui.GetWindowPos() + ImGui.GetWindowSize() / 2f;
        var drawList = ImGui.GetWindowDrawList();

        DrawArrow(drawList, center, angle, 16f, ImGui.ColorConvertFloat4ToU32(ColorMain));

        var dist = Vector3.Distance(localPlayer.Position, target.Position);
        var text = $"{target.Name.TextValue}  {dist:F0}m";
        var textSize = ImGui.CalcTextSize(text);
        drawList.AddText(center + new Vector2(-textSize.X / 2f, 14f),
            ImGui.ColorConvertFloat4ToU32(ColorMain), text);
    }

    private static void DrawArrow(ImDrawListPtr drawList, Vector2 center, float angle, float size, uint col)
    {
        var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var perp = new Vector2(-dir.Y, dir.X);
        var tip = center + dir * size;
        var base1 = center - dir * (size * 0.6f) + perp * (size * 0.5f);
        var base2 = center - dir * (size * 0.6f) - perp * (size * 0.5f);
        drawList.AddTriangleFilled(tip, base1, base2, col);
        drawList.AddCircle(center, size * 0.9f, col, 0, 1.5f);
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
