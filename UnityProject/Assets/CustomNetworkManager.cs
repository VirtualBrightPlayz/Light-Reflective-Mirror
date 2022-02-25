using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class CustomNetworkManager : NetworkManager
{
    public NetworkStateSystem system;

    public override void OnServerConnect(NetworkConnection conn)
    {
        base.OnServerConnect(conn);
        system.OnServerConnect(conn as NetworkConnectionToClient);
    }

    public override void OnServerReady(NetworkConnection conn)
    {
        base.OnServerReady(conn);
        // if (conn == NetworkServer.localConnection)
            // system.OnClientConnect(conn);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        system.OnStartClient();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        system.OnStartHost();
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        system.OnStopHost();
    }

    public override void OnClientConnect(NetworkConnection conn)
    {
        base.OnClientConnect(conn);
        system.OnClientConnect(conn);
    }
}
