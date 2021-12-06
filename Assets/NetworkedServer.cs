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
    //holding player accounts
    LinkedList<PlayerAccount> playerAccounts;
    #region chat room variable
    //status for checking is there already player 1 joined or not for chatting purpose
    int chatterWaitingID = -1;
    //handle name of player 1 during game room of 3 chatter
    string chatterWaitingIDN = "";
    //status for checking is there already player 2 joined or not for chatting purpose
    int chatterWaitingID2 = -1;
    //handle name of player 2 during game room of 3 chatter
    string chatterWaitingIDN2 = "";   
    LinkedList<ChatRoom> chatRooms;
    #endregion
    #region game room variable
    //status for checking is there already player 1 joined or not for playing purpose
    int playerWaiting = -1;       
    //handle name of player 1 during game room of 2 player
    string playerWaitingN = "";
    LinkedList<GameRoom> gameRooms;
    //making array and   
    //by default I am providing 0-9 where no use of zero  
    static char[] arr = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' };
    // The flag veriable checks who has won if it's value is 1 then some one has won the match if -1 then Match has Draw if 0 then match is still running  
    static int flag = 0;
    static int choice; //This holds the choice at which position user want to mark   
    #endregion

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
        //created empty list of chat room and game room for server
        chatRooms = new LinkedList<ChatRoom>();
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
    //msg received from client id
    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);
        string[] csv = msg.Split(',');
        //catch signifier for differentiate the message type
        int signifier = int.Parse(csv[0]);
        //holding name during account create or login
        string n = "";
        //holding password during account create or login
        string p = "";
        //check the array length and then read the value
        if (csv.Length > 1)
            n = csv[1];
        if (csv.Length > 2)
        {
            p = csv[2];
        }
        //flag for checking duplicate user during user creation
        bool nameIsInUse = false;
        //flag for chekcing valid user during login
        bool validUser = false;
        try
        {
            if (signifier == ClientToServerSignifiers.CreateAccount)//msg format: signifier, name,password
            {
                Debug.Log("create account signifier detect");
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
                    SendMessageToClient(ServerToClientSignifiers.AccountCreationFailed + "," + n, id);   //msg format: signifier, name                 
                }
                else
                {
                    ///if not create new, add to list
                    PlayerAccount playerAccount = new PlayerAccount(id, n, p);
                    playerAccounts.AddLast(playerAccount);
                    Debug.Log("create success");
                    //send to client suc or fail                   
                    AppendLogFile(n + ":Account creation succeed from connection " + id);
                    SendMessageToClient(ServerToClientSignifiers.AccountCreationComplete + "," + n, id); //msg format: signifier, name
                    // save list to hd
                    SavePlayerManagementFile();
                }
            }
            else if (signifier == ClientToServerSignifiers.Login)//msg format: signifier, name,password
            {
                Debug.Log("login signifier detect");

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
                    SendMessageToClient(ServerToClientSignifiers.LoginComplete + "," + n, id);//msg format: signifier, name
                }
                else
                {
                    AppendLogFile(n+":Login failed from connection " + id);
                    SendMessageToClient(ServerToClientSignifiers.LoginFailed + "," + n, id);//msg format: signifier, name
                }
            }
            else if (signifier == ClientToServerSignifiers.JoinChatRoomQueue)//msg format: signifier, joined chatter name
            {
                Debug.Log("join chat room queue signifier detect");
                if (chatterWaitingID == -1)//no chatter joined still now
                {
                    chatterWaitingID = id;
                    if (csv.Length > 1)
                    {
                        chatterWaitingIDN = csv[1];
                        AppendLogFile(csv[1]+":player join in game room from connection " + id);
                        SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], id);//msg format: signifier,clientid,joined chatter name
                    }
                    else
                    {
                        AppendLogFile("player join in game room from connection " + id);
                        SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id, id);//msg format: signifier,clientid
                    }
                }
                else if (chatterWaitingID2 == -1) //only 1 chatter joined 
                {
                    chatterWaitingID2 = id;
                    if (csv.Length > 1)
                    {
                        chatterWaitingIDN2 = csv[1];
                        AppendLogFile(csv[1] + ":player join in game room from connection " + id);
                        SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], id); //msg format: signifier,clientid,joined chatter name
                        //send notification to other member of room new memeber joined
                        SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], chatterWaitingID);//msg format: signifier,clientid,joined chatter name
                    }
                    else
                    {
                        AppendLogFile("player join in game room from connection " + id);
                        SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id, id);//msg format: signifier,clientid
                        //send notification to other member of room new memeber joined
                        SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id, chatterWaitingID);//msg format: signifier,clientid
                    }
                }
                else //2 chatter joined
                {
                    Debug.Log("Creating chat room now.");
                    //creating chat room and assigned 3 player
                    ChatRoom gr = new ChatRoom();
                    gr.Player1=new PlayerAccount(chatterWaitingID, chatterWaitingIDN,"");
                    gr.Player2 = new PlayerAccount(chatterWaitingID2, chatterWaitingIDN2,"");
                    gr.Player3 = new PlayerAccount(id, csv[1],"");
                    chatRooms.AddLast(gr);                   
                    AppendLogFile(csv[1] + ":player join in game room from connection " + id);
                    AppendLogFile("start game with players(connection_id:name) " + gr.getChatters());
                    //send messag to every memeber of chat room
                    SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], id);//msg format: signifier, client id, joined chatter name
                    //send notification to other member of room new memeber joined
                    SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], chatterWaitingID);//msg format: signifier, client id, joined chatter name
                    SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], chatterWaitingID2);//msg format: signifier, client id, joined chatter name
                    //nofify all member of game room about chat begin
                    SendMessageToClient(ServerToClientSignifiers.ChatStart + gr.getChatters(), gr.Player1.id);//msg format: siginifier, list of player from 
                    SendMessageToClient(ServerToClientSignifiers.ChatStart + gr.getChatters(), gr.Player2.id);
                    SendMessageToClient(ServerToClientSignifiers.ChatStart + gr.getChatters(), gr.Player3.id);
                    //reseting all flag for further chat room
                    chatterWaitingID = -1;
                    chatterWaitingID2 = -1;
                    chatterWaitingIDN = "";
                    chatterWaitingIDN2 = "";
                }
            }
            else if (signifier == ClientToServerSignifiers.JoinDGameRoomQueue)//msg format: signifier, joined player name
            {
                Debug.Log("join game room queue signifier detect");
                if (playerWaiting == -1)//no player joined
                {
                    playerWaiting = id;
                    if (csv.Length > 1)
                    {
                        playerWaitingN = csv[1];
                        AppendLogFile(csv[1] + ":player join in game room for play from connection " + id);
                        SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], id);//msg format:signifier, clientid, joined player name
                    }
                    else
                    {
                        AppendLogFile("player join in game room for play from connection " + id);
                        SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id, id);// msg format: signifier, clientid
                    }
                }               
                else//only 1 player joined
                {
                    Debug.Log("creating game room");
                    GameRoom gr = new GameRoom();
                    gr.Player1 = new PlayerAccount(playerWaiting, playerWaitingN, "");                    
                    gr.Player2 = new PlayerAccount(id, csv[1], "");
                    gameRooms.AddLast(gr);                    
                    AppendLogFile(csv[1] + ":player join in game room for tick tac toe from connection " + id);
                    AppendLogFile("start game with players(connection_id:name) " + gr.getPlayers());
                    SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], id);//msg format:signifier, clientid, joined player name
                    SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], playerWaiting);
                    Debug.Log("DGAME START");
                    //sending notification about game start
                    SendMessageToClient(ServerToClientSignifiers.DGameStart + ",1,"+gr.Player2.name, gr.Player1.id);//msg format:signifier, player number of yours, opponent player name
                    SendMessageToClient(ServerToClientSignifiers.DGameStart +",2,"+gr.Player1.name, id);//msg format:signifier, player number of yours, opponent player name
                    //reset flag for next game room
                    playerWaiting = -1;                   
                    playerWaitingN = "";                   
                }
            }

            else if (signifier == ClientToServerSignifiers.PlayGame)
            {
                Debug.Log("play game siginifier detected");
                GameRoom gr = GetGameRoomClientId(id);
                if (gr != null)
                {                                        
                        if (csv.Length > 3)
                        {
                            arr[int.Parse(csv[3])] = char.Parse(csv[2]);
                        }
                        flag = CheckWin();// calling of check win  
                        Debug.Log("flag after check " + flag+" and "+ arr[1]+"|"+ arr[2] + "|" + arr[3]+";"+arr[4] + "|" + arr[5] + "|" + arr[6] + ";" + arr[7] + "|" + arr[8] + "|" + arr[9]);

                    if (flag == 1)
                    {
                        AppendLogFile(csv[1]+" Player turn " + csv[2] + " in " + csv[3] + ". Now "+csv[1]+" has won!");
                        Debug.Log("win " + flag);
                    }
                    if (flag == -1)
                    {
                        AppendLogFile("After "+csv[1] + " Player turn " + csv[2] + " in " + csv[3] + ". Now play has been drawn!");
                        Debug.Log("draw " + flag);
                    }
                    if (flag == 0)
                    {
                        AppendLogFile(csv[1] + " Player turn " + csv[2] + " in " + csv[3] + ". Now " + gr.Player1.name + " chance!");
                        Debug.Log("play " + flag); 
                    }
                    if (gr.Player1.id == id)
                    {
                        SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "," + gr.Player1.name+","+ csv[2] + ","+ csv[3]+","+flag, gr.Player2.id);//msg format: signifier,opponent player name,turn value,cell played by current player,flag after checking turn
                        SendMessageToClient(ServerToClientSignifiers.SelfPlay + "," + gr.Player1.name + "," + csv[2] + "," + csv[3] + "," + flag, id);//msg format: signifier,self player name,turn value,cell played by current player,flag after checking turn
                        foreach (PlayerAccount ob in gr.ObserverList)
                        {
                            SendMessageToClient(ServerToClientSignifiers.OtherPlay + "," + gr.Player1.name + "," + csv[2] + "," + csv[3] + "," + flag, ob.id);//msg format: signifier,turned player name,turn value,cell played by current player,flag after checking turn
                        }
                    }
                    else
                    {
                        SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "," + gr.Player2.name+"," + csv[2] + "," + csv[3] + "," + flag, gr.Player1.id);//msg format: signifier,opponent player name,turn value,cell played by current player,flag after checking turn
                        SendMessageToClient(ServerToClientSignifiers.SelfPlay + "," + gr.Player2.name + "," + csv[2] + "," + csv[3] + "," + flag, id);//msg format: signifier,self player name,turn value,cell played by current player,flag after checking turn
                        foreach (PlayerAccount ob in gr.ObserverList)
                        {
                            SendMessageToClient(ServerToClientSignifiers.OtherPlay + "," + gr.Player2.name + "," + csv[2] + "," + csv[3] + "," + flag, ob.id);//msg format: signifier,turned player name,turn value,cell played by current player,flag after checking turn
                        }
                    }
                    
                }
            }
            else if (signifier == ClientToServerSignifiers.SendMsg)
            {
                Debug.Log("send message signifier dectected. " + msg);
                ChatRoom gr = GetChatRoomClientId(id);
                if (gr != null)
                {                    
                    AppendLogFile(csv[2]+":"+csv[1]+" from connection "+id);
                    //sending message to all member of chat room
                    SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1]+","+csv[2], gr.Player1.id);//msg format: signifier, client id, message content, msg sender name
                    SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], gr.Player2.id);//msg format: signifier, client id, message content, msg sender name
                    SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], gr.Player3.id);//msg format: signifier, client id, message content, msg sender name

                    foreach (PlayerAccount ob in gr.ObserverList)
                    {
                        SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], ob.id);//msg format: signifier, client id, message content, msg sender name
                    }
                }
            }
            else if (signifier == ClientToServerSignifiers.SendPrefixMsg)
            {
                Debug.Log("send prefixed message signifier dectected. " + msg);
                ChatRoom gr = GetChatRoomClientId(id);
                if (gr != null)
                {                   
                    AppendLogFile(csv[2] + ":" + csv[1] + " from connection " + id);
                    SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], gr.Player1.id);//msg format: signifier, client id, message content, msg sender name
                    SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], gr.Player2.id);//msg format: signifier, client id, message content, msg sender name
                    SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], gr.Player3.id);   //msg format: signifier, client id, message content, msg sender name                 
                    foreach (PlayerAccount ob in gr.ObserverList)
                    {
                        SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], ob.id);//msg format: signifier, client id, message content, msg sender name
                    }
                }
            }
            else if (signifier == ClientToServerSignifiers.SendClientMsg)
            {
                Debug.Log("send private message signifier dectected. " + msg);

                Debug.Log("message for: " + csv[1].Substring(0, csv[1].IndexOf(':')));
                //receiving receiver id for sending the private message
                int recId =int.Parse( csv[1].Substring(0, csv[1].IndexOf(':')));
                Debug.Log("rid " + recId);
                if (csv.Length > 3)
                {
                    AppendLogFile(csv[3] + ":" + csv[2] + " to connection " + recId);
                    SendMessageToClient(ServerToClientSignifiers.ReceiveCMsg + "," + id + "," + csv[2] + "," + csv[3], recId);//msg format: signifier, clientid, message content, sender name
                    SendMessageToClient(ServerToClientSignifiers.ReceiveCMsg + "," + id + "," + csv[2] + "," + csv[3], id);//msg format: signifier, clientid, message content, sender name
                }
            }
            else if (signifier == ClientToServerSignifiers.JoinAsObserver)
            {
                Debug.Log("join as observer in chat signifier detected. ");
                foreach (ChatRoom gr in chatRooms)
                {
                    gr.addObserver(id, csv[1]);                                        
                    AppendLogFile(csv[1] + ":join as observer from connection "+id);
                    //notify to all chatter about observer joining
                    SendMessageToClient(ServerToClientSignifiers.someoneJoinedAsObserver + "," + id+","+ csv[1], gr.Player1.id);//msg format: signifier,client id, observer name
                    SendMessageToClient(ServerToClientSignifiers.someoneJoinedAsObserver + "," + id + "," + csv[1], gr.Player2.id);//msg format: signifier,client id, observer name
                    SendMessageToClient(ServerToClientSignifiers.someoneJoinedAsObserver + "," + id + "," + csv[1], gr.Player3.id);//msg format: signifier,client id, observer name
                }
            }
            else if (signifier == ClientToServerSignifiers.JoinDAsObserver)
            {
                Debug.Log("join as observer in play signifier detected. ");
                foreach (GameRoom gr in gameRooms)
                {
                    gr.addObserver(id, csv[1]);
                    AppendLogFile(csv[1] + ":join as observer from connection " + id);
                    //notify to all chatter about observer joining
                    SendMessageToClient(ServerToClientSignifiers.someoneJoinedAsObserver + "," + id + "," + csv[1], gr.Player1.id);//msg format: signifier,client id, observer name
                    SendMessageToClient(ServerToClientSignifiers.someoneJoinedAsObserver + "," + id + "," + csv[1], gr.Player2.id);//msg format: signifier,client id, observer name                    
                }
            }
            else if (signifier==ClientToServerSignifiers.ReplayMsg)
            {
                Debug.Log("replay request signifier detected. ");
                string[] contain=ReadLogFile();
                foreach (var line in contain)
                {
                    SendMessageToClient(ServerToClientSignifiers.ReplayMsg + "," + line, id);//msg format: signifier, msg
                }                
            }
        }
        catch (Exception ex)
        {
            Debug.Log("error" + ex.Message);
        }

    }
    //Checking that any player has won or not  
    private static int CheckWin()
    {
        #region Horzontal Winning Condtion
        //Winning Condition For First Row   
        if (arr[1] == arr[2] && arr[2] == arr[3]) {return 1; }
        //Winning Condition For Second Row   
        else if (arr[4] == arr[5] && arr[5] == arr[6]) { return 1; }
        //Winning Condition For Third Row   
        else if (arr[7] == arr[8] && arr[8] == arr[9]) { return 1; }
        #endregion
        #region vertical Winning Condtion
        //Winning Condition For First Column       
        else if (arr[1] == arr[4] && arr[4] == arr[7]) { return 1; }
        //Winning Condition For Second Column  
        else if (arr[2] == arr[5] && arr[5] == arr[8]) { return 1; }
        //Winning Condition For Third Column  
        else if (arr[3] == arr[6] && arr[6] == arr[9]) { return 1; }
        #endregion
        #region Diagonal Winning Condition
        else if (arr[1] == arr[5] && arr[5] == arr[9]) { return 1; }
        else if (arr[3] == arr[5] && arr[5] == arr[7]) { return 1; }
        #endregion
        #region Checking For Draw
        // If all the cells or values filled with X or O then any player has won the match  
        else if (arr[1] != '1' && arr[2] != '2' && arr[3] != '3' 
            && arr[4] != '4' && arr[5] != '5' && arr[6] != '6' 
            && arr[7] != '7' && arr[8] != '8' && arr[9] != '9')
        {
            return -1;
        }
        #endregion
        else
        {
            return 0;
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
        }
        return contain;
    }
    public ChatRoom GetChatRoomClientId(int playerId)
    {
        foreach (ChatRoom gr in chatRooms)
        {            
                if (gr.Player1.id == playerId || gr.Player2.id == playerId || gr.Player3.id == playerId)
                {
                    return gr;
                }            
        }
        return null;
    }
    public GameRoom GetGameRoomClientId(int playerId)
    {
        foreach (GameRoom gr in gameRooms)
        {          
            if (gr.Player1.id == playerId || gr.Player2.id == playerId)
            {
                return gr;
            }
        }
        return null;
    }
}
public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1;
    public const int Login = 2;
    public const int JoinChatRoomQueue = 3;
    public const int PlayGame = 4;
    public const int SendMsg = 5;
    public const int SendPrefixMsg = 6;
    public const int JoinAsObserver = 7;
    public const int SendClientMsg = 8;
    public const int ReplayMsg = 9;
    public const int JoinDGameRoomQueue = 10;
    public const int JoinDAsObserver = 11;
}
public static class ServerToClientSignifiers
{
    public const int LoginComplete = 1;
    public const int LoginFailed = 2;
    public const int AccountCreationComplete = 3;
    public const int AccountCreationFailed = 4;
    public const int OpponentPlay = 5;
    public const int ChatStart = 6;
    public const int ReceiveMsg = 7;
    public const int someoneJoinedAsObserver = 8;
    public const int ListOfPlayer = 8;
    public const int JoinedPlay=9;
    public const int ReceiveCMsg = 10;
    public const int ReplayMsg = 11;
    public const int DGameStart = 12;
    public const int SelfPlay = 13;
    public const int OtherPlay = 14;
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
    public List<PlayerAccount> ObserverList;
    public PlayerAccount Player1, Player2;
    public GameRoom()
    {       
        ObserverList = new List<PlayerAccount>();
    }   
    public void addObserver(int id, string n)
    {
        if (!ObserverList.Contains(new PlayerAccount(id, n, "")))
            ObserverList.Add(new PlayerAccount(id, n, ""));
    }
    public string getPlayers()
    {
        string p = "";        
        p += "," + Player1.id + ":" + Player1.name;
        p += "," + Player2.id + ":" + Player2.name;    
        return p;
    }
}

public class ChatRoom
{
    public List<PlayerAccount> ObserverList;
    public PlayerAccount Player1, Player2, Player3;
    public ChatRoom()
    {        
        ObserverList = new List<PlayerAccount>();
    }   
    public void addObserver(int id, string n)
    {
        if(!ObserverList.Contains(new PlayerAccount(id, n, "")))
            ObserverList.Add(new PlayerAccount(id, n, ""));
    }
    public string getChatters()
    {
        string p = "";
        p += "," + Player1.id + ":" + Player1.name;
        p += "," + Player2.id + ":" + Player2.name;
        p += "," + Player3.id + ":" + Player3.name;        
        return p;
    }
}
