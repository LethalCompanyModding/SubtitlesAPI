using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace Subtitles.NetWorking
{
    internal static class UnityNetcodePatcher
    {
        private const string GoName = "SubtitlesNetcodeHelper";
        public static SubtitleNetworkBehaviour Instance;

        // Ensure the helper object exists on BOTH host and clients
        public static void EnsureInitialized()
        {
            if (Instance != null)
                return;

            var go = GameObject.Find(GoName);
            if (go == null)
            {
                go = new GameObject(GoName);
                UnityEngine.Object.DontDestroyOnLoad(go);
            }

            Instance = go.GetComponent<SubtitleNetworkBehaviour>()
                       ?? go.AddComponent<SubtitleNetworkBehaviour>();

            if (!go.TryGetComponent(out NetworkObject _))
                go.AddComponent<NetworkObject>();

            // IMPORTANT: DO NOT SPAWN — UnityNetcodePatcher does not require it
        }

        // Called by Whisper on the client
        public static void SendSubtitleFromClient(string text, string color)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            EnsureInitialized();

            if (NetworkManager.Singleton.IsServer)
            {
                // Host shortcut
                Instance.ServerHandleSubtitle(text, color, NetworkManager.Singleton.LocalClientId);
            }
            else
            {
                Instance.ClientSendSubtitleRpc(text, color);
            }
        }

        // The actual network behaviour
        public class SubtitleNetworkBehaviour : NetworkBehaviour
        {
            // Client → Server
            [ServerRpc(RequireOwnership = false)]
            public void ClientSendSubtitleRpc(string text, string color, ServerRpcParams rpcParams = default)
            {
                ulong senderId = rpcParams.Receive.SenderClientId;
                ServerHandleSubtitle(text, color, senderId);
            }

            // Server logic: compute distances + dead/alive filtering
            public void ServerHandleSubtitle(string text, string color, ulong senderId)
            {
                var start = StartOfRound.Instance;
                if (start == null) return;

                var sender = start.allPlayerScripts.FirstOrDefault(p => p.actualClientId == senderId);
                if (sender == null) return;

                bool senderDead = sender.isPlayerDead || sender.spectatedPlayerScript != null;

                List<ulong> targets = new();

                foreach (var p in start.allPlayerScripts)
                {
                    if (p == null) continue;
                    if (p.actualClientId == senderId) continue;

                    bool otherDead = p.isPlayerDead || p.spectatedPlayerScript != null;
                    if (otherDead != senderDead)
                        continue;

                    float dist = Vector3.Distance(sender.transform.position, p.transform.position);
                    if (dist <= 20f)
                        targets.Add(p.actualClientId);
                }

                // Send to all matching clients
                if (targets.Count > 0)
                {
                    BroadcastSubtitleClientRpc(text, color, new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams { TargetClientIds = targets }
                    });
                }

                // Host displays locally
                if (senderId == NetworkManager.Singleton.LocalClientId)
                    if (Plugin.SupressOthers.Value == false && Plugin.globalSubtitleShufOff.Value == false)
                    Plugin.Instance.subtitles.Add(Subtitles.Patches.AudioSourcePatch.FormatSubtitles(text, color));
            }

            // Server → Clients
            [ClientRpc]
            public void BroadcastSubtitleClientRpc(string text, string color, ClientRpcParams rpcParams = default)
            {
                if (Plugin.Speach2textLogs.Value == true) Plugin.ManualLogSource.LogInfo($"[Subtitle RPC] Client {NetworkManager.Singleton.LocalClientId} received: \"{text}\"");
                if (Plugin.SupressOthers.Value == false && Plugin.globalSubtitleShufOff.Value == false)
                Plugin.Instance.subtitles.Add(Subtitles.Patches.AudioSourcePatch.FormatSubtitles(text, color));
            }
        }
    }
}
