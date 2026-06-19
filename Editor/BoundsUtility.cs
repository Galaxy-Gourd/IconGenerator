using UnityEngine;

namespace GalaxyGourd.IconGen
{
    internal static class BoundsUtility
    {
        /// <summary>
        /// World-space AABB encompassing every renderer under the root. Returns false if there are none.
        /// SkinnedMeshRenderer and MeshRenderer bounds are both world-space, so this works post-rotation.
        /// </summary>
        public static bool TryGetRenderBounds(GameObject root, out Bounds bounds)
        {
            bounds = new Bounds();
            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: false);
            bool found = false;

            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                // Particle/trail renderers report unstable or empty bounds; skip them.
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                    continue;
                if (!r.enabled)
                    continue;

                if (!found)
                {
                    bounds = r.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            return found;
        }

        /// <summary>
        /// Orthographic half-height that fits the given bounds into the target aspect, with padding.
        /// </summary>
        public static float FitOrthoSize(Bounds bounds, float targetAspect, float padding)
        {
            float w = Mathf.Max(bounds.size.x, 1e-5f);
            float h = Mathf.Max(bounds.size.y, 1e-5f);
            float objAspect = w / h;

            float halfHeight = objAspect > targetAspect
                ? (w / targetAspect) * 0.5f
                : h * 0.5f;

            return halfHeight * (1f + padding);
        }

        /// <summary>
        /// Perspective camera distance (from bounds center along view axis) needed to fit the bounds.
        /// </summary>
        public static float FitPerspectiveDistance(Bounds bounds, float targetAspect, float verticalFovDeg, float padding)
        {
            float w = Mathf.Max(bounds.size.x, 1e-5f);
            float h = Mathf.Max(bounds.size.y, 1e-5f);
            float objAspect = w / h;

            float halfHeight = objAspect > targetAspect
                ? (w / targetAspect) * 0.5f
                : h * 0.5f;
            halfHeight *= (1f + padding);

            float halfFovRad = verticalFovDeg * 0.5f * Mathf.Deg2Rad;
            float dist = halfHeight / Mathf.Tan(halfFovRad);
            return dist + bounds.size.z * 0.5f; // pull back to clear the nearest face
        }
    }
}
