using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace GalaxyGourd.IconGen
{
    /// <summary>
    /// Renders a prefab to a transparent Texture2D.
    ///
    /// Transparency is recovered with a two-pass black/white reconstruction rather than relying on
    /// URP to preserve the camera's alpha channel (it does not, post-blit). The object is rendered
    /// once on a black background and once on white; per pixel, (white - black) == (1 - alpha), which
    /// yields true straight-alpha coverage and clean edges regardless of pipeline version. Math is done
    /// in linear space when the project is linear, so anti-aliased edges don't pick up a dark halo.
    /// </summary>
    internal static class IconRenderer
    {
        private const int PreviewLongEdge = 320; // cap preview resolution so the window stays responsive
        private const int MaxSuperEdge = 8192;   // guard against absurd RT sizes

        public static Texture2D Render(IconGenSettings s, GameObject prefab, bool preview, out string error)
        {
            error = null;
            if (prefab == null)
            {
                error = "No prefab assigned.";
                return null;
            }

            // Resolve target + super-sampled dimensions.
            int targetW = s.ResolutionWidth;
            int targetH = s.ResolutionHeight;
            int ssaa = Mathf.Clamp(s.ssaa, 1, 8);

            if (preview)
            {
                float scale = (float)PreviewLongEdge / Mathf.Max(targetW, targetH);
                targetW = Mathf.Max(1, Mathf.RoundToInt(targetW * scale));
                targetH = Mathf.Max(1, Mathf.RoundToInt(targetH * scale));
                ssaa = Mathf.Min(ssaa, 2);
            }

            int superW = targetW * ssaa;
            int superH = targetH * ssaa;

            if (superW > MaxSuperEdge || superH > MaxSuperEdge)
            {
                error = $"Supersampled resolution {superW}x{superH} exceeds the {MaxSuperEdge}px limit. Lower SSAA or resolution.";
                return null;
            }

            Scene previewScene = default;
            bool sceneOpen = false;
            GameObject instance = null;
            GameObject camGO = null;
            RenderTexture rt = null;
            Texture2D readTex = null;

            var prevAmbientMode = RenderSettings.ambientMode;
            var prevAmbientLight = RenderSettings.ambientLight;
            float prevAmbientIntensity = RenderSettings.ambientIntensity;

            try
            {
                previewScene = EditorSceneManager.NewPreviewScene();
                sceneOpen = true;

                // ---- Spawn + orient ----
                instance = PrefabUtility.InstantiatePrefab(prefab, previewScene) as GameObject;
                if (instance == null)
                {
                    error = "Failed to instantiate prefab.";
                    return null;
                }
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.Euler(s.ResolveEuler());

                if (!BoundsUtility.TryGetRenderBounds(instance, out Bounds bounds))
                {
                    error = "Prefab has no active mesh/skinned renderers to capture.";
                    return null;
                }

                // ---- Lighting rig ----
                SetupLights(s, previewScene);

                // ---- Camera ----
                camGO = new GameObject("IconGen_Camera");
                SceneManager.MoveGameObjectToScene(camGO, previewScene);
                var cam = camGO.AddComponent<Camera>();
                cam.scene = previewScene;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.cullingMask = ~0;
                cam.allowMSAA = false;
                cam.useOcclusionCulling = false;
                cam.aspect = (float)targetW / targetH;

                var camData = cam.GetUniversalAdditionalCameraData();
                camData.renderPostProcessing = false; // post forces alpha to 1
                camData.antialiasing = AntialiasingMode.None; // we supersample instead
                camData.renderShadows = false;

                FrameCamera(cam, bounds, s);

                // ---- Render target ----
                rt = new RenderTexture(superW, superH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
                {
                    antiAliasing = 1
                };
                rt.Create();
                readTex = new Texture2D(superW, superH, TextureFormat.RGBA32, false, false);

                bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;

                // ---- Ambient (best-effort flat ambient; explicit lights do the heavy lifting) ----
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = s.ambientColor * s.ambientIntensity;
                RenderSettings.ambientIntensity = 1f;

                Color[] super;

                if (s.transparent)
                {
                    Color[] black = RenderRead(cam, rt, readTex, Color.black);
                    Color[] white = RenderRead(cam, rt, readTex, Color.white);
                    super = ReconstructAlpha(black, white, linear);
                }
                else
                {
                    var bg = s.backgroundColor;
                    bg.a = 1f;
                    Color[] opaque = RenderRead(cam, rt, readTex, bg);
                    super = new Color[opaque.Length];
                    for (int i = 0; i < opaque.Length; i++)
                    {
                        var c = opaque[i];
                        if (linear) c = ToLinear(c);
                        c.a = 1f;
                        super[i] = c;
                    }
                }

                Color[] target = Downsample(super, superW, superH, targetW, targetH, ssaa, linear);

                var result = new Texture2D(targetW, targetH, TextureFormat.RGBA32, false, false);
                result.SetPixels(target);
                result.Apply(false, false);
                return result;
            }
            catch (Exception e)
            {
                error = e.Message;
                Debug.LogException(e);
                return null;
            }
            finally
            {
                RenderSettings.ambientMode = prevAmbientMode;
                RenderSettings.ambientLight = prevAmbientLight;
                RenderSettings.ambientIntensity = prevAmbientIntensity;

                if (readTex != null) UnityEngine.Object.DestroyImmediate(readTex);
                if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                if (sceneOpen) EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        private static void FrameCamera(Camera cam, Bounds bounds, IconGenSettings s)
        {
            // Object is pre-rotated, so the camera always looks down world -Z at the front (+Z) face.
            Vector3 center = bounds.center;

            if (s.projection == IconProjection.Orthographic)
            {
                cam.orthographic = true;
                cam.orthographicSize = BoundsUtility.FitOrthoSize(bounds, cam.aspect, s.padding);
                float depth = bounds.size.z;
                float dist = depth + 2f;
                cam.transform.position = center + Vector3.forward * dist;
                cam.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = dist + depth + 10f;
            }
            else
            {
                cam.orthographic = false;
                cam.fieldOfView = s.fieldOfView;
                float dist = BoundsUtility.FitPerspectiveDistance(bounds, cam.aspect, s.fieldOfView, s.padding);
                cam.transform.position = center + Vector3.forward * dist;
                cam.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
                cam.nearClipPlane = Mathf.Max(0.01f, dist - bounds.size.z - 5f);
                cam.farClipPlane = dist + bounds.size.z + 10f;
            }
        }

        private static void SetupLights(IconGenSettings s, Scene scene)
        {
            CreateLight(scene, "IconGen_Key", LightType.Directional,
                Quaternion.Euler(s.keyPitch, s.keyYaw, 0f), s.keyColor, s.keyIntensity);

            if (s.fillEnabled)
            {
                CreateLight(scene, "IconGen_Fill", LightType.Directional,
                    Quaternion.Euler(s.keyPitch * 0.4f, s.keyYaw + 150f, 0f), s.fillColor, s.fillIntensity);
            }

            if (s.rimEnabled)
            {
                CreateLight(scene, "IconGen_Rim", LightType.Directional,
                    Quaternion.Euler(-15f, s.keyYaw + 180f, 0f), s.rimColor, s.rimIntensity);
            }
        }

        private static void CreateLight(Scene scene, string name, LightType type, Quaternion rot, Color color, float intensity)
        {
            if (intensity <= 0f) return;
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.rotation = rot;
            var light = go.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }

        private static Color[] RenderRead(Camera cam, RenderTexture rt, Texture2D readTex, Color bg)
        {
            cam.backgroundColor = bg;
            RenderCameraToTexture(cam, rt);

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            readTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            readTex.Apply(false);
            RenderTexture.active = prev;
            return readTex.GetPixels();
        }

        private static void RenderCameraToTexture(Camera cam, RenderTexture rt)
        {
            // Unity 2022.2+/Unity 6 SRP path. Falls back to legacy render if the request type is unsupported.
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                RenderPipeline.SubmitRenderRequest(cam, request);
            }
            else
            {
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;
            }
        }

        private static Color[] ReconstructAlpha(Color[] black, Color[] white, bool linear)
        {
            var outColors = new Color[black.Length];
            for (int i = 0; i < black.Length; i++)
            {
                Color a = black[i];
                Color b = white[i];
                if (linear) { a = ToLinear(a); b = ToLinear(b); }

                // For an opaque pixel the object occludes the background, so a == b => alpha 1.
                // Where background shows through, (b - a) == (1 - alpha) per channel.
                float diff = ((b.r - a.r) + (b.g - a.g) + (b.b - a.b)) / 3f;
                float alpha = Mathf.Clamp01(1f - diff);

                if (alpha > 1e-4f)
                {
                    float inv = 1f / alpha; // a is premultiplied (rendered on black); un-premultiply
                    outColors[i] = new Color(a.r * inv, a.g * inv, a.b * inv, alpha);
                }
                else
                {
                    outColors[i] = new Color(0f, 0f, 0f, 0f);
                }
            }
            return outColors;
        }

        private static Color[] Downsample(Color[] super, int sw, int sh, int tw, int th, int factor, bool linear)
        {
            var outColors = new Color[tw * th];

            for (int ty = 0; ty < th; ty++)
            {
                for (int tx = 0; tx < tw; tx++)
                {
                    float sumR = 0f, sumG = 0f, sumB = 0f, sumA = 0f;
                    int count = 0;

                    int sx0 = tx * factor;
                    int sy0 = ty * factor;
                    for (int dy = 0; dy < factor; dy++)
                    {
                        int sy = sy0 + dy;
                        if (sy >= sh) continue;
                        int rowBase = sy * sw;
                        for (int dx = 0; dx < factor; dx++)
                        {
                            int sx = sx0 + dx;
                            if (sx >= sw) continue;
                            Color p = super[rowBase + sx];
                            // alpha-weighted (premultiplied) accumulation avoids dark fringing
                            sumR += p.r * p.a;
                            sumG += p.g * p.a;
                            sumB += p.b * p.a;
                            sumA += p.a;
                            count++;
                        }
                    }

                    Color outC;
                    if (count > 0 && sumA > 1e-4f)
                    {
                        float invA = 1f / sumA;
                        float oa = sumA / count;
                        outC = new Color(sumR * invA, sumG * invA, sumB * invA, oa);
                    }
                    else
                    {
                        outC = new Color(0f, 0f, 0f, 0f);
                    }

                    if (linear) outC = ToGamma(outC);
                    outColors[ty * tw + tx] = outC;
                }
            }

            return outColors;
        }

        private static Color ToLinear(Color c)
        {
            return new Color(
                Mathf.GammaToLinearSpace(c.r),
                Mathf.GammaToLinearSpace(c.g),
                Mathf.GammaToLinearSpace(c.b),
                c.a);
        }

        private static Color ToGamma(Color c)
        {
            return new Color(
                Mathf.LinearToGammaSpace(c.r),
                Mathf.LinearToGammaSpace(c.g),
                Mathf.LinearToGammaSpace(c.b),
                c.a);
        }
    }
}
