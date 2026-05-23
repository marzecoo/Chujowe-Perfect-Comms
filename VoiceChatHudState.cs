using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MiraAPI.LocalSettings;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using static UnityEngine.UI.Button;
using Object = UnityEngine.Object;

namespace VoiceChatPlugin.VoiceChat;

public static class VoiceChatHudState
{
    // ── Grid-injected buttons ─────────────────────────────────────────────────
    // Parented into HudManagerPatches.UiTopRight so GridArrange positions them.
    // AspectPosition is destroyed on each button — same as TOU-Mira does for
    // every button it adds to that row (MapButton, chat, settings, zoom, wiki…).
    // UiGrid.ArrangeChilds() runs every HudManager.Update frame and will place
    // them automatically at the end of the row.

    private static GameObject? _micButtonObj;
    private static PassiveButton? _micButton;
    private static GameObject? _spkButtonObj;
    private static PassiveButton? _spkButton;
    private static GameObject? _jailButtonObj;
    private static PassiveButton? _jailButton;

    // ── Tooltip objects (parented to HUD root, not the grid) ──────────────────
    private static GameObject?  _micTooltip;
    private static GameObject?  _spkTooltip;
    private static TextMeshPro? _micTooltipTmp;
    private static TextMeshPro? _spkTooltipTmp;

    // ── State ─────────────────────────────────────────────────────────────────
    private static bool _micMuted;
    private static bool _impostorHeld;
    private static bool _pushToTalkHeld;
    private static bool _speakerMuted;

    public static bool IsMuted        => _micMuted;
    public static bool IsImpostorRadio => _impostorHeld && CanUseImpostorRadio();
    public static bool IsSpeakerMuted  => _speakerMuted;

    // ── Init ──────────────────────────────────────────────────────────────────

    internal static void Init()
    {
        SceneManager.sceneLoaded +=
            (UnityEngine.Events.UnityAction<Scene, LoadSceneMode>)((_, __) =>
            {
                DestroyButtons();
                DestroyTooltips();
            });

        var settings = LocalSettingsTabSingleton<VoiceChatLocalSettings>.Instance;
        if (settings != null)
        {
            _micMuted     = settings.StartMuted.Value;
            _speakerMuted = settings.StartDeafened.Value;
        }
    }

    // ── Public HUD entry point (called every frame by VCManager) ─────────────

    internal static void UpdateHud()
    {
        var hud = HudManager.Instance;
        if (hud == null) return;

        EnsureGridButtons(hud);
        EnsureTooltips(hud);
        VoiceRoleMuteState.Update();
        ApplyMicState();
        UpdateJailButtonVisibility();
        RefreshButtonVisuals();
    }

    // ── Grid button creation ──────────────────────────────────────────────────
    //
    // TOU-Mira's CreateUiRow() runs from HudManager.Update() and only executes
    // once LocalPlayer + LocalPlayer.Data are non-null. It sets UiTopRight to
    // instance.MapButton.transform.parent.gameObject and attaches a GridArrange
    // to it. After that, UiGrid.ArrangeChilds() is called every Update frame.
    //
    // The correct way to add a button to the row (matching what TOU-Mira does
    // for ZoomButton, WikiButton, etc.):
    //   1. Instantiate from instance.MapButton.gameObject.
    //   2. Parent to UiTopRight.transform.
    //   3. Destroy the cloned AspectPosition — GridArrange owns layout now.
    //   4. SetAsLastSibling so it appears at the end of the row.
    //
    // We don't call ArrangeChilds() ourselves — TOU-Mira already does it every
    // Update and will pick up our new children automatically.

