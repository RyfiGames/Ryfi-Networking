/* Copyright Notice
 ********************************************************************************
 * Copyright (C) Ryan Magilton - All Rights Reserved                            *
 * Unauthorized copying of this file, via any medium is strictly prohibited     *
 * without explicit permission                                                  *
 * Written by Ryan Magilton <ryfiandsen@comcast.net>, July 2020                 *
 ********************************************************************************/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ryfi.Networking
{
    public class NetworkedObject : MonoBehaviour
    {

        public int networkID;
        public int typeID;

        public delegate void VarChangedCallback(string varName, string newValue);
        public VarChangedCallback anyVarChanged;

        private List<string> networkVars = new List<string>();
        private List<string> varVals = new List<string>();
        private List<VarChangedCallback> varCallbacks = new List<VarChangedCallback>();

        private void Awake()
        {
            anyVarChanged = EmptyCallback;
        }

        // Start is called before the first frame update
        void Start()
        {
            //ClientB.singleton.RegisterNetObject(this, );
            ServerB.singleton.RegisterNetObject(this);
        }

        public bool HasVar(string vname)
        {
            return networkVars.Contains(vname);
        }

        public string GetVar(string vname, string defVal = "")
        {
            if (!networkVars.Contains(vname) || vname.Contains(" "))
            {
                return defVal;
            }

            int vind = networkVars.IndexOf(vname);
            return varVals[vind];
        }

        public void SetVar(string vname, string nval)
        {
            //print(vname + " " + nval);

            if (vname.Contains(" "))
            {
                Debug.LogWarning("Network Vars can not have spaces in the name. " + "\"" + vname + "\"" + " was not set, may cause errors.");
                return;
            }
            if (vname.Contains("\"") || nval.Contains("\""))
            {
                Debug.LogWarning("Network Vars can not have quotations. " + "\"" + vname + "\"" + " was not set, may cause errors.");
                return;
            }
            int vind = networkVars.IndexOf(vname);

            if (vind < 0)
            {
                networkVars.Add(vname);
                varVals.Add(nval);
                varCallbacks.Add(EmptyCallback);
                anyVarChanged(vname, nval);
                return;
            }

            string b4 = varVals[vind];
            varVals[vind] = nval;

            if (b4 != nval)
            {
                varCallbacks[vind](vname, nval);
                anyVarChanged(vname, nval);
            }
        }

        public void SetVar(string vname, int nval)
        {
            SetVar(vname, nval.ToString());
        }

        public void SetVar(string vname, float nval)
        {
            SetVar(vname, nval.ToString());
        }

        public void SetVar(string vname, bool nval)
        {
            SetVar(vname, nval.ToString());
        }

        public void SetCallback(string vname, VarChangedCallback callback)
        {
            if (vname.Contains(" "))
            {
                Debug.LogWarning("Network Vars can not have spaces in the name. " + "\"" + vname + "\"" + " was not set, may cause errors.");
                return;
            }
            if (vname.Contains("\""))
            {
                Debug.LogWarning("Network Vars can not have quotations. " + "\"" + vname + "\"" + " was not set, may cause errors.");
                return;
            }

            int vind = networkVars.IndexOf(vname);

            if (vind < 0)
            {
                networkVars.Add(vname);
                varVals.Add("");
                varCallbacks.Add(callback);
                return;
            }

            varCallbacks[vind] = callback;
        }

        public void EmptyCallback(string s1, string s2)
        {

        }

        public void SetAllData(string allData)
        {
            string[] sdata = allData.Split(' ');
            int vc = int.Parse(sdata[2].Split(':')[1]);
            for (int i = 3; i < sdata.Length - 1; i++)
            {
                string vn = sdata[i].Split(':')[0];
                if (sdata[i].Split('\"').Length == 3)
                {
                    SetVar(vn, sdata[i].Split('\"')[1]);
                    continue;
                }
                else
                {
                    string totalData = sdata[i].Split('\"')[1];
                    i++;
                    for (; i < sdata.Length; i++)
                    {
                        totalData += sdata[i];
                        if (sdata[i].EndsWith("\""))
                        {
                            totalData = totalData.Substring(0, totalData.Length - 1);
                            break;
                        }
                    }

                    SetVar(vn, totalData);
                }
            }
        }

        public string GetAllData()
        {
            string final = "";

            final += "NetworkID:" + networkID + " TypeID:" + typeID + " VarCount:" + networkVars.Count + " ";

            for (int i = 0; i < networkVars.Count; i++)
            {
                final += networkVars[i] + ":\"" + varVals[i] + "\" ";
            }

            return final;
        }

        public void GetAllVars(out List<string> vnames, out List<string> vvals)
        {
            vnames = new List<string>(networkVars);
            vvals = new List<string>(varVals);
        }

        private void OnDestroy()
        {
            if (ServerB.singleton.serverStarted)
            {
                //            print("destroyed " + networkID);
                ServerB.singleton.DestroyNetObj(this);
            }
        }

    }
}
