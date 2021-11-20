using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;
using System;

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5491;
    LinkedList<PlayerAccount> playerAccounts;
    int playerWaitingID = -1;
    int playerWaitingID2 = -1;
    string playerWaitingIDN = "";
    string playerWaitingIDN2 = "";
    LinkedList<GameRoom> gameRooms;
    // Start is called before the first frame update
    void Start()
    {
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);
        playerAccounts = new LinkedList<PlayerAccount>();
        //read in player accounts
        LoadPlayerManagementFile();
        gameRooms = new LinkedList<GameRoom>();
    }

    // Update is called once per frame
    void Update()
    {

        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID
            , out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                AppendLogFile("Start connection " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                AppendLogFile("Disconnection " + recConnectionID);
                break;
        }

    }

    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);
    }

    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);
        string[] csv = msg.Split(',');
        int signifier = int.Parse(csv[0]);
        string n = "";
        string p = "";
        if (csv.Length > 1)
            n = csv[1];
        if (csv.Length > 2)
        {

            p = csv[2];
        }
        bool nameIsInUse = false;
        bool validUser = false;
        try
        {
            if (signifier == ClientToServerSignifiers.CreateAccount)
            {
                Debug.Log("create account");
                //chk if player already exists
                foreach (PlayerAccount pa in playerAccounts)
                {
                    if (pa.name == n)
                    {
                        nameIsInUse = true;
                        break;
                    }
                }
                if (nameIsInUse)
                {
                    AppendLogFile(n+":Account creation failed from connection " + id);
                    SendMessageToClient(ServerToClientSignifiers.AccountCreationFailed + "," + n, id);
                    // + "," + System.DateTime.Now.ToString("HH:mm:ss MM/dd/yyyy"));
                }
                else
                {
                    ///if not create new, add to list
                    PlayerAccount playerAccount = new PlayerAccount(id, n, p);
                    playerAccounts.AddLast(playerAccount);
                    //send to client suc or fail
                    Debug.Log("create success");
                    AppendLogFile(n + ":Account creation succeed from connection " + id);
                    SendMessageToClient(ServerToClientSignifiers.AccountCreationComplete + "," + n, id);
                    // save list to hd
                    SavePlayerManagementFile();
                }


            }
            else if (signifier == ClientToServerSignifiers.Login)
            {
                Debug.Log("login");

                //chk if player is already exists,
                foreach (PlayerAccount pa in playerAccounts)
                {
                    if (pa.name == n && pa.password == p)
                    {
                        validUser = true;
                        Debug.Log("login success");
                        break;
                    }
                }
                //send to client suc or fail
                if (validUser)
                {
                    AppendLogFile(n+":Login succeed from connection " + id);
                    SendMessageToClient(ServerToClientSignifiers.LoginComplete + "," + n, id);
                }
                else
                {
                    AppendLogFile(n+":Login failed from connection " + id);
                    SendMessageToClient(ServerToClientSignifiers.LoginFailed + "," + n, id);
                }
            }
            else if (signifier == ClientToServerSignifiers.JoinGammeRoomQueue)
            {
                if (playerWaitingID == -1)
                {
                    playerWaitingID = id;
                    if (csv.Length > 1)
                    {
                        playerWaitingIDN = csv[1];
                        AppendLogFile(csv[1]+":player join in game room from connection " + id);
                        SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], id);
                    }
                    else
                    {
                        AppendLogFile("player join in game room from connection " + id);
                        SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id, id);
                    }
                }
                else if (playerWaitingID2 == -1)
                {
                    playerWaitingID2 = id;
                    if (csv.Length > 1)
                    {
                        playerWaitingIDN2 = csv[1];
                        AppendLogFile(csv[1] + ":player join in game room from connection " + id);
                        SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], id);
                        SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], playerWaitingID);
                    }
                    else
                    {
                        AppendLogFile("player join in game room from connection " + id);
                        SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id, id);
                        SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id, playerWaitingID);
                    }
                }

                else
                {
                    GameRoom gr = new GameRoom();
                    gr.Player1=new PlayerAccount(playerWaitingID, playerWaitingIDN,"");
                    gr.Player2 = new PlayerAccount(playerWaitingID2, playerWaitingIDN2,"");
                    gr.Player3 = new PlayerAccount(id, csv[1],"");
                    gameRooms.AddLast(gr);
                    //foreach (PlayerAccount pa in gr.PlayerList)
                    //{
                    AppendLogFile(csv[1] + ":player join in game room from connection " + id);
                    AppendLogFile("start game with players(connection_id:name) " + gr.getPlayers());
                    SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], id);
                    SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], playerWaitingID);
                    SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], playerWaitingID2);
                    SendMessageToClient(ServerToClientSignifiers.GameStart + gr.getPlayers(), gr.Player1.id);
                    SendMessageToClient(ServerToClientSignifiers.GameStart + gr.getPlayers(), gr.Player2.id);
                    SendMessageToClient(ServerToClientSignifiers.GameStart + gr.getPlayers(), gr.Player3.id);
                    //}

                    playerWaitingID = -1;
                    playerWaitingID2 = -1;
                    playerWaitingIDN = "";
                    playerWaitingIDN2 = "";
                }
            }
            else if (signifier == ClientToServerSignifiers.PlayGame)
            {
                GameRoom gr = GetGameRoomClientId(id);
                if (gr != null)
                {
                    //if (gr.PlayerList[0].id == id)
                    //    SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "," + gr.PlayerList[1].name, id);
                    //else SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "," + gr.PlayerList[0].name, id);
                }
            }
            else if (signifier == ClientToServerSignifiers.SendMsg)
            {
                Debug.Log("send from s: " + msg);
                GameRoom gr = GetGameRoomClientId(id);
                if (gr != null)
                {
                    //foreach (PlayerAccount pa in gr.PlayerList)
                    //{
                    //    SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + csv[1], pa.id);
                    //}
                    AppendLogFile(csv[2]+":"+csv[1]+" from connection "+id);
                    SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1]+","+csv[2], gr.Player1.id);
                    SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], gr.Player2.id);
                    SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], gr.Player3.id);
                    foreach (PlayerAccount ob in gr.ObserverList)
                    {
                        SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], ob.id);
                    }
                }
            }
            else if (signifier == ClientToServerSignifiers.SendPrefixMsg)
            {
                Debug.Log("send pr from s: " + msg);
                GameRoom gr = GetGameRoomClientId(id);
                if (gr != null)
                {
                    //foreach (PlayerAccount pa in gr.PlayerList)
                    //{
                    AppendLogFile(csv[2] + ":" + csv[1] + " from connection " + id);
                        SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], gr.Player1.id);
                    SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], gr.Player2.id);
                    SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], gr.Player3.id);
                    //}
                    foreach (PlayerAccount ob in gr.ObserverList)
                    {
                        SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], ob.id);
                    }

                }
            }
            else if (signifier == ClientToServerSignifiers.SendClientMsg)
            {
                Debug.Log("send pr from c: " + msg);

                Debug.Log("p: " + csv[1].Substring(0, csv[1].IndexOf(':')));
                
                    int recId =int.Parse( csv[1].Substring(0, csv[1].IndexOf(':')));
                Debug.Log("rid " + recId);
                if (csv.Length > 3)
                {
                    AppendLogFile(csv[3] + ":" + csv[2] + " to connection " + recId);
                    SendMessageToClient(ServerToClientSignifiers.ReceiveCMsg + "," + id + "," + csv[2] + "," + csv[3], recId);
                    SendMessageToClient(ServerToClientSignifiers.ReceiveCMsg + "," + id + "," + csv[2] + "," + csv[3], id);
                }

            }
            else if (signifier == ClientToServerSignifiers.JoinAsObserver)
            {
                Debug.Log("join as observer"+gameRooms.Count);
                foreach (GameRoom gr in gameRooms)
                {
                    gr.addObserver(id, csv[1]);
                    //foreach (PlayerAccount pa in gr.PlayerList)
                    //{
                    AppendLogFile(csv[1] + ":join as observer from connection "+id);
                    SendMessageToClient(ServerToClientSignifiers.someoneJoinedAsObserver + "," + id+","+ csv[1], gr.Player1.id);
                    SendMessageToClient(ServerToClientSignifiers.someoneJoinedAsObserver + "," + id + "," + csv[1], gr.Player2.id);
                    SendMessageToClient(ServerToClientSignifiers.someoneJoinedAsObserver + "," + id + "," + csv[1], gr.Player3.id);
                    //}


                }

            }
            else if (signifier==ClientToServerSignifiers.ReplayMsg)
            {
                Debug.Log("replay req");
                string[] contain=ReadLogFile();
                foreach (var line in contain)
                {
                    SendMessageToClient(ServerToClientSignifiers.ReplayMsg + "," + line, id);
                }
                
            }
        }
        catch (Exception ex)
        {
            Debug.Log("error" + ex.Message);
        }

    }

    public void SavePlayerManagementFile()
    {
        StreamWriter sw = new StreamWriter(Application.dataPath + Path.DirectorySeparatorChar + "PlayerManagementFile.txt");
        foreach (PlayerAccount pa in playerAccounts)
        {
            sw.WriteLine(PlayerAccount.PlayerIdSinifier+","+pa.id + "," + pa.name + "," + pa.password);
        }
        sw.Close();
    }

    public void LoadPlayerManagementFile()
    {
        if (File.Exists(Application.dataPath + Path.DirectorySeparatorChar + "PlayerManagementFile.txt"))
        {
            StreamReader sr = new StreamReader(Application.dataPath + Path.DirectorySeparatorChar + "PlayerManagementFile.txt");
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                string[] csv = line.Split(',');

                int signifier = int.Parse(csv[0]);
                if (signifier == PlayerAccount.PlayerIdSinifier)
                {
                    playerAccounts.AddLast(new PlayerAccount(int.Parse(csv[1]), csv[2], csv[3]));
                }
            }
        }
    }
    public void AppendLogFile(string line)
    {
        StreamWriter sw = new StreamWriter(Application.dataPath + Path.DirectorySeparatorChar + "Log.txt", true);

        sw.WriteLine(System.DateTime.Now.ToString("yyyyMMdd HHmmss") +": "+ line);

        sw.Close();
    }

    public string[] ReadLogFile()
    {
        string[] contain = null;
        if (File.Exists(Application.dataPath + Path.DirectorySeparatorChar + "Log.txt"))
        {
            contain = File.ReadAllLines(Application.dataPath + Path.DirectorySeparatorChar + "Log.txt");
            //StreamReader sr = new StreamReader(Application.dataPath + Path.DirectorySeparatorChar + "Log.txt");
            //string line;
            //while ((line = sr.ReadLine()) != null)
            //{
            //    string[] csv = line.Split(',');

            //    int signifier = int.Parse(csv[0]);

            //}
        }
        return contain;
    }
    public GameRoom GetGameRoomClientId(int playerId)
    {
        foreach (GameRoom gr in gameRooms)
        {
            //foreach (PlayerAccount pa in gr.PlayerList)
            //{
                if (gr.Player1.id == playerId || gr.Player2.id == playerId || gr.Player3.id == playerId)
                {
                    return gr;
                }
            //}

        }
        return null;
    }
}
public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1;
    public const int Login = 2;
    public const int JoinGammeRoomQueue = 3;
    public const int PlayGame = 4;
    public const int SendMsg = 5;
    public const int SendPrefixMsg = 6;
    public const int JoinAsObserver = 7;
    public const int SendClientMsg = 8;
    public const int ReplayMsg = 9;
}
public static class ServerToClientSignifiers
{
    public const int LoginComplete = 1;
    public const int LoginFailed = 2;
    public const int AccountCreationComplete = 3;
    public const int AccountCreationFailed = 4;
    public const int OpponentPlay = 5;
    public const int GameStart = 6;
    public const int ReceiveMsg = 7;
    public const int someoneJoinedAsObserver = 8;
    public const int ListOfPlayer = 8;
    public const int JoinedPlay=9;
    public const int ReceiveCMsg = 10;
    public const int ReplayMsg = 11;
}
public class PlayerAccount
{
    public const int PlayerIdSinifier = 1;
    public string name, password;
    public int id;
    public PlayerAccount(int i, string n, string p)
    {
        id = i;
        name = n;
        password = p;
    }

}
public class GameRoom
{
    //public int playerId1, playerId2, playerID3;
    //public string playerIdN, playerId2N, playerID3N;
    public List<PlayerAccount> ObserverList;
    public PlayerAccount Player1, Player2, Player3;
    //int P1,string n1,int P2,string n2,int P3, string n3
    public GameRoom()
    {
        //playerId1 = P1;
        //playerIdN = n1;
        //playerId2 = P2;
        //playerId2N = n2;
        //playerID3N = n3;
        //playerID3 = P3;
        //PlayerList = new List<PlayerAccount>();
        ObserverList = new List<PlayerAccount>();
    }
    //public void addPlayer(int id, string n)
    //{
    //    PlayerList.Add(new PlayerAccount(id, n, ""));
    //}
    public void addObserver(int id, string n)
    {
        if(!ObserverList.Contains(new PlayerAccount(id, n, "")))
            ObserverList.Add(new PlayerAccount(id, n, ""));
    }
    public string getPlayers()
    {
        string p = "";
        //foreach (PlayerAccount item in PlayerList)
        //{
            p += "," + Player1.id + ":" + Player1.name;
        p += "," + Player2.id + ":" + Player2.name;
        p += "," + Player3.id + ":" + Player3.name;
        //}
        return p;
    }
}