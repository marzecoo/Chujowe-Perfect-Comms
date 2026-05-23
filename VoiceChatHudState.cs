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
    // ── Grid-parented button objects ──────────────────────────────────────────
    private static GameObject? _micButtonObj;
    private static GameObject? _spkButtonObj;
    private static GameObject? _jailButtonObj;

    private static PassiveButton? _micButton;
    private static PassiveButton? _spkButton;
    private static PassiveButton? _jailButton;

    // Label TMP components on each button
    private static TextMeshPro? _micLabelTmp;
    private static TextMeshPro? _spkLabelTmp;
    private static TextMeshPro? _jailLabelTmp;

    // ── State ─────────────────────────────────────────────────────────────────
    private static bool _micMuted;
    private static bool _impostorHeld;
    private static bool _pushToTalkHeld;
    private static bool _speakerMuted;
    private static float _overlayScale = 1f;   // kept for API compat, not used for position

    public static bool IsMuted        => _micMuted;
    public static bool IsImpostorRadio => _impostorHeld && CanUseImpostorRadio();
    public static bool IsSpeakerMuted => _speakerMuted;

    // ── Constants ─────────────────────────────────────────────────────────────
    // The TOU Mira grid uses CellSize 0.85×0.85.  We match that so each button
    // occupies exactly one grid cell.  The icon lives centred in the cell and a
    // small TMP label is placed just below it (local-space, so it extends below
    // the cell but doesn't affect grid layout).
    private const float IconLocalScale  = 0.55f;   // sprite scale inside cell
    private const float LabelFontSize   = 1.15f;   // matches DraftCancelButton style
    private const float LabelYOffset    = -0.15f;  // below cell centre (tightened)
    private const float RingScale   = 0.48f;
    private const int   ButtonSortOrder = 32760;

    // ── Init ──────────────────────────────────────────────────────────────────
    internal static void Init()
    {
        SceneManager.sceneLoaded +=
            (UnityEngine.Events.UnityAction<Scene, LoadSceneMode>)((_, __) => DestroyButtons());

        var settings = LocalSettingsTabSingleton<VoiceChatLocalSettings>.Instance;
        if (settings != null)
        {
            _micMuted     = settings.StartMuted.Value;
            _speakerMuted = settings.StartDeafened.Value;
        }
    }

    // ── Main update called every frame ────────────────────────────────────────
    internal static void UpdateHud()
    {
        var hud = HudManager.Instance;
        if (hud == null) return;

        EnsureHudButtons(hud);
        VoiceRoleMuteState.Update();
        ApplyMicState();
        UpdateHudButtonsVisibility();
        RefreshButtonVisuals();
    }

    // ── Ensure buttons are created and parented to UiTopRight ─────────────────
    private static void EnsureHudButtons(HudManager hud)
    {
        // UiTopRight is the grid container created by TOU Mira's HudManagerPatches.
        // It is hud.MapButton.transform.parent.gameObject.
        // We wait until it exists (TOU Mira creates it during Update, not Start).
        var uiTopRight = hud.MapButton?.transform.parent?.gameObject;
        if (uiTopRight == null) return;

        if (_micButtonObj == null)
            _micButtonObj = CreateGridButton(hud, uiTopRight, "VC_MicButton",
                "VoiceChatPlugin.Resources.MicOn.png", ToggleMutePublic,
                MicLabelText(), out _micButton, out _micLabelTmp);

        if (_spkButtonObj == null)
            _spkButtonObj = CreateGridButton(hud, uiTopRight, "VC_SpkButton",
                "VoiceChatPlugin.Resources.SpeakerOn.png", ToggleSpeakerPublic,
                SpkLabelText(), out _spkButton, out _spkLabelTmp);

        // Jail button: created but kept hidden until needed.
        if (_jailButtonObj == null)
            _jailButtonObj = CreateGridButton(hud, uiTopRight, "VC_JailUnmuteButton",
                "VoiceChatPlugin.Resources.JailUnmute.png", JailUnmutePublic,
                "Unmute", out _jailButton, out _jailLabelTmp);

        // Ensure our buttons are always the last siblings so they appear at the
        // end of the grid row.  Do this every frame — other buttons may be added.
        if (_micButtonObj  != null) _micButtonObj.transform.SetAsLastSibling();
        if (_spkButtonObj  != null) _spkButtonObj.transform.SetAsLastSibling();
        if (_jailButtonObj != null) _jailButtonObj.transform.SetAsLastSibling();
    }

    // ── Create a single grid-compatible button ────────────────────────────────
    private static GameObject CreateGridButton(
        HudManager hud,
        GameObject parent,
        string name,
        string iconResource,
        Action onClick,
        string labelText,
        out PassiveButton passiveButton,
        out TextMeshPro labelTmp)
    {
        // Clone MapButton so the collider, renderer hierarchy, and layer are
        // already set up correctly for the TOU grid.
        var go = Object.Instantiate(hud.MapButton.gameObject, parent.transform);
        go.name = name;

        // Remove any AspectPosition — position is fully managed by GridArrange.
        var ap = go.GetComponent<AspectPosition>();
        if (ap != null) Object.Destroy(ap);

        // Clear the cloned sprites so only our icon shows.
        foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
            sr.color = Color.clear;

        // Icon child.
        var iconGO = new GameObject("VCIcon");
        iconGO.transform.SetParent(go.transform, false);
        iconGO.transform.localPosition = Vector3.zero;
        iconGO.layer = go.layer;
        var iconSr = iconGO.AddComponent<SpriteRenderer>();
        iconSr.sprite = LoadSprite(iconResource);
        iconSr.sortingLayerName = VCSorting.Layer;
        iconSr.sortingOrder     = ButtonSortOrder;
        go.transform.localScale = Vector3.one * IconLocalScale;

        // Label child — styled like Among Us' Brooke font: black text with white
        // outline, placed just below the button cell in local space.
        var labelGO = new GameObject("VCLabel");
        labelGO.transform.SetParent(go.transform, false);
        // Compensate local-scale so the label renders at a readable world size.
        float invScale = IconLocalScale > 0f ? 1f / IconLocalScale : 1f;
        labelGO.transform.localScale    = new Vector3(invScale, invScale, 1f);
        labelGO.transform.localPosition = new Vector3(0f, LabelYOffset * invScale, -0.1f);
        labelGO.layer = go.layer;

        var tmp = labelGO.AddComponent<TextMeshPro>();
        tmp.text               = labelText;
        tmp.fontSize           = LabelFontSize;
        tmp.fontSizeMin        = LabelFontSize;
        tmp.fontSizeMax        = LabelFontSize;
        tmp.alignment          = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.sortingLayerID     = SortingLayer.NameToID(VCSorting.Layer);
        tmp.sortingOrder       = ButtonSortOrder + 1;
        tmp.characterSpacing = 2f;
        tmp.color     = Color.black;
        var brookeFont = hud.KillButton?.buttonLabelText?.font;
        if (brookeFont != null) tmp.font = brookeFont;
        var mat = Object.Instantiate(tmp.fontMaterial);
        mat.EnableKeyword("OUTLINE_ON");
        mat.SetColor(ShaderUtilities.ID_OutlineColor, Color.white);
        mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.15f);
        mat.SetFloat(ShaderUtilities.ID_FaceDilate,   0.20f);
        tmp.fontMaterial = mat;
        tmp.rectTransform.sizeDelta = new Vector2(1.8f, 0.45f);

        // Wire the click handler.
        passiveButton = go.GetComponent<PassiveButton>();
        passiveButton.OnClick = new ButtonClickedEvent();
        passiveButton.OnClick.AddListener((Action)onClick);
        passiveButton.OnMouseOver = new UnityEvent();
        passiveButton.OnMouseOut  = new UnityEvent();

        labelTmp = tmp;
        return go;
    }

    // ── Visibility ────────────────────────────────────────────────────────────
    private static void UpdateHudButtonsVisibility()
    {
        _micButtonObj?.SetActive(true);
        _spkButtonObj?.SetActive(true);

        bool jailVisible = VoiceRoleMuteState.CanLocalJailorUnmute(out _);
        _jailButtonObj?.SetActive(jailVisible);
    }

    // ── Visual state refresh ──────────────────────────────────────────────────
    private static void RefreshButtonVisuals()
    {
        // Mic button
        if (_micButtonObj != null)
        {
            var sr = _micButtonObj.transform.Find("VCIcon")?.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                if (VoiceRoleMuteState.TryGetLocalMeetingVoiceBlockReason(out _))
                {
                    sr.sprite = Sprites.MicOff;
                    sr.color  = new Color(1f, 0.65f, 0.15f);
                    if(_micLabelTmp != null)
                    _micLabelTmp.color = new Color(1f, 0.65f, 0.15f);
                }
                else if (_micMuted)
                {
                    sr.sprite = Sprites.MicOff;
                    sr.color  = new Color(1f, 0.4f, 0.4f);
                    if(_micLabelTmp != null)
                    _micLabelTmp.color = new Color(1f, 0.4f, 0.4f);

                }
                else if (IsInImpostorRadioMode())
                {
                    sr.sprite = Sprites.MicOn;
                    sr.color  = new Color(1f, 0.55f, 0.1f);
                    if(_micLabelTmp != null)
                    {
                        _micLabelTmp.color     = new Color(1f, 0.55f, 0.1f);

                    }
                }
                else
                {
                    sr.sprite = Sprites.MicOn;
                    sr.color  = Color.white;
                    if(_micLabelTmp != null)
                    {
                        _micLabelTmp.color = Color.black;
                    }
                    
                }
            }

            if (_micLabelTmp != null)
                _micLabelTmp.text = MicLabelText();
        }

        // Speaker button
        if (_spkButtonObj != null)
        {
            var sr = _spkButtonObj.transform.Find("VCIcon")?.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.sprite = _speakerMuted ? Sprites.SpkOff : Sprites.SpkOn;
                sr.color  = _speakerMuted ? new Color(1f, 0.4f, 0.4f) : Color.white;
                if (_spkLabelTmp != null)
                    {
                        _spkLabelTmp.color = _speakerMuted ? new Color(1f, 0.4f, 0.4f) : Color.black;
        }
            }

            if (_spkLabelTmp != null)
                _spkLabelTmp.text = SpkLabelText();
        }

        // Jail button (label is always "Unmute")
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

    private static string MicLabelText()
    {
        if (VoiceRoleMuteState.TryGetLocalMeetingVoiceBlockReason(out _))
            return "Blocked";
        if (IsInImpostorRadioMode())
            return "Radio";
        return _micMuted ? "Unmute" : "Mute";
    }

    private static string SpkLabelText() => _speakerMuted ? "Off" : "On";

    // ── Public actions ────────────────────────────────────────────────────────
    internal static void ApplyMicState()
    {
        var settings = LocalSettingsTabSingleton<VoiceChatLocalSettings>.Instance;
        bool radioTransmit  = IsInImpostorRadioMode();
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

    internal static void ToggleMutePublic()    => SetMuted(!_micMuted);
    internal static void ToggleSpeakerPublic() => SetSpeakerMuted(!_speakerMuted);
    internal static void JailUnmutePublic()
    {
        VoiceRoleMuteState.LocalJailorAllowVoice();
        UpdateHudButtonsVisibility();
        RefreshButtonVisuals();
    }

    internal static void SetMuted(bool muted)
    {
        _micMuted = muted;
        ApplyMicState();
        if (muted) MeetingSpeakingIndicatorPatch.ClearLocalIndicator();
        RefreshButtonVisuals();
    }

    internal static void SetSpeakerMuted(bool muted)
    {
        _speakerMuted = muted;
        ApplySpeakerState();
        RefreshButtonVisuals();
    }

    // ── Overlay scale (API compat — no positional effect in grid mode) ─────────
    public static void ApplyOverlayScale(float scale)
    {
        _overlayScale = Mathf.Clamp(scale, 0.75f, 1.5f);
        // Scale does not apply to grid buttons (grid cell size is fixed), but we
        // keep the field so callers in VoiceChatLocalSettings compile without change.
    }

    // ── Indicator position (removed — grid manages layout) ───────────────────
    public static void ApplyIndicatorPosition(IndicatorPosition pos)
    {
        // No-op: position is entirely controlled by the TOU Mira GridArrange.
        // Kept so existing callers in VoiceChatLocalSettings compile unchanged.
    }

    // ── Impostor radio hold tracking ──────────────────────────────────────────
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

        bool prev     = _impostorHeld;
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
        _micButton  = null; _spkButton  = null; _jailButton  = null;
        _micLabelTmp = null; _spkLabelTmp = null; _jailLabelTmp = null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static bool CanUseImpostorRadio()
        => PlayerControl.LocalPlayer != null
        && PlayerControl.LocalPlayer.Data?.Role?.IsImpostor == true
        && PlayerControl.LocalPlayer.Data?.IsDead == false
        && VoiceChatGameOptions.GetInstance().ImpostorPrivateRadio.Value;

    private static readonly Dictionary<string, Sprite> _spriteCache = new();

    public static Sprite? LoadSprite(string path, bool highQuality = false)
    {
        var cacheKey = highQuality ? path + "#hq" : path;
        if (_spriteCache.TryGetValue(cacheKey, out var cached)) return cached;
        try
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, highQuality)
            {
                wrapMode    = TextureWrapMode.Clamp,
                filterMode  = FilterMode.Bilinear,
                anisoLevel  = highQuality ? 16 : 1,
                mipMapBias  = highQuality ? -1.15f : 0f,
            };
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(path)!;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            tex.LoadImage(ms.ToArray(), false);
            tex.wrapMode   = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = highQuality ? 16 : 1;
            tex.mipMapBias = highQuality ? -1.15f : 0f;
            if (highQuality) tex.Apply(true, false);
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
        public static Sprite MicOn      => LoadSprite("VoiceChatPlugin.Resources.MicOn.png")!;
        public static Sprite MicOff     => LoadSprite("VoiceChatPlugin.Resources.MicOff.png")!;
        public static Sprite SpkOn      => LoadSprite("VoiceChatPlugin.Resources.SpeakerOn.png")!;
        public static Sprite SpkOff     => LoadSprite("VoiceChatPlugin.Resources.SpeakerOff.png")!;
        public static Sprite JailUnmute => LoadSprite("VoiceChatPlugin.Resources.JailUnmute.png")!;
    }
}