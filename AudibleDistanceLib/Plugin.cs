using BepInEx;
using BepInEx.Logging;
using GameNetcodeStuff;
using UnityEngine;

namespace AudibleDistanceLib;

[BepInPlugin(LCMPluginInfo.PLUGIN_GUID, LCMPluginInfo.PLUGIN_NAME, LCMPluginInfo.PLUGIN_VERSION)]
public class AudibleDistanceLib : BaseUnityPlugin
{
  public static ManualLogSource ManualLogSource;

  private void Awake()
  {
    ManualLogSource = Logger;
    ManualLogSource.LogInfo($"{LCMPluginInfo.PLUGIN_NAME} {LCMPluginInfo.PLUGIN_VERSION} loaded!");
  }

  /// <summary>
  /// Checks if the <see cref="GameNetworkManager.localPlayerController"/> is within
  /// the audible distance.
  /// </summary>
  /// <param name="gameNetworkManager">A <see cref="GameNetworkManager"/> instance.</param>
  /// <param name="source">An <see cref="AudioSource"/> instance.</param>
  /// <param name="volume">An <see cref="float"/> value passed as volume.</param>
  /// <param name="minimumAudibleVolume">An <see cref="float"/> value configured as minimum audible volume.</param>
  /// <returns>True if the local/speculating player is in within audible distance.</returns>
  /// 
  public static (bool Audible, float Strength, AudioSourceAnalysis? Info) AudioSourceAnalyzer(GameNetworkManager gameNetworkManager, AudioSource source, float volume, float minimumAudibleVolume = 12f)
  {
    if (volume == 0 || source == null || gameNetworkManager?.localPlayerController == null) return (false, 0f, null);

    bool isPlayerDead = gameNetworkManager.localPlayerController.isPlayerDead;
    bool isSpeculating = (Object)(object)gameNetworkManager.localPlayerController.spectatedPlayerScript != null;
    PlayerControllerB playerController = (!isPlayerDead || !isSpeculating) ? gameNetworkManager.localPlayerController : gameNetworkManager.localPlayerController.spectatedPlayerScript;

    Vector3 playerPos = playerController.transform.position;
    Vector3 toSource = source.transform.position - playerPos;
    float distance = toSource.magnitude;

    // Horizontal projection
    Vector3 flat = new Vector3(toSource.x, 0f, toSource.z);
    float flatMag = flat.magnitude;

    float horizAngle = 0f;
    float verticalAngleDeg = 0f;
    string cardinal = "unknown";

    if (flatMag <= 0.0001f)
    {
      verticalAngleDeg = Mathf.Sign(toSource.y) * 90f;
      cardinal = toSource.y > 0 ? "above" : "below";
    }
    else
    {
      Vector3 playerForward = playerController.transform.forward;
      horizAngle = Vector3.SignedAngle(playerForward, flat, Vector3.up);
      verticalAngleDeg = Mathf.Atan2(toSource.y, flatMag) * Mathf.Rad2Deg;
      cardinal = AngleToCardinal(horizAngle);
    }

    // Compute audible strength
    float audibleVolume = EvaluateVolumeAt(source, distance) * volume;

    bool audible = audibleVolume >= (minimumAudibleVolume / 100f);
    float strength = Mathf.Clamp01(audibleVolume);

    var info = new AudioSourceAnalysis
    {
      DistanceMeters = distance,
      HorizontalAngleDegrees = horizAngle,
      VerticalAngleDegrees = verticalAngleDeg,
      Direction = toSource,
      Cardinal = cardinal
    };

    return (audible, strength, info);
  }

  private static float EvaluateVolumeAt(AudioSource source, float distance)
  {
    AnimationCurve curve = null;
    float range = source.maxDistance - source.minDistance;

    if (distance < source.minDistance)
    {
      return 1;
    }
    else if (distance > source.maxDistance)
    {
      return 0;
    }

    switch (source.rolloffMode)
    {
      case AudioRolloffMode.Linear:
        curve = AnimationCurve.Linear(0, 1, 1, 0);
        break;
      case AudioRolloffMode.Logarithmic:
        curve = new(
            new(0, 1),
            new(range / 4, 1 / (source.minDistance + range / 4)),
            new(range / 2, 1 / (source.minDistance + range / 2)),
            new(3 * range / 4, 1 / (source.minDistance + 3 * range / 4)),
            new(1, 0));
        break;
      case AudioRolloffMode.Custom:
        curve = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
        break;
    }

    if (curve is null)
    {
      return 1;
    }

    float evalutationDistance = (distance - source.minDistance) / range;

    return curve.Evaluate(evalutationDistance);
  }

  // --- Directional helpers ---
  public struct AudioSourceAnalysis
  {
    public float DistanceMeters;
    public float HorizontalAngleDegrees;
    public float VerticalAngleDegrees;
    public Vector3 Direction;
    public string Cardinal;
  }

  private static string AngleToCardinal(float angle)
  {
    // normalize -180..180
    float a = angle;
    if (a <= -180f) a += 360f;
    if (a > 180f) a -= 360f;

    if (a >= -22.5f && a < 22.5f) return "front";
    if (a >= 22.5f && a < 67.5f) return "front-right";
    if (a >= 67.5f && a < 112.5f) return "right";
    if (a >= 112.5f && a < 157.5f) return "back-right";
    if (a >= 157.5f || a < -157.5f) return "back";
    if (a >= -157.5f && a < -112.5f) return "back-left";
    if (a >= -112.5f && a < -67.5f) return "left";
    if (a >= -67.5f && a < -22.5f) return "front-left";
    return "unknown";
  }
}
