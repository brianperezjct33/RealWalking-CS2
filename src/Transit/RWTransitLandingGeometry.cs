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
    // Shared closest-point and candidate-ranking geometry.
    internal static partial class RWTransitRide
    {
        private static void ConsiderStation(Vector3 p,string source,Vector3 origin,
            Vector3 station,float baseY,float radius,float maxDY,float extraPenalty,
            ref float best,ref Vector3 landing,ref string chosen)
        {
            float dx=p.x-origin.x,dz=p.z-origin.z;
            float d=Mathf.Sqrt(dx*dx+dz*dz);
            float sx=p.x-station.x,sz=p.z-station.z;
            float ds=Mathf.Sqrt(sx*sx+sz*sz);
            float dy=Mathf.Abs(p.y-baseY);
            if(d<2.2f || d>radius || ds>18.0f || dy>maxDY ||
                Mathf.Abs(p.y-station.y)>3.0f || !IsFinite(p.y))return;
            float score=d+.6f*dy+.4f*ds+extraPenalty;
            if(score>=best)return;
            best=score;landing=p;chosen=source;
        }
        private static void Consider(Vector3 p,string source,Vector3 origin,float baseY,
            float radius,float maxDY,ref float best,ref Vector3 landing,ref string chosen)
        {
            float dx=p.x-origin.x,dz=p.z-origin.z,d=Mathf.Sqrt(dx*dx+dz*dz);
            float dy=Mathf.Abs(p.y-baseY);
            // Must not land exactly on railway/road vehicle center; nearby pedestrian
            // Net/Area is needed, and layer mismatch is capped to prevent roofs.
            // Leave a small horizontal clearance from vehicle center: never teleport
            // to the exact same XZ as the train/car, even if a nav triangle overlaps.
            if(d<1.6f || d>radius || dy>maxDY || !IsFinite(p.y))return;
            // Prefer shortest horizontal walking transfer, lightly penalize vertical gap.
            float score=d+.65f*dy;
            if(score>=best)return;
            best=score;landing=p;chosen=source;
        }
        private static bool InsideTriXZ(Vector3 a,Vector3 b,Vector3 c,Vector3 p,out Vector3 at)
        {
            at=Vector3.zero;
            float den=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);
            if(Mathf.Abs(den)<1e-5f)return false;
            float u=((b.z-c.z)*(p.x-c.x)+(c.x-b.x)*(p.z-c.z))/den;
            float v=((c.z-a.z)*(p.x-c.x)+(a.x-c.x)*(p.z-c.z))/den;
            if(u<0f||v<0f||u+v>1f)return false;
            at=a*u+b*v+c*(1f-u-v);
            return true;
        }
        private static Vector3 ClosestTrianglePointXZ(Vector3 a,Vector3 b,Vector3 c,Vector3 p)
        {
            float denom=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);
            if(Mathf.Abs(denom)>1e-5f)
            {
                float u=((b.z-c.z)*(p.x-c.x)+(c.x-b.x)*(p.z-c.z))/denom;
                float v=((c.z-a.z)*(p.x-c.x)+(a.x-c.x)*(p.z-c.z))/denom;
                if(u>=0f&&v>=0f&&u+v<=1f)return a*u+b*v+c*(1f-u-v);
            }
            Vector3 best=Edge(a,b,p),q=Edge(b,c,p),r=Edge(c,a,p);
            if(XZSquare(q,p)<XZSquare(best,p))best=q;
            if(XZSquare(r,p)<XZSquare(best,p))best=r;
            return best;
        }
        private static Vector3 Edge(Vector3 a,Vector3 b,Vector3 p)
        {
            float dx=b.x-a.x,dz=b.z-a.z,den=dx*dx+dz*dz;
            float t=den<1e-6f?0f:Mathf.Clamp01(((p.x-a.x)*dx+(p.z-a.z)*dz)/den);
            return Vector3.Lerp(a,b,t);
        }
        private static float XZSquare(Vector3 a,Vector3 b)
        {float dx=a.x-b.x,dz=a.z-b.z;return dx*dx+dz*dz;}
    }
}
