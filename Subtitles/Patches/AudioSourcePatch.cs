using HarmonyLib;
using Subtitles.NetWorking;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static AudibleDistanceLib.AudibleDistanceLib;
using static SubtitlesAPI.SubtitlesAPI;

namespace Subtitles.Patches;

[HarmonyPatch(typeof(AudioSource))]
public class AudioSourcePatch
{
    private static readonly string BGcolor = Plugin.backgroundcolour.Value;
    private static readonly byte alpha = (byte)((Plugin.BackgroundOpacity.Value / 100f) * 255f);
    private static readonly string alphaHex = alpha.ToString("X2");
    private static readonly string HighlightColor = BGcolor + alphaHex;

    // Speech handler registration guard (single registration)
    private static readonly object speechHandlerLock = new object();
    private static bool speechHandlerRegistered = false;
    private static string lastText = null;

    [HarmonyPrefix]
    [HarmonyPatch(nameof(AudioSource.PlayOneShotHelper), new[] { typeof(AudioSource), typeof(AudioClip), typeof(float) })]
    public static void PlayOneShotHelper_Prefix(AudioSource source, ref AudioClip clip, float volumeScale)
    {
        if (IsInWithinAudiableDistance(GameNetworkManager.Instance, source, volumeScale, Plugin.Instance.minimumAudibleVolume.Value))
        {
            AddSubtitle(clip, source, volumeScale);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(AudioSource.Play), new[] { typeof(double) })]
    public static void PlayDelayed_Prefix(AudioSource __instance)
    {
        if (__instance.clip == null) return;
        if (IsInWithinAudiableDistance(GameNetworkManager.Instance, __instance, __instance.volume, Plugin.Instance.minimumAudibleVolume.Value))
        {
            AddSubtitle(__instance.clip, __instance, __instance.volume);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(AudioSource.Play), new System.Type[] { })]
    public static void Play_Prefix(AudioSource __instance)
    {
        if (__instance.clip == null) return;
        if (IsInWithinAudiableDistance(GameNetworkManager.Instance, __instance, __instance.volume, Plugin.Instance.minimumAudibleVolume.Value))
        {
            AddSubtitle(__instance.clip, __instance, __instance.volume);
        }
    }

