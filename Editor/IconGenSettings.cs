using System;
using UnityEngine;

namespace GalaxyGourd.IconGen
{
    public enum IconProjection
    {
        Orthographic,
        Perspective
    }

    public enum OrientationPreset
    {
        Front,
        Back,
        Left,
        Right,
        Top,
        Isometric,
        Custom
    }

    public enum LightingPreset
    {
        Studio,
        Soft,
        Dramatic,
        Flat,
        Custom
    }

    /// <summary>
    /// All knobs for a capture. Held in-memory by the window (persisted to EditorPrefs as JSON),
    /// and also savable as a project asset to reuse as a preset across item categories.
    /// </summary>
    public class IconGenSettings : ScriptableObject
    {
        // ---- Output resolution (grid-cell model for tetris-style inventories) ----
        [Min(1)] public int cellWidth = 1;
        [Min(1)] public int cellHeight = 1;
        [Min(8)] public int pixelsPerCell = 256;

        // Supersampling factor. Render at resolution * ssaa, then downsample for clean edges.
        [Range(1, 8)] public int ssaa = 4;

        // ---- Framing ----
        // Fractional margin added around the fitted bounds. 0 = tight, 0.1 = 10% breathing room.
        [Range(0f, 1f)] public float padding = 0.08f;
        public IconProjection projection = IconProjection.Orthographic;
        [Range(5f, 90f)] public float fieldOfView = 30f; // perspective only

        // ---- Orientation (applied to the spawned object; camera stays axis-aligned) ----
        public OrientationPreset orientationPreset = OrientationPreset.Isometric;
        public Vector3 customEuler = Vector3.zero;

        // ---- Background ----
        public bool transparent = true;
        public Color backgroundColor = new Color(0.15f, 0.15f, 0.17f, 1f); // used when transparent == false

        // ---- Lighting ----
        public LightingPreset lightingPreset = LightingPreset.Studio;

        public float keyPitch = 35f;
        public float keyYaw = -35f;
        public float keyIntensity = 1.4f;
        public Color keyColor = Color.white;

        public bool fillEnabled = true;
        public float fillIntensity = 0.5f;
        public Color fillColor = new Color(0.8f, 0.85f, 1f, 1f);

        public bool rimEnabled = true;
        public float rimIntensity = 0.8f;
        public Color rimColor = Color.white;

        public Color ambientColor = new Color(0.5f, 0.5f, 0.55f, 1f);
        [Range(0f, 2f)] public float ambientIntensity = 0.45f;

        // ---- Output destination ----
        public string outputDirectory = "Assets/Icons";
        public string fileName = ""; // empty => use prefab name
        public bool configureAsSprite = true;

        public int ResolutionWidth => Mathf.Max(1, cellWidth) * Mathf.Max(8, pixelsPerCell);
        public int ResolutionHeight => Mathf.Max(1, cellHeight) * Mathf.Max(8, pixelsPerCell);
        public float TargetAspect => (float)ResolutionWidth / ResolutionHeight;

        public Vector3 ResolveEuler()
        {
            switch (orientationPreset)
            {
                case OrientationPreset.Front: return new Vector3(0f, 0f, 0f);
                case OrientationPreset.Back: return new Vector3(0f, 180f, 0f);
                case OrientationPreset.Left: return new Vector3(0f, 90f, 0f);
                case OrientationPreset.Right: return new Vector3(0f, -90f, 0f);
                case OrientationPreset.Top: return new Vector3(90f, 0f, 0f);
                case OrientationPreset.Isometric: return new Vector3(30f, 45f, 0f);
                default: return customEuler;
            }
        }

        public void ApplyLightingPreset(LightingPreset preset)
        {
            lightingPreset = preset;
            switch (preset)
            {
                case LightingPreset.Studio:
                    keyPitch = 35f; keyYaw = -35f; keyIntensity = 1.4f; keyColor = Color.white;
                    fillEnabled = true; fillIntensity = 0.5f; fillColor = new Color(0.8f, 0.85f, 1f, 1f);
                    rimEnabled = true; rimIntensity = 0.8f; rimColor = Color.white;
                    ambientColor = new Color(0.5f, 0.5f, 0.55f, 1f); ambientIntensity = 0.45f;
                    break;
                case LightingPreset.Soft:
                    keyPitch = 45f; keyYaw = -20f; keyIntensity = 1.0f; keyColor = Color.white;
                    fillEnabled = true; fillIntensity = 0.75f; fillColor = Color.white;
                    rimEnabled = false; rimIntensity = 0f;
                    ambientColor = Color.white; ambientIntensity = 0.7f;
                    break;
                case LightingPreset.Dramatic:
                    keyPitch = 25f; keyYaw = -55f; keyIntensity = 1.9f; keyColor = new Color(1f, 0.97f, 0.9f, 1f);
                    fillEnabled = false; fillIntensity = 0f;
                    rimEnabled = true; rimIntensity = 1.2f; rimColor = new Color(0.7f, 0.8f, 1f, 1f);
                    ambientColor = new Color(0.2f, 0.2f, 0.28f, 1f); ambientIntensity = 0.2f;
                    break;
                case LightingPreset.Flat:
                    keyPitch = 90f; keyYaw = 0f; keyIntensity = 0.0f; keyColor = Color.white;
                    fillEnabled = false; fillIntensity = 0f;
                    rimEnabled = false; rimIntensity = 0f;
                    ambientColor = Color.white; ambientIntensity = 1.6f;
                    break;
                case LightingPreset.Custom:
                    // leave values as-is
                    break;
            }
        }

        public IconGenSettings Clone()
        {
            return Instantiate(this);
        }
    }
}
