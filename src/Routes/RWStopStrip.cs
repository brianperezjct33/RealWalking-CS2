// Real Walking Standalone S1.4 - lightweight transit station strip.
// Original compact Unity IMGUI presentation. Inspired by the GPLv2 FPC Continued
// StopStripPanel behaviour; upstream copyright and license are preserved in UPSTREAM/.
// Read-only: route, stops, targeted waypoint and line colour only. No city/simulation writes.
using System;
using System.Collections.Generic;
using Colossal.Entities;
using Game;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Pathfind;
using Game.Routes;
using Game.UI;
using Game.Vehicles;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Color = UnityEngine.Color;

namespace CS2RealWalkingG1Bridge
{
    internal sealed class RWStopStripSnapshot
    {
        internal readonly string Line;
        internal readonly string[] Stops;
        internal readonly int Next;
        internal readonly int Current;
        internal readonly Color Tint;
        internal readonly Vector3[] StopPositions;
        internal readonly float SampleAt;
        internal RWStopStripSnapshot(string line, string[] stops, int next, int current, Color tint,Vector3[] positions)
        {
            Line=line; Stops=stops; Next=next; Current=current; Tint=tint;
            StopPositions=positions;SampleAt=UnityEngine.Time.realtimeSinceStartup;
        }
    }

    internal static class RWStopStripState
    {
        internal static RWStopStripSnapshot? Latest;
        internal static bool Visible { get { return Latest != null && RWTransitRide.Active; } }
        internal static void Clear() { Latest=null; }
    }

