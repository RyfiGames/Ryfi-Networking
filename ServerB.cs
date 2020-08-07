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
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using static Ryfi.Networking.Action;

namespace Ryfi.Networking
{
    public class ServerB : MonoBehaviour
    {

        public static ServerB singleton;

        public bool allowDupeNames;
        private List<string> usedNames = new List<string>();

        public int port = 1818;
        public TcpListener server;
        public bool serverStarted;

        public List<ServerClient> clients;
        private List<ServerClient> disconnectList;

        public int idMax;
        public int netObjMax;

        public List<NetworkedObject> networkedObjects = new List<NetworkedObject>();

        public List<IServerEventListener> listeners = new List<IServerEventListener>();

        public Dictionary<ServerFunction, string> functionToString = new Dictionary<ServerFunction, string>();
        public Dictionary<string, ServerFunction> stringToFunction = new Dictionary<string, ServerFunction>();

        private string lastFullData = "";

        // Start is called before the first frame update
        void Awake()
        {
            singleton = this;
        }

        public void StartServer()
        {
            clients = new List<ServerClient>();
            disconnectList = new List<ServerClient>();
            networkedObjects.Clear();

            try
            {
                server = new TcpListener(IPAddress.Any, port);
                server.Start();

                StartListening();
                serverStarted = true;
                Debug.Log("Server started on port " + port.ToString());
                foreach (IServerEventListener l in listeners)
                {
                    l.OnServerStarted();
                }
            }
            catch (Exception e)
            {
                Debug.Log("Socket Error: " + e.Message);
            }
        }

        private void Update()
        {
            if (!serverStarted)
                return;

            foreach (ServerClient c in clients)
            {
                if (!IsConnected(c.tcp))
                {
                    c.tcp.Close();
                    disconnectList.Add(c);
                    foreach (IServerEventListener l in listeners)
                    {
                        l.OnPlayerLeave(c);
                    }
                    continue;
                }
                else
                {
                    NetworkStream s = c.tcp.GetStream();
                    while (s.DataAvailable)
                    {

                        StreamReader reader = new StreamReader(s, true);
                        string data = reader.ReadLine();

                        if (data != null)
                            OnIncomingData(c, data);

                    }
                }
            }

            foreach (ServerClient c in disconnectList)
            {
                if (clients.Contains(c))
                    clients.Remove(c);
            }

            disconnectList.Clear();

            if (lastFullData != FullData())
            {
                Broadcast(FullData(), clients);
                lastFullData = FullData();
            }

        }

        private bool IsConnected(TcpClient c)
        {
            try
            {
                if (c != null && c.Client != null && c.Client.Connected)
                {
                    if (c.Client.Poll(0, SelectMode.SelectRead))
                    {
                        return !(c.Client.Receive(new byte[1], SocketFlags.Peek) == 0);
                    }

                    return true;
                }
                else
                    return false;
            }
            catch
            {
                return false;
            }
        }

        private void StartListening()
        {
            server.BeginAcceptTcpClient(AcceptTcpClient, server);
        }

        private void AcceptTcpClient(IAsyncResult ar)
        {
            TcpListener listener = (TcpListener)ar.AsyncState;

            ServerClient nsc = new ServerClient("Guest", listener.EndAcceptTcpClient(ar), idMax);
            idMax++;

            clients.Add(nsc);
            StartListening();

            //Broadcast(nsc.clientName + " has connected", clients);
        }

        private void OnIncomingData(ServerClient c, string data)
        {

            string[] sdata = data.Split(' ');

            if (sdata[0] == "NC")
            {
                if (!allowDupeNames && usedNames.Contains(sdata[1].ToLower()))
                {
                    int nadd = 1;
                    while (usedNames.Contains(sdata[1].ToLower() + nadd))
                    {
                        nadd++;
                    }
                    sdata[1] = sdata[1] + nadd;
                }
                usedNames.Add(sdata[1].ToLower());

                c.clientName = sdata[1];
                Broadcast("CID " + c.clientID, c);
                print("Client(id:" + c.clientID + ") name changed to " + c.clientName);
                foreach (IServerEventListener l in listeners)
                {
                    l.OnPlayerJoin(c, sdata);
                }
            }
            else
            {
                if (sdata[0] == "ACT")
                {
                    ReceiveAction(c, sdata);
                }

                foreach (IServerEventListener l in listeners)
                {
                    l.OnNewData(c, data);
                }
            }
        }

