using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Subtitles.Patches;

[HarmonyPatch(typeof(HUDManager))]
public class HUDManagerPatch
{
    private static TextMeshProUGUI subtitleGUItext;

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
        string hex = Plugin.backgroundColour.Value;
        // Remove #
        if (hex.StartsWith("#"))
            hex = hex.Substring(1);

        // Parse components
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

        float maxWidth;
        if (Plugin.ParentboxWidth.Value <= -1){
            CanvasScaler scaler = GameObject.Find(Constants.PlayerScreenGUIName).GetComponentInParent<CanvasScaler>();
            float canvasScaleFactor = scaler != null ? scaler.scaleFactor : 1f;
            maxWidth = Screen.width * 0.375f/canvasScaleFactor;
        } else {
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

        subtitleGUItext = textComponent;        
    }

    [HarmonyPostfix]
    [HarmonyPatch("Update")]
    private static void Update_Postfix()
    {
        subtitleGUItext.text = GetLatestSubtitles();
    }

    private static string GetLatestSubtitles()
    {
        StringBuilder stringBuilder = new();
        IList<string> latestSubtitles = Plugin.Instance.subtitles.TakeLast(Constants.DefaultVisibleSubtitleLines).ToList();
        string delimiter = string.Empty;

        foreach (string subtitle in latestSubtitles)
        {
            stringBuilder.Append(delimiter);
            stringBuilder.Append(subtitle);

            delimiter = Constants.HtmlLineBreakTag;
        }

        return stringBuilder.ToString();
    }
}
