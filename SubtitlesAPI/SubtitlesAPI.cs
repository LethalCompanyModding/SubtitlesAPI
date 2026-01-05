using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Newtonsoft.Json;
using SubtitlesAPI.Locales;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SubtitlesAPI;

[BepInPlugin(pluginGuid, pluginName, pluginVersion)]
public class SubtitlesAPI : BaseUnityPlugin
{
    private const string pluginGuid = "JustJelly.SubtitlesAPI";
    private const string pluginName = "SubtitlesAPI";
    private const string pluginVersion = "0.0.10";

    private static Dictionary<string, ISubtitleLocalization> _locales;
    public static ManualLogSource ManualLogSource;

    public static ISubtitleLocalization Localization;
    public static ConfigEntry<string> SelectedLocale;
    public static ConfigEntry<bool> CustomLocale;

    public static void DiscoverLocales()
    {
        _locales = new Dictionary<string, ISubtitleLocalization>(StringComparer.OrdinalIgnoreCase);

        var types = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t =>
                typeof(ISubtitleLocalization).IsAssignableFrom(t) &&
                !t.IsInterface &&
                !t.IsAbstract &&
                t != typeof(CustomSubtitleLocalization)   // <-- EXCLUDE CUSTOM
            );

        foreach (var type in types)
        {
            ISubtitleLocalization instance = (ISubtitleLocalization)Activator.CreateInstance(type);
            string localeCode = instance.Locale;

            if (!_locales.ContainsKey(localeCode))
                _locales.Add(localeCode, instance);
        }
    }

    public ISubtitleLocalization GetDefaultLocalization(string locale)
    {
        if (_locales == null)
            DiscoverLocales();

        if (_locales.TryGetValue(locale, out var loc))
            return loc;

        return _locales["en"]; // fallback
    }

    private ISubtitleLocalization LoadCustomLocale(string folder)
    {
        string simplePath = Path.Combine(folder, "SingleSubtitles.json");
        string timedPath = Path.Combine(folder, "DialogueSubtitles.json");

        if (!File.Exists(simplePath) || !File.Exists(timedPath))
            return null;

        var simpleDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(simplePath));

        var timedJson = JsonConvert.DeserializeObject<Dictionary<string, List<TimedEntry>>>(File.ReadAllText(timedPath));
        var timedDict = timedJson.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Select(e => (e.time, e.text)).ToList()
        );

        return new CustomSubtitleLocalization(simpleDict, timedDict);
    }

    private class TimedEntry
    {
        public float time { get; set; }
        public string text { get; set; }
    }

    private void Awake()
    {
        ManualLogSource = BepInEx.Logging.Logger.CreateLogSource(pluginGuid);
        ManualLogSource.LogInfo($"{pluginName} {pluginVersion} loaded!");

        DiscoverLocales();
        string supportedLocales = string.Join(", ", _locales.Keys.OrderBy(k => k));

        SelectedLocale = Config.Bind<string>(
            section: "​Options",
            key: "Locale",
            defaultValue: "en",
            description: $"The localization to use. Supported locales: {supportedLocales}");

        CustomLocale = Config.Bind<bool>(
            section: "​Options",
            key: "Custom Locale",
            defaultValue: false,
            description: "If true, the mod will create a folder to add Custom locales. (Useful for testing new locales & memes)");

        string customLocalePath = Path.Combine(Paths.ConfigPath, "SubtitlesAPI");

        if (CustomLocale.Value == true)
        {
            Directory.CreateDirectory(customLocalePath);

            string simplePath = Path.Combine(customLocalePath, "SingleSubtitles.json");
            string timedPath = Path.Combine(customLocalePath, "DialogueSubtitles.json");

            if (!File.Exists(simplePath) || !File.Exists(timedPath))
            {
                var defaultLoc = GetDefaultLocalization(SelectedLocale.Value);

                File.WriteAllText(simplePath,
                    JsonConvert.SerializeObject(defaultLoc.Translations, Formatting.Indented));

                var timedDict = defaultLoc.DialogueTranslations
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value.Select(t => new { time = t.Item1, text = t.Item2 }).ToList()
                    );

                File.WriteAllText(timedPath, JsonConvert.SerializeObject(timedDict, Formatting.Indented));

                ManualLogSource.LogInfo("Generated default custom locale files.");
            }

            Localization = LoadCustomLocale(customLocalePath) ?? new EnglishSubtitleLocalization();
            return;
        }


        // ⭐ NORMAL MODE
        Localization = GetDefaultLocalization(SelectedLocale.Value);

        if (Localization == null)
        {
            ManualLogSource.LogWarning("Unable to find chosen locale, defaulting to English.");
            Localization = new EnglishSubtitleLocalization();
        }
    }
}
