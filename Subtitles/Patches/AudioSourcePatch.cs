using PySpeech;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static AudibleDistanceLib.AudibleDistanceLib;
using static SubtitlesAPI.SubtitlesAPI;

namespace Subtitles.Patches;

[HarmonyPatch(typeof(AudioSource))]
public class AudioSourcePatch
{
    private static readonly string BGcolor = Plugin.backgroundColour.Value;
    private static readonly byte alpha = (byte)((Plugin.BackgroundOpacity.Value / 100f) * 255f);
    private static readonly string alphaHex = alpha.ToString("X2");
    private static readonly string HighlightColor = BGcolor + alphaHex;

    // Speech handler control & de-duplication
    private static readonly object speechHandlerLock = new object();
    private static bool speechHandlerRegistered = false;
    private static string lastRecognizedText = string.Empty;
    private static DateTime lastRecognizedAt = DateTime.MinValue;
    // Window during which identical recognition is considered duplicate (adjust as needed)
    private static readonly TimeSpan duplicateWindow = TimeSpan.FromMilliseconds(800);

    [HarmonyPrefix]
    [HarmonyPatch(nameof(AudioSource.PlayOneShotHelper), new[] { typeof(AudioSource), typeof(AudioClip), typeof(float) })]
    public static void PlayOneShotHelper_Prefix(AudioSource source, ref AudioClip clip, float volumeScale)
    {
        if (IsInWithinAudiableDistance(GameNetworkManager.Instance, source, volumeScale, Plugin.Instance.minimumAudibleVolume.Value))
        {
            AddSubtitle(clip);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(AudioSource.Play), new[] { typeof(double) })]
    public static void PlayDelayed_Prefix(AudioSource __instance)
    {
        if (__instance.clip == null) return;
        if (IsInWithinAudiableDistance(GameNetworkManager.Instance, __instance, __instance.volume, Plugin.Instance.minimumAudibleVolume.Value))
        {
            AddSubtitle(__instance.clip);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(AudioSource.Play), new System.Type[] { })]
    public static void Play_Prefix(AudioSource __instance)
    {
        if (__instance.clip == null) return;
        if (IsInWithinAudiableDistance(GameNetworkManager.Instance, __instance, __instance.volume, Plugin.Instance.minimumAudibleVolume.Value))
        {
            AddSubtitle(__instance.clip);
        }
    }

    private static void AddSubtitle(AudioClip clip)
    {
        if (clip?.name is null)
        {
            return;
        }

        string clipName = Path.GetFileNameWithoutExtension(clip.name);

        if (Localization.Translations.TryGetValue(clipName, out string soundTranslation))
        {
            if (Plugin.Instance.logSoundNames.Value)
            {
                Plugin.ManualLogSource.LogInfo($"Found translation for {clipName}!");
            }

            Plugin.Instance.subtitles.Add(FormatSoundTranslation(soundTranslation));
        }
        else if (Localization.DialogueTranslations.TryGetValue(clipName, out List<(float, string)> translations))
        {
            if (Plugin.Instance.logSoundNames.Value)
            {
                Plugin.ManualLogSource.LogInfo($"Found dialogue translation for {clipName}!");
            }

            foreach ((float startTimestamp, string timedTranslation) in translations)
            {
                Plugin.Instance.subtitles.Add(ForamtDialogueTranslation(timedTranslation), startTimestamp);
            }
        }
        else if (Plugin.SpeachToText.Value == true)
        {
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

    // Register the PySpeech custom handler exactly once and filter duplicate rapid callbacks.
    private static void RegisterSpeechHandlerOnce()
    {
        lock (speechHandlerLock)
        {
            if (speechHandlerRegistered) return;

            Speech.RegisterCustomHandler((obj, recognized) =>
            {
                try
                {
                    string text = recognized?.Text?.Trim();
                    if (string.IsNullOrEmpty(text)) return;

                    bool isDuplicate = false;
                    lock (speechHandlerLock)
                    {
                        if (text == lastRecognizedText && (DateTime.Now - lastRecognizedAt) < duplicateWindow)
                        {
                            isDuplicate = true;
                        }
                        else
                        {
                            lastRecognizedText = text;
                            lastRecognizedAt = DateTime.Now;
                        }
                    }

                    if (isDuplicate) return;

                    if (Plugin.Instance.logSoundNames.Value)
                    {
                        Plugin.ManualLogSource.LogInfo($"Whisper output: {text}");
                    }

                    // Add formatted subtitle. If thread affinity issues appear, dispatch to Unity main thread here.
                    Plugin.Instance.subtitles.Add(FormatHumanDialogue(text));
                }
                catch (Exception ex)
                {
                    // Avoid crashing on unexpected handler exceptions; log for diagnosis.
                    if (Plugin.Instance?.logSoundNames.Value == true)
                    {
                        Plugin.ManualLogSource.LogWarning($"Speech handler exception: {ex}");
                    }
                }
            });

            speechHandlerRegistered = true;
            if (Plugin.Instance.logSoundNames.Value)
            {
                Plugin.ManualLogSource.LogInfo("Speech-to-text handler registered (single registration).");
            }
        }
    }

    private static string FormatSoundTranslation(string translation)
    {
        if (Plugin.BackgroundVisible.Value == true)
        {
            if (translation.StartsWith("[") && translation.EndsWith("]"))
            {
                return $"<mark={HighlightColor}><color={Plugin.mainTextColour.Value}>{translation}</color></mark>";
            }
            return $"<mark={HighlightColor}><color={Plugin.mainTextColour.Value}>[{translation}]</color></mark>";
        }
        else
        {
            if (translation.StartsWith("[") && translation.EndsWith("]"))
            {
                return $"<color={Plugin.mainTextColour.Value}>{translation}</color>";
            }
            return $"<color={Plugin.mainTextColour.Value}>[{translation}]</color>";
        }
    }

    private static string ForamtDialogueTranslation(string translation)
    {
        if (translation.StartsWith("[") && translation.EndsWith("]"))
        {
            return FormatSoundTranslation(translation);
        }

        if (Plugin.BackgroundVisible.Value == true)
        {
            return $"<mark={HighlightColor}><color={Plugin.diologColour.Value}>[{translation}]</color></mark>";
        }
        else
        {
            return $"<color={Plugin.diologColour.Value}>{translation}</color>";
        }
    }

    private static string FormatHumanDialogue(string Words)
    {

        if (Words.StartsWith("[") && Words.EndsWith("]"))
        {
            return FormatSoundTranslation(Words);
        }

        if (Plugin.BackgroundVisible.Value == true)
        {
            return $"<mark={HighlightColor}><color={Plugin.speach2TextColour.Value}>[{Words}]</color></mark>";
        }
        else
        {
            return $"<color={Plugin.speach2TextColour.Value}>{Words}</color>";
        }

    }

}