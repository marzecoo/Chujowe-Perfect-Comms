using HarmonyLib;
using MiraAPI.Keybinds;
using Rewired;

namespace VoiceChatPlugin.VoiceChat;

[HarmonyPatch]
public static class VoiceChatPatches
{

    internal static void RegisterKeybindHandlers()
    {
        VoiceChatKeybinds.ToggleMute.OnActivate(VoiceChatHudState.ToggleMutePublic);
        VoiceChatKeybinds.ToggleSpeaker.OnActivate(VoiceChatHudState.ToggleSpeakerPublic);
        VoiceChatKeybinds.VolumeMenu.OnActivate(VoiceVolumeMenu.Toggle);
    }

    [HarmonyPostfix, HarmonyPatch(typeof(KeyboardJoystick), nameof(KeyboardJoystick.Update))]
    static void KeyboardUpdate_Post()
    {
        var action = VoiceChatKeybinds.ImpostorRadio.RewiredInputAction;
        if (action == null) return;

        var player = ReInput.players.GetPlayer(0);
        bool held  = player.GetButton(action.id); 
        bool down  = player.GetButtonDown(action.id); 
        bool up    = player.GetButtonUp(action.id);  

        VoiceChatHudState.UpdateImpostorRadioHold(held, down, up);

        var pttAction = VoiceChatKeybinds.PushToTalk.RewiredInputAction;
        if (pttAction == null) return;

        VoiceChatHudState.UpdatePushToTalkHeld(player.GetButton(pttAction.id));
    }
}
