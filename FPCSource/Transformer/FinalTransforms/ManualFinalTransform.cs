using FirstPersonCameraContinued.DataModels;
using Unity.Mathematics;

namespace FirstPersonCameraContinued.Transformer.FinalTransforms
{
    /// <summary>
    /// Handles applying manual movement to the rig from the model
    /// </summary>
    internal class ManualFinalTransform : IFinalCameraTransform
    {
        public float movementSpeed = 0.1f;
        public float runSpeed = 0.35f;
       
        // Manual camera height is always a WORLD coordinate. The FPC height
        // offset remains an input accumulator for R/F and is consumed only as
        // a delta. Following a vehicle uses a different final transform.
        private bool _heightOffsetInitialized;
        private float _lastAppliedHeightOffset;

        internal void ResetNavigationHandoff()
        {
            // Called when the camera switches to/from an entity follow target.
            // The next manual frame keeps its current world height.
            _heightOffsetInitialized = false;
            _lastAppliedHeightOffset = 0f;
        }

        /// <summary>
        /// Apply the transformation
        /// </summary>
        /// <param name="rig"></param>
        /// <param name="model"></param>
        public void Apply( VirtualCameraRig rig, CameraDataModel model )
        {
            if (Mod.FirstPersonModSettings != null)
            {
                movementSpeed = Mod.FirstPersonModSettings.MovementSpeed;
                runSpeed = Mod.FirstPersonModSettings.RunSpeed; 
            }

            var right = new float3( rig.RigTransform.right.x, 0f, rig.RigTransform.right.z );
            var forward = new float3( rig.RigTransform.forward.x, 0f, rig.RigTransform.forward.z );

            // Calculate the direction vector without applying the movement speed yet
            var direction = float3.zero;

            // Don't apply any transformations during transitions
            if ( !model.IsTransitioningIn && !model.IsTransitioningOut )
            {
                // Add to the direction based on the input axes
                if ( model.Movement.x > 0 )
                    direction += right;
                else if ( model.Movement.x < 0 )
                    direction -= right;

                if ( model.Movement.y > 0 )
                    direction += forward;
                else if ( model.Movement.y < 0 )
                    direction -= forward;
            }

            // Normalize the direction vector if there's movement to maintain a constant speed
            if ( math.length( direction ) > 0 )
                direction = math.normalize( direction );

            var position = model.Position;
            position += direction * ( model.IsSprinting ? runSpeed : movementSpeed );
            if (CS2RealWalkingG1Bridge.RWWalkState.Mode ==
                    CS2RealWalkingG1Bridge.RWTravelMode.Navigation)
            {
                // No terrain or water sampling anywhere in the navigation
                // height path. During initial surface acquisition, preserve the
                // existing world Y rather than dropping to the seabed.
                if (CS2RealWalkingG1Bridge.RWCameraHeight.TryGet(out float eyeY))
                    position.y = eyeY;
                // Synchronize the offset baseline without applying it while
                // surface navigation owns the absolute eye height. When R/F
                // switches to Free, its increment is then applied exactly once.
                _lastAppliedHeightOffset = model.HeightOffset;
                _heightOffsetInitialized = true;
            }
            else
            {
                // Free flight: horizontal input never changes world-space Y.
                // R/F changes HeightOffset by +/-1; apply that input *once*.
                // No terrain, sea level, riverbed or FPC baseline sampling.
                if (!_heightOffsetInitialized)
                {
                    _lastAppliedHeightOffset = model.HeightOffset;
                    _heightOffsetInitialized = true;
                }
                float manualHeightDelta = model.HeightOffset - _lastAppliedHeightOffset;
                if (math.isfinite(manualHeightDelta))
                    position.y += manualHeightDelta;
                _lastAppliedHeightOffset = model.HeightOffset;
            }
            model.Position = position;
        }
    }
}
