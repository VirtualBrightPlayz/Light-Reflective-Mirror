using System;
using System.Collections;
using System.Collections.Generic;
using LightReflectiveMirror;
using Mirror;
using UnityEngine;

public class NetworkStateSystem : MonoBehaviour
{
    public class NetworkState
    {
        // TODO: scenes
        public List<NetworkStateObject> objects = new List<NetworkStateObject>();
    }

    public class NetworkStateObject
    {
        public Guid assetId;
        public ulong sceneId;
        public Guid ownerData;
        public bool isPlayer;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public List<NetworkStateBehaviour> behaviours = new List<NetworkStateBehaviour>();
    }

    public class NetworkStateBehaviour
    {
        public byte[] data;
    }

    public struct OwnerDeltaMessage : NetworkMessage
    {
        public Guid ownerId;
        public uint ownedObjectId;
        public NetworkIdentity ownedObject => NetworkIdentity.spawned[ownedObjectId];
    }

    public struct PlayerDeltaMessage : NetworkMessage
    {
        public Guid ownerId;
        public uint ownedObjectId;
        public NetworkIdentity ownedObject => NetworkIdentity.spawned[ownedObjectId];
    }

    public struct OwnerGuidMessage : NetworkMessage
    {
        public Guid ownerId;
    }

    public LightReflectiveMirrorTransport lrm;
    public bool autoOwnerChecking = true;
    public static NetworkState lastState;
    // server
    private Dictionary<NetworkConnectionToClient, Guid> ownerGuids = new Dictionary<NetworkConnectionToClient, Guid>();
    private Dictionary<NetworkConnectionToClient, NetworkIdentity> playerObjects = new Dictionary<NetworkConnectionToClient, NetworkIdentity>();
    private Dictionary<NetworkConnectionToClient, List<NetworkIdentity>> ownerObjects = new Dictionary<NetworkConnectionToClient, List<NetworkIdentity>>();
    // client
    private Dictionary<Guid, uint> remotePlayerObjects = new Dictionary<Guid, uint>();
    private Guid localGuid = Guid.Empty;
    private Dictionary<uint, Guid> remoteOwnedObjects = new Dictionary<uint, Guid>();
    // new server
    private Dictionary<Guid, NetworkConnectionToClient> owners = new Dictionary<Guid, NetworkConnectionToClient>();
    private Dictionary<Guid, NetworkStateObject> ownerStates = new Dictionary<Guid, NetworkStateObject>();

    public void Start()
    {
        lrm.OnPreSwitchHost = PreSwitchHost;
    }

    public void OnDestroy()
    {
    }

    public void OnStartClient()
    {
        remoteOwnedObjects.Clear();
        remotePlayerObjects.Clear();
        NetworkClient.RegisterHandler<OwnerDeltaMessage>(Client_OwnerDelta);
        NetworkClient.RegisterHandler<PlayerDeltaMessage>(Client_PlayerDelta);
        NetworkClient.RegisterHandler<OwnerGuidMessage>(Client_SetGuid);
    }

    public void OnStartHost()
    {
        ownerObjects.Clear();
        playerObjects.Clear();
        owners.Clear();
        ownerStates.Clear();
        PostSwitchHost();
        NetworkServer.RegisterHandler<OwnerGuidMessage>(Server_SetGuid);
    }

    public void OnStopHost()
    {
        ownerGuids.Clear();
    }

    public void OnServerConnect(NetworkConnectionToClient conn)
    {
        if (NetworkServer.localConnection == conn)
            owners.Add(localGuid, conn);
        if (ownerStates.ContainsKey(localGuid))
            SpawnStateObject(ownerStates[localGuid]);
        ownerGuids.Add(conn, Guid.NewGuid());
        conn.Send(new OwnerGuidMessage()
        {
            ownerId = ownerGuids[conn]
        });
        CheckOwnersForNew(conn);
    }

    public void OnClientConnect(NetworkConnection conn)
    {
        NetworkClient.Send(new OwnerGuidMessage()
        {
            ownerId = localGuid
        });
    }

