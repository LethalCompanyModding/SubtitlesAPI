# AudibleDistanceLib

A small library for audible distance determination.

## Usage

### Example

```cs
using static AudibleDistanceLib.AudibleDistanceLib;

[HarmonyPrefix]
[HarmonyPatch(nameof(AudioSource.PlayOneShotHelper), new[] { typeof(AudioSource), typeof(AudioClip), typeof(float) })]
public static void PlayOneShotHelper_Prefix(AudioSource source, ref AudioClip clip, float volumeScale)
{
    var (audible, strength, info) = AudioSourceAnalyzer(GameNetworkManager.Instance, source, volumeScale, Plugin.Instance.minimumAudibleVolume.Value);

    if (audible)
    { 
        distance = info.DistanceMeters;
        horizAngle = info.HorizontalAngleDegrees;
        verticalAngleDeg = info.VerticalAngleDegrees;
        toSource = info.Direction;
        cardinal = info.Cardinal;
        // do something with the info
        // do something with strength (float 0-1 baised off distance and volume)
    }
}
```
