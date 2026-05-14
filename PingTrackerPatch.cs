using HarmonyLib;
using TMPro;
using System.Collections.Generic;
using VoiceChatPlugin.VoiceChat;
using UnityEngine;
using Object = UnityEngine.Object;
using MiraAPI.LocalSettings;

namespace VoiceChatPlugin;

internal static class VCSorting
{
    public const string Layer = "UI";
    public const int    Base  = 32700; 
    public const int    Ring  = 32705;
    public const int    Text  = 32710; 
    public const int    Glow  = 32690; 
}

[HarmonyPatch(typeof(PingTracker), nameof(PingTracker.Update))]
public static class PingTrackerPatch
{
    private const float IconScale   = 0.16f;
    private const float LabelSize   = 0.95f;
    private const float SlotWidth   = 0.52f;
    private const float SlotHeight  = 0.58f;
    private const float LabelOffset = 0.34f;
    private const float RingScale   = 0.48f;
    private const float LevelSmoothSpeed = 12f;
    private const float FadeInSpeed = 7f;
    private const float FadeOutSpeed = 5f;
    private static GameObject?       _barRoot;
    private static AspectPosition?   _barAspect;
    private static bool              _layoutVertical;
    private static SpeakingBarPosition _barPosition = SpeakingBarPosition.TopRight;
    private static GameObject? _cacheHolder;
    private static readonly Dictionary<byte, GameObject>        _iconCache        = new();
    private static readonly Dictionary<byte, OutfitFingerprint> _cacheFingerprint = new();
    private static readonly Dictionary<byte, SpeakerSlot> _slots = new();
    private static readonly HashSet<byte> _activeSpeakerIds = new();
    private static readonly Dictionary<byte, float> _activeSpeakerLevels = new();
    private static readonly List<byte> _fadedSlotIds = new();
    private static bool _layoutDirty;

    public static void ApplySpeakingBarPosition(SpeakingBarPosition pos)
    {
        _barPosition    = pos;
        _layoutVertical = pos is SpeakingBarPosition.TopLeft
            or SpeakingBarPosition.TopRight
            or SpeakingBarPosition.BottomLeft
            or SpeakingBarPosition.BottomRight;
        if (_barAspect == null) return;
        ApplyPositionToAspect(_barAspect, pos);
        _barAspect.AdjustPosition();
        _layoutDirty = true;
        LayoutSlotsIfDirty();
    }


    static void Postfix(PingTracker __instance)
    {
        if (__instance?.text == null) return;

        EnsureBar(__instance);
        if (_barRoot == null) return;

        var room = VoiceChatRoom.Current;
        var overlay = VoiceOverlayState.Current(room);
        _activeSpeakerIds.Clear();
        _activeSpeakerLevels.Clear();

        foreach (var remote in overlay.RemotePlayers)
        {
            if (remote.IsSpeaking && remote.IsAudible)
            {
                _activeSpeakerIds.Add(remote.PlayerId);
                _activeSpeakerLevels[remote.PlayerId] = remote.Level;
            }
        }

        if (PlayerControl.LocalPlayer && overlay.Local.IsSpeaking)
        {
            byte lid = PlayerControl.LocalPlayer.PlayerId;
            if (lid != byte.MaxValue)
            {
                _activeSpeakerIds.Add(lid);
                _activeSpeakerLevels[lid] = overlay.Local.Level;
            }
        }

        if (MeetingHud.Instance == null)
            MeetingSpeakingIndicatorPatch.UpdateIndicators(overlay);

        foreach (var kv in _slots)
        {
            kv.Value.IsSpeaking = false;
            kv.Value.TargetLevel = 0f;
        }

        // Add / rebuild slots for speaking players
        foreach (byte id in _activeSpeakerIds)
        {
            var liveFp = GetFingerprint(id);
            float level = _activeSpeakerLevels.TryGetValue(id, out var currentLevel) ? currentLevel : 0f;
            if (_slots.TryGetValue(id, out var slot))
            {
                if (slot.Fingerprint != liveFp)
                {
                    RemoveSlot(id);
                    InvalidateCacheFor(id);
                }
                else
                {
                    slot.TargetLevel = level;
                    slot.IsSpeaking = true;
                    continue;
                }
            }
            AddSlot(id, level);
        }

        UpdateSlotRings();

        _fadedSlotIds.Clear();
        foreach (var kv in _slots)
            if (!kv.Value.IsSpeaking && kv.Value.Visibility <= 0.01f)
                _fadedSlotIds.Add(kv.Key);
        foreach (var id in _fadedSlotIds) RemoveSlot(id);

        LayoutSlotsIfDirty();

        _barRoot.SetActive(_slots.Count > 0);
    }

