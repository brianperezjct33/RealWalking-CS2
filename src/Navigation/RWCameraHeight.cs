// SPDX-License-Identifier: GPL-2.0-only
// RC13: one-way, world-space camera height bridge for verified pedestrian surfaces.
// FPC owns horizontal movement and camera rendering; RW owns height in Navigation.
// Neither terrain nor water is sampled by this bridge.  The generation token
// prevents a stale stair/platform height from leaking into a new walking session.
namespace CS2RealWalkingG1Bridge
{
    internal static class RWCameraHeight
    {
        private static bool _valid;
        private static float _eyeY;
        private static int _generation = -1;

        internal static void Clear()
        {
            _valid = false;
            _generation = -1;
        }

        internal static bool Publish(float worldEyeY)
        {
            if (RWWalkState.Mode != RWTravelMode.Navigation ||
                float.IsNaN(worldEyeY) || float.IsInfinity(worldEyeY))
                return false;
            _eyeY = worldEyeY;
            _generation = RWWalkState.Generation;
            _valid = true;
            return true;
        }

        internal static bool TryGet(out float worldEyeY)
        {
            worldEyeY = _eyeY;
            return _valid &&
                RWWalkState.Mode == RWTravelMode.Navigation &&
                _generation == RWWalkState.Generation;
        }
    }
}
