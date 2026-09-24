using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityLab.Components;

namespace UnityLab.EditorTools
{
    /// <summary>
    /// Step 2 of the Breakout generator: builds the playable levels around whatever background the open scene has.
    /// Everything it creates is made of stock Unity components plus the Unity Lab package.
    /// </summary>
    public static class BreakoutBuilder
    {
        private const string RootName = "Breakout";
        private const string Level1Path = "Assets/Scenes/Breakout_Level1.unity";
        private const string Level2Path = "Assets/Scenes/Breakout_Level2.unity";
        private const int GameplaySortingOrder = 1000;

        // Demo setting: level 1 becomes 4 one-hit bricks so the jump to level 2 is quick to show.
        // Set to false to get the full 30-brick level the assignment asks for, then rebuild.
        private const bool EasyFirstLevel = false;

        private const string BloomProfilePath = BreakoutSetup.GeneratedFolder + "/BreakoutBloom.asset";
        private const string TrailMaterialPath = BreakoutSetup.GeneratedFolder + "/BreakoutTrail.mat";
        private const string MenuPath = "Assets/Scenes/Breakout_Menu.unity";

        private static readonly Color BallColor = new Color(0.6f, 0.95f, 1f);
        private static readonly Color BrickWeakColor = new Color(0.45f, 0.85f, 0.45f);
        private static readonly Color BrickMediumColor = new Color(0.98f, 0.78f, 0.30f);
        private static readonly Color BrickStrongColor = new Color(0.93f, 0.38f, 0.38f);

        [MenuItem("Unity Lab/Breakout/2. Build game (Level 1 + Level 2)", priority = 1)]
        public static void BuildGame()
        {
            if (!ValidateSetup())
            {
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Scene scene = EditorSceneManager.GetActiveScene();
            if (FindCamera(scene) == null)
            {
                EditorUtility.DisplayDialog("Breakout", "The open scene has no camera. Open the scene with your background and try again.", "OK");
                return;
            }

            BreakoutSetup.EnsureFolder("Assets/Scenes");

            BuildLevel(1);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), Level1Path);

