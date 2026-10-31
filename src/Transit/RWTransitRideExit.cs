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
    internal static partial class RWTransitRide
    {
        internal static bool Disembark()
        {
            if(!Active)return false;
            // Road vehicles may have walkable sidewalks alongside them even
            // while traveling. Require the same observed, sustained full stop
            // used for boarding, independently of the landing geometry.
            if (RoadExitNeedsStop && !RWTransitMotion.IsRideStoppedNow(Body))
            {
                RWJournal.Warn("[RW-TRANSIT] EXIT-REJECT moving road vehicle " +
                    RWTransitMotion.RideDiagnostic());
                RWTransitHint.Notify(RWLocalization.T("rw.transit.exit_wait_stopped"));
                return false;
            }
            Vector3 eye;string reason;
            if(!FindLanding(out eye,out reason))
            {
                RWJournal.Warn("[RW-TRANSIT] EXIT-REJECT " + reason);
                RWTransitHint.Notify(RWLocalization.T(IsRail?"rw.transit.exit_no_platform":(IsWater?"rw.transit.exit_no_quay":"rw.transit.exit_no_path")));
                return false;
            }
            // The vehicle might have moved during the landing search.
            // Recheck immediately before mutating any FPC follow state.
            if (RoadExitNeedsStop && !RWTransitMotion.IsRideStoppedNow(Body))
            {
                RWJournal.Warn("[RW-TRANSIT] EXIT-REJECT road vehicle departed during landing search");
                RWTransitHint.Notify(RWLocalization.T("rw.transit.exit_wait_stopped"));
                return false;
            }
            if(!RWTransitFpc.TryDisembark(eye,out string fail))
            {
                RWJournal.Warn("[RW-TRANSIT] EXIT-ERROR " + fail);
                RWTransitHint.Notify(RWLocalization.T("rw.transit.exit_failed",fail));
                return false;
            }
            RWJournal.Warn("[RW-TRANSIT] EXIT-SUCCESS eye="+eye.ToString("F2")+" "+reason);
            End("E disembark");
            return true;
        }
    }

}
