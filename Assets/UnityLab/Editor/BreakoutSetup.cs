using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityLab.EditorTools
{
    /// <summary>
    /// Step 1 of the Breakout generator: creates the layers, sprites and materials the built scene needs.
    /// Editor-only helper. Deleting the Editor folder after generating the scenes does not affect the game.
    /// </summary>
    public static class BreakoutSetup
    {
        public const string GeneratedFolder = "Assets/UnityLab/Generated";
        public const string SquareSpritePath = GeneratedFolder + "/BreakoutSquare.png";
        public const string CircleSpritePath = GeneratedFolder + "/BreakoutCircle.png";
        public const string BouncePath = GeneratedFolder + "/BreakoutBounce.physicsMaterial2D";
        public const string SpriteMaterialPath = GeneratedFolder + "/BreakoutSprite.mat";
        public const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        public const string BallLayer = "Ball";
        public const string PaddleLayer = "Paddle";
        public const string BrickLayer = "Brick";

        [MenuItem("Unity Lab/Breakout/1. Prepare project", priority = 0)]
        public static void Prepare()
        {
            EnsureFolder(GeneratedFolder);

            EnsureLayer(BallLayer);
            EnsureLayer(PaddleLayer);
            EnsureLayer(BrickLayer);

            EnsureSprite(SquareSpritePath, false);
            EnsureSprite(CircleSpritePath, true);
            EnsureBounceMaterial();
            EnsureSpriteMaterial();
            LowerBounceThreshold();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Last, because importing a package can trigger a reload that cuts this method short.
            bool importedTmp = EnsureTextMeshProResources();

            string message = importedTmp
                ? "Layers, sprites and materials are ready.\n\nTextMeshPro resources were imported. Wait for Unity to finish compiling, then run:\nUnity Lab > Breakout > 2. Build game"
                : "Layers, sprites and materials are ready.\n\nNow open the scene that has your background and run:\nUnity Lab > Breakout > 2. Build game";
            EditorUtility.DisplayDialog("Breakout setup", message, "OK");
        }

        public static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || path == "Assets" || AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void EnsureLayer(string layerName)
        {
            if (LayerMask.NameToLayer(layerName) >= 0)
            {
                return;
            }

            Object tagManagerAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            SerializedObject tagManager = new SerializedObject(tagManagerAsset);
            SerializedProperty layers = tagManager.FindProperty("layers");

            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(slot.stringValue))
                {
                    slot.stringValue = layerName;
                    tagManager.ApplyModifiedPropertiesWithoutUndo();
                    return;
                }
            }

            Debug.LogError("Breakout setup: no free user layer slot for '" + layerName + "'.");
        }

        private static void EnsureSprite(string path, bool circle)
        {
            if (File.Exists(path))
            {
                return;
            }

            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[size * size];
            float radius = size * 0.5f;
            float inner = (radius - 0.5f) * (radius - 0.5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool inside = true;
                    if (circle)
                    {
                        float dx = x + 0.5f - radius;
                        float dy = y + 0.5f - radius;
                        inside = dx * dx + dy * dy <= inner;
                    }

                    pixels[y * size + x] = new Color32(255, 255, 255, inside ? (byte)255 : (byte)0);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = size;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }

        private static void EnsureBounceMaterial()
        {
            if (AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(BouncePath) != null)
            {
                return;
            }

            PhysicsMaterial2D material = new PhysicsMaterial2D("BreakoutBounce")
            {
                bounciness = 1f,
                friction = 0f
            };
            AssetDatabase.CreateAsset(material, BouncePath);
        }

        private static void EnsureSpriteMaterial()
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(SpriteMaterialPath) != null)
            {
                return;
            }

            // Unlit so the gameplay pieces stay readable over a dark, 2D-lit background.
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            AssetDatabase.CreateAsset(new Material(shader), SpriteMaterialPath);
        }

        private static void LowerBounceThreshold()
        {
            // Default threshold (1) can swallow bounces on small playfields, leaving the ball stuck.
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/Physics2DSettings.asset");
            if (assets.Length == 0)
            {
                return;
            }

            SerializedObject serialized = new SerializedObject(assets[0]);
            SerializedProperty threshold = serialized.FindProperty("m_VelocityThreshold");
            if (threshold != null && threshold.floatValue > 0.11f)
            {
                threshold.floatValue = 0.1f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static bool EnsureTextMeshProResources()
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(TmpSettingsPath) != null)
            {
                return false;
            }

            string package = FindTmpEssentialsPackage();
            if (package == null)
            {
                Debug.LogWarning("Breakout setup: TMP Essential Resources package not found. Import it via Window > TextMeshPro > Import TMP Essential Resources.");
                return false;
            }

            AssetDatabase.ImportPackage(package, false);
            return true;
        }

        private static string FindTmpEssentialsPackage()
        {
            string[] roots = { "Library/PackageCache", "Packages" };
            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string directory in Directory.GetDirectories(root, "com.unity.ugui*"))
                {
                    string candidate = Path.Combine(directory, "Package Resources", "TMP Essential Resources.unitypackage");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }
    }
}
