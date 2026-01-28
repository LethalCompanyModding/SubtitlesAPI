using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Subtitles.Patches;

[HarmonyPatch(typeof(HUDManager))]
public class HUDManagerPatch
{
  private static TextMeshProUGUI subtitleGUItext;
  private static RectTransform subtitleGUIRect;
  private static GameObject subtitleBackgroundObject;
  private static Image subtitleBackgroundImage;
  private static LayoutElement subtitleLayout;

  [HarmonyPostfix]
  [HarmonyPatch("Awake")]
  private static void Awake_Postfix(ref HUDManager __instance)
  {
    GameObject subtitlesGUI = new("SubtitlesGUI");
    RectTransform guiRect = subtitlesGUI.AddComponent<RectTransform>();
    guiRect.SetParent(GameObject.Find(Constants.PlayerScreenGUIName).transform, false);

    var fitter = subtitlesGUI.AddComponent<ContentSizeFitter>();
    fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
    fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

    GameObject bgObj = new("SubtitleBackground");
    RectTransform bgRect = bgObj.AddComponent<RectTransform>();
    bgRect.SetParent(guiRect, false);

    // Stretch background to fill parent
    bgRect.anchorMin = Vector2.zero;
    bgRect.anchorMax = Vector2.one;
    bgRect.offsetMin = Vector2.zero;
    bgRect.offsetMax = Vector2.zero;

    Image bgImage = bgObj.AddComponent<Image>();
    string hex = Plugin.backgroundcolour.Value;
    if (hex.StartsWith("#"))
      hex = hex.Substring(1);

    byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
    byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
    byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);

    float a = Plugin.BackgroundOpacity.Value / 100f;
    bgImage.color = new Color(r / 255f, g / 255f, b / 255f, a);
    bgObj.SetActive(Plugin.showParentBox.Value);

    bgObj.transform.SetAsFirstSibling();

    TextMeshProUGUI textComponent = subtitlesGUI.AddComponent<TextMeshProUGUI>();

    textComponent.alignment = TextAlignmentOptions.Center;
    textComponent.font = __instance.controlTipLines[0].font;
    textComponent.fontSize = Plugin.SubtitleSize.Value;
    textComponent.enableWordWrapping = true;
    textComponent.enableAutoSizing = false;
    textComponent.richText = true;

    float maxWidth;
    if (Plugin.ParentboxWidth.Value <= -1)
    {
      CanvasScaler scaler = GameObject.Find(Constants.PlayerScreenGUIName).GetComponentInParent<CanvasScaler>();
      float canvasScaleFactor = scaler != null ? scaler.scaleFactor : 1f;
      maxWidth = Screen.width * 0.375f / canvasScaleFactor;
    }
    else
    {
      maxWidth = Plugin.ParentboxWidth.Value;
    }

    var layout = subtitlesGUI.AddComponent<LayoutElement>();
    layout.preferredWidth = maxWidth;
    layout.flexibleWidth = 0;

    string[] parts = Plugin.textPosition.Value.Split(',');
    int x = 0;
    int y = -125;
    if (parts.Length == 2)
    {
      int.TryParse(parts[0], out x);
      int.TryParse(parts[1], out y);
    }
    guiRect.anchoredPosition = new Vector2(x, y);