        private void ReceiveAction(ServerClient sender, string[] sdata)
        {
            string actionID = sdata[1];
            string fname = sdata[2];
            object[] fparams = new object[sdata.Length - 3];
            for (int i = 3; i < sdata.Length; i++)
            {
                if (sdata[i].Contains("%s%"))
                {
                    sdata[i] = sdata[i].Replace("%s%", " ");
                    fparams[i - 3] = sdata[i];
                }
                else if (sdata[i].StartsWith("NETOBJ"))
                {
                    if (int.TryParse(sdata[i].Substring(6), out int r))
                    {
                        NetworkedObject no = NetObjByID(r);
                        if (no != null)
                        {
                            fparams[i - 3] = no;
                            continue;
                        }
                        fparams[i - 3] = sdata[i];
                    }
                    fparams[i - 3] = sdata[i];
                }
                else
                {
                    fparams[i - 3] = sdata[i];
                }

            }

            ActionInfo actionInfo = new ActionInfo(actionID, sender, fname, fparams);
            if (stringToFunction.ContainsKey(fname))
            {
                stringToFunction[fname].Invoke(actionInfo);
            }
            else
            {
                ReturnAction(sender, actionID, "functionMismatchServer", null);
            }
        }

        public void Broadcast(string data, ServerClient cl)
        {
            List<ServerClient> lsc = new List<ServerClient>
        {
            cl
        };
            Broadcast(data, lsc);
        }
        public void BroadcastAll(string data)
        {
            Broadcast(data, clients);
        }
        public void Broadcast(string data, List<ServerClient> cl)
        {
            if (data.Length * 2 / 1000 > 63)
            {
                Debug.LogWarning("Failed to send data, larger than 63k bytes. Please use BroadcastBigData() instead");
                return;
            }

            foreach (ServerClient c in cl)
            {
                try
                {
                    StreamWriter writer = new StreamWriter(c.tcp.GetStream());
                    writer.WriteLine(data);
                    writer.Flush();
                }
                catch (Exception e)
                {
                    Debug.Log("Write Error: " + e.Message + " to client " + c.clientName);
                }
            }
        }

        public void ReturnAction(ActionInfo info, bool accepted, object[] returnData)
        {
            ReturnAction(info.sender, info.actionID, accepted ? "accepted" : "rejected", returnData);
        }

        private void ReturnAction(ServerClient sendTo, string actionID, string state, object[] returnData)
        {
            string data = "ACT ";
            data += actionID + " " + state + " ";

            foreach (object o in returnData)
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

                Debug.LogWarning("Only string, int, float, bool, and network objects can be passed as action return parameters, 'null' parameter inserted");
                data += "null";
                return;
            }

            Broadcast(data, sendTo);
        }

        public void RegisterServerFunction(string functionName, ServerFunction function)
        {
            functionToString.Add(function, functionName);
            stringToFunction.Add(functionName, function);
        }

        public void RegisterEventListener(IServerEventListener listener)
        {
            listeners.Add(listener);
        }

        public void RegisterNetObject(NetworkedObject nO)
        {
            if (!serverStarted)
                return;

            networkedObjects.Add(nO);
            nO.networkID = netObjMax;
            netObjMax++;
        }

        public void DestroyNetObj(NetworkedObject no)
        {
            networkedObjects.Remove(no);
        }

        public string FullData()
        {
            string final = "SFD ";
            foreach (NetworkedObject no in networkedObjects)
            {
                final += "\t" + no.GetAllData();
            }
            return final;
        }

        public NetworkedObject NetObjByID(int nid)
        {
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
            if (serverStarted)
            {
                Broadcast("END", clients);
                server.Stop();
                serverStarted = false;
                clients.Clear();
            }
        }
    }

    public class ServerClient
    {
        public TcpClient tcp;
        public string clientName;
        public int clientID;

        public ServerClient(TcpClient clientSocket, int clientID)
        {
            clientName = "Guest";
            tcp = clientSocket;
            this.clientID = clientID;
        }
        public ServerClient(string cname, TcpClient clientSocket, int clientID)
        {
            if (cname.Contains(" "))
            {
                cname = cname.Replace(' ', '_');
                Debug.LogWarning("Network Names can not contain space characters, name was fixed, but may cause issues: " + cname);
            }

            clientName = cname;
            tcp = clientSocket;
            this.clientID = clientID;
        }
    }

    public interface IServerEventListener
    {
        void OnServerStarted();
        void OnPlayerJoin(ServerClient sc, string[] data);
        void OnPlayerLeave(ServerClient sc);
        void OnNewData(ServerClient from, string data);
    }
}