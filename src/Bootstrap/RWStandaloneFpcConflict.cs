// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Reflection;

namespace CS2RealWalkingG1Bridge
{
    internal static class RWStandaloneFpcConflict
    {
        internal static bool Blocked { get; private set; }
        private static bool _warned;

        internal static bool Check()
        {
            if (Blocked) return true;
            Assembly embedded = typeof(FirstPersonCameraContinued.Mod).Assembly;
            foreach (Assembly candidate in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (ReferenceEquals(candidate, embedded)) continue;
                try
                {
                    string name = candidate.GetName().Name;
                    if (!string.Equals(name, "FirstPersonCameraContinued", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(name, "FirstPersonCamera", StringComparison.OrdinalIgnoreCase)
                        && candidate.GetType("FirstPersonCameraContinued.Mod", false) == null)
                        continue;

                    Blocked = true;
                    if (!_warned)
                    {
                        _warned = true;
                        FirstPersonCameraContinued.Mod.log.Error(
                            "[Real Walking Standalone] " + RWLocalization.T("rw.fpc.conflict"));
                    }
                    return true;
                }
                catch (Exception) { /* Dynamic assemblies may not provide normal metadata. */ }
            }
            return false;
        }
    }
}
