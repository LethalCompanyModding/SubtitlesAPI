using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace Subtitles.NetWorking
{
    internal static class UnityNetcodePatcher
    {
        private const string GoName = "SubtitlesNetcodeHelper";
        private static SubtitleNetworkBehaviour instance;

        // Ensure the NetworkBehaviour exists in the scene (call from Plugin.Awake)
        public static void EnsureInitialized()
        {
            if (instance != null) return;

            var go = GameObject.Find(GoName) ?? new GameObject(GoName);
            Object.DontDestroyOnLoad(go);

            instance = go.GetComponent<SubtitleNetworkBehaviour>();
            if (instance == null)
            {
                instance = go.AddComponent<SubtitleNetworkBehaviour>();

                // make sure there's a NetworkObject if Netcode is used
                if (go.GetComponent<NetworkObject>() == null)
                {
                    go.AddComponent<NetworkObject>();
                }
            }
        }

        // Send a subtitle string to host; host will forward to other clients.
        // If Netcode is not active, this call silently returns.
        public static void SendSubtitle(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            EnsureInitialized();

            if (instance == null) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;

            // If we are host/server we can broadcast directly.
            if (NetworkManager.Singleton.IsServer)
            {
                instance.BroadcastSubtitleToClients(text);
                return;
            }

            // Client -> Server via ServerRpc
            instance.SendSubtitleToServerRpc(text);
        }

        // Component that owns the RPC methods.
        public class SubtitleNetworkBehaviour : NetworkBehaviour
        {
            // Called on client, invoked on server/host.
            public void SendSubtitleToServerRpc(string text, ServerRpcParams serverRpcParams = default)
            {
                // Forward to all clients except the sender
                var sender = serverRpcParams.Receive.SenderClientId;
                var targets = NetworkManager.Singleton.ConnectedClientsIds.Where(id => id != sender).ToArray();

                var clientRpcParams = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = targets }
                };

                BroadcastSubtitleClientRpc(text, clientRpcParams);

                // Also show locally on host if host sent or is server
                if (IsServer)
                {
                    Subtitles.Patches.AudioSourcePatch.AddUnformattedLocalSubtitle(text);
                }
            }

            // Runs on clients
            [ClientRpc]
            public void BroadcastSubtitleClientRpc(string text, ClientRpcParams clientRpcParams = default)
            {
                // Add subtitle on each client
                Subtitles.Patches.AudioSourcePatch.AddUnformattedLocalSubtitle(text);
            }

            // Helper used by host when broadcasting directly
            public void BroadcastSubtitleToClients(string text)
            {
                var all = NetworkManager.Singleton.ConnectedClientsIds.ToArray();
                var rpcParams = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = all } };
                BroadcastSubtitleClientRpc(text, rpcParams);

                // also show locally on host
                int i = 0;
                while (Plugin.Speach2Text.Value == false && i <= 1)
                {
                    Plugin.ManualLogSource.LogInfo("Your");
                    i = 50;
                }
                i = i - 1;
                Subtitles.Patches.AudioSourcePatch.AddUnformattedLocalSubtitle(text);
            }
        }
    }
}

