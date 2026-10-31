// SPDX-License-Identifier: MIT
// Read-only ECS pedestrian geometry snapshots (Net and Navigation Area).
// No game entity writes. Rendering is isolated in RWOverlayView.cs.
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
    // Read-only Navigation Area triangle sampling.
    public sealed partial class RWOverlayScanSystem
    {
#if RW_AREA
        private struct AreaVisitor : INativeQuadTreeIterator<Game.Areas.AreaSearchItem,QuadTreeBoundsXZ>
        {
            public float2 CenterXZ;
            public float Radius;
            public NativeList<Game.Areas.AreaSearchItem> Matches;
            public int Dropped;
            public bool Intersect(QuadTreeBoundsXZ bounds)
            {
                Bounds3 b=bounds.m_Bounds;
                return b.max.x>=CenterXZ.x-Radius&&b.min.x<=CenterXZ.x+Radius&&
                       b.max.z>=CenterXZ.y-Radius&&b.min.z<=CenterXZ.y+Radius;
            }
            public void Iterate(QuadTreeBoundsXZ bounds, Game.Areas.AreaSearchItem item)
            {
                if(!Intersect(bounds))return;
                if(Matches.Length<4096)Matches.Add(item);else Dropped++;
            }
        }
        private static float TriangleDistanceXZ(Vector3 a,Vector3 b,Vector3 c,Vector3 eye,out float y)
        {
            // If inside, barycentric interpolates the sloped face, not the average height.
            float det=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);
            if(Mathf.Abs(det)>0.00001f)
            {
                float wa=((b.z-c.z)*(eye.x-c.x)+(c.x-b.x)*(eye.z-c.z))/det;
                float wb=((c.z-a.z)*(eye.x-c.x)+(a.x-c.x)*(eye.z-c.z))/det;
                float wc=1-wa-wb;
                if(wa>=-0.0001f&&wb>=-0.0001f&&wc>=-0.0001f)
                {
                    y=wa*a.y+wb*b.y+wc*c.y;
                    return 0f;
                }
            }
            Vector3[] pts={a,b,c,a};
            return ClosestPolylineXZ(pts,eye,out y);
        }
        private void CollectAreas(RWOverlaySnapshot snap)
        {
            var system=World.GetExistingSystemManaged<Game.Areas.SearchSystem>();
            if(system==null){snap.Status="Game.Areas.SearchSystem unavailable";return;}
            snap.NavAvailable=true;
            JobHandle dependency;
            var tree=system.GetSearchTree(true,out dependency);
            dependency.Complete();
            var matches=new NativeList<Game.Areas.AreaSearchItem>(Allocator.Temp);
            try
            {
                var visitor=new AreaVisitor{
                    CenterXZ=new float2(snap.Eye.x,snap.Eye.z),Radius=snap.SearchRadius,Matches=matches
                };
                tree.Iterate(ref visitor,0);
                var output=new List<RWOverlayTriangle>(180);
                var seen=new HashSet<long>();
                for(int i=0;i<matches.Length;i++)
                {
                    var item=matches[i];
                    Entity area=item.m_Area;
                    if(!EntityManager.Exists(area)||!EntityManager.HasComponent<Game.Areas.Navigation>(area))continue;
                    if(!EntityManager.HasBuffer<Game.Areas.Triangle>(area)||!EntityManager.HasBuffer<Game.Areas.Node>(area))continue;
                    long key=((long)area.Index<<32) ^ ((long)area.Version<<16) ^ (uint)item.m_Triangle;
                    if(!seen.Add(key))continue;
                    var tris=EntityManager.GetBuffer<Game.Areas.Triangle>(area);
                    var nodes=EntityManager.GetBuffer<Game.Areas.Node>(area);
                    if(item.m_Triangle<0||item.m_Triangle>=tris.Length)continue;
                    var ix=tris[item.m_Triangle].m_Indices;
                    if(ix.x<0||ix.y<0||ix.z<0||ix.x>=nodes.Length||ix.y>=nodes.Length||ix.z>=nodes.Length)continue;
                    Vector3 a=V(nodes[ix.x].m_Position),b=V(nodes[ix.y].m_Position),c=V(nodes[ix.z].m_Position);
                    float y;
                    float d=TriangleDistanceXZ(a,b,c,snap.Eye,out y);
                    if(d>snap.SearchRadius)continue;
                    snap.TotalNav++;
                    int maxAreaCandidates=snap.SearchRadius>90f?850:500;
                    if(snap.EntryRequestId==0 && output.Count>=maxAreaCandidates)
                    {snap.Skipped++;continue;}
                    output.Add(new RWOverlayTriangle{
                        A=a,B=b,C=c,NearY=y,DistanceXZ=d,
                        AreaId=area.Index,TriangleId=item.m_Triangle
                    });
                }
                float rankY=RWWalkState.Mode==RWTravelMode.Navigation &&
                    !float.IsNaN(RWWalkState.TargetFootY)?RWWalkState.TargetFootY:snap.FootY;
                if(snap.EntryRequestId!=0)output.Sort((a,b)=>(a.DistanceXZ*a.DistanceXZ+
                    (a.NearY-snap.Eye.y)*(a.NearY-snap.Eye.y)).CompareTo(
                    b.DistanceXZ*b.DistanceXZ+(b.NearY-snap.Eye.y)*(b.NearY-snap.Eye.y)));
                else output.Sort((a,b)=>(a.DistanceXZ+1.75f*Mathf.Abs(a.NearY-rankY)).CompareTo(b.DistanceXZ+1.75f*Mathf.Abs(b.NearY-rankY)));
                int maxTriangles=snap.EntryRequestId!=0?4096:
                    (snap.SearchRadius>90f?500:(snap.SearchRadius>24f?350:MaxShowTriangles));
                // Entry results are ordered by 3D proximity, not height-band quotas.
                if(output.Count>maxTriangles){snap.Skipped+=output.Count-maxTriangles;output.RemoveRange(maxTriangles,output.Count-maxTriangles);}
                snap.Triangles=output.ToArray();
                snap.Skipped+=visitor.Dropped;
            }
            finally{matches.Dispose();}
        }
#endif
    }
}
