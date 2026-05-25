#if MACOS
using VoiceChatPlugin.VoiceChat;

namespace VoiceChatPlugin.Audio;

internal static class MacOsUnityMicrophonePermissionProbe
{
    public static void LogPermissionProbe()
    {
        try
        {
            var devices = UnityEngine.Microphone.devices;
            VoiceDiagnostics.Log("mac.permission", $"unityMicrophoneDevices={devices?.Length ?? 0}");
        }
        catch (System.Exception ex)
        {
            VoiceDiagnostics.Log("mac.permission", $"unityProbe=false error=\"{ex.Message}\"");
        }
    }
}
#endif
