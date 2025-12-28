using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace Subtitles;

[BepInPlugin(pluginGuid, pluginName, pluginVersion)]
[BepInDependency("JustJelly.AudibleDistanceLib")]
[BepInDependency("JustJelly.SubtitlesAPI")]
[BepInDependency("JS03.PySpeech", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    private const string pluginGuid = "JustJelly.Subtitles";
    private const string pluginName = "Subtitles";
    private const string pluginVersion = "2.2.0";

    private Harmony harmony;

    public static Plugin Instance;
    public static ManualLogSource ManualLogSource;

    public SubtitleList subtitles = [];
    public ConfigEntry<float> minimumAudibleVolume;
    public ConfigEntry<bool> logSoundNames;
    public static ConfigEntry<string> mainTextColour;
    public static ConfigEntry<string> diologColour;
    public static ConfigEntry<float> SubtitleSize;
    public static ConfigEntry<bool> BackgroundVisible;
    public static ConfigEntry<string> backgroundColour;
    public static ConfigEntry<int> BackgroundOpacity;
    public static ConfigEntry<bool> showParentBox;
    public static ConfigEntry<string> textPosition;
    public static ConfigEntry<float> ParentboxWidth;
    public static ConfigEntry<bool> SpeachToText;
    public static ConfigEntry<bool> ReducedCaptions;
    public static ConfigEntry<string> speach2TextColour;
    private void Awake()
    {
        Instance ??= this;

        ManualLogSource = BepInEx.Logging.Logger.CreateLogSource(pluginGuid);
        ManualLogSource.LogInfo($"{pluginName} {pluginVersion} loaded!");

        minimumAudibleVolume = Config.Bind<float>(
            section: "Options",
            key: "MinimumAudibleVolume",
            defaultValue: 12f,
            description: "The minimum volume this mod determines is audible. Scale of 0-100. Any sound heard above this volume will be displayed on subtitles, any sound below will not.");

        ReducedCaptions = Config.Bind<bool>(
            section: "Options",
            key: "ReducedCaptions",
            defaultValue: true,
            description: "Add's a cooldown to constantly repeating audio.");

        SubtitleSize = Config.Bind<float>(
            section: "Options",
            key: "fontSize",
            defaultValue: 15f,
            description: "Change the size of subtitle text ingame!\n (global for all subtitle classes also restart to update)");

        SpeachToText = Config.Bind<bool>(
            section: "Options",
            key: "SpeachToText",
            defaultValue: false,
            description: "If true and PySpeech lib is installed, will attempt to add CC for humans! \n (Do Not turn on if PySpeech isn't installed. It ill spam errors into your logs.)"
            );
        
        BackgroundVisible = Config.Bind<bool>(
            section: "Options",
            key: "BackgroundVisible",
            defaultValue: false, 
            description: "Adds a opuaqe background to the bar where text normaly is. ");

        mainTextColour = Config.Bind<string>(
            section: "Customization",
            key: "mainTextColour",
            defaultValue: "#FFF000",
            description: "Change the color of subtitles to your desire!");

        diologColour = Config.Bind<string>(
            section: "Customization",
            key: "diologColour",
            defaultValue: "#00FF00",
            description: "Change the color of subtitles to your desire!");

        speach2TextColour = Config.Bind<string>(
            section: "Customization",
            key: "speach2TextColour",
            defaultValue: "#ff9900",
            description: "Change the color of subtitles to your desire!");

        backgroundColour = Config.Bind<string>(
            section: "Customization",
            key: "BackgroundColour",
            defaultValue: "#000000",
            description: "Adds a opuaqe background to the bar where text normaly is.");

        BackgroundOpacity = Config.Bind<int>(
            section: "Customization",
            key: "BackgroundOpacity",
            defaultValue: 50,
            description: "Adds a opuaqe background to the bar where text normaly is.");

        logSoundNames = Config.Bind<bool>(
            section: "Contributors/Developers",
            key: "LogSoundNames",
            defaultValue: false,
            description: "Whether the mod should log the names of sounds. Only valuable if trying to add more subtitles / localization.");

        showParentBox = Config.Bind<bool>(
            section: "Contributors/Developers",
            key: "showParentBox",
            defaultValue: false,
            description: "Makes the parentBox visable to help with moving the location of the bar. Also works as a replacement for background text (with color)");

        ParentboxWidth = Config.Bind<float>(
            section: "Contributors/Developers",
            key: "ParentboxWidth",
            defaultValue: -1f,
            description: "Helps with word wraping in larger fonts. (in pixles (-1 is autoconfigured)) Rejoin btw");
        
        textPosition = Config.Bind<string>(
            section: "Options",
            key: "textPosition",
            defaultValue: "0,-125",
            description:"Move the Postion of the menubar (x,y)");

        harmony = new Harmony(pluginGuid);
        harmony.PatchAll();
    }
}