    // store references for runtime updates
    subtitleBackgroundObject = bgObj;
    subtitleBackgroundImage = bgImage;
    subtitleGUIRect = guiRect;
    subtitleLayout = layout;
    subtitleGUItext = textComponent;
  }
  [HarmonyPostfix]
  [HarmonyPatch("Update")]
  private static void Update_Postfix()
  {
    UpdateParentbox();
    UpdateTextStyle();
    DrawSubtitles();
  }

  private static void UpdateParentbox()
  {
    if (subtitleBackgroundObject == null || subtitleBackgroundImage == null)
      return;

    // Always update position & size (even when hidden)
    if (subtitleGUIRect != null)
    {
      string[] parts = Plugin.textPosition.Value.Split(',');
      int x = 0;
      int y = -125;

      if (parts.Length == 2)
      {
        int.TryParse(parts[0], out x);
        int.TryParse(parts[1], out y);
      }
      subtitleGUIRect.anchoredPosition = new Vector2(x, y);

      float maxWidth;

      if (Plugin.ParentboxWidth.Value <= -1)
      {
        CanvasScaler scaler = GameObject.Find(Constants.PlayerScreenGUIName).GetComponentInParent<CanvasScaler>();
        float canvasScaleFactor = scaler != null ? scaler.scaleFactor : 1f;
        maxWidth = Screen.width * 0.375f / canvasScaleFactor;
      }
      else
      {
        maxWidth = Plugin.ParentboxWidth.Value;
      }

      subtitleLayout.preferredWidth = maxWidth;

      // Force Unity to rebuild the layout so the background resizes
      LayoutRebuilder.ForceRebuildLayoutImmediate(subtitleGUIRect);
    }

    // Toggle visibility live
    subtitleBackgroundObject.SetActive(Plugin.showParentBox.Value);

    // Only update color/opacity when visible
    if (!Plugin.showParentBox.Value)
      return;

    string hex = Plugin.backgroundcolour.Value.TrimStart('#');
    if (hex.Length < 6)
      return;

    byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
    byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
    byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);

    float a = Mathf.Clamp01(Plugin.BackgroundOpacity.Value / 100f);
    subtitleBackgroundImage.color = new Color(r / 255f, g / 255f, b / 255f, a);
  }

  private static void UpdateTextStyle()
  {
    if (subtitleGUItext == null || subtitleGUIRect == null)
      return;

    subtitleGUItext.fontSize = Plugin.SubtitleSize.Value;
    subtitleGUItext.alignment = Plugin.SubtitleAlignment.Value switch
    {
      "Left" => TextAlignmentOptions.Left,
      "Right" => TextAlignmentOptions.Right,
      _ => TextAlignmentOptions.Center,
    };
    subtitleGUItext.ForceMeshUpdate();

    LayoutRebuilder.ForceRebuildLayoutImmediate(subtitleGUIRect);
  }

  // Build the subtitle text using per-entry alpha from SubtitleList.TakeLastWithAlpha(...)
  private static void DrawSubtitles()
  {
    var entries = Plugin.Instance.subtitles.TakeLast(Constants.DefaultVisibleSubtitleLines);

    var sb = new StringBuilder();
    string delimiter = string.Empty;

    foreach (var (alpha, formattedOrPlain) in entries)
    {
      string display = ApplyAlphaToFormatted(formattedOrPlain, alpha);

      sb.Append(delimiter);
      sb.Append(display);

      delimiter = Constants.HtmlLineBreakTag;
    }

    subtitleGUItext.text = sb.ToString();
  }

  // Helper: append alpha byte to any hex color tags like <color=#RRGGBB> -> <color=#RRGGBBAA>
  private static string ApplyAlphaToFormatted(string input, float alpha)
  {
    if (string.IsNullOrEmpty(input)) return input;

    byte a = (byte)Mathf.RoundToInt(alpha * 2.55f);
    string aHex = a.ToString("X2");


    string result = Regex.Replace(input, @"<color=\#([0-9A-Fa-f]{6})([0-9A-Fa-f]{2})?>", m =>
    {
      string rgb = m.Groups[1].Value;
      string existingAlphaHex = m.Groups[2].Success ? m.Groups[2].Value : "FF";
      int existAlpha = Mathf.RoundToInt((Convert.ToInt32(existingAlphaHex, 16) / 255f) * 100f);
      if (existAlpha <= alpha) return $"<color=#{rgb}{existingAlphaHex}>"; else return $"<color=#{rgb}{aHex}>";
    });

    if (Plugin.BackgroundVisible.Value == true && Plugin.BackgroundOpacity.Value >= alpha)
    {
      result = Regex.Replace(result, @"<mark=\#([0-9A-Fa-f]{6})([0-9A-Fa-f]{2})?>", m =>
      {
        var rgb = m.Groups[1].Value;
        return $"<mark=#{rgb}{aHex}>";
      });
    }
    return result;
  }
}
