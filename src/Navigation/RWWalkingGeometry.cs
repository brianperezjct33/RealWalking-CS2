// SPDX-License-Identifier: MIT
// Real Walking navigation: exact pedestrian Net/Area height and bounded movement.
// The controller publishes world-space eye height to the embedded FPC camera.
// Free-flight height and transit-follow behavior are handled separately.
using System;
using System.Reflection;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CS2RealWalkingG1Bridge
{
    // XZ projection, surface selection, and verified stair seam geometry.
    internal sealed partial class RWWalkingController
    {
        private static bool IsFinite(float f) {return !float.IsNaN(f)&&!float.IsInfinity(f);}
        private static float ClosestPolyline(Vector3[] pts,Vector3 p,out float height,out Vector3 tangent)
        {
            height=0f;tangent=Vector3.forward;float best=float.PositiveInfinity;
            for(int i=1;i<pts.Length;i++)
            {
                Vector3 a=pts[i-1],b=pts[i];
                float dx=b.x-a.x,dz=b.z-a.z;
                float len=dx*dx+dz*dz;
                float t=len<1e-7f?0f:Mathf.Clamp01(((p.x-a.x)*dx+(p.z-a.z)*dz)/len);
                float x=a.x+t*dx,z=a.z+t*dz;
                float d=(p.x-x)*(p.x-x)+(p.z-z)*(p.z-z);
                if(d<best)
                {
                    best=d;height=a.y+t*(b.y-a.y);
                    if(len>1e-7f)tangent=new Vector3(dx,0f,dz)/Mathf.Sqrt(len);
                }
            }
            return Mathf.Sqrt(best);
        }
        private static float ClosestEdge(Vector3 a,Vector3 b,Vector3 p,out float height,out Vector3 tangent)
        {
            float dx=b.x-a.x,dz=b.z-a.z,denom=dx*dx+dz*dz;
            float t=denom<1e-7f?0f:Mathf.Clamp01(((p.x-a.x)*dx+(p.z-a.z)*dz)/denom);
            float x=a.x+t*dx,z=a.z+t*dz;
            height=a.y+t*(b.y-a.y);
            tangent=denom<1e-7f?Vector3.forward:new Vector3(dx,0f,dz)/Mathf.Sqrt(denom);
            float ex=p.x-x,ez=p.z-z;
            return Mathf.Sqrt(ex*ex+ez*ez);
        }
        private static float ClosestTriangleEdge(Vector3 a,Vector3 b,Vector3 c,Vector3 p,
            out float height,out Vector3 tangent)
        {
            float yy; Vector3 tt;
            float best=ClosestEdge(a,b,p,out height,out tangent);
            float d=ClosestEdge(b,c,p,out yy,out tt);
            if(d<best){best=d;height=yy;tangent=tt;}
            d=ClosestEdge(c,a,p,out yy,out tt);
            if(d<best){best=d;height=yy;tangent=tt;}
            return best;
        }
        private static bool InsideTriangle(Vector3 a,Vector3 b,Vector3 c,Vector3 p,out float y)
        {
            y=0f;
            float det=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);
            if(Mathf.Abs(det)<0.00001f)return false;
            float wa=((b.z-c.z)*(p.x-c.x)+(c.x-b.x)*(p.z-c.z))/det;
            float wb=((c.z-a.z)*(p.x-c.x)+(a.x-c.x)*(p.z-c.z))/det;
            float wc=1f-wa-wb;
            if(wa < -0.0001f ||wb < -0.0001f||wc < -0.0001f)return false;
            y=wa*a.y+wb*b.y+wc*c.y;
            return true;
        }
        // Find the nearest point on the boundary of the XY/Z-projected Area triangle.
        // The returned boundary position is used for both elevation and link geometry.
        private static float EdgePointXZ(Vector3 a,Vector3 b,Vector3 p,out Vector3 point)
        {
            float dx=b.x-a.x,dz=b.z-a.z,den=dx*dx+dz*dz;
            float t=den<1e-7f?0f:Mathf.Clamp01(((p.x-a.x)*dx+(p.z-a.z)*dz)/den);
            point=Vector3.Lerp(a,b,t);
            float vx=p.x-point.x,vz=p.z-point.z;
            return Mathf.Sqrt(vx*vx+vz*vz);
        }
        private static float NearestTriangleBoundaryPoint(RWOverlayTriangle tri,Vector3 p,out Vector3 point)
        {
            float best=EdgePointXZ(tri.A,tri.B,p,out point);
            Vector3 next;
            float d=EdgePointXZ(tri.B,tri.C,p,out next);
            if(d<best){best=d;point=next;}
            d=EdgePointXZ(tri.C,tri.A,p,out next);
            if(d<best){best=d;point=next;}
            return best;
        }
        // A real (sloped) approach segment must face the LOWER end of a stair.
        // Reject flat platforms, vertical faces, high/upper ends and overpasses.
        private static bool IsLowerEndOfSlope(RWOverlayTriangle tri,Vector3 nearEdge)
        {
            float ymin=Mathf.Min(tri.A.y,Mathf.Min(tri.B.y,tri.C.y));
            float ymax=Mathf.Max(tri.A.y,Mathf.Max(tri.B.y,tri.C.y));
            if(ymax-ymin<.30f || nearEdge.y>ymin+.60f || nearEdge.y>ymax-.20f)return false;
            Vector3 normal=Vector3.Cross(tri.B-tri.A,tri.C-tri.A);
            if(Mathf.Abs(normal.y)<1e-5f)return false;
            float grade=Mathf.Sqrt(normal.x*normal.x+normal.z*normal.z)/Mathf.Abs(normal.y);
            return grade>=RampMinGrade && grade<=RampMaxGrade;
        }
        // Connect a *known* Net corridor to a floating lower stair edge by a short
        // interpolated run-up. Never scan global ground/terrain or connect to a random
        // nearby area purely because it is close in XZ.
        private static bool TryVerifiedRamp(RWOverlaySnapshot snap,RWOverlayTriangle tri,
            Vector3 p,float referenceFootY,Vector3 edgePoint,float edgeDistance,
            out float rampY,out float score)
        {
            rampY=0f;score=float.PositiveInfinity;
            if(RWWalkState.LastNetId<0 || edgeDistance>RampReach ||
               !(RWWalkState.Surface.StartsWith("NET ") || RWWalkState.Surface.StartsWith("RAMP ")))
                return false;
            if(!IsLowerEndOfSlope(tri,edgePoint))return false;
            // Do not fabricate an approach while moving away from / parallel to a stair.
            // An already acquired RAMP may continue while stopped or moving laterally.
            if(!RWWalkState.Surface.StartsWith("RAMP "))
            {
                Vector3 toward=new Vector3(edgePoint.x-p.x,0f,edgePoint.z-p.z);
                Vector3 motion=RWWalkState.NavigationMove;
                if(toward.sqrMagnitude<1e-6f || motion.sqrMagnitude<1e-7f ||
                   Vector3.Dot(toward,motion) <= .08f*Mathf.Sqrt(toward.sqrMagnitude*motion.sqrMagnitude))
                    return false;
            }
            for(int i=0;i<snap.Lanes.Length;i++)
            {
                var lane=snap.Lanes[i];
                if(lane.Id!=RWWalkState.LastNetId || lane.Points==null || lane.Points.Length<2)continue;
                float baseY;Vector3 axis;
                float baseDistance=ClosestPolyline(lane.Points,edgePoint,out baseY,out axis);
                if(baseDistance>lane.WalkableHalfWidth+.60f)continue;
                float rise=edgePoint.y-baseY;
                if(rise<.07f || rise>RampMaxRise)continue;
                // Do not approach a bridge underside or a disconnected upper deck.
                if(Mathf.Abs(baseY-referenceFootY)>RampMaxRise+.25f)continue;
                rampY=Mathf.Lerp(baseY,edgePoint.y,1f-edgeDistance/RampReach);
                if(Mathf.Abs(rampY-referenceFootY)>1.85f)return false;
                // Prefer the run-up near its *lower* entrance, even while Net
                // remains present. At the Area boundary we finish at exact edge Y.
                score=-1.15f + .16f*(edgeDistance/RampReach);
                return true;
            }
            return false;
        }
        private static bool SelectSurface(RWOverlaySnapshot s,Vector3 p,float referenceFootY,
            out float y,out string source,out float distance,out float gap,out float width,out Vector3 tangent)
        {
            y=0f;source="none";distance=float.PositiveInfinity;gap=float.PositiveInfinity;width=0f;
            tangent=Vector3.forward;
            float bestScore=float.PositiveInfinity;
            // Navigation area: trust interpolation ONLY if XZ lies INSIDE the triangle.
            if(s.NavAvailable)
            {
                for(int i=0;i<s.Triangles.Length;i++)
                {
                    var tri=s.Triangles[i];float yy,edgeY;Vector3 edgeTangent;
                    float edgeDistance=ClosestTriangleEdge(tri.A,tri.B,tri.C,p,out edgeY,out edgeTangent);
                    bool inside=InsideTriangle(tri.A,tri.B,tri.C,p,out yy);
                    // Widen the SEAM only when crossing from a verified Net corridor
                    // into an Area stair. Never expand an existing platform Area over
                    // adjacent tracks: inside Areas retain their real triangle borders.
                    if(!inside)
                    {
                        Vector3 nearEdge;
                        float actualEdgeDistance=NearestTriangleBoundaryPoint(tri,p,out nearEdge);
                        float rampY,rampScore;
                        if(TryVerifiedRamp(s,tri,p,referenceFootY,nearEdge,actualEdgeDistance,
                            out rampY,out rampScore))
                        {
                            if(rampScore<bestScore)
                            {
                                bestScore=rampScore;y=rampY;
                                source="RAMP "+tri.AreaId+"/"+tri.TriangleId;
                                distance=actualEdgeDistance;gap=Mathf.Abs(rampY-referenceFootY);
                                width=0f;tangent=edgeTangent;
                            }
                            continue;
                        }
                        // Important: stop G1.8 from snapping directly to the height
                        // of a sloped triangle that does NOT reach the Net corridor.
                        // Ordinary micro cracks between Area triangles keep the old
                        // narrow 0.40m tolerance; this is not a general Area expansion.
                        if(RWWalkState.Surface.StartsWith("NET ") &&
                           IsLowerEndOfSlope(tri,nearEdge) && nearEdge.y-referenceFootY>.35f)
                            continue;
                        if(edgeDistance>AreaEdgeTolerance)continue;
                        yy=edgeY;
                    }
                    float dy=Mathf.Abs(yy-referenceFootY);
                    if(dy>(inside?2.5f:SeamStepHeight))continue;
                    float score=dy*1.7f-0.25f+(inside?0f:edgeDistance*2.2f);
                    if(RWWalkState.Surface=="AREA "+tri.AreaId+"/"+tri.TriangleId)score-=.30f;
                    if(score<bestScore)
                    {
                        bestScore=score;y=yy;source="AREA "+tri.AreaId+"/"+tri.TriangleId;
                        distance=inside?0f:edgeDistance;gap=dy;width=0f;tangent=edgeTangent;
                    }
                }
            }
            // Centerline is a HEIGHT REFERENCE ONLY. Never constrain player X/Z to it.
            for(int i=0;i<s.Lanes.Length;i++)
            {
                var lane=s.Lanes[i];float yy;Vector3 laneTangent;
                float d=ClosestPolyline(lane.Points,p,out yy,out laneTangent);
                // Use EXACTLY the inferred width rendered by the yellow overlay.
                // It is a user-tested walking envelope, NOT physical collision geometry.
                float halfWidth=lane.WalkableHalfWidth;
                if(d>halfWidth)continue;
                float dy=Mathf.Abs(yy-referenceFootY);
                if(dy>2.5f)continue;
                float score=(d/Mathf.Max(.35f,halfWidth))*.65f+dy*1.7f;
                // Sticky source helps a quay when two nearly-equal, differently
                // elevated prefab lanes overlap. Never synthesize/smooth their Y.
                if(RWWalkState.Surface=="NET "+lane.Id && dy<.45f)score-=.70f;
                if(score<bestScore){bestScore=score;y=yy;source="NET "+lane.Id;distance=d;gap=dy;width=halfWidth*2f;tangent=laneTangent;}
            }
            return source!="none";
        }
        private static bool TryValidCandidate(RWOverlaySnapshot snap,Vector3 p,float referenceFootY,
            bool hasPrevious,float previousFootY,
            out float y,out string source,out float distance,out float gap,out float width,out Vector3 tangent)
        {
            y=0f;source="none";distance=0f;gap=0f;width=0f;tangent=Vector3.forward;
            return SelectSurface(snap,p,referenceFootY,out y,out source,out distance,out gap,out width,out tangent)
                && (!hasPrevious || Mathf.Abs(y-previousFootY)<2f);
        }
        // Retain motion parallel to the last valid sidewalk/Area edge. We never
        // drive the player to a different XZ level or jump across a 10m bridge gap.
        private static bool TrySlideAlongEdge(RWOverlaySnapshot snap,Vector3 safe,Vector3 proposed,
            Vector3 tangent,float referenceY,float previousFootY,
            out Vector3 accepted,out float surfaceY,out string surface,out float distance,out float gap,
            out float width,out Vector3 newTangent)
        {
            accepted=safe;surfaceY=0f;surface="none";distance=gap=width=0f;
            newTangent=tangent;
            tangent.y=0f;
            if(tangent.sqrMagnitude<.0001f)return false;
            tangent.Normalize();
            Vector3 inputXZ=proposed-safe;inputXZ.y=0f;
            float travel=Vector3.Dot(inputXZ,tangent);
            if(Mathf.Abs(travel)<.002f)return false;
            for(int pass=0;pass<5;pass++)
            {
                float fraction=1f/(1<<pass);
                Vector3 probe=safe+tangent*(travel*fraction);
                if(TryValidCandidate(snap,probe,referenceY,true,previousFootY,
                    out surfaceY,out surface,out distance,out gap,out width,out newTangent))
                {accepted=probe;return true;}
            }
            return false;
        }
    }
}