    private static void EnsureGridButtons(HudManager hud)
    {
        // Resolve UiTopRight via the static field on HudManagerPatches.
        // This is set by CreateUiRow which runs every HudManager.Update, so it
        // will be non-null very shortly after entering a game.
        var uiTopRight = GetUiTopRight();
        if (uiTopRight == null) return;

        if (_micButtonObj == null)
        {
            _micButtonObj = CreateGridButton(hud, uiTopRight, "VC_MicButton",
                "VoiceChatPlugin.Resources.MicOn.png", ToggleMutePublic,
                ShowMicTooltip, HideTooltips);
            _micButton = _micButtonObj.GetComponent<PassiveButton>();
            VoiceChatPluginMain.Logger.LogInfo("[VC] Mic button created and added to UiTopRight grid.");
        }

        if (_spkButtonObj == null)
        {
            _spkButtonObj = CreateGridButton(hud, uiTopRight, "VC_SpkButton",
                "VoiceChatPlugin.Resources.SpeakerOn.png", ToggleSpeakerPublic,
                ShowSpeakerTooltip, HideTooltips);
            _spkButton = _spkButtonObj.GetComponent<PassiveButton>();
            VoiceChatPluginMain.Logger.LogInfo("[VC] Speaker button created and added to UiTopRight grid.");
        }

        if (_jailButtonObj == null)
        {
            _jailButtonObj = CreateGridButton(hud, uiTopRight, "VC_JailUnmuteButton",
                "VoiceChatPlugin.Resources.JailUnmute.png", JailUnmutePublic,
                null, null);
            _jailButton = _jailButtonObj.GetComponent<PassiveButton>();
            _jailButtonObj.SetActive(false);
        }

        // Keep our buttons at the end of the row. SetAsLastSibling is cheap and
        // harmless to call every frame — GridArrange lays out left-to-right in
        // sibling order, so last sibling = rightmost position.
        if (_micButtonObj  != null) _micButtonObj.transform.SetAsLastSibling();
        if (_spkButtonObj  != null) _spkButtonObj.transform.SetAsLastSibling();
        if (_jailButtonObj != null) _jailButtonObj.transform.SetAsLastSibling();
    }

    /// <summary>
    /// Creates one button in the TOU-Mira UiTopRight grid row.
    /// Matches TOU-Mira's pattern exactly: clone MapButton, re-parent into the
    /// grid container, destroy AspectPosition, set our icon and click handler.
    /// </summary>
    private static GameObject CreateGridButton(
        HudManager hud,
        GameObject uiTopRight,
        string name,
        string iconResource,
        Action onClick,
        Action? onMouseOver,
        Action? onMouseOut)
    {
        // Clone MapButton — same source TOU-Mira uses for ZoomButton/WikiButton.
        var go = Object.Instantiate(hud.MapButton.gameObject, uiTopRight.transform);
        go.name = name;

        // Destroy the cloned AspectPosition. GridArrange owns layout; a
        // lingering AspectPosition would fight it and move the button every frame.
        var ap = go.GetComponent<AspectPosition>();
        if (ap != null) Object.Destroy(ap);

        // Also kill any AspectPosition on children (MapButton has one on a child).
        foreach (var childAp in go.GetComponentsInChildren<AspectPosition>())
            Object.Destroy(childAp);

        // Hide all existing sprites on the clone — we add our own icon child.
        ClearButtonBG(go);

        // Add our icon as a child SpriteRenderer in the UI sorting layer.
        CreateIconChild(go, iconResource);

        // Wire up click / hover.
        var pb = go.GetComponent<PassiveButton>();
        pb.OnClick = new ButtonClickedEvent();
        pb.OnClick.AddListener((UnityAction)(() => onClick()));
        pb.OnMouseOver = new UnityEvent();
        pb.OnMouseOut  = new UnityEvent();
        if (onMouseOver != null)
            pb.OnMouseOver.AddListener((UnityAction)(() => onMouseOver()));
        if (onMouseOut != null)
            pb.OnMouseOut.AddListener((UnityAction)(() => onMouseOut()));

        return go;
    }

    // ── UiTopRight access ─────────────────────────────────────────────────────
    //
    // UiTopRight is a static field on TownOfUs.Patches.HudManagerPatches.
    // We read it via reflection once per scene (cached after first non-null hit).
    // It is set to instance.MapButton.transform.parent.gameObject by CreateUiRow
    // on the first HudManager.Update where LocalPlayer is ready.

    private static GameObject? _cachedUiTopRight;
    private static bool        _uiTopRightSearched;
    private static float       _retryTimer;
    private const  float       RetryInterval = 0.25f;

