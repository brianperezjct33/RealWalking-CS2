// SPDX-License-Identifier: MIT
// Real Walking: read-only transport motion sampling for safe E boarding.
// A stopped vehicle is eligible regardless of WHY it stopped (station, signal, congestion).
using System;
using System.Collections.Generic;
using Game.Common;
using Game.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace CS2RealWalkingG1Bridge
{
    internal static class RWTransitMotion
    {
        private struct Sample
        {
            internal float3 Position;
            internal quaternion Rotation;
            internal float Time;
            internal float StillSince; // -1 = no continuous stop observed yet
            internal bool Ready;
            internal bool WaterPlanarOnly;
        }

        // Rendered vehicle transforms, not route/Boarding flags. Tolerances absorb
        // floating-point/interpolation jitter; they are NOT permission to board a moving car.
        private const float MaxLinearSpeed = 0.12f;  // m/s (~0.43 km/h)
        private const float MaxAngularSpeed = 0.6f; // degrees per real second
        // A docked ferry or cruise ship can heave and pitch with the water. That is not
        // horizontal navigation; use planar drift + heading to distinguish sailing.
        // Road/rail/air vehicles retain the original 3D test.
        private const float WaterMaxHorizontalSpeed = 0.12f; // m/s
        private const float WaterMaxHeadingSpeed = 0.9f; // degrees per real second
        private const float WaterPlanarJitter = 0.012f; // m
        private const float WaterHeadingJitter = 0.08f; // degrees
        private const float MinObservedStop = 0.60f; // require several 3 Hz scans
        private const float MaxObservationGap = 1.0f;
        private const float MaxConfirmationAge = 0.65f;
        private static readonly Dictionary<Entity, Sample> Samples = new Dictionary<Entity, Sample>();
        private static float _nextCleanup;

        // Exit gating must not reuse Samples: RWTransitNearbySystem clears that
        // dictionary when first-person walking becomes a vehicle ride.
        private static Entity _rideBody = Entity.Null;
        private static Sample _rideSample;
        private static bool _hasRideSample;
        private const float RideSampleInterval = 0.15f;

        internal static void BeginRide(Entity body)
        {
            _rideBody = body;
            _hasRideSample = false;
            _rideSample = default(Sample);
            // No first-sighting exemption: continuous stillness must be observed.
            TickRide();
        }

        internal static void EndRide()
        {
            _rideBody = Entity.Null;
            _hasRideSample = false;
            _rideSample = default(Sample);
        }

        // Called every update while riding. The minimum sampling interval
        // avoids counting frame-to-frame interpolation noise as actual speed.
        internal static void TickRide()
        {
            if (_rideBody == Entity.Null) return;
            float now = Time.realtimeSinceStartup;
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !TryPose(world.EntityManager, _rideBody,
                    out float3 position, out quaternion rotation))
            {
                _hasRideSample = false;
                return;
            }
            if (!_hasRideSample)
            {
                _rideSample = NewSample(position, rotation, now, false);
                _hasRideSample = true;
                return;
            }
            float dt = now - _rideSample.Time;
            if (dt < RideSampleInterval) return;
            if (dt > MaxObservationGap)
            {
                _rideSample = NewSample(position, rotation, now, false);
                return;
            }
            bool still = WithinMovement(_rideSample, position, rotation, dt);
            float since = still ? (_rideSample.StillSince >= 0f ? _rideSample.StillSince : _rideSample.Time) : -1f;
            _rideSample = new Sample
            {
                Position = position, Rotation = rotation, Time = now,
                StillSince = since,
                Ready = still && now - since >= MinObservedStop,
                WaterPlanarOnly = false
            };
        }

        // Used by BOTH the E hint and both checks bracketing the exit camera
        // handoff. A recently departed bus loses eligibility immediately.
        internal static bool IsRideStoppedNow(Entity body)
        {
            if (body == Entity.Null || body != _rideBody || !_hasRideSample ||
                !_rideSample.Ready || !Application.isFocused) return false;
            float age = Time.realtimeSinceStartup - _rideSample.Time;
            if (age < 0f || age > MaxConfirmationAge) return false;
            World world = World.DefaultGameObjectInjectionWorld;
            return world != null && TryPose(world.EntityManager, body,
                out float3 position, out quaternion rotation) &&
                WithinMovement(_rideSample, position, rotation, Mathf.Max(0.05f, age));
        }

        internal static string RideDiagnostic()
        {
            if (!_hasRideSample) return "no-exit-motion-sample";
            return "rideReady=" + _rideSample.Ready +
                   " rideStillSince=" + _rideSample.StillSince.ToString("F2") +
                   " rideSampleAge=" + (Time.realtimeSinceStartup - _rideSample.Time).ToString("F2");
        }

        internal static void Clear()
        {
            Samples.Clear();
            _nextCleanup = 0f;
        }

        // Called for EACH nearby physical body, even if another vehicle is closer.
        // Never allow boarding on the first sighting: speed is not yet known.
        internal static bool Observe(Entity body, float3 position, quaternion rotation, float now, bool waterPlanarOnly)
        {
            if (body == Entity.Null || !math.all(math.isfinite(position))) return false;
            Cleanup(now);
            if (!Samples.TryGetValue(body, out Sample old))
            {
                Samples[body] = NewSample(position, rotation, now, waterPlanarOnly);
                return false;
            }
            // A change of classification must start a fresh observation window.
            if (old.WaterPlanarOnly != waterPlanarOnly)
            {
                Samples[body] = NewSample(position, rotation, now, waterPlanarOnly);
                return false;
            }
            float dt = now - old.Time;
            if (dt <= 0.1f)
                return old.Ready && WithinMovement(old, position, rotation, Mathf.Max(0.01f, dt));
            if (dt > MaxObservationGap)
            {
                Samples[body] = NewSample(position, rotation, now, waterPlanarOnly);
                return false;
            }

            bool still = WithinMovement(old, position, rotation, dt);
            float stillSince = still ? (old.StillSince >= 0f ? old.StillSince : old.Time) : -1f;
            bool ready = still && now - stillSince >= MinObservedStop;
            Samples[body] = new Sample
            {
                Position = position, Rotation = rotation, Time = now,
                StillSince = stillSince, Ready = ready, WaterPlanarOnly = waterPlanarOnly
            };
            return ready;
        }

        // Called both by the E prompt and immediately before switching FPC to Follow.
        // Re-read the live physical body's position to reject a vehicle that just started.
        internal static bool IsStoppedNow(Entity body, bool waterPlanarOnly)
        {
            if (body == Entity.Null || !Samples.TryGetValue(body, out Sample sample) ||
                !sample.Ready || sample.WaterPlanarOnly != waterPlanarOnly)
                return false;
            float elapsed = Time.realtimeSinceStartup - sample.Time;
            if (elapsed < 0f || elapsed > MaxConfirmationAge) return false;
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !TryPose(world.EntityManager, body, out float3 position, out quaternion rotation))
                return false;
            // The minimum position/angle allowance is only for render interpolation jitter;
            // actual observed speed must already have stayed below the low stop thresholds.
            float dt = Mathf.Max(0.05f, elapsed);
            return WithinMovement(sample, position, rotation, dt);
        }

        private static Sample NewSample(float3 p, quaternion r, float now, bool waterPlanarOnly)
        {
            return new Sample { Position = p, Rotation = r, Time = now, StillSince = -1f, Ready = false, WaterPlanarOnly = waterPlanarOnly };
        }

        private static bool WithinMovement(Sample old, float3 p, quaternion r, float dt)
        {
            if (!math.all(math.isfinite(p))) return false;
            if (old.WaterPlanarOnly)
            {
                // Ignore water-induced Y motion and roll/pitch: a berthed ferry or liner
                // should not lose its E prompt because its hull gently rocks.
                float2 horizontal = new float2(p.x - old.Position.x, p.z - old.Position.z);
                float headingChange = HeadingDegrees(old.Rotation, r);
                return math.length(horizontal) <= WaterMaxHorizontalSpeed * dt + WaterPlanarJitter &&
                       headingChange <= WaterMaxHeadingSpeed * dt + WaterHeadingJitter;
            }
            float linearDistance = math.distance(old.Position, p);
            float angularDistance = RotationDegrees(old.Rotation, r);
            return linearDistance <= MaxLinearSpeed * dt + 0.008f &&
                   angularDistance <= MaxAngularSpeed * dt + 0.06f;
        }

        // Only the XZ heading matters for a vessel resting on waves.
        private static float HeadingDegrees(quaternion a, quaternion b)
        {
            float3 forwardA = math.mul(a, new float3(0f, 0f, 1f));
            float3 forwardB = math.mul(b, new float3(0f, 0f, 1f));
            float angleA = math.atan2(forwardA.x, forwardA.z);
            float angleB = math.atan2(forwardB.x, forwardB.z);
            float delta = angleB - angleA;
            // Handles the -180/180 degree wrap correctly.
            return math.abs(math.atan2(math.sin(delta), math.cos(delta))) * (180f / math.PI);
        }

        private static float RotationDegrees(quaternion a, quaternion b)
        {
            float dot = math.clamp(math.abs(math.dot(a.value, b.value)), 0f, 1f);
            return 2f * math.acos(dot) * (180f / math.PI);
        }

        private static bool TryPose(EntityManager em, Entity e, out float3 p, out quaternion r)
        {
            p = default(float3);
            r = quaternion.identity;
            if (!em.Exists(e)) return false;
            if (em.HasComponent<InterpolatedTransform>(e))
            {
                var t = em.GetComponentData<InterpolatedTransform>(e);
                p = t.m_Position; r = t.m_Rotation;
            }
            else if (em.HasComponent<Game.Objects.Transform>(e))
            {
                var t = em.GetComponentData<Game.Objects.Transform>(e);
                p = t.m_Position; r = t.m_Rotation;
            }
            else return false;
            return math.all(math.isfinite(p));
        }

        internal static string WaterDiagnostic(Entity body, bool waterPlanarOnly)
        {
            if (body == Entity.Null || !Samples.TryGetValue(body, out Sample sample))
                return "no-motion-sample";
            float age = Time.realtimeSinceStartup - sample.Time;
            return "observed=" + (sample.StillSince >= 0f) +
                   " ready=" + sample.Ready +
                   " sampleAge=" + age.ToString("F2") +
                   " liveStopped=" + IsStoppedNow(body, waterPlanarOnly);
        }

        private static void Cleanup(float now)
        {
            if (now < _nextCleanup) return;
            _nextCleanup = now + 3f;
            List<Entity> stale = null;
            foreach (var pair in Samples)
            {
                if (now - pair.Value.Time <= 2f) continue;
                if (stale == null) stale = new List<Entity>();
                stale.Add(pair.Key);
            }
            if (stale != null)
                foreach (var e in stale) Samples.Remove(e);
        }
    }
}
