using BepInEx;
using BepInEx.Logging;
using GameNetcodeStuff;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace AudibleDistanceLib;

[BepInPlugin(pluginGuid, pluginName, pluginVersion)]
public class AudibleDistanceLib : BaseUnityPlugin
{
    private const string pluginGuid = "JustJelly.AudibleDistanceLib";
    private const string pluginName = "AudibleDistanceLib";
    private const string pluginVersion = "0.0.1";

    public static ManualLogSource ManualLogSource;

    private void Awake()
    {
        ManualLogSource = BepInEx.Logging.Logger.CreateLogSource(pluginGuid);
        ManualLogSource.LogInfo($"{pluginName} {pluginVersion} loaded!");
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
    public static bool IsInWithinAudiableDistance(GameNetworkManager gameNetworkManager, AudioSource source, float volume, float minimumAudibleVolume = 12f)
    {
        if (volume == 0
            || source is null
            || gameNetworkManager?.localPlayerController is null)
        {
            return false;
        }

        bool isPlayerDead = gameNetworkManager.localPlayerController.isPlayerDead;
        bool isSpeculating = (System.Object)(object)gameNetworkManager.localPlayerController.spectatedPlayerScript != null;
        PlayerControllerB playerController = (!isPlayerDead || !isSpeculating) ? gameNetworkManager.localPlayerController : gameNetworkManager.localPlayerController.spectatedPlayerScript;

        float distance = Vector3.Distance(playerController.transform.position, source.transform.position);
        float audibleVolume = EvaluateVolumeAt(source, distance) * volume;

        return audibleVolume >= (minimumAudibleVolume / 100);
    }

    /// <summary>
    /// Public helper: returns audible strength in range [0,1] for the given source/volume.
    /// Strength = EvaluateVolumeAt(source, distance) * volume. Returns 0 if inputs invalid.
    /// </summary>
    public static float GetAudibleStrength(GameNetworkManager gameNetworkManager, AudioSource source, float volume)
    {
        if (volume <= 0f || source is null || gameNetworkManager?.localPlayerController is null)
            return 0f;

        bool isPlayerDead = gameNetworkManager.localPlayerController.isPlayerDead;
        bool isSpeculating = (System.Object)(object)gameNetworkManager.localPlayerController.spectatedPlayerScript != null;
        PlayerControllerB playerController = (!isPlayerDead || !isSpeculating) ? gameNetworkManager.localPlayerController : gameNetworkManager.localPlayerController.spectatedPlayerScript;
        if (playerController is null) return 0f;

        float distance = Vector3.Distance(playerController.transform.position, source.transform.position);
        float strength = EvaluateVolumeAt(source, distance) * volume;

        // clamp to sensible range
        if (float.IsNaN(strength) || float.IsInfinity(strength))
            return 0f;
        return Mathf.Clamp01(strength);
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

    public struct AudioSourceLocationInfo
    {
        public float DistanceMeters;
        public float HorizontalAngleDegrees; // -180..180
        public float VerticalAngleDegrees;   // positive = up
        public Vector3 Direction;            // world vector from player to source
        public string Cardinal;              // "front", "front-left", "left", "back", "above", "below", etc.
    }

    /// <summary>
    /// Describe an AudioSource relative to the local player. Returns null if inputs invalid.
    /// </summary>
    public static AudioSourceLocationInfo? DescribeAudioSource(GameNetworkManager gameNetworkManager, AudioSource source)
    {
        if (source is null || gameNetworkManager?.localPlayerController is null) return null;

        bool isPlayerDead = gameNetworkManager.localPlayerController.isPlayerDead;
        bool isSpeculating = (System.Object)(object)gameNetworkManager.localPlayerController.spectatedPlayerScript != null;
        PlayerControllerB playerController = (!isPlayerDead || !isSpeculating) ? gameNetworkManager.localPlayerController : gameNetworkManager.localPlayerController.spectatedPlayerScript;
        if (playerController is null) return null;

        Vector3 playerPos = playerController.transform.position;
        Vector3 toSource = source.transform.position - playerPos;
        float distance = toSource.magnitude;

        // Horizontal projection
        Vector3 flat = new Vector3(toSource.x, 0f, toSource.z);
        float flatMag = flat.magnitude;
        if (flatMag <= 0.0001f)
        {
            float verticalAngle = Mathf.Sign(toSource.y) * 90f;
            return new AudioSourceLocationInfo
            {
                DistanceMeters = distance,
                HorizontalAngleDegrees = 0f,
                VerticalAngleDegrees = verticalAngle,
                Direction = toSource,
                Cardinal = toSource.y > 0 ? "above" : "below"
            };
        }

        Vector3 playerForward = playerController.transform.forward;
        float horizAngle = Vector3.SignedAngle(playerForward, flat, Vector3.up); // -180..180

        float verticalAngleDeg = Mathf.Atan2(toSource.y, flatMag) * Mathf.Rad2Deg;

        string cardinal = AngleToCardinal(horizAngle);

        return new AudioSourceLocationInfo
        {
            DistanceMeters = distance,
            HorizontalAngleDegrees = horizAngle,
            VerticalAngleDegrees = verticalAngleDeg,
            Direction = toSource,
            Cardinal = cardinal
        };
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

    /// <summary>
    /// Find all other players (component instances) to the local player within maxDistance meters.
    /// Uses StartOfRound.Instance.allPlayerScripts if available; falls back to scanning objects that look like players.
    /// Returns an ordered list (nearest first) of (PlayerId, distanceMeters). Empty list when none found or on error.
    /// </summary>
    public static List<(ulong clientId, float distance)> FindNearestOtherPlayerWithinDistance(GameNetworkManager gameNetworkManager, float maxDistance)
    {
        var result = new List<(ulong clientId, float distance)>();

        var local = gameNetworkManager.localPlayerController;
        if (local == null) return result;

        bool localIsDead = local.isPlayerDead || local.spectatedPlayerScript != null;

        var start = StartOfRound.Instance;
        if (start == null || start.allPlayerScripts == null)
            return result;

        var players = start.allPlayerScripts;

        for (int i = 0; i < players.Length; i++)
        {
            var p = players[i];
            if (p == null) continue;

            if (p == local) continue;

            bool otherIsDead = p.isPlayerDead;
            if (otherIsDead != localIsDead)
                continue;

            float dist = Vector3.Distance(local.transform.position, p.transform.position);
            if (dist > maxDistance) continue;

            ulong clientId = p.actualClientId;

            // Only the server is allowed to check ConnectedClients
            if (NetworkManager.Singleton.IsServer)
            {
                if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId))
                    continue;
            }

            result.Add((clientId, dist));
        }

        return result;
    }


}