    private static GameObject? GetUiTopRight()
    {
        if (_cachedUiTopRight != null) return _cachedUiTopRight;

        // Throttle the reflection scan — no need to run it every frame.
        _retryTimer -= Time.deltaTime;
        if (_retryTimer > 0f) return null;
        _retryTimer = RetryInterval;

        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("TownOfUs.Patches.HudManagerPatches");
                if (t == null) continue;

                var fi = t.GetField("UiTopRight",
                    BindingFlags.Public | BindingFlags.Static);

                if (fi == null)
                {
                    if (!_uiTopRightSearched)
                    {
                        _uiTopRightSearched = true;
                        VoiceChatPluginMain.Logger.LogWarning(
                            "[VC] HudManagerPatches found but has no 'UiTopRight' field. " +
                            "TOU-Mira version mismatch — VC buttons will not appear.");
                    }
                    return null;
                }

                var result = fi.GetValue(null) as GameObject;
                if (result != null)
                {
                    _cachedUiTopRight = result;
                    VoiceChatPluginMain.Logger.LogInfo(
                        "[VC] UiTopRight resolved — VC buttons will be added to the grid row.");
                    return _cachedUiTopRight;
                }

                // Field exists but is still null: CreateUiRow hasn't fired yet
                // because LocalPlayer isn't ready. Normal — keep retrying silently.
                return null;
            }