    // Unlike the old 1800-line FPC ActivatedUISystem, this system is dedicated to
    // one purpose: extracting station information for a Real Walking boarding ride.
    public sealed partial class RWStopStripSystem : GameSystemBase
    {
        private NameSystem _names;
        private EntityQuery _namedEdges;
        private RWStopNames _stationNames;
        private Entity _stationRoute;
        private float _nextRead;
        private bool _loggedError;
        private Entity _lastRoute;
        protected override void OnCreate()
        {
            base.OnCreate();
            _namedEdges=GetEntityQuery(ComponentType.ReadOnly<Game.Net.Edge>(),ComponentType.ReadOnly<Aggregated>());
        }
        protected override void OnUpdate()
        {
            if (!RWTransitRide.Active || !UnityEngine.Application.isFocused)
            {
                RWStopStripState.Clear();
                _lastRoute=Entity.Null;
                _stationRoute=Entity.Null;
                _stationNames=null;
                return;
            }
            float now=UnityEngine.Time.realtimeSinceStartup;
            if(now<_nextRead)return;
            _nextRead=now+.5f;
            try
            {
                if(_names==null) _names=World.GetExistingSystemManaged<NameSystem>();
                if(_names==null) { RWStopStripState.Clear(); return; }
                RWStopStripSnapshot result=ReadRoute();
                RWStopStripState.Latest=result;
                if(result!=null)
                    RWStopStripHud.Ensure();
            }
            catch(Exception ex)
            {
                RWStopStripState.Clear();
                if(!_loggedError)
                {
                    _loggedError=true;
                    RWJournal.Error("[RW-STOPSTRIP] read failed; HUD disabled until next ride: "+ex);
                }
            }
        }
        private RWStopStripSnapshot ReadRoute()
        {
            var em=EntityManager;
            Entity body=RWTransitRide.Followed;
            if(body==Entity.Null || !em.Exists(body))return null;
            Entity root=body;
            if(em.HasComponent<Game.Vehicles.Controller>(body))
            {
                Entity owner=em.GetComponentData<Game.Vehicles.Controller>(body).m_Controller;
                if(owner!=Entity.Null && em.Exists(owner))root=owner;
            }
            // Some vehicle layouts place the route on the physical carriage, not the root.
            Entity route=Entity.Null;
            if(em.HasComponent<CurrentRoute>(root))route=em.GetComponentData<CurrentRoute>(root).m_Route;
            if(route==Entity.Null && em.HasComponent<CurrentRoute>(body))
                route=em.GetComponentData<CurrentRoute>(body).m_Route;
            if(route==Entity.Null || !em.Exists(route) || !em.HasBuffer<RouteWaypoint>(route))return null;
            if(_stationNames==null || _stationRoute!=route)
            {
                _stationNames=new RWStopNames(World,em,_names,_namedEdges);
                _stationRoute=route;
            }
            DynamicBuffer<RouteWaypoint> waypoints=em.GetBuffer<RouteWaypoint>(route,true);
            if(waypoints.Length==0)return null;
            var stops=new List<RouteStop>();
            // Store waypoint indices so we can show the next scheduled station rather
            // than falsely assuming the geographically closest station is the next one.
            for(int i=0;i<waypoints.Length && i<512;i++)
            {
                Entity w=waypoints[i].m_Waypoint;
                if(w==Entity.Null || !em.Exists(w) || !em.HasComponent<Connected>(w))continue;
                Entity stop=em.GetComponentData<Connected>(w).m_Connected;
                if(stop==Entity.Null || !em.Exists(stop) || !em.HasComponent<TransportStop>(stop))continue;
                if(stops.Count>0 && stops[stops.Count-1].Stop==stop)continue;
                stops.Add(new RouteStop(stop,i,""));
                if(stops.Count>=80)break;
            }
            if(stops.Count==0)return null;
            // A cyclic line may end with its start stop repeated; render once.
            if(stops.Count>1 && stops[0].Stop==stops[stops.Count-1].Stop)
                stops.RemoveAt(stops.Count-1);
            int next=-1,current=-1;
            int waypointIndex=-1;
            Entity targetEntity=Entity.Null;
            if(em.HasComponent<Target>(root))targetEntity=em.GetComponentData<Target>(root).m_Target;
            if(targetEntity==Entity.Null && em.HasComponent<Target>(body))
                targetEntity=em.GetComponentData<Target>(body).m_Target;
            if(targetEntity!=Entity.Null && em.Exists(targetEntity) && em.HasComponent<Waypoint>(targetEntity))
                waypointIndex=em.GetComponentData<Waypoint>(targetEntity).m_Index;
            else if(targetEntity!=Entity.Null)
                for(int i=0;i<waypoints.Length;i++)
                    if(waypoints[i].m_Waypoint==targetEntity){waypointIndex=i;break;}
            if(waypointIndex>=0)
            {
                int bestDelta=int.MaxValue;
                for(int i=0;i<stops.Count;i++)
                {
                    int d=(stops[i].WaypointIndex-waypointIndex+waypoints.Length)%waypoints.Length;
                    if(d<bestDelta){bestDelta=d;next=i;}
                }
                bool boarding=false;
                if(em.HasComponent<PublicTransport>(root))
                    boarding=(em.GetComponentData<PublicTransport>(root).m_State & PublicTransportFlags.Boarding)!=0;
                if(boarding && next>=0 && stops[next].WaypointIndex==waypointIndex)
                    current=next;
            }
            // Real line identity is stored on the route, not in the route-tool
            // localization string. Named lines must take precedence over generic
            // localized asset/tool labels. Never invent a line number.
            string line=ReadRealLineName(route);
            Color tint=Color.white;
            if(em.HasComponent<Game.Routes.Color>(route))
            {
                var col=em.GetComponentData<Game.Routes.Color>(route).m_Color;
                tint=new Color32(col.r,col.g,col.b,255);
            }
            bool rail=RWTransitRide.IsRail;
            bool aircraft=RWTransitRide.Kind==RWVehicleKind.Airplane;
            var streets=new (string main,string cross)[stops.Count];
            var baseCounts=new Dictionary<string,int>();
            for(int i=0;i<stops.Count;i++)
            {
                streets[i]=_stationNames.Streets(stops[i].Stop);
                string basename=RWStopNames.BaseName(streets[i].main);
                baseCounts[basename]=baseCounts.ContainsKey(basename)?baseCounts[basename]+1:1;
            }
            var names=new string[stops.Count];
            for(int i=0;i<stops.Count;i++)
            {
                string basename=RWStopNames.BaseName(streets[i].main);
                names[i]=_stationNames.Display(stops[i].Stop,streets[i].main,streets[i].cross,
                    baseCounts[basename],rail,aircraft);
                if(string.IsNullOrEmpty(names[i]))names[i]=RWLocalization.T("rw.stop.default",i+1);
            }
            if(_lastRoute!=route)
            {
                _lastRoute=route;
                RWJournal.Warn("[RW-STOPSTRIP] route="+route.Index+":"+route.Version+
                    " stops="+names.Length+" next="+next+" name="+line);
            }
            var positions=new Vector3[stops.Count];
            for(int i=0;i<stops.Count;i++)
            {
                Entity stop=stops[i].Stop;
                if(em.HasComponent<Game.Objects.Transform>(stop))
                {
                    var t=em.GetComponentData<Game.Objects.Transform>(stop).m_Position;
                    positions[i]=new Vector3(t.x,t.y,t.z);
                }
                else if(em.HasComponent<Game.Net.Node>(stop))
                {
                    var t=em.GetComponentData<Game.Net.Node>(stop).m_Position;
                    positions[i]=new Vector3(t.x,t.y,t.z);
                }
                else
                {
                    // On certain vanilla/subway stations the TransportStop is a
                    // marker without Transform/Node. FPC Continued gets its
                    // physical station anchor from the route waypoint Position.
                    Entity w=waypoints[stops[i].WaypointIndex].m_Waypoint;
                    // The current Game.dll does not expose that Pathfind position component.
                    // Use known Net.Node/Objects.Transform types only. If the waypoint
                    // has neither, preserve NaN: never invent an underground platform.
                    if(em.HasComponent<Game.Net.Node>(w))
                    {
                        var p=em.GetComponentData<Game.Net.Node>(w).m_Position;
                        positions[i]=new Vector3(p.x,p.y,p.z);
                    }
                    else if(em.HasComponent<Game.Objects.Transform>(w))
                    {
                        var p=em.GetComponentData<Game.Objects.Transform>(w).m_Position;
                        positions[i]=new Vector3(p.x,p.y,p.z);
                    }
                    else positions[i]=new Vector3(float.NaN,float.NaN,float.NaN);
                }
            }
            return new RWStopStripSnapshot(line,names,next,current,tint,positions);
        }
        private string ReadRealLineName(Entity route)
        {
            string custom="";
            try
            {
                if(_names.TryGetCustomName(route,out custom) &&
                   !string.IsNullOrWhiteSpace(custom))
                {
                    RWJournal.Warn("[RW-LINE-NAME] custom route="+route.Index+" name="+custom);
                    return custom.Trim();
                }
            }
            catch(Exception ex)
            {
                RWJournal.Warn("[RW-LINE-NAME] custom unavailable: "+ex.GetType().Name);
            }
            string auto=SafeName(route);
            // NameSystem sometimes resolves an unnamed route as the localized
            // route *creation tool*, not a passenger service's actual line name.
            // Showing that UI label as a transport line would be misleading.
            if(!LooksLikeRouteTool(auto)) return auto;
            return RWLocalization.T("rw.route.unnamed",RWTransitRide.Name);
        }
        private static bool LooksLikeRouteTool(string name)
        {
            if(string.IsNullOrWhiteSpace(name))return true;
            string v=name.Trim().ToLowerInvariant();
            return v.Contains("工具") || v.Contains("路线工具") ||
                   v.Contains("line tool") || v.Contains("route tool") ||
                   v.StartsWith("assets.name[") || v.StartsWith("tools.") ||
                   v=="stop" || v=="transport route";
        }
        private string SafeName(Entity ent)
        {
            try { return (_names.GetRenderedLabelName(ent)??"").Trim(); }
            catch { return ""; }
        }
        private struct RouteStop
        {
            internal Entity Stop;
            internal int WaypointIndex;
            internal string Name;
            internal RouteStop(Entity e,int i,string n){Stop=e;WaypointIndex=i;Name=n;}
        }
    }
}
