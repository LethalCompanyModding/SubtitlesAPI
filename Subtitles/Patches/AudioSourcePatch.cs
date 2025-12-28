using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
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

    // AddSubtitle now accepts source + volumeScale so we can compute distance-based alpha and direction
    private static void AddSubtitle(AudioClip clip, AudioSource source, float volumeScale)
    {
        if (clip?.name is null)
        {
            return;
        }

        string clipName = Path.GetFileNameWithoutExtension(clip.name);

        // get audible strength 0..1 using AudibleDistanceLib
        float strength = GetAudibleStrength(GameNetworkManager.Instance, source, volumeScale);

        // compute alpha hex based on strength; limit variation to 25% (min alpha = 0.75)
        string alphaHexForThis = ComputeAlphaHexFromStrength(strength);

        // get direction info (may be null)
        var locInfo = DescribeAudioSource(GameNetworkManager.Instance, source);

        if (Localization.Translations.TryGetValue(clipName, out string soundTranslation) && Plugin.SuprressGameCaptions.Value == false)
        {
            if (Plugin.Instance.logSoundNames.Value)
            {
                Plugin.ManualLogSource.LogInfo($"Found translation for {clipName} (strength {strength:F2})!");
            }

            string inner = ApplyDirectionalWrap(soundTranslation, locInfo);
            string formatted = FormatWithInner(inner, Plugin.mainTextColour.Value);
            formatted = InjectAlphaIntoColorTags(formatted, alphaHexForThis);
            Plugin.Instance.subtitles.Add(formatted);
        }
        else if (Localization.DialogueTranslations.TryGetValue(clipName, out List<(float, string)> translations) && Plugin.SuprressGameCaptions.Value == false)
        {
            if (Plugin.Instance.logSoundNames.Value)
            {
                Plugin.ManualLogSource.LogInfo($"Found dialogue translation for {clipName} (strength {strength:F2})!");
            }

            foreach ((float startTimestamp, string timedTranslation) in translations)
            {
                string inner = ApplyDirectionalWrap(timedTranslation, locInfo);
                string formatted = FormatWithInner(inner, Plugin.diologColour.Value);
                formatted = InjectAlphaIntoColorTags(formatted, alphaHexForThis);
                Plugin.Instance.subtitles.Add(formatted, startTimestamp);
            }
        }
        else if (Plugin.Speach2Text.Value == true)
        {
            // register reflective handler once; handler will add local subtitle and forward network
            RegisterSpeechHandlerOnce();
        }
        else
        {
            if (Plugin.Instance.logSoundNames.Value)
            {
                Plugin.ManualLogSource.LogInfo($"No translation for {clipName}.");
            }
        }
    }

    // Format helper that uses inner text exactly (does not add default brackets).
    private static string FormatWithInner(string inner, string color)
    {
        if (Plugin.BackgroundVisible.Value == true)
        {
            return $"<mark={HighlightColor}><color={color}>{inner}</color></mark>";
        }

        return $"<color={color}>{inner}</color>";
    }

    // Wrap the text with directional characters based on location info.
    // Preserves inner content if the original text was bracketed (removes original [] then applies directional wrap).
    private static string ApplyDirectionalWrap(string text, AudioSourceLocationInfo? info)
    {
        string content = text;
        bool wasBracketed = false;
        if (!string.IsNullOrEmpty(text) && text.StartsWith("[") && text.EndsWith("]"))
        {
            wasBracketed = true;
            content = text.Substring(1, text.Length - 2);
        }

        if (info == null) // no direction available, keep bracketed style if it was bracketed, otherwise use default [..]
        {
            return wasBracketed ? $"[{content}]" : $"[{content}]";
        }

        var loc = info.Value;

        // Vertical overrides horizontal if strongly up/down
        if (loc.VerticalAngleDegrees >= 25f)
        {
            return $"^{content}^";
        }

        if (loc.VerticalAngleDegrees <= -25f)
        {
            return $"v{content}v";
        }

        // horizontal sectors
        string c = loc.Cardinal ?? "front";
        if (c.Contains("left"))
        {
            return $"<{content}]";
        }
        if (c.Contains("right"))
        {
            return $"[{content}>"; // symmetric right marker
        }
        if (c.Contains("back"))
        {
            return $"*{content}*"; // use braces for back
        }

        // front or unknown
        return $"[{content}]";
    }

    private static string ComputeAlphaHexFromStrength(float strength)
    {
        try
        {
            // clamp input
            float s = Mathf.Clamp01(strength);

            // Tunables: minAlpha and sample mapping could be configured later.
            const float minAlpha = 0.75f; // lowest opacity
            const float sampleStrength = 0.75f; // sample point
            const float sampleAlpha = 0.50f;    // desired alpha at sampleStrength

            if (sampleAlpha <= minAlpha || sampleAlpha > 1f || sampleStrength <= 0f)
            {
                float alphaLinear = minAlpha + (1f - minAlpha) * s;
                byte aLinear = (byte)Mathf.Clamp(Mathf.RoundToInt(alphaLinear * 255f), 0, 255);
                return aLinear.ToString("X2");
            }

            float rhs = (sampleAlpha - minAlpha) / (1f - minAlpha);
            rhs = Mathf.Clamp(rhs, 1e-6f, 1f);
            float gamma = Mathf.Log(rhs) / Mathf.Log(sampleStrength);

            float alphaFloat = minAlpha + (1f - minAlpha) * Mathf.Pow(s, gamma);
            alphaFloat = Mathf.Clamp01(alphaFloat);
            byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(alphaFloat * 255f), 0, 255);
            return a.ToString("X2");
        }
        catch
        {
            return "FF";
        }
    }

    // Inject the two-digit alpha hex into any <color=#RRGGBB...> tags.
    // Does not modify <mark=> tags (background) to keep background opacity unchanged.
    private static string InjectAlphaIntoColorTags(string input, string alphaHex)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(alphaHex)) return input;

        return Regex.Replace(input, @"<color=\#([0-9A-Fa-f]{6})([0-9A-Fa-f]{2})?>", m =>
        {
            var rgb = m.Groups[1].Value;
            return $"<color=#{rgb}{alphaHex}>";
        });
    }

    // register handler via reflection so missing JS03.PySpeech doesn't break the plugin
    private static void RegisterSpeechHandlerOnce()
    {
        lock (speechHandlerLock)
        {
            if (speechHandlerRegistered) return;

            // find PySpeech.Speech type in loaded assemblies
            Type speechType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                .FirstOrDefault(t => string.Equals(t.FullName, "PySpeech.Speech", StringComparison.Ordinal) || t.Name == "Speech");

            if (speechType == null)
            {
                if (Plugin.Instance.logSoundNames.Value)
                    Plugin.ManualLogSource.LogInfo("PySpeech not found; skipping speech handler registration.");
                speechHandlerRegistered = true; // mark true to avoid repeated costly lookups
                return;
            }

            var registerMethod = speechType.GetMethod("RegisterCustomHandler", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (registerMethod == null)
            {
                Plugin.ManualLogSource.LogWarning("PySpeech found but no RegisterCustomHandler method.");
                speechHandlerRegistered = true;
                return;
            }

            // parameter delegate type
            var paramType = registerMethod.GetParameters().First().ParameterType;

            // our handler method compatible with (object, object) arguments; we'll adapt via CreateDelegate
            MethodInfo handlerMethod = typeof(AudioSourcePatch).GetMethod(nameof(OnSpeechCallback), BindingFlags.NonPublic | BindingFlags.Static);
            try
            {
                var del = Delegate.CreateDelegate(paramType, handlerMethod);
                registerMethod.Invoke(null, new object[] { del });
                speechHandlerRegistered = true;
                if (Plugin.Instance.logSoundNames.Value)
                    Plugin.ManualLogSource.LogInfo("Registered PySpeech handler via reflection.");
            }
            catch (Exception ex)
            {
                Plugin.ManualLogSource.LogWarning($"Failed to create/invoke PySpeech handler delegate: {ex}");
                speechHandlerRegistered = true;
            }
        }
    }

    // callback invoked by PySpeech via delegate; use reflection to read recognized.Text
    private static void OnSpeechCallback(object sender, object recognized)
    {
        try
        {
            if (recognized == null) return;
            var textProp = recognized.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
            if (textProp == null) return;
            string text = textProp.GetValue(recognized) as string;
            if (string.IsNullOrWhiteSpace(text)) return;

            if (Plugin.Instance.logSoundNames.Value)
            {
                Plugin.ManualLogSource.LogInfo($"Whisper Speech-to-text output: {text}");
            }

            // Add local subtitle if enabled
            if (Plugin.SelfCaptions.Value)
            {
                Plugin.Instance.subtitles.Add(FormatHumanDialogue(text));
            }

            // Forward over Netcode (no-op if netcode not active)
            try
            {
                Subtitles.NetWorking.UnityNetcodePatcher.SendSubtitle(text);
            }
            catch (Exception ex)
            {
                if (Plugin.Instance.logSoundNames.Value)
                {
                    Plugin.ManualLogSource.LogWarning($"UnityNetcodePatcher.SendSubtitle failed: {ex}");
                }
            }
        }
        catch (Exception ex)
        {
            if (Plugin.Instance?.logSoundNames.Value == true)
            {
                Plugin.ManualLogSource.LogWarning($"OnSpeechCallback exception: {ex}");
            }
        }
    }

    public static void AddUnformattedLocalSubtitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        string formatted = FormatHumanDialogue(raw);
        Plugin.Instance.subtitles.Add(formatted);
    }

    private static string FormatWithColor(string text, string color)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        bool bracketed = text.StartsWith("[") && text.EndsWith("]");
        string inner = bracketed ? text : $"[{text}]";

        if (Plugin.BackgroundVisible.Value == true)
        {
            return $"<mark={HighlightColor}><color={color}>{inner}</color></mark>";
        }

        return $"<color={color}>{inner}</color>";
    }

    private static string FormatSoundTranslation(string translation)
    {
        // If already bracketed use mainTextColour for inner content (original behaviour).
        if (translation.StartsWith("[") && translation.EndsWith("]"))
        {
            if (Plugin.BackgroundVisible.Value == true)
            {
                return $"<mark={HighlightColor}><color={Plugin.mainTextColour.Value}>{translation}</color></mark>";
            }
            return $"<color={Plugin.mainTextColour.Value}>{translation}</color>";
        }

        // Non-bracketed sound translations use mainTextColour and get wrapped by helper.
        return FormatWithColor(translation, Plugin.mainTextColour.Value);
    }

    private static string ForamtDialogueTranslation(string translation)
    {
        // Bracketed dialogue delegates to sound formatting (same inner color)
        if (translation.StartsWith("[") && translation.EndsWith("]"))
        {
            return FormatSoundTranslation(translation);
        }

        // Non-bracketed dialogue uses dialog color
        return FormatWithColor(translation, Plugin.diologColour.Value);
    }

    private static string FormatHumanDialogue(string words)
    {
        // Bracketed human speech -> treat like a sound (main color)
        if (words.StartsWith("[") && words.EndsWith("]"))
        {
            return FormatSoundTranslation(words);
        }

        // Non-bracketed human speech uses HumanTextColor
        return FormatWithColor(words, Plugin.HumanTextColor.Value);
    }

}