    private static void AddSubtitle(AudioClip clip, AudioSource source, float volumeScale)
    {
        if (Plugin.globalSubtitleShufOff.Value == true) return;
        if (clip?.name is null)
        {
            return;
        }

        string clipName = Path.GetFileNameWithoutExtension(clip.name);
        float strength = GetAudibleStrength(GameNetworkManager.Instance, source, volumeScale);
        var locInfo = DescribeAudioSource(GameNetworkManager.Instance, source);

        if (Localization.Translations.TryGetValue(clipName, out string soundTranslation) && Plugin.SuprressGameCaptions.Value == false)
        {
            if (Plugin.Instance.logSoundNames.Value)
            {
                Plugin.ManualLogSource.LogInfo($"Found translation for {clipName} (strength {strength:F2})!");
            }

            Plugin.Instance.subtitles.Add(FormatSubtitles(soundTranslation, Plugin.mainTextColour.Value, locInfo, strength));
        }
        else if (Localization.DialogueTranslations.TryGetValue(clipName, out List<(float, string)> translations) && Plugin.SuprressGameCaptions.Value == false)
        {
            if (Plugin.Instance.logSoundNames.Value)
            {
                Plugin.ManualLogSource.LogInfo($"Found dialogue translation for {clipName} (strength {strength:F2})!");
            }

            foreach ((float startTimestamp, string timedTranslation) in translations)
            {
                string formatted = FormatSubtitles(timedTranslation, Plugin.diologColour.Value, locInfo, strength);
                Plugin.Instance.subtitles.Add(formatted, startTimestamp);
            }
        }
        else if (Plugin.Speach2Text.Value == true)
        {

            if (speechHandlerRegistered) return;
            speechHandlerRegistered = true;   // <-- Fix 1: prevent duplicate registration

            var t = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                .FirstOrDefault(x => x.FullName == "PySpeech.Speech" || x.Name == "Speech");

            if (t == null) return;

            var m = t.GetMethod("RegisterCustomHandler",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (m == null) return;

            var p = m.GetParameters()[0].ParameterType;
            var w = new Action<object, object>((_, r) => OnSpeechCallback(r));
            var d = Delegate.CreateDelegate(p, w.Target, w.Method);

            m.Invoke(null, new object[] { d });
        }
        else
        {
            if (Plugin.Instance.logSoundNames.Value)
            {
                Plugin.ManualLogSource.LogInfo($"No translation for {clipName}.");
            }
        }
    }

    // callback invoked by PySpeech via delegate; use reflection to read recognized.Text
    private static void OnSpeechCallback(object recognized)
    {
        var textProp = recognized.GetType().GetProperty("Text");
        if (textProp == null) return;

        string text = textProp.GetValue(recognized) as string;
        if (string.IsNullOrWhiteSpace(text)) return;

        if (Plugin.SelfCaptions.Value == true) FormatSubtitles(text, Plugin.SelfTextColor.Value);

        if (text == lastText) return;
        lastText = text;

        Plugin.ManualLogSource.LogInfo($"Whisper STT: {text}");

        UnityNetcodePatcher.SendSubtitleFromClient(text, Plugin.SelfTextColor.Value);
    }
    // Format subtitles for everything 
    public static string FormatSubtitles(string text, string color, AudioSourceLocationInfo? info = null, float strength = 0f)
    {
        string inner = info != null ? ApplyDirectionalWrap(text, info) : text;
        string finalColor = Mathf.Clamp01(strength) != 0 ? CalAlphaColor(color, strength) : color;
        if (Plugin.BackgroundVisible.Value == true)
        {
            return $"<mark={HighlightColor}><color={finalColor}>{inner}</color></mark>";
        }
        return $"<color={finalColor}>{inner}</color>";
    }

    // Inject the two-digit alpha hex into any <color=#RRGGBB...> tags.
    // Does not modify <mark=> tags (background) to keep background opacity unchanged.
    private static string CalAlphaColor(string color, float strength)
    {
        float s = Mathf.Clamp01(strength);

        // Tunables you can adjust
        float minAlpha = 0.20f;        // alpha at strength = 0
        float sampleStrength = 0.80f;  // point we want to control
        float sampleAlpha = 0.50f;     // alpha at strength = 0.75

        // Compute gamma curve
        float rhs = Mathf.Clamp((sampleAlpha - minAlpha) / (1f - minAlpha), 1e-6f, 1f);
        float gamma = Mathf.Log(rhs) / Mathf.Log(sampleStrength);

        float alpha = minAlpha + (1f - minAlpha) * Mathf.Pow(s, gamma);
        alpha = Mathf.Clamp01(alpha);

        byte a = (byte)Mathf.RoundToInt(alpha * 255f);
        string alphaHex = a.ToString("X2");

        string output = color + alphaHex;
        return output;
    }

    // Wrap the text with directional characters based on location info.
    // Preserves inner content if the original text was bracketed (removes original [] then applies directional wrap).
    private static string ApplyDirectionalWrap(string text, AudioSourceLocationInfo? info)
    {
        string content = text;

        if (info == null)
            return $"[{content}]";

        var loc = info.Value;

        string vertical = "";
        if (loc.VerticalAngleDegrees >= 25f)
            vertical = "^";
        else if (loc.VerticalAngleDegrees <= -25f)
            vertical = "v";

        string leftSide = "";
        string rightSide = "";

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
            // forward
            if (vertical == "")
            {
                // neutral forward
                return $"[{content}]";
            }
            else
            {
                // pure vertical
                return $"{vertical} {content} {vertical}";
            }
        }
        return $"{leftSide} {content} {rightSide}";
    }

}