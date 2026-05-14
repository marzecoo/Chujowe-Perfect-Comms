using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VoiceChatPlugin.VoiceChat;

internal static class VoiceAudioOcclusion
{
    private const float CacheSeconds = 0.15f;
    private const float PositionQuantize = 4f;
    private const int MaxCacheEntries = 128;
    private static readonly int ShadowMask = LayerMask.GetMask("Shadow");
    private static readonly Dictionary<OcclusionCacheKey, OcclusionCacheEntry> Cache = new();

    public static VoiceOcclusionDiagnostics Inspect(Vector2 listenerPos, Vector2 targetPos)
    {
        var occlusion = ResolveOcclusion(listenerPos, targetPos);
        return new VoiceOcclusionDiagnostics(occlusion.HasWall, occlusion.HasClosedDoor, !occlusion.IsOccluded);
    }

    public static float ApplyFalloff(float distance, float maxDistance, VoiceFalloffMode mode)
    {
        if (maxDistance <= 0f) return 0f;
        float t = Mathf.Clamp01(distance / maxDistance);
        return mode switch
        {
            VoiceFalloffMode.Smooth => 1f - SmoothStep(t),
            VoiceFalloffMode.VoiceFocused => t < 0.35f ? 1f : Mathf.Pow(1f - t, 1.35f),
            _ => 1f - t,
        };
    }

    public static VoiceOcclusionResult Evaluate(Vector2 listenerPos, Vector2 targetPos, VoiceOcclusionMode mode)
    {
        if (mode == VoiceOcclusionMode.Off)
            return new(false, false, 1f, VoiceAudioFilterMode.None);

        var occlusion = ResolveOcclusion(listenerPos, targetPos);
        if (!occlusion.IsOccluded)
            return new(false, false, 1f, VoiceAudioFilterMode.None);

        return mode switch
        {
            VoiceOcclusionMode.HardBlock => new(occlusion.HasWall, occlusion.HasClosedDoor, 0f, VoiceAudioFilterMode.None),
            VoiceOcclusionMode.VisionOnly => new(occlusion.HasWall, occlusion.HasClosedDoor, 0f, VoiceAudioFilterMode.None),
            VoiceOcclusionMode.SoftMuffle => new(occlusion.HasWall, occlusion.HasClosedDoor, 0.70f, VoiceAudioFilterMode.WallMuffle),
            _ => new(occlusion.HasWall, occlusion.HasClosedDoor, 0.35f, VoiceAudioFilterMode.WallMuffle),
        };
    }

    private static VoiceOcclusionResult ResolveOcclusion(Vector2 listenerPos, Vector2 targetPos)
    {
        var key = OcclusionCacheKey.Create(listenerPos, targetPos);
        float now = Time.time;
        if (Cache.TryGetValue(key, out var cached) && now - cached.Time <= CacheSeconds)
            return cached.Result;

        if (Cache.Count > MaxCacheEntries)
            Cache.Clear();

        bool hasWall = Physics2D.Linecast(listenerPos, targetPos, ShadowMask);
        bool hasClosedDoor = IsClosedDoorBetween(listenerPos, targetPos);
        var result = new VoiceOcclusionResult(hasWall, hasClosedDoor, hasWall || hasClosedDoor ? 0f : 1f, VoiceAudioFilterMode.None);
        Cache[key] = new OcclusionCacheEntry(result, now);
        return result;
    }

    public static bool TryGetCameraListenerPosition(Vector2 targetPos, out Vector2 position)
    {
        position = default;
        var minigame = Minigame.Instance;
        if (minigame == null) return false;

        switch (minigame)
        {
            case SurveillanceMinigame:
                return TryGetNearestCameraPosition(SkeldCameras, targetPos, out position);
            case PlanetSurveillanceMinigame planet:
                if (TryGetPlanetCameraPosition(planet, out position)) return true;
                position = planet.TargetPosition;
                return position != default;
            case FungleSurveillanceMinigame fungle:
                if (fungle.securityCamera?.cam != null)
                {
                    position = fungle.securityCamera.cam.transform.position;
                    return true;
                }
                position = fungle.TargetPosition;
                return position != default;
            default:
                return false;
        }
    }