    private void Server_SetGuid(NetworkConnection conn, OwnerGuidMessage msg)
    {
        if (owners.ContainsKey(msg.ownerId))
        {
            return;
        }
        owners.Add(msg.ownerId, conn as NetworkConnectionToClient);
        if (ownerStates.ContainsKey(msg.ownerId))
        {
            SpawnStateObject(ownerStates[msg.ownerId]);
        }
        // PostSwitchHost();
    }

    private void Client_SetGuid(OwnerGuidMessage msg)
    {
        localGuid = msg.ownerId;
    }

    private void Client_PlayerDelta(PlayerDeltaMessage msg)
    {
        if (msg.ownerId == Guid.Empty)
            return;
        if (remotePlayerObjects.ContainsKey(msg.ownerId))
        {
            remotePlayerObjects[msg.ownerId] = msg.ownedObjectId;
        }
        else
        {
            remotePlayerObjects.Add(msg.ownerId, msg.ownedObjectId);
        }
    }

    private void Client_OwnerDelta(OwnerDeltaMessage msg)
    {
        if (remoteOwnedObjects.ContainsKey(msg.ownedObjectId))
        {
            if (msg.ownerId == Guid.Empty)
            {
                remoteOwnedObjects.Remove(msg.ownedObjectId);
            }
            else
            {
                remoteOwnedObjects[msg.ownedObjectId] = msg.ownerId;
            }
        }
        else if (msg.ownerId != Guid.Empty)
        {
            remoteOwnedObjects.Add(msg.ownedObjectId, msg.ownerId);
        }
    }

    public void Update()
    {
        if (autoOwnerChecking)
        {
            if (NetworkServer.active)
                CheckOwners();
        }
    }

    public void CheckOwnersForNew(NetworkConnectionToClient conn)
    {
        foreach (var kvp in ownerObjects)
        {
            Guid guid = ownerGuids[kvp.Key];
            foreach (var own in kvp.Value)
            {
                conn.Send(new OwnerDeltaMessage()
                {
                    ownedObjectId = own.netId,
                    ownerId = guid
                });
            }
        }
        foreach (var kvp in playerObjects)
        {
            NetworkServer.SendToReady(new PlayerDeltaMessage()
            {
                ownedObjectId = kvp.Value.netId,
                ownerId = ownerGuids[kvp.Key]
            });
        }
    }

    public void CheckOwners()
    {
        foreach (var kvp in NetworkServer.connections)
        {
            if (!ownerObjects.ContainsKey(kvp.Value))
                ownerObjects.Add(kvp.Value, new List<NetworkIdentity>());
            foreach (var own in ownerObjects[kvp.Value].ToArray())
            {
                if (own.connectionToClient != kvp.Value)
                {
                    ownerObjects[kvp.Value].Remove(own);
                    Guid guid = Guid.Empty;
                    if (own.connectionToClient != null && ownerGuids.ContainsKey(own.connectionToClient))
                        guid = ownerGuids[own.connectionToClient];
                    NetworkServer.SendToReady(new OwnerDeltaMessage()
                    {
                        ownedObjectId = own.netId,
                        ownerId = guid
                    });
                }
            }
            foreach (var own in kvp.Value.clientOwnedObjects)
            {
                if (own.connectionToClient == kvp.Value && !ownerObjects[kvp.Value].Contains(own))
                {
                    ownerObjects[kvp.Value].Add(own);
                    NetworkServer.SendToReady(new OwnerDeltaMessage()
                    {
                        ownedObjectId = own.netId,
                        ownerId = ownerGuids[own.connectionToClient]
                    });
                }
            }

            if (!playerObjects.ContainsKey(kvp.Value))
            {
                playerObjects.Add(kvp.Value, null);
                NetworkServer.SendToReady(new PlayerDeltaMessage()
                {
                    ownedObjectId = 0,
                    ownerId = ownerGuids[kvp.Value]
                });
            }
            if (kvp.Value.identity != playerObjects[kvp.Value])
            {
                playerObjects[kvp.Value] = kvp.Value.identity;
                NetworkServer.SendToReady(new PlayerDeltaMessage()
                {
                    ownedObjectId = kvp.Value.identity.netId,
                    ownerId = ownerGuids[kvp.Value]
                });
            }
        }
    }

