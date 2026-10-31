// SPDX-License-Identifier: MIT
// NO game entity creation, no vehicle AI writes, and no save modifications.
using System;
using System.Reflection;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CS2RealWalkingG1Bridge
{
    internal sealed class RWTransitHint : MonoBehaviour
    {
        private static RWTransitHint _instance;
        private GUIStyle _style;
        private static string _notice="";
        private static float _noticeUntil;
        internal static void Notify(string message)
        {
            _notice=message;
            _noticeUntil=UnityEngine.Time.realtimeSinceStartup+2.5f;
        }
        internal static void Ensure()
        {
            if (_instance!=null)return;
            var obj=new GameObject("RW_Transit_Boarding_Hint");
            obj.hideFlags=HideFlags.HideAndDontSave;
            DontDestroyOnLoad(obj);
            _instance=obj.AddComponent<RWTransitHint>();
        }
        internal static void Release()
        {
            if(_instance!=null)Destroy(_instance.gameObject);
            _instance=null;
            RWTransitState.Clear();
        }
        private void OnGUI()
        {
            if(UnityEngine.Event.current.type!=EventType.Repaint)return;
            bool riding=RWTransitRide.Active;
            bool notice=UnityEngine.Time.realtimeSinceStartup<_noticeUntil;
            bool canExit = riding && RWTransitRide.CanShowExitPrompt;
            if(!canExit && !( !riding && RWTransitState.Ready ) && !notice)return;
            if(_style==null)
            {
                _style=new GUIStyle(GUI.skin.box);
                _style.fontSize=17;
                _style.alignment=TextAnchor.MiddleCenter;
                _style.normal.textColor=Color.white;
            }
            string message=notice?_notice : canExit ? RWLocalization.T("rw.transit.exit_prompt") : RWLocalization.T("rw.transit.board_prompt",RWTransitState.VehicleName);
            float width=Mathf.Min(Screen.width-20f,Mathf.Max(notice?410f:riding?310f:270f,_style.CalcSize(new GUIContent(message)).x+36f));
            float promptY = riding && RWStopStripState.Visible ? Screen.height-412f : Screen.height-115f;
            GUI.Box(new Rect((Screen.width-width)*.5f,promptY,width,40f),message,_style);
        }
    }
}