    private static readonly Vector2[] SkeldCameras =
    [
        new(13.2417f, -4.348f),
        new(0.6216f, -6.5642f),
        new(-7.1503f, 1.6709f),
        new(-17.8098f, -4.8983f),
    ];

    private static readonly Vector2[] PolusCameras =
    [
        new(29f, -15.7f),
        new(15.4f, -15.4f),
        new(24.4f, -8.5f),
        new(17f, -20.6f),
        new(4.7f, -22.73f),
        new(11.6f, -8.2f),
    ];

    private static readonly Vector2[] AirshipCameras =
    [
        new(-8.2872f, 0.0527f),
        new(-4.0477f, 9.1447f),
        new(23.5616f, 9.8882f),
        new(4.881f, -11.1688f),
        new(30.3702f, -0.874f),
        new(3.3018f, 16.2631f),
    ];

    private static bool TryGetPlanetCameraPosition(PlanetSurveillanceMinigame planet, out Vector2 position)
    {
        position = default;
        int index = Mathf.Clamp(planet.currentCamera, 0, 5);
        var cameras = ResolveMapId() == 4 ? AirshipCameras : PolusCameras;
        if (index < 0 || index >= cameras.Length) return false;
        position = cameras[index];
        return true;
    }

    private static bool TryGetNearestCameraPosition(Vector2[] cameras, Vector2 targetPos, out Vector2 position)
    {
        position = default;
        if (cameras.Length == 0) return false;

        float bestDistance = float.MaxValue;
        foreach (var camera in cameras)
        {
            float distance = Vector2.SqrMagnitude(targetPos - camera);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            position = camera;
        }

        return bestDistance < float.MaxValue;
    }

    private static int ResolveMapId()
    {
        try
        {
            var options = GameOptionsManager.Instance?.CurrentGameOptions;
            object? mapId = options?.GetType().GetProperty("MapId", BindingFlags.Public | BindingFlags.Instance)?.GetValue(options)
                            ?? options?.GetType().GetField("MapId", BindingFlags.Public | BindingFlags.Instance)?.GetValue(options);
            if (mapId != null) return Convert.ToInt32(mapId);
        }
        catch { }

        try
        {
            var ship = ShipStatus.Instance;
            if (ship == null) return -1;
            if (ship.GetType().Name.Contains("Airship", StringComparison.OrdinalIgnoreCase)) return 4;
            if (ship.Type == ShipStatus.MapType.Pb) return 2;
            if (ship.Type == ShipStatus.MapType.Fungle) return 5;
            if (ship.Type == ShipStatus.MapType.Hq) return 1;
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    private static float SmoothStep(float t)
        => t * t * (3f - 2f * t);

    private static bool IsClosedDoorBetween(Vector2 listenerPos, Vector2 targetPos)
    {
        var doors = ShipStatus.Instance?.AllDoors;
        if (doors == null) return false;

        foreach (var door in doors)
        {
            if (door == null || door.IsOpen) continue;

            Vector2 doorPos = door.transform.position;
            if (DistancePointToSegment(doorPos, listenerPos, targetPos) <= 0.65f)
                return true;
        }

        return false;
    }

    private static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float denom = Vector2.Dot(ab, ab);
        if (denom <= 0.0001f) return Vector2.Distance(point, a);

        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / denom);
        return Vector2.Distance(point, a + ab * t);
    }

    private readonly record struct OcclusionCacheKey(int Ax, int Ay, int Bx, int By)
    {
        public static OcclusionCacheKey Create(Vector2 a, Vector2 b)
        {
            int ax = Mathf.RoundToInt(a.x * PositionQuantize);
            int ay = Mathf.RoundToInt(a.y * PositionQuantize);
            int bx = Mathf.RoundToInt(b.x * PositionQuantize);
            int by = Mathf.RoundToInt(b.y * PositionQuantize);

            if (ax > bx || (ax == bx && ay > by))
                return new OcclusionCacheKey(bx, by, ax, ay);
            return new OcclusionCacheKey(ax, ay, bx, by);
        }
    }

    private readonly record struct OcclusionCacheEntry(VoiceOcclusionResult Result, float Time);
}

internal readonly record struct VoiceOcclusionDiagnostics(
    bool HasWall,
    bool HasClosedDoor,
    bool InSight);
