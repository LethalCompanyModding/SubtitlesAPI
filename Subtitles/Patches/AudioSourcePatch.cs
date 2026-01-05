using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static AudibleDistanceLib.AudibleDistanceLib;
using static SubtitlesAPI.SubtitlesAPI;

namespace Subtitles.Patches;

[HarmonyPatch(typeof(AudioSource))]
public class AudioSourcePatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(AudioSource.PlayOneShotHelper), new[] { typeof(AudioSource), typeof(AudioClip), typeof(float) })]
    public static void PlayOneShotHelper_Prefix(AudioSource source, ref AudioClip clip, float volumeScale)
    {
        var (audible, strength, info) = AudioSourceAnalyzer(GameNetworkManager.Instance, source, volumeScale, Plugin.Instance.minimumAudibleVolume.Value);

        if (audible) AddSubtitle(clip, strength, info);
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(AudioSource.Play), new[] { typeof(double) })]
    public static void PlayDelayed_Prefix(AudioSource __instance)
    {
        if (__instance.clip == null) return;
        var (audible, strength, info) = AudioSourceAnalyzer(GameNetworkManager.Instance, __instance, __instance.volume, Plugin.Instance.minimumAudibleVolume.Value);

        if (audible) AddSubtitle(__instance.clip, strength, info);
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(AudioSource.Play), new System.Type[] { })]
    public static void Play_Prefix(AudioSource __instance)
    {
        if (__instance.clip == null) return;
        var (audible, strength, info) = AudioSourceAnalyzer(GameNetworkManager.Instance, __instance, __instance.volume, Plugin.Instance.minimumAudibleVolume.Value);

        if (audible) AddSubtitle(__instance.clip, strength, info);
    }

    private static void AddSubtitle(AudioClip clip, float strength, AudioSourceAnalysis? info)
    {
        if (Plugin.globalSubtitleShufOff.Value == true) return;
        if (clip?.name is null) return;

        string clipName = Path.GetFileNameWithoutExtension(clip.name);

        if (Localization.Translations.TryGetValue(clipName, out string soundTranslation))
        {
            if (Plugin.SuprressGameCaptions.Value == false)
            {
                if (Plugin.Instance.logSoundNames.Value)
                {
                    Plugin.ManualLogSource.LogInfo($"Found translation for {clipName} (strength {strength:F2})!");
                }
                Plugin.Instance.subtitles.Add(FormatSubtitles(soundTranslation, Plugin.mainTextColour.Value, info, strength));
            }
        }
        else if (Localization.DialogueTranslations.TryGetValue(clipName, out List<(float, string)> translations))
        {
            if (Plugin.SuprressGameCaptions.Value == false)
            { 
                if (Plugin.Instance.logSoundNames.Value)
                {
                    Plugin.ManualLogSource.LogInfo($"Found dialogue translation for {clipName} (strength {strength:F2})!");
                }
                foreach ((float startTimestamp, string timedTranslation) in translations)
                {
                    string formatted = FormatSubtitles(timedTranslation, Plugin.diologColour.Value, null, strength);
                    Plugin.Instance.subtitles.Add(formatted, startTimestamp);
                }
            }
        }
        else
        {
            if (Plugin.Instance.logSoundNames.Value)
            {
                Plugin.ManualLogSource.LogInfo($"No translation for {clipName}.");
            }
        }
    }

    public static string FormatSubtitles(string text, string color, AudioSourceAnalysis? info = null, float strength = 1f)
    {
        string inner = info != null && Plugin.DirectinalAudioCues.Value == true ? ApplyDirectionalWrap(text, info) : text;
        string finalColor = Mathf.Clamp01(strength) != 1f && Plugin.DistanceFade.Value == true ? CalAlphaColor(color, strength) : color;
        if (Plugin.BackgroundVisible.Value == true)
        {
            byte alpha = (byte)Mathf.Clamp(Mathf.RoundToInt(Plugin.BackgroundOpacity.Value * 2.55f), 0, 255);
            string highlightColor = $"{Plugin.backgroundcolour.Value}{alpha:X2}";
            return $"<mark={highlightColor}><color={finalColor}>{inner}</color></mark>";
        }
        return $"<color={finalColor}>{inner}</color>";
    }

    private static string CalAlphaColor(string color, float strength)
    {
        float s = Mathf.Clamp01(strength);

        float minAlpha = 0.20f;
        float sampleStrength = 0.80f;
        float sampleAlpha = 0.50f;

        float rhs = Mathf.Clamp((sampleAlpha - minAlpha) / (1f - minAlpha), 1e-6f, 1f);
        float gamma = Mathf.Log(rhs) / Mathf.Log(sampleStrength);

        float alpha = minAlpha + (1f - minAlpha) * Mathf.Pow(s, gamma);
        alpha = Mathf.Clamp01(alpha);

        byte a = (byte)Mathf.RoundToInt(alpha * 255f);
        string alphaHex = a.ToString("X2");

        return color + alphaHex;
    }

    private static string ApplyDirectionalWrap(string text, AudioSourceAnalysis? info)
    {
        string content = text;

        if (info == null) return $"[{content}]";

        var loc = info.Value;

        string vertical = "";
        if (loc.VerticalAngleDegrees >= 25f)
            vertical = "^";
        else if (loc.VerticalAngleDegrees <= -25f)
            vertical = "v";

        string leftSide;
        string rightSide;

        string c = loc.Cardinal?.ToLower() ?? "front";

        if (c.Contains("left"))
        {
            leftSide = "<";
            rightSide = vertical != "" ? vertical : "]";
        }
        else if (c.Contains("right"))
        {
            leftSide = vertical != "" ? vertical : "[";
            rightSide = ">";
        }
        else if (c.Contains("back"))
        {
            leftSide = vertical != "" ? vertical : "*";
            rightSide = vertical != "" ? vertical : "*";
        }
        else
        {
            if (vertical == "")
            {
                return $"[{content}]";
            }
            else
            {
                return $"{vertical} {content} {vertical}";
            }
        }
        return $"{leftSide} {content} {rightSide}";
    }

}