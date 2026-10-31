// SPDX-License-Identifier: GPL-2.0-only
// RC15: read-only terrain/water sampler shared by entry safety and waterfront
// walking-envelope checks. A sampled water height by itself is insufficient:
// WaterUtils.SampleHeight returns terrain height in dry locations.
using Game;
using Game.Routes;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace CS2RealWalkingG1Bridge
{
    internal sealed class RWWaterSafety
    {
        private TerrainHeightData _terrain;
        private WaterSurfaceData<SurfaceWater> _surface;

        private RWWaterSafety(TerrainHeightData terrain, WaterSurfaceData<SurfaceWater> surface)
        {
            _terrain = terrain;
            _surface = surface;
        }

        internal static RWWaterSafety TryCreate()
        {
            try
            {
                var world = World.DefaultGameObjectInjectionWorld;
                if (world == null) return null;
                var terrain = world.GetExistingSystemManaged<TerrainSystem>();
                var water = world.GetExistingSystemManaged<WaterSystem>();
                if (terrain == null || water == null || !water.Loaded) return null;
                var heights = terrain.GetHeightData();
                var surface = water.GetSurfaceData(out var dependency);
                dependency.Complete();
                return surface.isCreated ? new RWWaterSafety(heights, surface) : null;
            }
            catch (System.Exception) { return null; }
        }

        // The wetness check is based on WATER minus TERRAIN at this XZ, not on
        // the walkway height. This distinguishes a dry road from a dock above
        // genuinely flooded ground. Returns false if the sample is unusable.
        internal bool TryWaterAt(Vector3 point, out float waterY, out bool wet)
        {
            waterY = float.NaN;
            wet = false;
            try
            {
                float3 p = new float3(point.x, 0f, point.z);
                float land = TerrainUtils.SampleHeight(ref _terrain, p);
                float surface = WaterUtils.SampleHeight(ref _surface, ref _terrain, p);
                if (!math.isfinite(land) || !math.isfinite(surface)) return false;
                wet = surface > land + 0.35f;
                waterY = surface;
                return true;
            }
            catch (System.Exception) { return false; }
        }

        // Do not use water height as a replacement for walkway geometry.
        // Only reject a NAV/Net candidate when we have positively identified
        // flooded ground AND the candidate lies at or under the waterline.
        internal bool IsSafeEntry(Vector3 candidate)
        {
            if (!TryWaterAt(candidate, out float surface, out bool wet)) return true;
            if (wet) return candidate.y >= surface + 1.0f;
            // Canal-edge geometry can be almost level with the local sea bed:
            // water and terrain differ by less than the wetness tolerance at
            // the exact point. A nearby positive-water sample supplies the
            // waterline without indiscriminately banning dry inland paths.
            if (candidate.y >= surface + 1.0f) return true;
            Vector3 dx = new Vector3(2.5f,0f,0f);
            Vector3 dz = new Vector3(0f,0f,2.5f);
            return !IsNearWaterline(candidate, candidate + dx) &&
                   !IsNearWaterline(candidate, candidate - dx) &&
                   !IsNearWaterline(candidate, candidate + dz) &&
                   !IsNearWaterline(candidate, candidate - dz);
        }

        private bool IsNearWaterline(Vector3 candidate, Vector3 probe)
        {
            return TryWaterAt(probe, out float waterY, out bool wet) && wet &&
                   candidate.y < waterY + 1.0f;
        }

        // Does a nominally wide inferred Net corridor reach OPEN WATER?
        // Called only on potential expanded-width lanes during the throttled
        // ECS sampling pass, never on every rendering/walking frame.
        internal bool ReachesOpenWater(Vector3[] points, float halfWidth)
        {
            if (points == null || points.Length < 2 || halfWidth < 1.7f) return false;
            int mid = (points.Length - 1) / 2;
            Vector3 a = points[mid], b = points[mid + 1];
            Vector3 d = b - a;
            float len = Mathf.Sqrt(d.x * d.x + d.z * d.z);
            if (len < .01f) return false;
            Vector3 lateral = new Vector3(-d.z / len, 0f, d.x / len) * (halfWidth * 0.93f);
            Vector3 center = (a + b) * 0.5f;
            return IsOpenWater(center + lateral) || IsOpenWater(center - lateral);
        }

        private bool IsOpenWater(Vector3 point)
        {
            return TryWaterAt(point, out _, out bool wet) && wet;
        }
    }
}