            // TOU-Mira assembly not found at all.
            if (!_uiTopRightSearched)
            {
                _uiTopRightSearched = true;
                VoiceChatPluginMain.Logger.LogWarning(
                    "[VC] TownOfUs.Patches.HudManagerPatches not found in any assembly. " +
                    "Is TOU-Mira installed? VC buttons will not appear in the grid.");
            }
        }
        catch (Exception ex)
        {
            VoiceChatPluginMain.Logger.LogError($"[VC] GetUiTopRight error: {ex.Message}");
        }

        return null;
    }

    // ── Jail button visibility ────────────────────────────────────────────────

    private static void UpdateJailButtonVisibility()
    {
        if (_jailButtonObj == null) return;
        _jailButtonObj.SetActive(VoiceRoleMuteState.CanLocalJailorUnmute(out _));
    }

    // ── Tooltips ──────────────────────────────────────────────────────────────

    private static void EnsureTooltips(HudManager hud)
    {
        var root = hud.transform.parent != null ? hud.transform.parent : hud.transform;
        if (_micTooltip == null)
            _micTooltip = CreateTooltipObject(root, out _micTooltipTmp);
        if (_spkTooltip == null)
            _spkTooltip = CreateTooltipObject(root, out _spkTooltipTmp);
    }

    private static void ShowMicTooltip()
    {
        if (_micTooltip == null || _micTooltipTmp == null || _micButtonObj == null) return;

        string status = _micMuted ? "Muted"
            : IsInImpostorRadioMode() ? "Impostor Radio (held)"
            : "Active";

        var tab = LocalSettingsTabSingleton<VoiceChatLocalSettings>.Instance;
        string muteKey  = VoiceChatKeybinds.ToggleMute.CurrentKey.ToString();
        string radioKey = VoiceChatKeybinds.ImpostorRadio.CurrentKey.ToString();

        _micTooltipTmp.text =
            "<b>Microphone</b>\n" +
            $"Status: {status}\n" +
            $"Volume: {(int)(tab.MicVolume.Value * 100f)}%\n" +
            $"Mute: {muteKey}  |  Imp. Radio: {radioKey} (hold)";

        PositionNear(_micTooltip, _micButtonObj);
        _micTooltip.SetActive(true);
    }

    private static void ShowSpeakerTooltip()
    {
        if (_spkTooltip == null || _spkTooltipTmp == null || _spkButtonObj == null) return;

        string status = _speakerMuted ? "Muted" : "Active";
        var tab = LocalSettingsTabSingleton<VoiceChatLocalSettings>.Instance;
        string hotkey = VoiceChatKeybinds.ToggleSpeaker.CurrentKey.ToString();

        _spkTooltipTmp.text =
            "<b>Speaker</b>\n" +
            $"Status: {status}\n" +
            $"Volume: {(int)(tab.MasterVolume.Value * 100f)}%\n" +
            $"Hotkey: {hotkey}";

        PositionNear(_spkTooltip, _spkButtonObj);
        _spkTooltip.SetActive(true);
    }

    private static void HideTooltips()
    {
        _micTooltip?.SetActive(false);
        _spkTooltip?.SetActive(false);
    }

    private static void PositionNear(GameObject tooltip, GameObject btn)
    {
        var p = btn.transform.position;
        tooltip.transform.position = new Vector3(p.x - 0.2f, p.y - 0.9f, p.z - 1f);
    }

    private static GameObject CreateTooltipObject(Transform root, out TextMeshPro tmp)
    {
        var go = new GameObject("VC_Tooltip");
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3(0f, 0f, -80f);

        var bg = new GameObject("BG");
        bg.transform.SetParent(go.transform, false);
        var bgSr = bg.AddComponent<SpriteRenderer>();
        bgSr.sprite = CreateSolidSprite(new Color(0f, 0f, 0f, 0.82f));
        bgSr.sortingLayerName = VCSorting.Layer;
        bgSr.sortingOrder = 32761;
        bg.transform.localScale = new Vector3(2.6f, 2.0f, 1f);

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        textGo.transform.localPosition = new Vector3(0f, 0f, -80f);
        tmp = textGo.AddComponent<TextMeshPro>();
        tmp.fontSize = 1.5f;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.sortingLayerID = SortingLayer.NameToID(VCSorting.Layer);
        tmp.sortingOrder = 32762;
        tmp.rectTransform.sizeDelta = new Vector2(2.4f, 1.8f);
        go.SetActive(false);
        return go;
    }

    // ── Button visual refresh ─────────────────────────────────────────────────

    private static void RefreshButtonVisuals()
    {
        if (_micButtonObj != null)
        {
            var sr = _micButtonObj.transform.Find("VCIcon")?.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                if (VoiceRoleMuteState.TryGetLocalMeetingVoiceBlockReason(out _))
                {
                    sr.sprite = Sprites.MicOff;
                    sr.color  = new Color(1f, 0.65f, 0.15f);
                }
                else if (_micMuted)
                {
                    sr.sprite = Sprites.MicOff;
                    sr.color  = new Color(1f, 0.4f, 0.4f);
                }
                else if (IsInImpostorRadioMode())
                {
                    sr.sprite = Sprites.MicOn;
                    sr.color  = new Color(1f, 0.55f, 0.1f);
                }
                else
                {
                    sr.sprite = Sprites.MicOn;
                    sr.color  = Color.white;
                }
            }
        }

        if (_spkButtonObj != null)
        {
            var sr = _spkButtonObj.transform.Find("VCIcon")?.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.sprite = _speakerMuted ? Sprites.SpkOff : Sprites.SpkOn;
                sr.color  = _speakerMuted ? new Color(1f, 0.4f, 0.4f) : Color.white;
            }
        }

        if (_jailButtonObj != null)
        {
            var sr = _jailButtonObj.transform.Find("VCIcon")?.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.sprite = Sprites.JailUnmute;
                sr.color  = Color.white;
            }
        }
    }

    // ── State mutators ────────────────────────────────────────────────────────

    internal static void ApplyMicState()
    {
        var settings = LocalSettingsTabSingleton<VoiceChatLocalSettings>.Instance;
        bool radioTransmit   = IsInImpostorRadioMode();
        bool pushToTalkMuted = settings?.MicMode.Value == VoiceMicMode.PushToTalk
                               && !_pushToTalkHeld
                               && !radioTransmit;
        bool roleMuted = VoiceRoleMuteState.IsLocalMeetingVoiceBlocked();
        VoiceChatRoom.Current?.SetMute(_micMuted || pushToTalkMuted || roleMuted);
    }

    internal static void ApplySpeakerState()
    {
        if (_speakerMuted)
        {
            VoiceChatRoom.Current?.SetMasterVolume(0f);
        }
        else
        {
            var tab = LocalSettingsTabSingleton<VoiceChatLocalSettings>.Instance;
            if (tab != null)
                VoiceChatRoom.Current?.SetMasterVolume(tab.MasterVolume.Value);
        }
    }

    internal static void TrySyncHostRoomSettings() { }

    internal static void ToggleMutePublic() => SetMuted(!_micMuted);

    internal static void SetMuted(bool muted)
    {
        _micMuted = muted;
        ApplyMicState();
        if (muted)
            MeetingSpeakingIndicatorPatch.ClearLocalIndicator();
        RefreshButtonVisuals();
    }

    internal static void ToggleSpeakerPublic() => SetSpeakerMuted(!_speakerMuted);

    internal static void SetSpeakerMuted(bool muted)
    {
        _speakerMuted = muted;
        ApplySpeakerState();
        RefreshButtonVisuals();
    }

    internal static void JailUnmutePublic()
    {
        VoiceRoleMuteState.LocalJailorAllowVoice();
        UpdateJailButtonVisibility();
        RefreshButtonVisuals();
    }

    internal static void UpdateImpostorRadioHold(bool held, bool justPressed, bool justReleased)
    {
        if (!CanUseImpostorRadio())
        {
            if (_impostorHeld)
            {
                _impostorHeld = false;
                ApplyMicState();
                RefreshButtonVisuals();
            }
            return;
        }

        bool prev = _impostorHeld;
        _impostorHeld = held;
        if (prev != _impostorHeld)
        {
            ApplyMicState();
            RefreshButtonVisuals();
        }
    }

    internal static bool IsInImpostorRadioMode()
        => _impostorHeld && CanUseImpostorRadio() && !_micMuted;

    internal static void UpdatePushToTalkHeld(bool held)
    {
        if (_pushToTalkHeld == held) return;
        _pushToTalkHeld = held;
        ApplyMicState();
        RefreshButtonVisuals();
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    private static void DestroyButtons()
    {
        if (_micButtonObj  != null) { Object.Destroy(_micButtonObj);  _micButtonObj  = null; }
        if (_spkButtonObj  != null) { Object.Destroy(_spkButtonObj);  _spkButtonObj  = null; }
        if (_jailButtonObj != null) { Object.Destroy(_jailButtonObj); _jailButtonObj = null; }
        _micButton  = null;
        _spkButton  = null;
        _jailButton = null;

        // Reset the UiTopRight cache so we find it fresh next scene.
        // TOU-Mira reassigns it each time HudManager starts, so the old
        // cached reference becomes stale after a scene change.
        _cachedUiTopRight   = null;
        _uiTopRightSearched = false;
        _retryTimer         = 0f;
    }

    private static void DestroyTooltips()
    {
        if (_micTooltip != null) { Object.Destroy(_micTooltip); _micTooltip = null; }
        if (_spkTooltip != null) { Object.Destroy(_spkTooltip); _spkTooltip = null; }
        _micTooltipTmp = null;
        _spkTooltipTmp = null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool CanUseImpostorRadio()
        => PlayerControl.LocalPlayer != null
        && PlayerControl.LocalPlayer.Data?.Role?.IsImpostor == true
        && PlayerControl.LocalPlayer.Data?.IsDead == false
        && VoiceChatGameOptions.GetInstance().ImpostorPrivateRadio.Value;

    private static void ClearButtonBG(GameObject obj)
    {
        foreach (var sr in obj.GetComponentsInChildren<SpriteRenderer>())
            sr.color = Color.clear;
    }

    private static void CreateIconChild(GameObject parent, string resource)
    {
        var go = new GameObject("VCIcon");
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.layer = parent.layer;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite(resource);
        sr.sortingLayerName = VCSorting.Layer;
        sr.sortingOrder = 32760;
    }

    private static Sprite CreateSolidSprite(Color c)
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, c);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
    }

    private static readonly Dictionary<string, Sprite> _spriteCache = new();

    public static Sprite LoadSprite(string path, bool highQuality = false)
    {
        var cacheKey = highQuality ? path + "#hq" : path;
        if (_spriteCache.TryGetValue(cacheKey, out var cached)) return cached;
        try
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, highQuality)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = highQuality ? 16 : 1,
                mipMapBias = highQuality ? -1.15f : 0f,
            };
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(path)!;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            tex.LoadImage(ms.ToArray(), false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = highQuality ? 16 : 1;
            tex.mipMapBias = highQuality ? -1.15f : 0f;
            if (highQuality)
                tex.Apply(true, false);
            var spr = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 900f);
            spr.hideFlags |= HideFlags.HideAndDontSave | HideFlags.DontSaveInEditor;
            _spriteCache[cacheKey] = spr;
            return spr;
        }
        catch
        {
            VoiceDiagnostics.DebugError("[VC] Sprite load failed: " + path);
            return null!;
        }
    }

    private static class Sprites
    {
        public static Sprite MicOn      => LoadSprite("VoiceChatPlugin.Resources.MicOn.png");
        public static Sprite MicOff     => LoadSprite("VoiceChatPlugin.Resources.MicOff.png");
        public static Sprite SpkOn      => LoadSprite("VoiceChatPlugin.Resources.SpeakerOn.png");
        public static Sprite SpkOff     => LoadSprite("VoiceChatPlugin.Resources.SpeakerOff.png");
        public static Sprite JailUnmute => LoadSprite("VoiceChatPlugin.Resources.JailUnmute.png");
    }
}