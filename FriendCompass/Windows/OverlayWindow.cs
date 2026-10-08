using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using SceneCameraManager = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager;

namespace FriendCompass.Windows;

public unsafe class OverlayWindow : Window
{
    private readonly Plugin plugin;
    private static readonly uint Outline = 0xEE000000;
    private static readonly uint White = 0xFFFFFFFF;
    private static readonly uint Cyan = ImGui.ColorConvertFloat4ToU32(new(0.1f, 1f, 0.9f, 1f));
    private static readonly uint Amber = ImGui.ColorConvertFloat4ToU32(new(1f, 0.8f, 0.1f, 1f));

    public OverlayWindow(Plugin plugin)
        : base("FriendCompassOverlay", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        SizeCondition = ImGuiCond.Always;
        IsOpen = false;
    }

    public override void PreDraw()
    {
        var diameter = Math.Clamp(plugin.Configuration.CompassSize, 96f, 280f) * ImGuiHelpers.GlobalScale;
        var nameWidth = ImGui.CalcTextSize(plugin.TrackedFriend?.Name ?? "").X;
        var textWidth = ImGui.CalcTextSize("距最近位置 99999 米 · 分流 9").X;
        Size = new(Math.Max(diameter, Math.Max(nameWidth, textWidth)) + 40f * ImGuiHelpers.GlobalScale,
            diameter + ImGui.GetTextLineHeightWithSpacing() * 3f + 28f * ImGuiHelpers.GlobalScale);
    }

    public override void Draw()
    {
        var target = plugin.TrackedFriend;
        var position = plugin.Position;
        var localPlayer = plugin.ObjectTable.LocalPlayer;
        if (plugin.DisabledByDuty || !plugin.Configuration.ShowOverlay ||
            target == null || position == null || localPlayer == null)
            return;

        var diameter = Math.Clamp(plugin.Configuration.CompassSize, 96f, 280f) * ImGuiHelpers.GlobalScale;
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var center = start + new Vector2(width / 2f, diameter / 2f);
        var drawList = ImGui.GetWindowDrawList();
        var radius = diameter / 2f - 5f * ImGuiHelpers.GlobalScale;
        var color = position.Source == PositionSource.LastSeen ? Amber : Cyan;

        drawList.AddCircleFilled(center, radius, 0xA6000000, 64);
        drawList.AddCircle(center, radius, Outline, 64, 7f * ImGuiHelpers.GlobalScale);
        drawList.AddCircle(center, radius, White, 64, 2f * ImGuiHelpers.GlobalScale);

        // The view matrix supplies the actual camera-right axis, independent of yaw conventions.
        var cameraManager = SceneCameraManager.Instance();
        var camera = cameraManager == null ? null : cameraManager->CurrentCamera;
        if (camera != null && TrackingMath.TryCompassDirection(position.Position - localPlayer.Position,
                new(camera->ViewMatrix.M11, camera->ViewMatrix.M31), out var direction))
        {
            DrawArrow(drawList, center, direction, radius * 0.80f + 4f * ImGuiHelpers.GlobalScale, Outline);
            DrawArrow(drawList, center, direction, radius * 0.80f, color);
        }
        else
            drawList.AddCircleFilled(center, 9f * ImGuiHelpers.GlobalScale, color);

        var y = start.Y + diameter + 4f * ImGuiHelpers.GlobalScale;
        DrawCenteredText(drawList, start.X + width / 2f, y, White, target.Name);
        y += ImGui.GetTextLineHeightWithSpacing();
        var distance = Vector3.Distance(localPlayer.Position, position.Position);
        var instance = plugin.InstanceSwitcher.CurrentInstance();
        var distanceText = position.Source == PositionSource.LastSeen ? $"距最近位置 {distance:F0} 米" : $"{distance:F0} 米";
        if (instance > 0)
            distanceText += $" · 分流 {instance}";
        DrawCenteredText(drawList, start.X + width / 2f, y, color, distanceText);
        y += ImGui.GetTextLineHeightWithSpacing();
        DrawCenteredText(drawList, start.X + width / 2f, y, color, position.Describe(Environment.TickCount64));
        ImGui.Dummy(new(width, y - start.Y + ImGui.GetTextLineHeightWithSpacing()));
    }