    [ContextMenu("Pre")]
    public void PreSwitchHost()
    {
        NetworkState state = new NetworkState();
        foreach (var kvp in NetworkIdentity.spawned)
        {
            NetworkStateObject obj = new NetworkStateObject();
            obj.position = kvp.Value.transform.localPosition;
            obj.rotation = kvp.Value.transform.localRotation;
            obj.scale = kvp.Value.transform.localScale;
            obj.assetId = kvp.Value.assetId;
            obj.sceneId = kvp.Value.sceneId;
            if (remoteOwnedObjects.ContainsKey(kvp.Value.netId))
            {
                obj.ownerData = remoteOwnedObjects[kvp.Value.netId];
                obj.isPlayer = remotePlayerObjects.ContainsKey(obj.ownerData) && remotePlayerObjects[obj.ownerData] == kvp.Value.netId;
            }
            else
            {
                obj.ownerData = Guid.Empty;
                obj.isPlayer = false;
            }
            foreach (var beh in kvp.Value.NetworkBehaviours)
            {
                NetworkWriter writer = new NetworkWriter();
                beh.OnSerialize(writer, true);
                byte[] arr = writer.ToArray();
                NetworkStateBehaviour stateBehaviour = new NetworkStateBehaviour();
                stateBehaviour.data = arr;
                obj.behaviours.Add(stateBehaviour);
            }
            state.objects.Add(obj);
        }
        lastState = state;
        Debug.Log("Pre");
    }

    [ContextMenu("Post")]
    public void PostSwitchHost()
    {
        NetworkState state = lastState;
        if (state == null)
            return;
        foreach (var obj in state.objects)
        {
            SpawnStateObject(obj);
        }
        Debug.Log("Post");
    }

    public NetworkIdentity SpawnStateObject(NetworkStateObject obj)
    {
        if (obj.ownerData != Guid.Empty && !owners.ContainsKey(obj.ownerData))
        {
            if (!ownerStates.ContainsKey(obj.ownerData))
            {
                ownerStates.Add(obj.ownerData, obj);
            }
            return null;
        }
        NetworkIdentity net;
        if (obj.sceneId == 0)
        {
            if (obj.assetId == Guid.Empty)
                return null;
            if (!NetworkClient.GetPrefab(obj.assetId, out GameObject pref))
            {
                if (NetworkManager.singleton.playerPrefab.GetComponent<NetworkIdentity>().assetId != obj.assetId)
                    return null;
                pref = NetworkManager.singleton.playerPrefab;
            }
            net = Instantiate(pref, obj.position, obj.rotation).GetComponent<NetworkIdentity>();
            net.transform.localScale = obj.scale;
            if (owners.ContainsKey(obj.ownerData))
            {
                if (obj.isPlayer)
                {
                    if (owners[obj.ownerData].identity == null)
                        NetworkServer.AddPlayerForConnection(owners[obj.ownerData], net.gameObject);
                    else
                    {
                        Destroy(owners[obj.ownerData].identity);
                        NetworkServer.ReplacePlayerForConnection(owners[obj.ownerData], net.gameObject);
                    }
                }
                else
                    NetworkServer.Spawn(net.gameObject, owners[obj.ownerData]);
            }
            else
                NetworkServer.Spawn(net.gameObject);
        }
        else
        {
            net = NetworkIdentity.GetSceneIdentity(obj.sceneId);
        }

        for (int i = 0; i < net.NetworkBehaviours.Length; i++)
        {
            NetworkReader reader = new NetworkReader(obj.behaviours[i].data);
            net.NetworkBehaviours[i].OnDeserialize(reader, true);
        }

        return net;
    }
}