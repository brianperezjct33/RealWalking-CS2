// SPDX-License-Identifier: GPL-2.0-only
// RC18: true 3D closest-point selection on real pedestrian Net centerlines
// and Navigation Area triangles. No viewport/ray/focus/terrain ranking.
using System.Collections.Generic;
using UnityEngine;

namespace CS2RealWalkingG1Bridge
{
    internal static class RWEntryGeometry
    {
        internal struct Pick
        {
            internal Vector3 Point;
            internal float Distance3D,DistanceXZ;
            internal string Surface;
        }

        internal struct PickDiagnostics
        {
            internal int NavChecked, NetChecked, Candidates, WaterRejected;
            public override string ToString()
            { return "navChecked="+NavChecked+" netChecked="+NetChecked+
                " candidates="+Candidates+" waterRejected="+WaterRejected; }
        }

        private struct Candidate
        {
            internal Vector3 Point;
            internal float DistanceSq;
            internal string Surface;
        }

        internal static bool TryPickNearest(RWOverlaySnapshot snapshot, Vector3 camera,
            RWWaterSafety water, out Pick result, out PickDiagnostics diag)
        {
            result=default;
            diag=default;
            if (snapshot == null || !Finite(camera)) return false;
            var candidates=new List<Candidate>(256);
            float radiusSq=snapshot.SearchRadius*snapshot.SearchRadius;
            // Navigation areas are actual 3D triangles, NOT their centroid.
            // The closest point can be inside, on an edge, or at a vertex.
            if (snapshot.NavAvailable && snapshot.Triangles != null)
                foreach (var triangle in snapshot.Triangles)
                {
                    diag.NavChecked++;
                    Vector3 point=ClosestTriangle3D(camera,triangle.A,triangle.B,triangle.C);
                    Offer(point,"AREA "+triangle.AreaId+"/"+triangle.TriangleId,
                        camera,radiusSq,candidates);
                }
            // Pedestrian Net is sampled from the game's Bezier. Find the
            // closest point on every sample segment, never just a lane node.
            if (snapshot.Lanes != null)
                foreach (var lane in snapshot.Lanes)
                {
                    if (lane.Points == null || lane.Points.Length<2) continue;
                    for (int i=1;i<lane.Points.Length;i++)
                    {
                        diag.NetChecked++;
                        Vector3 point=ClosestSegment3D(camera,lane.Points[i-1],lane.Points[i]);
                        Offer(point,"NET "+lane.Id,camera,radiusSq,candidates);
                    }
                }
            diag.Candidates=candidates.Count;
            // Sort by Euclidean 3D distance *before* invoking potentially
            // expensive water samplers; only inspect farther candidates if
            // the nearer one is confirmed unsafe (waterway false Navigation).
            candidates.Sort((a,b)=>a.DistanceSq.CompareTo(b.DistanceSq));
            foreach (var candidate in candidates)
            {
                if (water != null && !water.IsSafeEntry(candidate.Point))
                {
                    diag.WaterRejected++;
                    continue;
                }
                float dx=candidate.Point.x-camera.x,dz=candidate.Point.z-camera.z;
                result=new Pick {
                    Point=candidate.Point,
                    Distance3D=Mathf.Sqrt(candidate.DistanceSq),
                    DistanceXZ=Mathf.Sqrt(dx*dx+dz*dz),
                    Surface=candidate.Surface
                };
                return true;
            }
            return false;
        }

        private static void Offer(Vector3 foot,string source,Vector3 camera,
            float radiusSq,List<Candidate> output)
        {
            if (!Finite(foot)) return;
            float dx=foot.x-camera.x,dz=foot.z-camera.z;
            if (dx*dx+dz*dz>radiusSq) return;
            output.Add(new Candidate {
                Point=foot,DistanceSq=(foot-camera).sqrMagnitude,Surface=source
            });
        }

        internal static Vector3 ClosestSegment3D(Vector3 point,Vector3 a,Vector3 b)
        {
            Vector3 e=b-a;
            float lengthSq=e.sqrMagnitude;
            if (lengthSq<1e-9f) return a;
            float t=Mathf.Clamp01(Vector3.Dot(point-a,e)/lengthSq);
            return a+t*e;
        }

        // Ericson, Real-Time Collision Detection: nearest point on a 3D
        // triangle using vertex, edge, and face Voronoi regions. Handles
        // degenerate area triangles via their three edges.
        internal static Vector3 ClosestTriangle3D(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
        {
            Vector3 ab=b-a,ac=c-a;
            if (Vector3.Cross(ab,ac).sqrMagnitude<1e-9f)
            {
                Vector3 q=ClosestSegment3D(p,a,b);
                Vector3 r=ClosestSegment3D(p,b,c);
                Vector3 s=ClosestSegment3D(p,c,a);
                float dq=(q-p).sqrMagnitude,dr=(r-p).sqrMagnitude,ds=(s-p).sqrMagnitude;
                return dq<=dr && dq<=ds?q:dr<=ds?r:s;
            }
            Vector3 ap=p-a;
            float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
            if (d1<=0f && d2<=0f) return a;
            Vector3 bp=p-b;
            float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);
            if (d3>=0f && d4<=d3) return b;
            float vc=d1*d4-d3*d2;
            if (vc<=0f && d1>=0f && d3<=0f)
                return a+ab*(d1/(d1-d3));
            Vector3 cp=p-c;
            float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);
            if (d6>=0f && d5<=d6) return c;
            float vb=d5*d2-d1*d6;
            if (vb<=0f && d2>=0f && d6<=0f)
                return a+ac*(d2/(d2-d6));
            float va=d3*d6-d5*d4;
            if (va<=0f && (d4-d3)>=0f && (d5-d6)>=0f)
            {
                Vector3 bc=c-b;
                return b+bc*((d4-d3)/((d4-d3)+(d5-d6)));
            }
            float denom=va+vb+vc;
            if (Mathf.Abs(denom)<1e-9f) return ClosestSegment3D(p,a,b);
            float v=vb/denom,w=vc/denom;
            return a+ab*v+ac*w;
        }

        private static bool Finite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
                   !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }
    }
}