    public void DrawMarkers()
    {
        var position = plugin.Position;
        var friend = plugin.TrackedFriend;
        if (plugin.DisabledByDuty || position == null || friend == null || plugin.ObjectTable.LocalPlayer == null ||
            plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas] ||
            plugin.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas51])
            return;

        var drawList = ImGui.GetForegroundDrawList(ImGuiHelpers.MainViewport);
        if (plugin.Configuration.ShowWorldMarker && position.Source != PositionSource.LastSeen)
            DrawWorldMarker(drawList, position, friend);
        if (plugin.Configuration.ShowMapMarker)
            DrawMapMarker(drawList, position, friend);
    }

    private static void DrawArrow(ImDrawListPtr drawList, Vector2 center, Vector2 direction, float size, uint color)
    {
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var shoulder = center - direction * (size * 0.05f);
        drawList.AddTriangleFilled(center + direction * size,
            shoulder + perpendicular * (size * 0.48f), shoulder - perpendicular * (size * 0.48f), color);
        drawList.AddQuadFilled(shoulder + perpendicular * (size * 0.18f),
            shoulder - perpendicular * (size * 0.18f),
            center - direction * (size * 0.75f) - perpendicular * (size * 0.18f),
            center - direction * (size * 0.75f) + perpendicular * (size * 0.18f), color);
    }

    private void DrawWorldMarker(ImDrawListPtr drawList, TrackedPosition position, FriendSnapshot friend)
    {
        // WorldToScreen's third output is inView, not behind-camera.
        if (!plugin.GameGui.WorldToScreen(position.Position, out var feet, out _) ||
            !plugin.GameGui.WorldToScreen(position.Position + new Vector3(0, position.Height + 0.6f, 0), out var head, out _))
            return;
        if (!IsFinite(feet) || !IsFinite(head))
            return;

        var scale = Math.Clamp(plugin.Configuration.WorldMarkerScale, 0.75f, 3f) * ImGuiHelpers.GlobalScale;
        drawList.AddLine(feet, head, Outline, 8f * scale);
        drawList.AddLine(feet, head, White, 5f * scale);
        drawList.AddLine(feet, head, Cyan, 3f * scale);
        drawList.AddCircle(feet, 8f * scale, Outline, 24, 6f * scale);
        drawList.AddCircle(feet, 8f * scale, Cyan, 24, 3f * scale);

        var marker = head - new Vector2(0, 14f * scale);
        DrawDiamond(drawList, marker, 14f * scale, Outline);
        DrawDiamond(drawList, marker, 11f * scale, White);
        DrawDiamond(drawList, marker, 8f * scale, Cyan);
        var text = position.Source == PositionSource.Party ? $"{friend.Name} · 小队坐标" : friend.Name;
        DrawLabel(drawList, marker + new Vector2(20f * scale, -ImGui.GetFontSize() / 2f), White, text);
    }

    private void DrawMapMarker(ImDrawListPtr drawList, TrackedPosition position, FriendSnapshot friend)
    {
        var addon = (AddonAreaMap*)plugin.GameGui.GetAddonByName("AreaMap").Address;
        var agent = AgentMap.Instance();
        if (addon == null || !addon->IsVisible || agent == null ||
            agent->SelectedTerritoryId != position.Context.Territory ||
            agent->SelectedMapId != position.Context.Map)
            return;

        var map = plugin.GetMap(agent->SelectedMapId);
        var component = addon->ComponentMap;
        if (map == null || component == null || component->BaseMapImage == null || component->OwnerNode == null)
            return;

        // Use the rendered texture node so zoom, panning and all parent/UI scales are included.
        var image = &component->BaseMapImage->AtkResNode;
        var owner = &component->OwnerNode->AtkResNode;
        if (image->Width == 0 || image->Height == 0 || owner->Width == 0 || owner->Height == 0)
            return;
        var texture = TrackingMath.WorldToTexture(position.Position, map.Value.SizeFactor, map.Value.OffsetX, map.Value.OffsetY);
        var local = TrackingMath.TextureToNode(texture, new(image->Width, image->Height));
        var screen = NodeToScreen(image, local);
        var clipMin = NodeToScreen(owner, Vector2.Zero);
        var clipMax = NodeToScreen(owner, new(owner->Width, owner->Height));
        if (!IsFinite(screen) || !IsFinite(clipMin) || !IsFinite(clipMax) ||
            screen.X < clipMin.X || screen.Y < clipMin.Y || screen.X > clipMax.X || screen.Y > clipMax.Y)
            return;

        var scale = Math.Clamp(plugin.Configuration.MapMarkerScale, 0.75f, 3f) * ImGuiHelpers.GlobalScale;
        var color = position.Source == PositionSource.LastSeen ? Amber : Cyan;
        drawList.PushClipRect(clipMin, clipMax, true);
        drawList.AddCircleFilled(screen, 11f * scale, Outline, 32);
        drawList.AddCircleFilled(screen, 8f * scale, color, 32);
        drawList.AddCircle(screen, 9f * scale, White, 32, 2f * scale);
        var text = position.Source == PositionSource.LastSeen ?
            $"{friend.Name} · {position.Describe(Environment.TickCount64)}" : friend.Name;
        var labelSize = ImGui.CalcTextSize(text) + new Vector2(8f, 6f);
        var label = screen + new Vector2(14f * scale, -labelSize.Y / 2f);
        if (label.X + labelSize.X > clipMax.X)
            label.X = screen.X - 14f * scale - labelSize.X;
        label = Vector2.Clamp(label, clipMin, Vector2.Max(clipMin, clipMax - labelSize));
        DrawLabel(drawList, label + new Vector2(4f, 3f), White, text);
        drawList.PopClipRect();
    }

    private static Vector2 NodeToScreen(AtkResNode* node, Vector2 local)
    {
        for (var current = node; current != null; current = current->ParentNode)
        {
            local = TrackingMath.TransformNodePoint(local, new(current->X, current->Y),
                new(current->OriginX, current->OriginY), new(current->ScaleX, current->ScaleY), current->Rotation);
        }
        return local + ImGuiHelpers.MainViewport.Pos;
    }

    private static bool IsFinite(Vector2 point) => float.IsFinite(point.X) && float.IsFinite(point.Y);

    private static void DrawDiamond(ImDrawListPtr drawList, Vector2 center, float radius, uint color)
        => drawList.AddQuadFilled(center + new Vector2(0, -radius), center + new Vector2(radius, 0),
            center + new Vector2(0, radius), center + new Vector2(-radius, 0), color);

    private static void DrawCenteredText(ImDrawListPtr drawList, float centerX, float y, uint color, string text)
        => DrawOutlinedText(drawList, new(centerX - ImGui.CalcTextSize(text).X / 2f, y), color, text);

    private static void DrawLabel(ImDrawListPtr drawList, Vector2 position, uint color, string text)
    {
        drawList.AddRectFilled(position - new Vector2(4f, 3f),
            position + ImGui.CalcTextSize(text) + new Vector2(4f, 3f), Outline, 3f);
        DrawOutlinedText(drawList, position, color, text);
    }

    private static void DrawOutlinedText(ImDrawListPtr drawList, Vector2 position, uint color, string text)
    {
        drawList.AddText(position + new Vector2(-1, -1), Outline, text);
        drawList.AddText(position + new Vector2(1, -1), Outline, text);
        drawList.AddText(position + new Vector2(-1, 1), Outline, text);
        drawList.AddText(position + new Vector2(1, 1), Outline, text);
        drawList.AddText(position, color, text);
    }
}
