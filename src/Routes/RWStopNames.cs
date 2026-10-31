// Real Walking S1.5 station naming.
// Transcribed/condensed from Cgameworld/FirstPersonCameraContinued
// Systems/FirstPersonCameraActivatedUISystem.cs (GPL-2.0).
// See UPSTREAM/FPC_LICENSE. All operations are read-only.
using System;
using System.Collections.Generic;
using Colossal.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.UI;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;

namespace CS2RealWalkingG1Bridge
{
    internal sealed class RWStopNames
    {
        private readonly EntityManager em;
        private readonly NameSystem names;
        private readonly World world;
        private readonly Dictionary<Entity,(string name,Entity edge)> nearestCache = new Dictionary<Entity,(string,Entity)>();
        private readonly Dictionary<Entity,(string name,Entity edge)> connectedCache = new Dictionary<Entity,(string,Entity)>();
        private readonly Dictionary<Entity,(string main,string cross)> streetCache = new Dictionary<Entity,(string,string)>();
        private readonly EntityQuery namedEdges;
        internal RWStopNames(World world,EntityManager em,NameSystem names,EntityQuery namedEdges)
        {this.world=world;this.em=em;this.names=names;this.namedEdges=namedEdges;}
        private bool Exists(Entity e){return e!=Entity.Null && em.Exists(e);}
        private static bool Valid(string s)
        {return !string.IsNullOrEmpty(s) && !s.StartsWith("Assets.NAME[") && !s.EndsWith("Bridge") && !s.EndsWith("Track");}
        private string Label(Entity e)
        {try{return Exists(e)?names.GetRenderedLabelName(e):"";}catch{return "";}}
        private static readonly (string full,string shortName)[] Suffixes = {
            (" Street"," St"),(" Avenue"," Ave"),(" Boulevard"," Blvd"),
            (" Road"," Rd"),(" Drive"," Dr"),(" Lane"," Ln"),
            (" Court"," Ct"),(" Place"," Pl"),(" Circle"," Cir"),
            (" Highway"," Hwy"),(" Parkway"," Pkwy"),(" Terrace"," Ter"),(" Way","")};
        private static string Base(string s)
        {
            if(string.IsNullOrEmpty(s))return s;
            foreach(var suffix in Suffixes)
            {
                if(s.EndsWith(suffix.full,StringComparison.OrdinalIgnoreCase))return s.Substring(0,s.Length-suffix.full.Length);
                if(suffix.shortName.Length>0 && s.EndsWith(suffix.shortName,StringComparison.OrdinalIgnoreCase))return s.Substring(0,s.Length-suffix.shortName.Length);
            }
            return s;
        }
        private static string Abbrev(string s)
        {
            if(string.IsNullOrEmpty(s))return s;
            foreach(var suffix in Suffixes)
                if(s.EndsWith(suffix.full,StringComparison.OrdinalIgnoreCase))
                {
                    string b=s.Substring(0,s.Length-suffix.full.Length);
                    if(Base(b)!=b)return b;
                    return suffix.shortName.Length==0?s:b+suffix.shortName;
                }
            return s;
        }
        private bool HasStreetName(Entity aggregate)
        {
            if(!Exists(aggregate))return false;
            if(names.TryGetCustomName(aggregate,out _))return true;
            return em.TryGetBuffer<RandomLocalizationIndex>(aggregate,true,out var indices)&&indices.Length>0;
        }
        private string GetRoadName(Entity edge)
        {
            if(!Exists(edge))return "";
            if(em.TryGetComponent<Aggregated>(edge,out var agg) && HasStreetName(agg.m_Aggregate))
            {var name=Label(agg.m_Aggregate); if(Valid(name))return name;}
            return "";
        }
        private bool IsMinor(Entity edge)
        {
            var prefabs=world.GetExistingSystemManaged<PrefabSystem>();
            if(prefabs==null||!Exists(edge))return false;
            if(em.TryGetComponent<Aggregated>(edge,out var aggregated))
            {
                if(em.TryGetComponent<PrefabRef>(aggregated.m_Aggregate,out var pref) &&
                   prefabs.TryGetPrefab<PrefabBase>(pref.m_Prefab,out var asset) &&
                   asset.TryGet<Game.Prefabs.Localization>(out var loc) && loc.m_LocalizationID=="Assets.ALLEY_NAME")return true;
                if(em.TryGetComponent<PrefabRef>(aggregated.m_Aggregate,out var ap))
                {string n=prefabs.GetPrefabName(ap.m_Prefab);if(n!=null&&n.Contains("Pedestrian"))return true;}
            }
            if(em.TryGetComponent<PrefabRef>(edge,out var ep))
            {string n=prefabs.GetPrefabName(ep.m_Prefab);return n!=null&&n.Contains("Pedestrian Street");}
            return false;
        }
        private (string name,Entity edge) ConnectedNamed(Entity first,int hops)
        {
            if(connectedCache.TryGetValue(first,out var hit))return hit;
            var seen=new HashSet<Entity>{first};var frontier=new List<Entity>{first};
            var regular=("",Entity.Null);var minor=("",Entity.Null);
            for(int hop=0;hop<hops&&frontier.Count>0&&regular.Item2==Entity.Null;hop++)
            {
                var next=new List<Entity>();
                foreach(var edge in frontier)
                {
                    if(!em.TryGetComponent<Game.Net.Edge>(edge,out var e))continue;
                    for(int side=0;side<2;side++)
                    {
                        Entity node=side==0?e.m_Start:e.m_End;
                        if(!em.TryGetBuffer<ConnectedEdge>(node,true,out var connected))continue;
                        foreach(var c in connected)
                        {
                            if(!seen.Add(c.m_Edge))continue;
                            string n=GetRoadName(c.m_Edge);
                            if(n.Length==0){next.Add(c.m_Edge);continue;}
                            if(IsMinor(c.m_Edge))
                            {if(minor.Item2==Entity.Null)minor=(n,c.m_Edge);next.Add(c.m_Edge);continue;}
                            regular=(n,c.m_Edge);break;
                        }
                        if(regular.Item2!=Entity.Null)break;
                    }
                    if(regular.Item2!=Entity.Null)break;
                }
                frontier=next;
            }
            var result=regular.Item2!=Entity.Null?regular:minor;
            connectedCache[first]=result;
            return result;
        }
        private float3 NodePos(Entity node)
        {return em.TryGetComponent<Game.Net.Node>(node,out var n)?n.m_Position:float3.zero;}
        private (string name,Entity edge) NearestNamed(Entity stop)
        {
            if(nearestCache.TryGetValue(stop,out var hit))return hit;
            var ret=("",Entity.Null);
            if(!em.TryGetComponent<Game.Objects.Transform>(stop,out var st))return ret;
            float best=1000f*1000f;
            using(var edges=namedEdges.ToEntityArray(Allocator.Temp))
            {
                for(int i=0;i<edges.Length;i++)
                {
                    var edge=edges[i];
                    if(!em.TryGetComponent<Game.Net.Edge>(edge,out var e))continue;
                    float d=math.distancesq(st.m_Position,(NodePos(e.m_Start)+NodePos(e.m_End))*.5f);
                    if(d>=best)continue;
                    string n=GetRoadName(edge);if(n.Length==0)continue;
                    best=d;ret=(n,edge);
                }
            }
            nearestCache[stop]=ret;
            return ret;
        }
        private string CrossAt(Entity node,string main)
        {
            if(!em.TryGetBuffer<ConnectedEdge>(node,true,out var edges))return "";
            foreach(var e in edges)
            {string n=GetRoadName(e.m_Edge);if(n.Length>0 && n!=main)return n;}
            return "";
        }
        private string FindCross(Entity road,string main,float3 position)
        {
            if(!em.TryGetComponent<Game.Net.Edge>(road,out var e))return "";
            bool start=math.distancesq(position,NodePos(e.m_Start))<=math.distancesq(position,NodePos(e.m_End));
            Entity n1=start?e.m_Start:e.m_End, n2=start?e.m_End:e.m_Start;
            string cross=CrossAt(n1,main);if(cross.Length>0)return cross;
            cross=CrossAt(n2,main);if(cross.Length>0)return cross;
            foreach(var node in new[]{n1,n2})
            {
                Entity last=road,cur=node;
                for(int i=0;i<5;i++)
                {
                    if(!em.TryGetBuffer<ConnectedEdge>(cur,true,out var attached))break;
                    Entity walk=Entity.Null;
                    foreach(var candidate in attached)
                    {
                        if(candidate.m_Edge==last)continue;
                        string name=GetRoadName(candidate.m_Edge);
                        if(name.Length>0&&name!=main)return name;
                        if(name==main&&walk==Entity.Null)walk=candidate.m_Edge;
                    }
                    if(walk==Entity.Null||!em.TryGetComponent<Game.Net.Edge>(walk,out var link))break;
                    last=walk;cur=link.m_Start==cur?link.m_End:link.m_Start;
                    cross=CrossAt(cur,main);if(cross.Length>0)return cross;
                }
            }
            return "";
        }
        internal (string main,string cross) Streets(Entity stop)
        {
            if(streetCache.TryGetValue(stop,out var cached))return cached;
            Entity road=Entity.Null;string name="";
            if(em.TryGetComponent<Building>(stop,out var b)&&Exists(b.m_RoadEdge))
            {road=b.m_RoadEdge;name=GetRoadName(road);}
            if(name.Length==0 && em.TryGetComponent<Owner>(stop,out var own)&&
                em.TryGetComponent<Building>(own.m_Owner,out var ob)&&Exists(ob.m_RoadEdge))
            {road=ob.m_RoadEdge;name=GetRoadName(road);}
            if(name.Length==0&&em.TryGetComponent<Attached>(stop,out var attached)&&Exists(attached.m_Parent))
            {road=attached.m_Parent;name=GetRoadName(road);}
            if(name.Length>0 && IsMinor(road))
            {var connected=ConnectedNamed(road,6);if(connected.edge!=Entity.Null&&!IsMinor(connected.edge)){name=connected.name;road=connected.edge;}}
            if(name.Length==0&&road!=Entity.Null){var connected=ConnectedNamed(road,6);name=connected.name;road=connected.edge;}
            if(name.Length==0){var near=NearestNamed(stop);name=near.name;road=near.edge;}
            if(name.Length==0)name=RWLocalization.T("rw.stop.generic");
            float3 pos=em.TryGetComponent<Game.Objects.Transform>(stop,out var t)?t.m_Position:float3.zero;
            string cross=road==Entity.Null?"":FindCross(road,name,pos);
            var answer=(name,cross);streetCache[stop]=answer;return answer;
        }
        private static string Join(string first,string second)
        {
            string locale=Game.SceneFlow.GameManager.instance.localizationManager.activeLocaleId;
            bool noSpace=locale=="ja-JP"||locale=="zh-HANS"||locale=="zh-HANT";
            return noSpace?first+second:first+" "+second;
        }
        private static string Localized(string key)
        {
            try{return Game.SceneFlow.GameManager.instance.localizationManager.activeDictionary.TryGetValue(key,out var value)?value:null;}
            catch{return null;}
        }
        private string BuildingType(Entity building)
        {
            if(!em.TryGetComponent<PrefabRef>(building,out var pref))return "";
            if(em.HasComponent<TransportStationData>(pref.m_Prefab))
            {
                var p=world.GetExistingSystemManaged<PrefabSystem>();
                if(p!=null&&em.TryGetComponent<UIObjectData>(pref.m_Prefab,out var ui)&&ui.m_Group!=Entity.Null)
                {
                    string group=p.GetPrefabName(ui.m_Group)??"";
                    string type=group.Contains("Train")?"Train":group.Contains("Subway")?"Subway":
                        group.Contains("Tram")?"Tram":group.Contains("Road")||group.Contains("Bus")?"Bus":null;
                    if(group.Contains("Air"))return Localized("Assets.NAME[Airport01]");
                    if(group.Contains("Ship")||group.Contains("Water"))return Localized("Assets.NAME[Harbor01]");
                    if(type!=null)
                    {
                        string s=Localized("Editor.ASSET_CATEGORY_TITLE[Vehicles/Services/"+type+"]");
                        if(!string.IsNullOrEmpty(s))return Join(s,RWLocalization.T("rw.stop.station_suffix"));
                    }
                }
            }
            return "";
        }
        private string MarkerName(Entity stop,string street,bool aircraft)
        {
            if(em.HasComponent<Game.Objects.OutsideConnection>(stop))
            {
                string city=Label(stop);
                if(!Valid(city))return null;
                if(aircraft){string airport=Localized("Assets.NAME[Airport01]");if(!string.IsNullOrEmpty(airport))return Join(city,airport);}
                return city;
            }
            if(!em.HasComponent<Game.Objects.Marker>(stop))return null;
            if(names.TryGetCustomName(stop,out var ownName))return ownName;
            Entity top=stop;
            for(int i=0;i<8;i++)
            {
                if(!em.TryGetComponent<Owner>(top,out var owner)||!Exists(owner.m_Owner))break;
                top=owner.m_Owner;
                if(names.TryGetCustomName(top,out var custom))return custom;
            }
            if(top==stop)return null;
            string kind=BuildingType(top);
            if(string.IsNullOrEmpty(kind))return null;
            string streetPart=Abbrev(street);
            return !Valid(streetPart)||(streetPart=="Stop"||streetPart==RWLocalization.T("rw.stop.generic"))?kind:Join(streetPart,kind);
        }
        private string Vanilla(Entity stop)
        {
            if(names.TryGetCustomName(stop,out var custom))return custom;
            if(BuildingUtils.GetAddress(em,stop,out var road,out var number)&&HasStreetName(road))
            {
                string name=Abbrev(Label(road));if(Valid(name))return number+" "+name;
            }
            return null;
        }
        private string Format(Entity stop,string main,string cross,int count)
        {
            if(names.TryGetCustomName(stop,out var custom))return custom;
            if(em.TryGetComponent<Owner>(stop,out var owner)&&Exists(owner.m_Owner)&&names.TryGetCustomName(owner.m_Owner,out var own))return own;
            string b=Base(main);
            if(count>1 && !string.IsNullOrEmpty(cross))return b+"/ "+Base(cross);
            return Abbrev(main);
        }
        internal string Display(Entity stop,string main,string cross,int baseCount,bool rail,bool aircraft)
        {
            // Preserve original FPC distinction: rail uses road/cross name; others
            // prefer marker and vanilla address before road/cross fallback.
            if(rail)return Format(stop,main,cross,baseCount);
            string marker=MarkerName(stop,main,aircraft);
            if(!string.IsNullOrEmpty(marker))return marker;
            string vanilla=Vanilla(stop);if(!string.IsNullOrEmpty(vanilla))return vanilla;
            return Format(stop,main,cross,baseCount);
        }
        internal static string BaseName(string name){return Base(name);}
    }
}
