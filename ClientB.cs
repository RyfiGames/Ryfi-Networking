/* Copyright Notice
 ********************************************************************************
 * Copyright (C) Ryan Magilton - All Rights Reserved                            *
 * Unauthorized copying of this file, via any medium is strictly prohibited     *
 * without explicit permission                                                  *
 * Written by Ryan Magilton <ryfiandsen@comcast.net>, July 2020                 *
 ********************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using UnityEngine;

namespace Ryfi.Networking
{

    public class ClientB : MonoBehaviour
    {

        public static ClientB singleton;

        public bool isClient;
        public TcpClient socket;
        private NetworkStream stream;
        private StreamWriter writer;
        private StreamReader reader;

        public string host = "127.0.0.1";
        public int port = 1818;

        public string clientName;
        private bool secured;

        public int myID;

        public List<NetworkedObject> networkedObjects = new List<NetworkedObject>();
        public List<IClientEventListener> listeners = new List<IClientEventListener>();

        public Dictionary<string, Action> waitingActions = new Dictionary<string, Action>();

        // Start is called before the first frame update
        void Awake()
        {
            singleton = this;
        }

        public void ConnectToServer()
        {
            if (isClient)
                return;

            networkedObjects.Clear();

            try
            {
                socket = new TcpClient(host, port);
                stream = socket.GetStream();
                writer = new StreamWriter(stream);
                reader = new StreamReader(stream);
                isClient = true;
                StartCoroutine(SecureConnection());
                foreach (IClientEventListener l in listeners)
                {
                    l.OnClientConnect();
                }
            }
            catch (Exception e)
            {
                Debug.Log("Socket Error: " + e.Message);
            }
        }

        private IEnumerator SecureConnection()
        {
            while (isClient && !secured)
            {
                SendData("NC " + clientName);
                print("Attempting Connection...");
                yield return new WaitForSeconds(10f);
            }
        }

        private void Update()
        {
            if (isClient)
            {
                List<string> dataBuffer = new List<string>();
                while (stream.DataAvailable)
                {
                    string data = reader.ReadLine();
                    if (data != null)
                        dataBuffer.Add(data);
                    //OnIncomingData(data);
                }
                string bsfd = "";
                for (int i = 0; i < dataBuffer.Count; i++)
                {
                    if (dataBuffer[i].Split(' ')[0] == "SFD")
                    {
                        bsfd = dataBuffer[i];
                    }
                    else
                    {
                        OnIncomingData(dataBuffer[i]);
                    }
                }
                if (bsfd != "")
                    OnIncomingData(bsfd);

                if (!socket.Connected)
                {
                    UnityEngine.SceneManagement.SceneManager.LoadScene(0);
                }
            }

        }

        private void OnIncomingData(string data)
        {
            string[] sdata = data.Split(' ');

            //print("SERVER: " + data);
            if (sdata[0] == "CID")
            {
                myID = int.Parse(sdata[1]);
                secured = true;
            }
            if (sdata[0] == "SFD" && !ServerB.singleton.serverStarted)
            {
                //if (debug)
                //    print(data);
                string[] tsdata = data.Split('\t');
                List<NetworkedObject> crossOff = new List<NetworkedObject>(networkedObjects);
                for (int i = 1; i < tsdata.Length; i++)
                {
                    int nid = int.Parse(tsdata[i].Split(' ')[0].Split(':')[1]);
                    NetworkedObject tno = SetNetObjData(nid, tsdata[i]);
                    if (tno)
                        crossOff.Remove(tno);
                }

                foreach (NetworkedObject dno in crossOff)
                {
                    networkedObjects.Remove(dno);
                    Destroy(dno.gameObject);
                }
            }
            if (sdata[0] == "ACT")
            {
                ReturnAction(sdata);
            }

            foreach (IClientEventListener cel in listeners)
            {
                cel.NewClientData(data);
            }
        }

        public void SendData(string data)
        {
            if (!isClient)
                return;

            if (data.Length * 2 / 1000 > 63)
            {
                Debug.LogWarning("Failed to send data, larger than 63k bytes. Please use SendBigData() instead");
                return;
            }

            writer.WriteLine(data);
            writer.Flush();
        }

        public void SendBigData(string dataKey, byte[] data)
        {
            if (!isClient)
                return;

            if (dataKey.Contains(" "))
            {
                Debug.LogWarning("Big data key can not include spaces, replaced with _");
                dataKey = dataKey.Replace(' ', '_');
            }

            SendData($"BD {dataKey} {data.Length}");

            stream.Write(data, 0, data.Length);
            stream.Flush();
        }

        public void InvokeAction(Action action)
        {
            string data = "ACT ";
            data += action.ActionID + " ";
            if (ServerB.singleton.functionToString.ContainsKey(action.serverFunction))
            {
                data += ServerB.singleton.functionToString[action.serverFunction];
            }
            else
            {
                Debug.LogWarning("Function not properly registered, action rejected");
                ActionReturn actionReturn = new ActionReturn(action, ActionState.functionMismatchClient);
                action.callback.Invoke(actionReturn);
                return;
            }

            foreach (object o in action.parameters)
            {
                data += " ";
                if (o is NetworkedObject no)
                {
                    data += "NETOBJ" + no.networkID;
                    continue;
                }
                if (o is string s)
                {
                    data += s.Replace(" ", "%s%");
                    continue;
                }
                if (o is int i)
                {
                    data += i.ToString();
                    continue;
                }
                if (o is float f)
                {
                    data += f.ToString();
                    continue;
                }
                if (o is bool b)
                {
                    data += b.ToString();
                    continue;
                }

                Debug.LogWarning("Only string, int, float, bool, and network objects can be passed as action parameters, action rejected");
                ActionReturn actionReturn = new ActionReturn(action, ActionState.badParams);
                action.callback.Invoke(actionReturn);
                return;
            }

            waitingActions.Add(action.ActionID, action);

            if (data.Length * 2 / 1000 > 63)
            {
                Debug.LogWarning("Failed to send action, data larger than 63k bytes.");
                ActionReturn actionReturn = new ActionReturn(action, ActionState.tooBig);
                action.callback.Invoke(actionReturn);
                return;
            }

            SendData(data);
        }

        private void ReturnAction(string[] sdata)
        {
            string actionID = sdata[1];
            string state = sdata[2];

            if (!waitingActions.ContainsKey(actionID))
                return;

            object[] rparams = new object[sdata.Length - 3];
            for (int i = 3; i < sdata.Length; i++)
            {
                if (sdata[i].Contains("%s%"))
                {
                    sdata[i] = sdata[i].Replace("%s%", " ");
                    rparams[i - 3] = sdata[i];
                }
                else if (sdata[i].StartsWith("NETOBJ"))
                {
                    if (int.TryParse(sdata[i].Substring(6), out int r))
                    {
                        NetworkedObject no = NetObjByID(r);
                        if (no != null)
                        {
                            rparams[i - 3] = no;
                            continue;
                        }
                        rparams[i - 3] = sdata[i];
                    }
                    rparams[i - 3] = sdata[i];
                }
                else
                {
                    rparams[i - 3] = sdata[i];
                }

            }

            ActionState actionState = ParseState(state);

            ActionReturn actionReturn = new ActionReturn(waitingActions[actionID], actionState, rparams);
            waitingActions[actionID].callback.Invoke(actionReturn);
        }

        private ActionState ParseState(string state)
        {
            switch (state)
            {
                case "accepted":
                    return ActionState.accepted;
                case "tooBig":
                    return ActionState.tooBig;
                case "badParams":
                    return ActionState.badParams;
                case "functionMismatchClient":
                    return ActionState.functionMismatchClient;
                case "functionMismatchServer":
                    return ActionState.functionMismatchServer;
                default:
                    return ActionState.rejected;
            }
        }

        public void RegisterEventListener(IClientEventListener listener)
        {
            listeners.Add(listener);
        }

        public void RegisterNetObject(NetworkedObject nO, int nid)
        {
            networkedObjects.Add(nO);
            nO.networkID = nid;
        }

        public NetworkedObject SetNetObjData(int nid, string data)
        {
            foreach (NetworkedObject no in networkedObjects)
            {
                if (no.networkID == nid)
                {
                    no.SetAllData(data);
                    return no;
                }
            }
            int t = int.Parse(data.Split(' ')[1].Split(':')[1]);

            foreach (IClientEventListener cel in listeners)
            {
                cel.SpawnNewObject(nid, t, data);
            }

            return null;
        }

        public NetworkedObject NetObjByID(int nid)
        {
            if (ServerB.singleton.serverStarted)
            {
                return ServerB.singleton.NetObjByID(nid);
            }

            NetworkedObject final = null;

            foreach (NetworkedObject no in networkedObjects)
            {
                if (no.networkID == nid)
                {
                    final = no;
                    break;
                }
            }

            return final;
        }

        private void OnDestroy()
        {
            if (isClient)
            {
                socket.Close();
                isClient = false;
            }
        }
    }

    public interface IClientEventListener
    {
        void OnClientConnect();
        void SpawnNewObject(int nid, int type, string data);
        void NewClientData(string data);
    }

    public class Action
    {
        private static string charArr = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";

        private string actionID;
        public string ActionID
        {
            get { return actionID; }
        }

        public delegate void ServerFunction(ActionInfo actionInfo);
        public delegate void ActionCallback(ActionReturn actionInfo);

        public ServerFunction serverFunction;
        public object[] parameters;

        public ActionCallback callback;

        public Action(ServerFunction serverFunction, params object[] parameters)
        {
            this.serverFunction = serverFunction;
            this.parameters = parameters;

            string rand = "";
            for (int i = 0; i < 25; i++)
            {
                rand += charArr[UnityEngine.Random.Range(0, charArr.Length)];
            }

            actionID = rand;
        }

        public void Invoke(ActionCallback callback)
        {
            this.callback = callback;
            ClientB.singleton.InvokeAction(this);
        }
    }

    public struct ActionInfo
    {
        public string actionID;
        public ServerClient sender;
        public string functionName;
        public object[] parameters;

        public ActionInfo(string actionID, ServerClient sender, string functionName, object[] parameters)
        {
            this.actionID = actionID;
            this.sender = sender;
            this.functionName = functionName;
            this.parameters = parameters;
        }

        public void Return(bool accepted, params object[] returnData)
        {
            ServerB.singleton.ReturnAction(this, accepted, returnData);
        }
    }

    public enum ActionState
    {
        accepted,
        rejected,
        tooBig,
        functionMismatchClient,
        functionMismatchServer,
        badParams
    }

    public struct ActionReturn
    {
        public Action origin;
        public ActionState actionState;
        public object[] returnData;

        public ActionReturn(Action origin, ActionState actionState, params object[] returnData)
        {
            this.origin = origin;
            this.actionState = actionState;
            this.returnData = returnData;
        }
    }
}