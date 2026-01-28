using System;
using System.Collections.Generic;

namespace SubtitlesAPI.Locales;

public class CustomSubtitleLocalization : ISubtitleLocalization
{
  public string Locale => "custom";

  public Dictionary<string, string> Translations { get; }
  public Dictionary<string, List<(float, string)>> DialogueTranslations { get; }

  public CustomSubtitleLocalization(
      Dictionary<string, string> translations,
      Dictionary<string, List<(float, string)>> dialogueTranslations)
  {
    Translations = translations ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    DialogueTranslations = dialogueTranslations ?? new Dictionary<string, List<(float, string)>>(StringComparer.OrdinalIgnoreCase);
  }
}
