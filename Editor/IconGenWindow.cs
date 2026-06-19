using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace GalaxyGourd.IconGen
{
    public class IconGenWindow : EditorWindow
    {
        private const string PrefsKey = "GalaxyGourd.IconGen.Settings";
        private const string GridPrefsKey = "GalaxyGourd.IconGen.ShowGrid";

        private IconGenSettings _settings;
        private GameObject _prefab;

        private Texture2D _preview;
        private Texture2D _checker;
        private bool _previewDirty = true;
        private string _status;
        private MessageType _statusType = MessageType.Info;
        private Vector2 _scroll;
        private bool _showGrid = true; // preview-only inventory-cell overlay

        [MenuItem("Tools/Galaxy Gourd/Icon Generator")]
        public static void Open()
        {
            var w = GetWindow<IconGenWindow>("Icon Generator");
            w.minSize = new Vector2(360, 560);
        }

        private void OnEnable()
        {
            _settings = CreateInstance<IconGenSettings>();
            _settings.hideFlags = HideFlags.HideAndDontSave;
            string json = EditorPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { JsonUtility.FromJsonOverwrite(json, _settings); }
                catch { /* fall back to defaults */ }
            }
            _checker = BuildChecker();
            _showGrid = EditorPrefs.GetBool(GridPrefsKey, true);
            _previewDirty = true;
        }

        private void OnDisable()
        {
            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(_settings));
            EditorPrefs.SetBool(GridPrefsKey, _showGrid);
            CleanupPreview();
            if (_checker != null) DestroyImmediate(_checker);
            if (_settings != null) DestroyImmediate(_settings);
        }

        private void OnGUI()
        {
            if (_previewDirty)
                RefreshPreview();

            DrawPreviewArea();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUI.BeginChangeCheck();

            DrawSource();
            DrawOutputResolution();
            DrawFraming();
            DrawOrientation();
            DrawLighting();
            DrawBackground();
            DrawDestination();

            if (EditorGUI.EndChangeCheck())
            {
                _previewDirty = true;
                Repaint();
            }

            EditorGUILayout.EndScrollView();

            DrawActions();

            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, _statusType);
        }

        // ---------------------------------------------------------------- preview

        private void DrawPreviewArea()
        {
            const float h = 240f;
            Rect area = GUILayoutUtility.GetRect(position.width, h, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(area, new Color(0.13f, 0.13f, 0.13f));

            if (_preview == null)
            {
                var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
                GUI.Label(area, _prefab == null ? "Assign a prefab to preview" : "No preview", style);
                return;
            }

            // Fit the preview into the area preserving aspect.
            float texAspect = (float)_preview.width / _preview.height;
            float pad = 12f;
            Rect inner = new Rect(area.x + pad, area.y + pad, area.width - pad * 2, area.height - pad * 2);

            float w, hh;
            if (texAspect > inner.width / inner.height)
            {
                w = inner.width;
                hh = w / texAspect;
            }
            else
            {
                hh = inner.height;
                w = hh * texAspect;
            }
            Rect dst = new Rect(inner.x + (inner.width - w) * 0.5f, inner.y + (inner.height - hh) * 0.5f, w, hh);

            // Checkerboard so transparency is visible.
            GUI.DrawTextureWithTexCoords(dst, _checker, new Rect(0, 0, w / 16f, hh / 16f));
            GUI.DrawTexture(dst, _preview, ScaleMode.StretchToFill, true);

            // Inventory-cell grid overlay (only meaningful when the icon spans more than one cell).
            bool multiCell = _settings.cellWidth > 1 || _settings.cellHeight > 1;
            if (multiCell)
            {
                if (_showGrid)
                    DrawGridOverlay(dst, _settings.cellWidth, _settings.cellHeight);

                var toggleRect = new Rect(area.x + 6, area.y + 6, 72, 18);
                EditorGUI.DrawRect(toggleRect, new Color(0f, 0f, 0f, 0.4f));
                bool newGrid = GUI.Toggle(toggleRect, _showGrid, " Grid");
                if (newGrid != _showGrid)
                {
                    _showGrid = newGrid;
                    Repaint();
                }
            }

            var dimStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.LowerRight };
            GUI.Label(area, $"{_settings.ResolutionWidth} x {_settings.ResolutionHeight}", dimStyle);
        }

        private void RefreshPreview()
        {
            _previewDirty = false;
            CleanupPreview();
            if (_prefab == null) return;

            _preview = IconRenderer.Render(_settings, _prefab, preview: true, out string err);
            if (_preview == null && !string.IsNullOrEmpty(err))
                SetStatus(err, MessageType.Warning);
        }

        private void CleanupPreview()
        {
            if (_preview != null)
            {
                DestroyImmediate(_preview);
                _preview = null;
            }
        }

        // ---------------------------------------------------------------- sections

        private void DrawSource()
        {
            EditorGUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            _prefab = (GameObject)EditorGUILayout.ObjectField("Prefab", _prefab, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck())
                _previewDirty = true;
        }

        private void DrawOutputResolution()
        {
            Header("Output Resolution");
            bool square = _settings.cellWidth == _settings.cellHeight;
            bool newSquare = EditorGUILayout.ToggleLeft("Square (1:1)", square);
            if (newSquare && !square)
            {
                _settings.cellWidth = 1;
                _settings.cellHeight = 1;
            }

            EditorGUILayout.BeginHorizontal();
            _settings.cellWidth = Mathf.Max(1, EditorGUILayout.IntField("Grid Cells (W x H)", _settings.cellWidth));
            _settings.cellHeight = Mathf.Max(1, EditorGUILayout.IntField(_settings.cellHeight));
            EditorGUILayout.EndHorizontal();
            _settings.pixelsPerCell = Mathf.Max(8, EditorGUILayout.IntField("Pixels Per Cell", _settings.pixelsPerCell));
            _settings.ssaa = EditorGUILayout.IntPopup("Supersampling", _settings.ssaa,
                new[] { "Off (1x)", "2x", "4x", "8x" }, new[] { 1, 2, 4, 8 });

            EditorGUILayout.LabelField(" ", $"Output: {_settings.ResolutionWidth} x {_settings.ResolutionHeight} px", EditorStyles.miniLabel);
        }

        private void DrawFraming()
        {
            Header("Framing");
            _settings.padding = EditorGUILayout.Slider("Margin", _settings.padding, 0f, 1f);
            _settings.projection = (IconProjection)EditorGUILayout.EnumPopup("Projection", _settings.projection);
            if (_settings.projection == IconProjection.Perspective)
                _settings.fieldOfView = EditorGUILayout.Slider("Field of View", _settings.fieldOfView, 5f, 90f);
        }

        private void DrawOrientation()
        {
            Header("Orientation");
            _settings.orientationPreset = (OrientationPreset)EditorGUILayout.EnumPopup("Preset", _settings.orientationPreset);
            if (_settings.orientationPreset == OrientationPreset.Custom)
                _settings.customEuler = EditorGUILayout.Vector3Field("Euler", _settings.customEuler);
            else
                EditorGUILayout.LabelField(" ", $"Euler {_settings.ResolveEuler()}", EditorStyles.miniLabel);
        }

        private void DrawLighting()
        {
            Header("Lighting");
            EditorGUI.BeginChangeCheck();
            var preset = (LightingPreset)EditorGUILayout.EnumPopup("Preset", _settings.lightingPreset);
            if (EditorGUI.EndChangeCheck() && preset != LightingPreset.Custom)
                _settings.ApplyLightingPreset(preset);
            else
                _settings.lightingPreset = preset;

            EditorGUILayout.LabelField("Key Light", EditorStyles.boldLabel);
            _settings.keyPitch = EditorGUILayout.Slider("Pitch", _settings.keyPitch, -90f, 90f);
            _settings.keyYaw = EditorGUILayout.Slider("Yaw", _settings.keyYaw, -180f, 180f);
            _settings.keyIntensity = EditorGUILayout.Slider("Intensity", _settings.keyIntensity, 0f, 4f);
            _settings.keyColor = EditorGUILayout.ColorField("Color", _settings.keyColor);

            _settings.fillEnabled = EditorGUILayout.ToggleLeft("Fill Light", _settings.fillEnabled);
            if (_settings.fillEnabled)
            {
                EditorGUI.indentLevel++;
                _settings.fillIntensity = EditorGUILayout.Slider("Intensity", _settings.fillIntensity, 0f, 4f);
                _settings.fillColor = EditorGUILayout.ColorField("Color", _settings.fillColor);
                EditorGUI.indentLevel--;
            }

            _settings.rimEnabled = EditorGUILayout.ToggleLeft("Rim Light", _settings.rimEnabled);
            if (_settings.rimEnabled)
            {
                EditorGUI.indentLevel++;
                _settings.rimIntensity = EditorGUILayout.Slider("Intensity", _settings.rimIntensity, 0f, 4f);
                _settings.rimColor = EditorGUILayout.ColorField("Color", _settings.rimColor);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.LabelField("Ambient", EditorStyles.boldLabel);
            _settings.ambientColor = EditorGUILayout.ColorField("Color", _settings.ambientColor);
            _settings.ambientIntensity = EditorGUILayout.Slider("Intensity", _settings.ambientIntensity, 0f, 2f);
        }

        private void DrawBackground()
        {
            Header("Background");
            _settings.transparent = EditorGUILayout.ToggleLeft("Transparent", _settings.transparent);
            if (!_settings.transparent)
                _settings.backgroundColor = EditorGUILayout.ColorField("Color", _settings.backgroundColor);
        }

        private void DrawDestination()
        {
            Header("Output");
            EditorGUILayout.BeginHorizontal();
            _settings.outputDirectory = EditorGUILayout.TextField("Directory", _settings.outputDirectory);
            if (GUILayout.Button("...", GUILayout.Width(28)))
            {
                string picked = EditorUtility.OpenFolderPanel("Icon Output Directory", _settings.outputDirectory, "");
                if (!string.IsNullOrEmpty(picked))
                    _settings.outputDirectory = ProjectRelativeIfPossible(picked);
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            _settings.fileName = EditorGUILayout.TextField("File Name", _settings.fileName);
            EditorGUILayout.LabelField(" ", string.IsNullOrWhiteSpace(_settings.fileName)
                ? "(defaults to prefab name)" : _settings.fileName + ".png", EditorStyles.miniLabel);
            _settings.configureAsSprite = EditorGUILayout.ToggleLeft("Import as Sprite", _settings.configureAsSprite);
        }

        private void DrawActions()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUI.DisabledScope(_prefab == null))
            {
                if (GUILayout.Button("Capture & Save", GUILayout.Height(30)))
                    Capture();
            }

            int batchCount = CountSelectedPrefabs();
            using (new EditorGUI.DisabledScope(batchCount == 0))
            {
                if (GUILayout.Button($"Batch Capture Selection ({batchCount})"))
                    BatchCapture();
            }

            if (GUILayout.Button("Batch Capture Folder..."))
                BatchCaptureFolder();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Preset"))
                SavePreset();
            if (GUILayout.Button("Load Preset"))
                LoadPreset();
            EditorGUILayout.EndHorizontal();
        }

        // ---------------------------------------------------------------- actions

        private void Capture()
        {
            var tex = IconRenderer.Render(_settings, _prefab, preview: false, out string err);
            if (tex == null)
            {
                SetStatus(err ?? "Capture failed.", MessageType.Error);
                return;
            }
            string path = IconOutput.Save(tex, _settings, _prefab, out string saveErr);
            DestroyImmediate(tex);
            if (path == null)
            {
                SetStatus(saveErr ?? "Save failed.", MessageType.Error);
                return;
            }

            SetStatus($"Saved: {path}", MessageType.Info);
            var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (asset != null) EditorGUIUtility.PingObject(asset);
        }

        private void BatchCapture()
        {
            var prefabs = GetSelectedPrefabs();
            RunBatch(prefabs);
        }

        private void BatchCaptureFolder()
        {
            string picked = EditorUtility.OpenFolderPanel("Batch Capture Folder", _settings.outputDirectory, "");
            if (string.IsNullOrEmpty(picked)) return;

            string rel = ProjectRelativeIfPossible(picked);
            if (!rel.StartsWith("Assets"))
            {
                SetStatus("Folder must be inside the project's Assets directory.", MessageType.Warning);
                return;
            }

            var list = new List<GameObject>();
            CollectPrefabsInFolder(rel, list, new HashSet<string>());
            if (list.Count == 0)
            {
                SetStatus($"No prefabs found under {rel}.", MessageType.Warning);
                return;
            }
            RunBatch(list);
        }

        private void RunBatch(List<GameObject> prefabs)
        {
            if (prefabs == null || prefabs.Count == 0)
            {
                SetStatus("Nothing to batch.", MessageType.Warning);
                return;
            }

            int ok = 0;
            bool cancelled = false;
            try
            {
                for (int i = 0; i < prefabs.Count; i++)
                {
                    var p = prefabs[i];
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Icon Generator", $"Capturing {p.name} ({i + 1}/{prefabs.Count})", (float)i / prefabs.Count))
                    {
                        cancelled = true;
                        break;
                    }

                    var tex = IconRenderer.Render(_settings, p, preview: false, out _);
                    if (tex == null) continue;

                    // Force prefab name in batch so a fixed File Name doesn't collide across items.
                    string savedName = _settings.fileName;
                    _settings.fileName = "";
                    string path = IconOutput.Save(tex, _settings, p, out _);
                    _settings.fileName = savedName;
                    DestroyImmediate(tex);
                    if (path != null) ok++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            string verb = cancelled ? "Batch cancelled" : "Batch complete";
            SetStatus($"{verb}: {ok}/{prefabs.Count} icons saved to {_settings.outputDirectory}", MessageType.Info);
        }

        private void SavePreset()
        {
            string path = EditorUtility.SaveFilePanelInProject("Save Icon Preset", "IconPreset", "asset",
                "Save current settings as a reusable preset asset.");
            if (string.IsNullOrEmpty(path)) return;
            var asset = _settings.Clone();
            asset.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            SetStatus($"Preset saved: {path}", MessageType.Info);
        }

        private void LoadPreset()
        {
            string path = EditorUtility.OpenFilePanel("Load Icon Preset", Application.dataPath, "asset");
            if (string.IsNullOrEmpty(path)) return;
            string rel = ProjectRelativeIfPossible(path);
            var asset = AssetDatabase.LoadAssetAtPath<IconGenSettings>(rel);
            if (asset == null)
            {
                SetStatus("Selected asset is not an IconGenSettings preset.", MessageType.Warning);
                return;
            }
            string json = JsonUtility.ToJson(asset);
            JsonUtility.FromJsonOverwrite(json, _settings);
            _previewDirty = true;
            SetStatus($"Loaded preset: {rel}", MessageType.Info);
        }

        // ---------------------------------------------------------------- helpers

        private static int CountSelectedPrefabs() => GetSelectedPrefabs().Count;

        /// <summary>
        /// Resolves the current Project selection to prefab assets. Directly-selected prefabs (and
        /// model assets) are included; selected folders are expanded recursively to the .prefab assets
        /// they contain. Results are de-duplicated.
        /// </summary>
        private static List<GameObject> GetSelectedPrefabs()
        {
            var list = new List<GameObject>();
            var seen = new HashSet<string>();

            foreach (var obj in Selection.GetFiltered<Object>(SelectionMode.Assets))
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;

                if (AssetDatabase.IsValidFolder(path))
                    CollectPrefabsInFolder(path, list, seen);
                else
                    TryAddSelectedAsset(obj, path, list, seen);
            }
            return list;
        }

        private static void CollectPrefabsInFolder(string folder, List<GameObject> list, HashSet<string> seen)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!seen.Add(path)) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null) list.Add(go);
            }
        }

        private static void TryAddSelectedAsset(Object obj, string path, List<GameObject> list, HashSet<string> seen)
        {
            if (!(obj is GameObject go)) return;
            if (PrefabUtility.GetPrefabAssetType(go) == PrefabAssetType.NotAPrefab) return;
            if (seen.Add(path)) list.Add(go);
        }

        private static string ProjectRelativeIfPossible(string absolute)
        {
            absolute = absolute.Replace('\\', '/');
            string data = Application.dataPath.Replace('\\', '/');
            string root = data.Substring(0, data.Length - "Assets".Length);
            return absolute.StartsWith(root) ? absolute.Substring(root.Length) : absolute;
        }

        private void SetStatus(string msg, MessageType type)
        {
            _status = msg;
            _statusType = type;
            Repaint();
        }

        private static void Header(string label)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }

        private static void DrawGridOverlay(Rect r, int cols, int rows)
        {
            Color lineColor = new Color(1f, 1f, 1f, 0.28f);
            Color borderColor = new Color(0.45f, 0.85f, 1f, 0.75f);
            const float t = 1f;   // interior line thickness
            const float b = 1.5f; // footprint border thickness

            for (int i = 1; i < cols; i++)
            {
                float x = r.x + r.width * i / cols;
                EditorGUI.DrawRect(new Rect(x - t * 0.5f, r.y, t, r.height), lineColor);
            }
            for (int j = 1; j < rows; j++)
            {
                float y = r.y + r.height * j / rows;
                EditorGUI.DrawRect(new Rect(r.x, y - t * 0.5f, r.width, t), lineColor);
            }

            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, b), borderColor);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - b, r.width, b), borderColor);
            EditorGUI.DrawRect(new Rect(r.x, r.y, b, r.height), borderColor);
            EditorGUI.DrawRect(new Rect(r.xMax - b, r.y, b, r.height), borderColor);
        }

        private static Texture2D BuildChecker()
        {
            const int n = 16;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            Color a = new Color(0.32f, 0.32f, 0.32f);
            Color b = new Color(0.22f, 0.22f, 0.22f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    t.SetPixel(x, y, ((x / (n / 2) + y / (n / 2)) % 2 == 0) ? a : b);
            t.Apply();
            return t;
        }
    }
}
