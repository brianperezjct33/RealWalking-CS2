// SPDX-License-Identifier: MIT
// Uses the previously verified Net owner->SubLane and Game.Areas.Navigation APIs.
using System;
using System.Collections.Generic;
using Colossal.Collections;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Net;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace CS2RealWalkingG1Bridge
{
    internal sealed class RWOverlayView : MonoBehaviour
    {
        private static RWOverlayView _instance;
        private Texture2D _pixel;
        private GUIStyle _hudStyle;
        private GUIStyle _smallStyle;
        private const float RadarRadius=25f;
        private const float RadarScale=3.7f;
        private static readonly Color PedNear=new Color(0.10f,1.00f,0.32f,0.85f);
        private static readonly Color PedOther=new Color(1.00f,0.60f,0.10f,0.55f);
        private static readonly Color AreaNear=new Color(0.10f,0.87f,1.00f,0.62f);
        private static readonly Color AreaOther=new Color(0.75f,0.40f,1.00f,0.40f);
        private static readonly Color CorridorEdge=new Color(1f,0.82f,0.1f,0.65f);

        internal static void Ensure()
        {
            if(_instance!=null)return;
            var go=new GameObject("RW_G1_4_Developer_Overlay_ONLY");
            go.hideFlags=HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            _instance=go.AddComponent<RWOverlayView>();
        }
        internal static void Release()
        {
            RWOverlayState.SetUnifiedDebug(false);
            RWOverlayState.Latest=null;
            if(_instance!=null)
            {
                Destroy(_instance.gameObject);
                _instance=null;
            }
        }
        private void Awake()
        {
            _pixel=new Texture2D(1,1,TextureFormat.ARGB32,false);
            _pixel.SetPixel(0,0,Color.white);
            _pixel.Apply();
            _pixel.hideFlags=HideFlags.HideAndDontSave;
            // GUI.skin is only safe to access from OnGUI, not Awake.
        }
        private void OnDestroy()
        {
            if(_pixel!=null)Destroy(_pixel);
            if(_instance==this)_instance=null;
        }
        private void PixelLine(Vector2 a,Vector2 b,Color color,float thickness)
        {
            if(_pixel==null)return;
            Vector2 d=b-a;
            float length=d.magnitude;
            if(length<0.4f)return;
            // Skip off-screen edges, but allow lines straddling the screen boundary.
            if(Mathf.Max(a.x,b.x)<-100||Mathf.Min(a.x,b.x)>Screen.width+100||
               Mathf.Max(a.y,b.y)<-100||Mathf.Min(a.y,b.y)>Screen.height+100)return;
            Matrix4x4 old=GUI.matrix;
            Color prev=GUI.color;
            try
            {
                GUI.color=color;
                GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg,a);
                GUI.DrawTexture(new Rect(a.x,a.y,length,thickness),_pixel,ScaleMode.StretchToFill);
            }
            finally{GUI.matrix=old;GUI.color=prev;}
        }
        private void Dot(Vector2 p,Color color,float size)
        {
            if(_pixel==null)return;
            Color prev=GUI.color;
            GUI.color=color;
            GUI.DrawTexture(new Rect(p.x-size/2,p.y-size/2,size,size),_pixel);
            GUI.color=prev;
        }
        private static bool ScreenProjection(Camera camera,Vector3 p,out Vector2 screen)
        {
            Vector3 s=camera.WorldToScreenPoint(p);
            screen=new Vector2(s.x,Screen.height-s.y);
            return s.z>0.05f;
        }
        private void WorldLine(Camera cam,Vector3 a,Vector3 b,Color color,float width)
        {
            Vector2 sa,sb;
            if(!ScreenProjection(cam,a,out sa)||!ScreenProjection(cam,b,out sb))return;
            PixelLine(sa,sb,color,width);
        }
        private static bool NearLayer(float y,float footY) {return Mathf.Abs(y-footY)<=2.5f;}
        private static Vector2 RadarPoint(Vector3 world,RWOverlaySnapshot s,Vector2 center)
        {
            float dx=world.x-s.Eye.x,dz=world.z-s.Eye.z;
            Vector3 f=s.Forward;
            float right=dx*f.z-dz*f.x;
            float ahead=dx*f.x+dz*f.z;
            return center+new Vector2(right*RadarScale,-ahead*RadarScale);
        }
        private void RadarSegment(Vector3 a,Vector3 b,RWOverlaySnapshot s,Vector2 center,Color color)
        {
            // Clamp to a square radar viewport using distance gating before drawing.
            float ra=DistanceXZ(s.Eye,a), rb=DistanceXZ(s.Eye,b);
            if(ra>RadarRadius+7f&&rb>RadarRadius+7f)return;
            PixelLine(RadarPoint(a,s,center),RadarPoint(b,s,center),color,1.8f);
        }
        private static float DistanceXZ(Vector3 a,Vector3 b)
        {
            float dx=a.x-b.x,dz=a.z-b.z;
            return Mathf.Sqrt(dx*dx+dz*dz);
        }
        private void OnGUI()
        {
            if((!RWOverlayState.Visible && !RWOverlayState.ShowRadar) || UnityEngine.Event.current.type!=EventType.Repaint || _pixel==null)return;
            if(_hudStyle==null)
            {
                _hudStyle=new GUIStyle(GUI.skin.label);
                _hudStyle.fontSize=13;
                _hudStyle.normal.textColor=Color.white;
                _smallStyle=new GUIStyle(GUI.skin.label);
                _smallStyle.fontSize=11;
                _smallStyle.normal.textColor=Color.white;
            }
            var snap=RWOverlayState.Latest;
            if(snap==null && !RWOverlayState.Visible)return;
            if(RWOverlayState.Visible)
            {
            GUI.Box(new Rect(12,12,430,192),GUIContent.none);
            GUI.Label(new Rect(21,17,390,20),"REAL WALKING / NAVIGATION DEBUG",_hudStyle);
            GUI.Label(new Rect(21,38,410,20),"Ctrl+Shift+J: walk/flight  |  Ctrl+Shift+V: debug",_smallStyle);
            GUI.Label(new Rect(21,56,415,20),"MODE: "+RWWalkState.Mode+"  |  "+RWWalkState.Status,_smallStyle);
            GUI.Label(new Rect(21,74,390,20),RWOverlayState.AllLayers?"LEVELS: ALL (orange/purple = other layer)":"LEVELS: NEAR ONLY (+/-2.5m)",_smallStyle);
            if(snap==null)
            {
                GUI.Label(new Rect(21,96,390,22),"Scanning ... (unpause game if necessary)",_smallStyle);
                return;
            }
            GUI.Label(new Rect(21,92,390,19),"Ped: "+snap.Lanes.Length+" / Nav: "+snap.Triangles.Length+
                " / Query: "+snap.QueryMilliseconds.ToString("F1")+" ms",_smallStyle);
            GUI.Label(new Rect(21,111,390,22),snap.Status??"No sampling result",_smallStyle);
            if(snap.Eye==Vector3.zero)return;
            RWOverlayPolyline bestLane=null;
            float bestLaneScore=float.PositiveInfinity;
            for(int i=0;i<snap.Lanes.Length;i++)
            {
                var l=snap.Lanes[i];
                float score=l.DistanceXZ+2f*Mathf.Abs(l.NearY-snap.FootY);
                if(score<bestLaneScore){bestLaneScore=score;bestLane=l;}
            }
            RWOverlayTriangle bestArea=null;
            float bestAreaScore=float.PositiveInfinity;
            for(int i=0;i<snap.Triangles.Length;i++)
            {
                var t=snap.Triangles[i];
                float score=t.DistanceXZ+2f*Mathf.Abs(t.NearY-snap.FootY);
                if(score<bestAreaScore){bestAreaScore=score;bestArea=t;}
            }
            GUI.Label(new Rect(21,132,390,20),bestLane==null?"Nearest ped: none":
                "Ped #"+bestLane.Id+" XZ="+bestLane.DistanceXZ.ToString("F1")+
                "  dY="+(snap.FootY-bestLane.NearY).ToString("+0.0;-0.0;0.0")+
                "  prefab="+bestLane.WidthMeters.ToString("F1")+" walk="+(bestLane.WalkableHalfWidth*2f).ToString("F1")+" ["+bestLane.WidthRule+"]",_smallStyle);
            GUI.Label(new Rect(21,153,390,20),bestArea==null?"Nearest nav: none":
                "Nav #"+bestArea.AreaId+" Tri "+bestArea.TriangleId+"  XZ="+bestArea.DistanceXZ.ToString("F1")+
                "  dY="+(snap.FootY-bestArea.NearY).ToString("+0.0;-0.0;0.0"),_smallStyle);

            // Main screen-space world overlay: green = Net pedestrian, cyan = Navigation Area.
            Camera renderCamera=Camera.main;
            if(renderCamera==null)
            {
                var cameras=Camera.allCameras;
                float best=float.PositiveInfinity;
                for(int i=0;i<cameras.Length;i++)
                {
                    if(cameras[i]==null||!cameras[i].enabled)continue;
                    float d=(cameras[i].transform.position-snap.Eye).sqrMagnitude;
                    if(d<best){best=d;renderCamera=cameras[i];}
                }
            }
            if(renderCamera!=null)
            {
                // Drawing is far more expensive than geometry collection: cap visual lines,
                // but leave the larger snapshot available to the walking controller.
                int drawnPed=0;
                int drawnNav=0;
                int pedDrawLimit=RWOverlayState.AllLayers?25:14;
                int navDrawLimit=RWOverlayState.AllLayers?35:20;
                for(int i=0;i<snap.Lanes.Length;i++)
                {
                    var line=snap.Lanes[i];
                    bool near=NearLayer(line.NearY,snap.FootY);
                    if(!near&&!RWOverlayState.AllLayers)continue;
                    if(drawnPed++>=pedDrawLimit)break;
                    Color color=near?PedNear:PedOther;
                    for(int k=1;k<line.Points.Length;k++)WorldLine(renderCamera,line.Points[k-1]+Vector3.up*.08f,line.Points[k]+Vector3.up*.08f,color,near?2.9f:1.7f);
                }
                // Optional lane-width diagnostic, limited to six nearest same-layer lanes to
                // avoid the expensive line count of the old radar. Yellow = inferred lane edge.
                if(RWOverlayState.ShowCorridor)
                {
                    int bands=0;
                    for(int i=0;i<snap.Lanes.Length&&bands<6;i++)
                    {
                        var lane=snap.Lanes[i];
                        if(!NearLayer(lane.NearY,snap.FootY)||lane.DistanceXZ>10f)continue;
                        bands++;
                        float half=lane.WalkableHalfWidth;
                        for(int j=1;j<lane.Points.Length;j++)
                        {
                            Vector3 a=lane.Points[j-1],b=lane.Points[j];
                            float dx=b.x-a.x,dz=b.z-a.z,len=Mathf.Sqrt(dx*dx+dz*dz);
                            if(len<.001f)continue;
                            Vector3 lateral=new Vector3(-dz/len,0f,dx/len)*half;
                            WorldLine(renderCamera,a+lateral+Vector3.up*.12f,b+lateral+Vector3.up*.12f,CorridorEdge,1.2f);
                            WorldLine(renderCamera,a-lateral+Vector3.up*.12f,b-lateral+Vector3.up*.12f,CorridorEdge,1.2f);
                        }
                    }
                }
                if(RWOverlayState.ShowNavigation)
                {
                    for(int i=0;i<snap.Triangles.Length;i++)
                    {
                        var t=snap.Triangles[i];
                        bool near=NearLayer(t.NearY,snap.FootY)||
                            (DistanceXZ(t.A,snap.Eye)<4f&&NearLayer(t.A.y,snap.FootY))||
                            (DistanceXZ(t.B,snap.Eye)<4f&&NearLayer(t.B.y,snap.FootY))||
                            (DistanceXZ(t.C,snap.Eye)<4f&&NearLayer(t.C.y,snap.FootY));
                        if(!near&&!RWOverlayState.AllLayers)continue;
                        if(drawnNav++>=navDrawLimit)break;
                        Color color=near?AreaNear:AreaOther;
                        WorldLine(renderCamera,t.A+Vector3.up*.12f,t.B+Vector3.up*.12f,color,1.5f);
                        WorldLine(renderCamera,t.B+Vector3.up*.12f,t.C+Vector3.up*.12f,color,1.5f);
                        WorldLine(renderCamera,t.C+Vector3.up*.12f,t.A+Vector3.up*.12f,color,1.5f);
                    }
                }
            }

            // Unified diagnostics also include the nearby geometry radar.
            } // end world overlay and its HUD.
            if(snap==null)return;
            if(RWOverlayState.ShowRadar)
            {
            float w=255f,h=265f;
            float rx=Mathf.Max(10f,Screen.width-w-12), ry=12f;
            Color previous=GUI.color;
            GUI.color=new Color(0.04f,0.07f,0.10f,0.98f);
            GUI.DrawTexture(new Rect(rx,ry,w,h),_pixel);
            GUI.color=previous;
            GUI.Box(new Rect(rx,ry,w,h),GUIContent.none);
            GUI.Label(new Rect(rx+11,ry+5,w-20,20),"NEARBY PEDESTRIAN GEOMETRY",_smallStyle);
            GUI.Label(new Rect(rx+11,ry+23,w-20,20),"Green: Ped  |  Cyan: Nav",_smallStyle);
            Vector2 center=new Vector2(rx+w/2,ry+150f);
            Color grid=new Color(1f,1f,1f,0.15f);
            PixelLine(center+new Vector2(-95,0),center+new Vector2(95,0),grid,1f);
            PixelLine(center+new Vector2(0,-95),center+new Vector2(0,95),grid,1f);
            for(int i=0;i<snap.Lanes.Length;i++)
            {
                var line=snap.Lanes[i];
                bool near=NearLayer(line.NearY,snap.FootY);
                if(!near&&!RWOverlayState.AllLayers)continue;
                for(int k=1;k<line.Points.Length;k++)RadarSegment(line.Points[k-1],line.Points[k],snap,center,near?PedNear:PedOther);
            }
            if(RWOverlayState.ShowNavigation)
            {
                for(int i=0;i<snap.Triangles.Length;i++)
                {
                    var t=snap.Triangles[i];
                    bool near=NearLayer(t.NearY,snap.FootY)||
                        (DistanceXZ(t.A,snap.Eye)<4f&&NearLayer(t.A.y,snap.FootY));
                    if(!near&&!RWOverlayState.AllLayers)continue;
                    Color co=near?AreaNear:AreaOther;
                    RadarSegment(t.A,t.B,snap,center,co);
                    RadarSegment(t.B,t.C,snap,center,co);
                    RadarSegment(t.C,t.A,snap,center,co);
                }
            }
            Dot(center,Color.white,8f);
            PixelLine(center,center+new Vector2(0,-17f),Color.white,2f);
            GUI.Label(new Rect(rx+11,ry+h-40,w-20,16),"White square: FPC | front = top",_smallStyle);
            GUI.Label(new Rect(rx+11,ry+h-24,w-20,16),"Ctrl+Shift+V: hide diagnostics",_smallStyle);
            }
        }
    }
}