            BuildLevel(2);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), Level2Path);

            BuildMenu();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), MenuPath);

            RegisterBuildScenes();
            EditorSceneManager.OpenScene(MenuPath);

            EditorUtility.DisplayDialog(
                "Breakout",
                "Done.\n\nBreakout_Menu, Breakout_Level1 and Breakout_Level2 were created in Assets/Scenes and added to Build Settings, with the menu first.\n\nControls: Left / Right arrows move the paddle, R restarts.",
                "OK");
        }

        private static bool ValidateSetup()
        {
            bool ready = LayerMask.NameToLayer(BreakoutSetup.BallLayer) >= 0
                         && LayerMask.NameToLayer(BreakoutSetup.PaddleLayer) >= 0
                         && LayerMask.NameToLayer(BreakoutSetup.BrickLayer) >= 0
                         && AssetDatabase.LoadAssetAtPath<Sprite>(BreakoutSetup.SquareSpritePath) != null
                         && AssetDatabase.LoadAssetAtPath<Sprite>(BreakoutSetup.CircleSpritePath) != null;

            if (!ready)
            {
                EditorUtility.DisplayDialog("Breakout", "Run 'Unity Lab > Breakout > 1. Prepare project' first.", "OK");
            }

            return ready;
        }

        private static void BuildLevel(int level)
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            RemoveExistingRoot(scene);

            Camera camera = FindCamera(scene);
            Sprite square = AssetDatabase.LoadAssetAtPath<Sprite>(BreakoutSetup.SquareSpritePath);
            Sprite circle = AssetDatabase.LoadAssetAtPath<Sprite>(BreakoutSetup.CircleSpritePath);
            Material spriteMaterial = AssetDatabase.LoadAssetAtPath<Material>(BreakoutSetup.SpriteMaterialPath);
            PhysicsMaterial2D bounce = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(BreakoutSetup.BouncePath);

            int ballLayer = LayerMask.NameToLayer(BreakoutSetup.BallLayer);
            int paddleLayer = LayerMask.NameToLayer(BreakoutSetup.PaddleLayer);
            int brickLayer = LayerMask.NameToLayer(BreakoutSetup.BrickLayer);

            float halfHeight = camera.orthographicSize;
            float aspect = camera.aspect;
            if (aspect < 1f || aspect > 2.5f)
            {
                aspect = 16f / 9f;
            }

            float halfWidth = halfHeight * aspect;
            Vector3 cameraPosition = camera.transform.position;
            float centerX = cameraPosition.x;
            float centerY = cameraPosition.y;
            float z = cameraPosition.z + 10f;

            float top = centerY + halfHeight;
            float bottom = centerY - halfHeight;
            float thickness = halfHeight * 0.25f;

            GameObject root = new GameObject(RootName);
            Transform parent = root.transform;

            // Bounds. Colliders only, parked just outside the view so the ball bounces exactly at the screen edge.
            CreateCollider("Wall Left", parent, new Vector3(centerX - halfWidth - thickness * 0.5f, centerY, z), new Vector2(thickness, halfHeight * 2f + thickness * 2f), false);
            CreateCollider("Wall Right", parent, new Vector3(centerX + halfWidth + thickness * 0.5f, centerY, z), new Vector2(thickness, halfHeight * 2f + thickness * 2f), false);
            CreateCollider("Wall Top", parent, new Vector3(centerX, top + thickness * 0.5f, z), new Vector2(halfWidth * 2f + thickness * 2f, thickness), false);

            GameObject failZone = CreateCollider("Fail Zone", parent, new Vector3(centerX, bottom - thickness * 0.5f, z), new Vector2(halfWidth * 2f, thickness), true);
            Obstacle failObstacle = failZone.AddComponent<Obstacle>();
            SetFields(failObstacle, ("targetLayer", 1 << ballLayer));

            // Paddle. Dynamic so the side walls stop it; Horizontal Move 2D overwrites the X velocity every step.
            float paddleWidth = halfWidth * 0.34f;
            float paddleHeight = halfHeight * 0.055f;
            float paddleY = bottom + halfHeight * 0.16f;
            GameObject paddle = CreateSprite("Paddle", parent, square, Color.white, new Vector3(centerX, paddleY, z), new Vector2(paddleWidth, paddleHeight), paddleLayer, spriteMaterial);
            Rigidbody2D paddleBody = paddle.AddComponent<Rigidbody2D>();
            paddleBody.gravityScale = 0f;
            paddleBody.mass = 100f;
            paddleBody.constraints = RigidbodyConstraints2D.FreezePositionY | RigidbodyConstraints2D.FreezeRotation;
            paddleBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            paddleBody.interpolation = RigidbodyInterpolation2D.Interpolate;
            paddle.AddComponent<BoxCollider2D>();
            HorizontalMove2D paddleMove = paddle.AddComponent<HorizontalMove2D>();
            SetFields(paddleMove, ("speed", halfWidth * 1.4f), ("inputScheme", (int)InputScheme.Arrows));

            // Ball. The root stays at scale 1 so the trail's world-space width is predictable;
            // the sprite lives on a scaled child.
            float ballSize = halfHeight * 0.07f;
            Vector3 ballPosition = new Vector3(centerX, paddleY + halfHeight * 0.12f, z);
            GameObject ball = new GameObject("Ball");
            ball.transform.SetParent(parent, false);
            ball.transform.position = ballPosition;
            ball.layer = ballLayer;

            CreateSprite("Ball Sprite", ball.transform, circle, BallColor, ballPosition, new Vector2(ballSize, ballSize), ballLayer, spriteMaterial);
            AddCometTrail(ball.transform, ballSize);

            Rigidbody2D ballBody = ball.AddComponent<Rigidbody2D>();
            ballBody.gravityScale = 0f;
            ballBody.linearDamping = 0f;
            ballBody.angularDamping = 0f;
            ballBody.constraints = RigidbodyConstraints2D.FreezeRotation;
            ballBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            ballBody.interpolation = RigidbodyInterpolation2D.Interpolate;
            CircleCollider2D ballCollider = ball.AddComponent<CircleCollider2D>();
            ballCollider.radius = ballSize * 0.5f;
            ballCollider.sharedMaterial = bounce;

            float ballSpeed = Mathf.Max(halfHeight * (level == 1 ? 1.15f : 1.4f), 2.5f);
            BallStarter2D starter = ball.AddComponent<BallStarter2D>();
            SetFields(starter,
                ("initialSpeed", ballSpeed),
                ("randomizeDirection", false),
                ("minimumHorizontalComponent", 0.55f),
                ("minimumVerticalComponent", 0.8f));

            DamageOnCollision ballDamage = ball.AddComponent<DamageOnCollision>();
            SetFields(ballDamage, ("damage", 1), ("targetLayer", 1 << brickLayer), ("destroySelfOnHit", false));

            BuildBricks(parent, square, spriteMaterial, brickLayer, level, centerX, top, halfWidth, halfHeight, z);
            AddBloom(parent, camera);

            // UI.
            GameObject canvasObject = new GameObject("UI Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(parent, false);
            canvasObject.layer = LayerMask.NameToLayer("UI");
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            TextMeshProUGUI scoreText = CreateText(canvasObject.transform, "Score Text", "Score: 0", 48f, TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -30f), new Vector2(600f, 70f));
            CreateText(canvasObject.transform, "Level Text", "Level " + level, 40f, TextAlignmentOptions.TopRight,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -30f), new Vector2(400f, 70f));

            GameObject resultPanel = new GameObject("Result Panel", typeof(Image));
            resultPanel.transform.SetParent(canvasObject.transform, false);
            resultPanel.layer = canvasObject.layer;
            RectTransform panelRect = resultPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            resultPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

            TextMeshProUGUI resultText = CreateText(resultPanel.transform, "Result Text", "Game over", 96f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1200f, 320f));
            CreateText(resultPanel.transform, "Hint Text", "Press R to restart", 42f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -160f), new Vector2(1200f, 90f));
            resultPanel.SetActive(false);

            // Managers.
            GameObject managers = new GameObject("Managers");
            managers.transform.SetParent(parent, false);

            ScoreManager scoreManager = managers.AddComponent<ScoreManager>();
            SetFields(scoreManager, ("scoreText", scoreText), ("scorePrefix", "Score: "), ("winScore", 0));

            TargetCounter targetCounter = managers.AddComponent<TargetCounter>();
            SetFields(targetCounter, ("targetLayer", 1 << brickLayer), ("targetTag", ""), ("winWhenZero", true));

            SceneLoader nextSceneLoader = null;
            if (level == 1)
            {
                GameObject loaderObject = new GameObject("Next Level Loader");
                loaderObject.transform.SetParent(parent, false);
                nextSceneLoader = loaderObject.AddComponent<SceneLoader>();
                SetFields(nextSceneLoader,
                    ("sceneName", Path.GetFileNameWithoutExtension(Level2Path)),
                    ("reloadCurrentScene", false),
                    ("delay", 0.4f),
                    ("loadOnTrigger", false));
            }

            GameController controller = managers.AddComponent<GameController>();
            SetFields(controller,
                ("winPanel", resultPanel),
                ("resultPanel", resultPanel),
                ("resultText", resultText),
                ("playerTransform", ball.transform),
                ("nextSceneLoader", nextSceneLoader));

            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void BuildMenu()
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            RemoveExistingRoot(scene);

            Camera camera = FindCamera(scene);
            GameObject root = new GameObject(RootName);
            AddBloom(root.transform, camera);

            GameObject canvasObject = new GameObject("Menu Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(root.transform, false);
            canvasObject.layer = LayerMask.NameToLayer("UI");
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            CreateText(canvasObject.transform, "Title", "BREAKOUT", 140f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 260f), new Vector2(1400f, 220f))
                .color = BallColor;

            Button playButton = CreateButton(canvasObject.transform, "Play Button", "PLAY", new Vector2(0f, 0f));
            Button exitButton = CreateButton(canvasObject.transform, "Exit Button", "EXIT", new Vector2(0f, -140f));

            GameObject loaderObject = new GameObject("Level 1 Loader");
            loaderObject.transform.SetParent(root.transform, false);
            SceneLoader loader = loaderObject.AddComponent<SceneLoader>();
            SetFields(loader,
                ("sceneName", Path.GetFileNameWithoutExtension(Level1Path)),
                ("reloadCurrentScene", false),
                ("delay", 0f),
                ("loadOnTrigger", false));
            UnityEventTools.AddVoidPersistentListener(playButton.onClick, loader.Load);

            MenuActions actions = root.AddComponent<MenuActions>();
            UnityEventTools.AddVoidPersistentListener(exitButton.onClick, actions.QuitGame);

            // UI clicks need an EventSystem; the gameplay scenes do not have buttons, so only the menu gets one.
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            eventSystem.transform.SetParent(root.transform, false);

            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 anchoredPosition)
        {
            GameObject go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(420f, 110f);
            rect.anchoredPosition = anchoredPosition;

            Image image = go.GetComponent<Image>();
            image.color = new Color(0.04f, 0.09f, 0.14f, 0.85f);

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.65f, 0.9f, 1f);
            colors.pressedColor = new Color(0.35f, 0.75f, 1f);
            button.colors = colors;

            CreateText(go.transform, "Label", label, 52f, TextAlignmentOptions.Center,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            return button;
        }

        private static void BuildBricks(Transform parent, Sprite square, Material material, int brickLayer, int level,
            float centerX, float top, float halfWidth, float halfHeight, float z)
        {
            GameObject bricksRoot = new GameObject("Bricks");
            bricksRoot.transform.SetParent(parent, false);

            bool easy = level == 1 && EasyFirstLevel;
            int columns = easy ? 4 : level == 1 ? 6 : 8;
            int rows = easy ? 1 : 5;
            int sizingColumns = level == 1 ? 6 : 8;

            float areaWidth = halfWidth * 2f * 0.84f;
            float stepX = areaWidth / sizingColumns;
            float stepY = halfHeight * 0.105f;
            float brickWidth = stepX * 0.88f;
            float brickHeight = stepY * 0.66f;
            float firstX = centerX - stepX * columns * 0.5f + stepX * 0.5f;
            float firstY = top - halfHeight * 0.2f;

            for (int row = 0; row < rows; row++)
            {
                int health;
                int points;
                Color color;
                GetBrickType(level, row, easy, out health, out points, out color);

                for (int column = 0; column < columns; column++)
                {
                    Vector3 position = new Vector3(firstX + stepX * column, firstY - stepY * row, z);
                    GameObject brick = CreateSprite("Brick " + (row + 1) + "-" + (column + 1), bricksRoot.transform, square, color,
                        position, new Vector2(brickWidth, brickHeight), brickLayer, material);
                    brick.AddComponent<BoxCollider2D>();

                    Health brickHealth = brick.AddComponent<Health>();
                    SetFields(brickHealth, ("maxHealth", health), ("destroyOnDeath", true), ("pointsOnDeath", points));
                }
            }
        }

        private static void AddCometTrail(Transform ball, float ballSize)
        {
            GameObject trailObject = new GameObject("Comet Trail");
            trailObject.transform.SetParent(ball, false);

            TrailRenderer trail = trailObject.AddComponent<TrailRenderer>();
            trail.time = 0.4f;
            trail.minVertexDistance = 0.01f;
            trail.autodestruct = false;
            trail.numCapVertices = 4;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.widthMultiplier = ballSize * 0.9f;
            trail.sortingOrder = GameplaySortingOrder - 1;
            trail.sharedMaterial = LoadOrCreateTrailMaterial();
            trail.receiveShadows = false;
            trail.shadowCastingMode = ShadowCastingMode.Off;

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(BallColor, 0f), new GradientColorKey(BallColor, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;
        }

        private static Material LoadOrCreateTrailMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(TrailMaterialPath);
            if (material != null)
            {
                return material;
            }

            // Sprites/Default renders the trail's vertex colours without needing a texture.
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            }

            material = new Material(shader);
            AssetDatabase.CreateAsset(material, TrailMaterialPath);
            AssetDatabase.SaveAssets();
            return material;
        }

        /// <summary>
        /// Bloom makes the bright gameplay pieces glow against the dark background, which is what the
        /// PaddleBall sample's ball material relies on. Its own material targets a 3D renderer, so it
        /// cannot be reused directly in this 2D project.
        /// </summary>
        private static void AddBloom(Transform parent, Camera camera)
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(BloomProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, BloomProfilePath);

                Bloom bloom = profile.Add<Bloom>(true);
                bloom.name = "Bloom";
                bloom.threshold.value = 0.75f;
                bloom.intensity.value = 1.6f;
                bloom.scatter.value = 0.75f;
                AssetDatabase.AddObjectToAsset(bloom, profile);

                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }

            GameObject volumeObject = new GameObject("Global Volume (Bloom)");
            volumeObject.transform.SetParent(parent, false);
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = profile;

            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        }

        private static void GetBrickType(int level, int row, bool easy, out int health, out int points, out Color color)
        {
            if (easy)
            {
                health = 1;
                points = 10;
                color = BrickWeakColor;
                return;
            }

            // Level 2 pushes the tougher rows further down, so the layout is harder to clear.
            int strongRows = level == 1 ? 1 : 2;
            int mediumRows = 2;

            if (row < strongRows)
            {
                health = 3;
                points = 30;
                color = BrickStrongColor;
            }
            else if (row < strongRows + mediumRows)
            {
                health = 2;
                points = 20;
                color = BrickMediumColor;
            }
            else
            {
                health = 1;
                points = 10;
                color = BrickWeakColor;
            }
        }

        private static GameObject CreateSprite(string name, Transform parent, Sprite sprite, Color color, Vector3 position,
            Vector2 size, int layer, Material material)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            go.layer = layer;

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = GameplaySortingOrder;
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }

            return go;
        }

        private static GameObject CreateCollider(string name, Transform parent, Vector3 position, Vector2 size, bool isTrigger)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;

            BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
            collider.size = size;
            collider.isTrigger = isTrigger;
            return go;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, string content, float fontSize,
            TextAlignmentOptions alignment, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;

            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return text;
        }

        private static void SetFields(Object target, params (string name, object value)[] fields)
        {
            SerializedObject serialized = new SerializedObject(target);
            foreach ((string name, object value) in fields)
            {
                SerializedProperty property = serialized.FindProperty(name);
                if (property == null)
                {
                    Debug.LogError("Breakout builder: '" + name + "' not found on " + target.GetType().Name);
                    continue;
                }

                switch (value)
                {
                    case null:
                        property.objectReferenceValue = null;
                        break;
                    case int number when property.propertyType == SerializedPropertyType.Enum:
                        property.enumValueIndex = number;
                        break;
                    case int number:
                        property.intValue = number;
                        break;
                    case float number:
                        property.floatValue = number;
                        break;
                    case bool flag:
                        property.boolValue = flag;
                        break;
                    case string text:
                        property.stringValue = text;
                        break;
                    case Object reference:
                        property.objectReferenceValue = reference;
                        break;
                    default:
                        Debug.LogError("Breakout builder: unsupported value for '" + name + "'.");
                        break;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RemoveExistingRoot(Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (go.name == RootName)
                {
                    Object.DestroyImmediate(go);
                }
            }
        }

        private static Camera FindCamera(Scene scene)
        {
            if (Camera.main != null)
            {
                return Camera.main;
            }

            foreach (GameObject go in scene.GetRootGameObjects())
            {
                Camera camera = go.GetComponentInChildren<Camera>(true);
                if (camera != null)
                {
                    return camera;
                }
            }

            return null;
        }

        private static void RegisterBuildScenes()
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(MenuPath, true),
                new EditorBuildSettingsScene(Level1Path, true),
                new EditorBuildSettingsScene(Level2Path, true)
            };

            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
            {
                if (existing.path != MenuPath && existing.path != Level1Path && existing.path != Level2Path)
                {
                    scenes.Add(existing);
                }
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
