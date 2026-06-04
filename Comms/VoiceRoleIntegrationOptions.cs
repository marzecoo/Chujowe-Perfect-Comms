using MiraAPI.GameOptions;
using MiraAPI.GameOptions.OptionTypes;
using UnityEngine;

namespace VoiceChatPlugin.VoiceChat;

public class VoiceRoleIntegrationOptions : AbstractOptionGroup
{
    public override string GroupName => VoiceChatLocalSettings.Censor("Mega Chujowe Perfect Comms: Role Voice Rules");
    public override Color GroupColor => Palette.CrewmateRoleHeaderBlue;
    public override uint GroupPriority => 1001;

    public ModdedToggleOption MuteBlackmailedInMeetings { get; } = new("<color=#FF0000><b>Blackmailer</b></color>: Mute Blackmailed in Meetings", true);
    public ModdedToggleOption MuteBlackmailedNextRound { get; } = new("<color=#FF0000><b>Blackmailer</b></color>: Mute Blackmailed Next Round", false);
    public ModdedToggleOption MuteParasiteControlled { get; } = new("<color=#FF0000><b>Parasite</b></color>: Mute Controlled Victim", true);
    public ModdedToggleOption ParasiteHearFromVictim { get; } = new("<color=#FF0000><b>Parasite</b></color>: Also Hear Controlled Victim", true);
    public ModdedToggleOption MutePuppeteerControlled { get; } = new("<color=#FF0000><b>Puppeteer</b></color>: Mute Controlled Victim", true);
    public ModdedToggleOption PuppeteerHearFromVictim { get; } = new("<color=#FF0000><b>Puppeteer</b></color>: Hear From Controlled Victim", true);

    public ModdedToggleOption MuffleBlindedOrFlashedHearing { get; } = new("<color=#FF0000><b>Eclipsal/Grenadier</b></color>: Muffle Blinded/Flashed Hearing", true);
    public ModdedToggleOption MuffleHypnotizedDuringHysteria { get; } = new("<color=#FF0000><b>Hypnotist</b></color>: Muffle Hypnotized During Hysteria", true);
    public ModdedToggleOption MuffleDoctorInjectorNegativeEffects { get; } = new("<color=#D12B2B><b>Doctor/Injector</b></color>: Muffle Negative Effects", true);
    public ModdedToggleOption MuffleHerbalistConfuse { get; } = new("<color=#D12B2B><b>Herbalist</b></color>: Muffle Confused Hearing", true);
    public ModdedToggleOption MuffleEvokerBlinded { get; } = new("<color=#5FBFCF><b>Evoker</b></color>: Muffle Blinded Hearing", true);
    public ModdedToggleOption CrewpostorUsesImpostorVoice { get; } = new("<color=#FF0000><b>Crewpostor</b></color>: Use Impostor Voice", true);
    public ModdedToggleOption TouMceHackerJamMutesVoice { get; } = new("<color=#D12B2B><b>Hacker</b></color>: Jam Mutes Voice", true);
    public ModdedToggleOption MuteGlitchHacked { get; } = new("<color=#00FF00><b>Glitch</b></color>: Mute Hacked Players", true);
    public ModdedToggleOption MuteSwooperWhileSwooped { get; } = new("<color=#8E7CC3><b>Hidden Roles</b></color>: Mute While Hidden (Swooper/Wraith)", true);
    public ModdedToggleOption MuteJailedInMeetings { get; } = new("<color=#A6A6A6><b>Jailor</b></color>: Mute Jailee in Meetings", true);
    public ModdedToggleOption JailorCanUnmuteJailed { get; } = new("<color=#A6A6A6><b>Jailor</b></color>: Can Unmute Jailee", true);
    public ModdedToggleOption TouMcePelicanBellyVoice { get; } = new("<color=#6A4C93><b>Pelican</b></color>: Belly Voice (Off Mutes Victims)", true);
    public ModdedEnumOption TouMceSpiritMasterGhostVoice { get; } = new("<color=#5FBFCF><b>Spirit Master</b></color>: Ghost Voice",
        (int)MediumGhostVoiceMode.Both,
        typeof(MediumGhostVoiceMode),
        ["None", "Spirit Master -> Ghost", "Ghost -> Spirit Master", "Both"]);
    public ModdedEnumOption MediumGhostVoice { get; } = new("<color=#A680FF><b>Medium</b></color>: Ghost Voice",
        (int)MediumGhostVoiceMode.None,
        typeof(MediumGhostVoiceMode),
        ["None", "Medium -> Ghost", "Ghost -> Medium", "Both"]);

    internal static VoiceRoleIntegrationOptions GetInstance() =>
        OptionGroupSingleton<VoiceRoleIntegrationOptions>.Instance;
}

public enum MediumGhostVoiceMode
{
    None,
    MediumToGhost,
    GhostToMedium,
    Both,
}
