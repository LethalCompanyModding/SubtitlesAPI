using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace Subtitles;

[BepInPlugin(pluginGuid, pluginName, pluginVersion)]
[BepInDependency("JustJelly.AudibleDistanceLib")]
[BepInDependency("JustJelly.SubtitlesAPI")]
public class Plugin : BaseUnityPlugin
{
    private const string pluginGuid = "JustJelly.Subtitles";
    private const string pluginName = "Subtitles";
    private const string pluginVersion = "2.2.3";
    private Harmony harmony;

    public static Plugin Instance;
    public static ManualLogSource ManualLogSource;

    public SubtitleList subtitles = [];
    public ConfigEntry<float> minimumAudibleVolume;
    public ConfigEntry<bool> logSoundNames;
    public static ConfigEntry<string> mainTextColour;
    public static ConfigEntry<string> diologColour;
    public static ConfigEntry<string> backgroundcolour;
    public static ConfigEntry<string> textPosition;
    public static ConfigEntry<float> SubtitleSize;
    public static ConfigEntry<float> ParentboxWidth;
    public static ConfigEntry<bool> BackgroundVisible;
    public static ConfigEntry<bool> showParentBox;
    public static ConfigEntry<bool> ReducedCaptions;
    public static ConfigEntry<bool> SuprressGameCaptions;
    public static ConfigEntry<bool> FadeTrans;
    public static ConfigEntry<int> BackgroundOpacity;
    public static ConfigEntry<bool> globalSubtitleShufOff;
    public static ConfigEntry<string> SubtitleAlignment;
    public static ConfigEntry<bool> DirectinalAudioCues;
    public static ConfigEntry<bool> DistanceFade;

    private void Awake()
    {
        var types = Assembly.GetExecutingAssembly().GetTypes();
        foreach (var type in types)
        {
            var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var method in methods)
            {
                var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                if (attributes.Length > 0)
                {
                    method.Invoke(null, null);
                }
            }
        }

        Instance ??= this;

        ManualLogSource = BepInEx.Logging.Logger.CreateLogSource(pluginGuid);
        ManualLogSource.LogInfo($"{pluginName} {pluginVersion} loaded!");

        globalSubtitleShufOff = Config.Bind<bool>(
            section: "Options",
            key: "globalSubtitleShufOff",
            defaultValue: false,
            description: "Ment to be used as a short curcit method to shut off subtitles");

        minimumAudibleVolume = Config.Bind<float>(
            section: "Options",
            key: "MinimumAudibleVolume",
            defaultValue: 12f,
            description: "The minimum volume this mod determines is audible. Scale of 0-100. Any sound heard above this volume will be displayed on subtitles, any sound below will not.");

        ReducedCaptions = Config.Bind<bool>(
            section: "Options",
            key: "ReducedCaptions",
            defaultValue: true,
            description: "Add's a cooldown to constantly repeating subtitles (to my best method)");

        SubtitleSize = Config.Bind<float>(
            section: "Text Options",
            key: "fontSize",
            defaultValue: 15f,
            description: "Change the size of subtitle text ingame!\n (global for all subtitle)");

        SubtitleAlignment = Config.Bind<string>(
            section: "Text Options",
            key: "SubtitleAlignment",
            defaultValue: "Center",
            description: "Change the alignment of subtitle text ingame! Options: Left, Center, Right"
            );

        FadeTrans = Config.Bind<bool>(
            section: "Options",
            key: "FadeTrans",
            defaultValue: false,
            description: "My best attempt to add animation polish. (and Directional Audio cues)"
            );

        DirectinalAudioCues = Config.Bind<bool>(
            section: "Text Options",
            key: "DirectinalAudioCues",
            defaultValue: true,
            description: "Adds a small visual cue to the subtitle to show the direction of the sound. (Most player sounds are spawned like in your head)"
            );

        DistanceFade = Config.Bind<bool>(
            section: "Text Options",
            key: "DistanceFade",
            defaultValue: true,
            description: "Fades subtitles based on distance & volume from the player."
            );

        SuprressGameCaptions = Config.Bind<bool>(
        section: "Contributors/Developers",
        key: "SuprressGameCaptions",
        defaultValue: false,
        description: "Supress Game Subtitles for only really getting non-cliped sounds"
        );

        BackgroundVisible = Config.Bind<bool>(
            section: "Options",
            key: "BackgroundVisible",
            defaultValue: false,
            description: "Adds a highlight for subtitles contrast.");

        mainTextColour = Config.Bind<string>(
            section: "Customization",
            key: "mainTextColour",
            defaultValue: "#FFF000",
            description: "Change the color of subtitles to your desire!");

        diologColour = Config.Bind<string>(
            section: "Customization",
            key: "diologColour",
            defaultValue: "#00FF00",
            description: "Change the color of subtitles to your desire! (Dialog such as intro speech)");

        backgroundcolour = Config.Bind<string>(
            section: "Customization",
            key: "backgroundcolour",
            defaultValue: "#000000",
            description: "Chooses the color of the background/highlight");

        BackgroundOpacity = Config.Bind<int>(
            section: "Customization",
            key: "BackgroundOpacity",
            defaultValue: 50,
            description: "Changes the transparancy of the highlight/background");

        logSoundNames = Config.Bind<bool>(
            section: "Contributors/Developers",
            key: "LogSoundNames",
            defaultValue: false,
            description: "Whether the mod should log the names of sounds. Only valuable if trying to add more subtitles / localization.");

        showParentBox = Config.Bind<bool>(
            section: "Contributors/Developers",
            key: "showParentBox",
            defaultValue: false,
            description: "Makes the parentBox visable to help with moving the location of the bar. Also works as a replacement for highlight/background text (with same color options)");

        ParentboxWidth = Config.Bind<float>(
            section: "Contributors/Developers",
            key: "ParentboxWidth",
            defaultValue: -1f,
            description: "Helps with word wraping in larger fonts. (in pixles (-1 is autoconfigured?) Recomend start 300)");

        textPosition = Config.Bind<string>(
            section: "Options",
            key: "textPosition",
            defaultValue: "0,-125",
            description: "Move the Postion of the parentbox (x,y)");

        harmony = new Harmony(pluginGuid);
        harmony.PatchAll();
    }
}