    private static void EnsureBar(PingTracker template)
    {
        if (_barRoot != null) return;

        _barRoot   = new GameObject("VC_SpeakingBar");
        _barRoot.transform.SetParent(template.transform.parent, false);
        _barAspect = _barRoot.AddComponent<AspectPosition>();

        var settings = LocalSettingsTabSingleton<VoiceChatLocalSettings>.Instance;
        if (settings != null)
        {
            _barPosition    = settings.SpeakingBarPosition.Value;
            _layoutVertical = _barPosition is SpeakingBarPosition.TopLeft
                or SpeakingBarPosition.TopRight
                or SpeakingBarPosition.BottomLeft
                or SpeakingBarPosition.BottomRight;
        }

        ApplyPositionToAspect(_barAspect, _barPosition);
        _barAspect.AdjustPosition();
        _barRoot.SetActive(false);
    }

    private static void ApplyPositionToAspect(AspectPosition asp, SpeakingBarPosition pos)
    {
        switch (pos)
        {
            case SpeakingBarPosition.TopMiddle:
                asp.Alignment        = AspectPosition.EdgeAlignments.Top;
                asp.DistanceFromEdge = new Vector3(0f, 0.25f, 0f);
                break;
            case SpeakingBarPosition.TopRight:
                asp.Alignment        = AspectPosition.EdgeAlignments.RightTop;
                asp.DistanceFromEdge = new Vector3(1.2f, 0.25f, 0f);
                break;
            case SpeakingBarPosition.BottomLeft:
                asp.Alignment        = AspectPosition.EdgeAlignments.LeftBottom;
                asp.DistanceFromEdge = new Vector3(0.60f, 0.35f, 0f);
                break;
            case SpeakingBarPosition.BottomMiddle:
                asp.Alignment        = AspectPosition.EdgeAlignments.Bottom;
                asp.DistanceFromEdge = new Vector3(0f, 0.35f, 0f);
                break;
            case SpeakingBarPosition.BottomRight:
                asp.Alignment        = AspectPosition.EdgeAlignments.RightBottom;
                asp.DistanceFromEdge = new Vector3(1.2f, 0.35f, 0f);
                break;
            default:
                asp.Alignment        = AspectPosition.EdgeAlignments.LeftTop;
                asp.DistanceFromEdge = new Vector3(0.60f, 0.25f, 0f);
                break;
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    private static class MeetingStartCachePatch
    {
        private static void Postfix(MeetingHud __instance)
        {
            if (__instance.playerStates == null) return;
            EnsureCacheHolder();
            foreach (var state in __instance.playerStates)
            {
                if (state?.PlayerIcon == null) continue;
                RefreshCacheFromMeeting(state.TargetPlayerId, state.PlayerIcon.gameObject);
            }
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Update))]
    private static class MeetingUpdateCachePatch
    {
        private static void Postfix(MeetingHud __instance)
        {
            if (__instance.playerStates == null || _cacheHolder == null) return;
            foreach (var state in __instance.playerStates)
            {
                if (state?.PlayerIcon == null) continue;
                if (_iconCache.ContainsKey(state.TargetPlayerId)) continue;
                RefreshCacheFromMeeting(state.TargetPlayerId, state.PlayerIcon.gameObject);
            }
        }
    }

    private static void EnsureCacheHolder()
    {
        if (_cacheHolder != null) return;
        _cacheHolder = new GameObject("VC_IconCacheHolder");
        Object.DontDestroyOnLoad(_cacheHolder);
        _cacheHolder.SetActive(false);
    }

    private static void RefreshCacheFromMeeting(byte playerId, GameObject sourceGO)
    {
        EnsureCacheHolder();
        if (_iconCache.TryGetValue(playerId, out var old) && old != null)
            Object.Destroy(old);

        var clone = Object.Instantiate(sourceGO, _cacheHolder!.transform);
        clone.name = $"VC_CachedIcon_{playerId}";
        clone.SetActive(false);
        foreach (var anim in clone.GetComponentsInChildren<Animator>(true))
            anim.enabled = false;
        foreach (var rb in clone.GetComponentsInChildren<Rigidbody2D>(true))
            rb.simulated = false;
        
        ApplyTopSorting(clone);

        _iconCache[playerId]        = clone;
        _cacheFingerprint[playerId] = GetFingerprint(playerId);
    }

    private static void InvalidateCacheFor(byte playerId)
    {
        if (_iconCache.TryGetValue(playerId, out var old) && old != null)
            Object.Destroy(old);
        _iconCache.Remove(playerId);
        _cacheFingerprint.Remove(playerId);
    }

    private static void AddSlot(byte playerId, float voiceLevel)
    {
        if (_barRoot == null) return;

        var fp   = GetFingerprint(playerId);
        var slot = new SpeakerSlot
        {
            Fingerprint = fp,
            PlayerColor = GetPaletteColor(FindPlayer(playerId)),
            Level = voiceLevel,
            TargetLevel = voiceLevel,
            SmoothedLevel = NormalizeVoiceLevel(voiceLevel),
            IsSpeaking = true
        };
        bool gotIcon = false;

        if (MeetingHud.Instance?.playerStates != null)
        {
            foreach (var state in MeetingHud.Instance.playerStates)
            {
                if (state == null || state.TargetPlayerId != playerId || state.PlayerIcon == null)
                    continue;

                RefreshCacheFromMeeting(playerId, state.PlayerIcon.gameObject);

                var copy = Object.Instantiate(state.PlayerIcon.gameObject, _barRoot.transform);
                copy.SetActive(true);
                copy.transform.localScale = Vector3.one * IconScale;
                
                EnsurePlayerModelVisible(copy);
                
                ApplyTopSorting(copy);
                slot.IconGO = copy;
                gotIcon = true;
                break;
            }
        }

        if (!gotIcon
            && _iconCache.TryGetValue(playerId, out var tmpl) && tmpl != null
            && _cacheFingerprint.TryGetValue(playerId, out var cachedFp) && cachedFp == fp)
        {
            var copy = Object.Instantiate(tmpl, _barRoot.transform);
            copy.SetActive(true);
            copy.transform.localScale = Vector3.one * IconScale;
            EnsurePlayerModelVisible(copy);
            ApplyTopSorting(copy);
            slot.IconGO = copy;
            gotIcon = true;
        }

        if (!gotIcon)
            gotIcon = TryBuildIconFromPlayerControl(playerId, slot);

        if (!gotIcon)
        {
            var pc       = FindPlayer(playerId);
            var circleGO = new GameObject("VC_Circle");
            circleGO.transform.SetParent(_barRoot.transform, false);
            var sr              = circleGO.AddComponent<SpriteRenderer>();
            sr.sprite           = CreateCircleSprite();
            sr.color            = GetPaletteColor(pc);
            sr.sortingLayerName = VCSorting.Layer;
            sr.sortingOrder     = VCSorting.Base;
            circleGO.transform.localScale = Vector3.one * (IconScale * 0.65f);
            slot.IconGO = circleGO;
        }

        CreateRing(playerId, slot);

        var player  = FindPlayer(playerId);
        var labelGO = new GameObject("VC_Label");
        labelGO.transform.SetParent(_barRoot.transform, false);
        var tmp = labelGO.AddComponent<TextMeshPro>();
        tmp.text               = player?.Data?.PlayerName ?? "?";
        tmp.fontSize           = LabelSize;
        tmp.alignment          = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.sortingLayerID     = SortingLayer.NameToID(VCSorting.Layer);
        tmp.sortingOrder       = VCSorting.Text;
        tmp.color              = Color.white;
        tmp.rectTransform.sizeDelta = new Vector2(1.4f, 0.45f);
        slot.LabelTMP = tmp;
        _slots[playerId] = slot;
        _layoutDirty = true;
    }

    private static bool TryBuildIconFromPlayerControl(byte playerId, SpeakerSlot slot)
    {
        if (_barRoot == null) return false;
        var pc = FindPlayer(playerId);
        if (pc == null) return false;

        try
        {
            var cosLayer = pc.GetComponentInChildren<CosmeticsLayer>();
            if (cosLayer == null) return false;

            var clone = Object.Instantiate(cosLayer.gameObject, _barRoot.transform);
            clone.SetActive(true);
            clone.name = $"VC_CosIcon_{playerId}";

            foreach (var anim in clone.GetComponentsInChildren<Animator>(true))
                anim.enabled = false;
            foreach (var rb in clone.GetComponentsInChildren<Rigidbody2D>(true))
                rb.simulated = false;
            foreach (var mono in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                var type = mono.GetType().Name;
                if (!type.Contains("Renderer") && !type.Contains("Transform") 
                    && !type.Contains("Layout") && !type.Contains("Canvas")
                    && !type.Contains("RectTransform") && !type.Contains("Mask"))
                {
                    mono.enabled = false;
                }
            }

            ApplyTopSorting(clone);
            clone.transform.localScale    = Vector3.one * IconScale;
            clone.transform.localPosition = Vector3.zero;
            EnsurePlayerModelVisible(clone);
            
            slot.IconGO = clone;
            return true;
        }
        catch { return false; }
    }

    private static void EnsurePlayerModelVisible(GameObject iconGO)
    {
        if (iconGO == null) return;

        string[] possibleNames = { "PlayerModel", "BeanSprite", "CharacterVisual", "Sprite" };
        
        foreach (var modelName in possibleNames)
        {
            Transform modelTransform = iconGO.transform.Find(modelName);
            if (modelTransform != null)
            {
                modelTransform.gameObject.SetActive(true);
                var animator = modelTransform.GetComponentInChildren<Animator>(true);
                if (animator != null)
                    animator.enabled = false;
                modelTransform.SetAsLastSibling();
                
                return;
            }
        }
    }

    private static void RemoveSlot(byte id)
    {
        if (!_slots.TryGetValue(id, out var slot)) return;
        if (slot.IconGO   != null) Object.Destroy(slot.IconGO);
        if (slot.RingGO   != null) Object.Destroy(slot.RingGO);
        if (slot.LabelTMP != null) Object.Destroy(slot.LabelTMP.gameObject);
        _slots.Remove(id);
        _layoutDirty = true;
    }

    private static void CreateRing(byte playerId, SpeakerSlot slot)
    {
        if (_barRoot == null) return;

        var ringGO = new GameObject($"VC_SpeakingRing_{playerId}");
        ringGO.transform.SetParent(_barRoot.transform, false);
        ringGO.transform.localScale = Vector3.one * RingScale;

        var sr = ringGO.AddComponent<SpriteRenderer>();
        sr.sprite = CreateRingSprite();
        sr.sortingLayerName = VCSorting.Layer;
        sr.sortingOrder = VCSorting.Ring;
        sr.maskInteraction = SpriteMaskInteraction.None;

        slot.RingGO = ringGO;
        slot.RingRenderer = sr;
    }

    private static void UpdateSlotRings()
    {
        foreach (var kv in _slots)
        {
            var slot = kv.Value;
            if (slot.RingRenderer == null || slot.RingGO == null) continue;

            slot.SmoothedLevel = Mathf.Lerp(
                slot.SmoothedLevel,
                NormalizeVoiceLevel(slot.TargetLevel),
                Mathf.Clamp01(Time.deltaTime * LevelSmoothSpeed));
            slot.Visibility = Mathf.MoveTowards(
                slot.Visibility,
                slot.IsSpeaking ? 1f : 0f,
                Time.deltaTime * (slot.IsSpeaking ? FadeInSpeed : FadeOutSpeed));
            slot.Level = slot.TargetLevel;

            float brightness = Mathf.SmoothStep(0f, 1f, slot.SmoothedLevel);
            Color color = Color.Lerp(slot.PlayerColor, new Color(0.55f, 1f, 0f, 1f), 0.72f);
            color.a = Mathf.Lerp(0.22f, 0.92f, brightness) * slot.Visibility;
            slot.RingRenderer.color = color;
            slot.RingGO.transform.localScale = Vector3.one * RingScale;
            slot.RingRenderer.enabled = slot.Visibility > 0.01f;
        }
    }

    private static float NormalizeVoiceLevel(float level)
    {
        if (level <= 0.003f) return 0f;
        float normalized = Mathf.InverseLerp(0.003f, 0.55f, level);
        return Mathf.Pow(Mathf.Clamp01(normalized), 0.65f);
    }

    private static void LayoutSlots()
    {
        _layoutDirty = false;
        if (_layoutVertical)
        {
            float totalH = _slots.Count * SlotHeight;
            float startY = totalH * 0.5f - SlotHeight * 0.5f;
            int i = 0;
            foreach (var kv in _slots)
            {
                float y = startY - i * SlotHeight;
                if (kv.Value.IconGO   != null)
                    kv.Value.IconGO.transform.localPosition  = new Vector3(0f, y, 0f);
                if (kv.Value.RingGO   != null)
                    kv.Value.RingGO.transform.localPosition  = new Vector3(0f, y, 0.02f);
                if (kv.Value.LabelTMP != null)
                    kv.Value.LabelTMP.transform.localPosition = new Vector3(0f, y - LabelOffset, 0f);
                i++;
            }
        }
        else
        {
            float totalW = _slots.Count * SlotWidth;
            float startX = -totalW * 0.5f + SlotWidth * 0.5f;
            int i = 0;
            foreach (var kv in _slots)
            {
                float x = startX + i * SlotWidth;
                if (kv.Value.IconGO   != null)
                    kv.Value.IconGO.transform.localPosition  = new Vector3(x, 0f, 0f);
                if (kv.Value.RingGO   != null)
                    kv.Value.RingGO.transform.localPosition  = new Vector3(x, 0f, 0.02f);
                if (kv.Value.LabelTMP != null)
                    kv.Value.LabelTMP.transform.localPosition = new Vector3(x, -LabelOffset, 0f);
                i++;
            }
        }
    }

    private static void LayoutSlotsIfDirty()
    {
        if (_layoutDirty)
            LayoutSlots();
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Start))]
    private static class HudStartPatch
    {
        private static void Postfix()
        {
            foreach (var kv in _slots)
            {
                if (kv.Value.IconGO   != null) Object.Destroy(kv.Value.IconGO);
                if (kv.Value.RingGO   != null) Object.Destroy(kv.Value.RingGO);
                if (kv.Value.LabelTMP != null) Object.Destroy(kv.Value.LabelTMP.gameObject);
            }
            _slots.Clear();
            _layoutDirty = false;
            if (_barRoot != null) { Object.Destroy(_barRoot); _barRoot = null; }
            _barAspect = null;
        }
    }

    private static void ApplyTopSorting(GameObject go)
    {
        foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
        {
            sr.sortingLayerName = VCSorting.Layer;
            sr.sortingOrder     = VCSorting.Base;
            sr.maskInteraction  = SpriteMaskInteraction.None;
        }
    }

    private static OutfitFingerprint GetFingerprint(byte playerId)
    {
        var pc = FindPlayer(playerId);
        if (pc?.Data == null) return default;
        var o = pc.Data.DefaultOutfit;
        return new OutfitFingerprint(o.ColorId, o.HatId, o.SkinId, o.VisorId);
    }

    private static PlayerControl? FindPlayer(byte id)
    {
        foreach (var pc in PlayerControl.AllPlayerControls)
            if (pc != null && pc.PlayerId == id) return pc;
        return null;
    }

    private static Color GetPaletteColor(PlayerControl? pc)
    {
        if (pc?.Data == null) return new Color(0.18f, 0.80f, 0.44f, 1f);
        int cid = pc.Data.DefaultOutfit.ColorId;
        return cid >= 0 && cid < Palette.PlayerColors.Length
            ? (Color)Palette.PlayerColors[cid]
            : Color.white;
    }

    private static Sprite? _circleSprite;
    private static Sprite? _ringSprite;
    private static Sprite CreateCircleSprite()
    {
        if (_circleSprite != null) return _circleSprite;
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - r + 0.5f, dy = y - r + 0.5f;
            float d  = Mathf.Sqrt(dx * dx + dy * dy);
            tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01((r - d) * 2f)));
        }
        tex.Apply();
        _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return _circleSprite;
    }

    private static Sprite CreateRingSprite()
    {
        if (_ringSprite != null) return _ringSprite;

        const int size = 128;
        const float inner = 0.88f;
        const float feather = 1.6f;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float center = size * 0.5f;
        float outerR = center - 1f;
        float innerR = outerR * inner;

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - center + 0.5f;
            float dy = y - center + 0.5f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float outerA = Mathf.Clamp01((outerR - d) / feather);
            float innerA = Mathf.Clamp01((d - innerR) / feather);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, outerA * innerA));
        }

        tex.Apply();
        _ringSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        _ringSprite.hideFlags |= HideFlags.HideAndDontSave;
        return _ringSprite;
    }

    // ── Data types ─────────────────────────────────────────────────────────────
    private readonly record struct OutfitFingerprint(
        int ColorId, string HatId, string SkinId, string VisorId);

    private class SpeakerSlot
    {
        public GameObject?       IconGO;
        public GameObject?       RingGO;
        public SpriteRenderer?   RingRenderer;
        public TextMeshPro?      LabelTMP;
        public OutfitFingerprint Fingerprint;
        public Color             PlayerColor;
        public float             Level;
        public float             TargetLevel;
        public float             SmoothedLevel;
        public float             Visibility;
        public bool              IsSpeaking;
    }
}
