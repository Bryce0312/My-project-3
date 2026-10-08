using System.IO;
using System.Linq;
using LivingRoom30;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LivingRoom30.Editor
{
    [InitializeOnLoad]
    public static class SceneNewBuilder
    {
        private const string ScenePath = "Assets/Scenes/SceneNew.unity";
        private const string MaterialFolder = "Assets/LivingRoom30/Materials";
        private const string XRReviewRigPrefabPath = "Assets/LivingRoom30/Prefabs/XR_ReviewRig.prefab";
        private const string XRDeviceSimulatorPrefabPath = "Assets/Samples/XR Interaction Toolkit/2.3.2/XR Device Simulator/XR Device Simulator.prefab";

        private static Material wallMaterial;
        private static Material floorMaterial;
        private static Material fabricBeigeMaterial;
        private static Material fabricGreenMaterial;
        private static Material walnutMaterial;
        private static Material metalBlackMaterial;
        private static Material screenMaterial;
        private static Material glassMaterial;
        private static Material rugMaterial;
        private static Material curtainMaterial;
        private static Material ceramicMaterial;
        private static Material leafMaterial;
        private static Material accentMaterial;

        static SceneNewBuilder()
        {
            EditorApplication.delayCall += BuildAutomaticallyIfNeeded;
        }

        public static void BuildAutomaticallyIfNeeded()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || File.Exists(ScenePath))
            {
                return;
            }

            BuildSceneNew();
        }

        [MenuItem("Tools/SceneNew/Rebuild 30sqm Living Room")]
        public static void RebuildSceneNew()
        {
            if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog(
                    "Rebuild SceneNew",
                    "Replace the generated SceneNew living room? Existing project scenes will not be changed.",
                    "Rebuild",
                    "Cancel"))
            {
                return;
            }

            BuildSceneNew();
        }

        public static void RebuildSceneNewBatch()
        {
            BuildSceneNew();
        }

        public static void RenderSceneNewPreview()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Camera previewCamera = Object.FindObjectsOfType<Camera>().FirstOrDefault(item => item.name == "S02_PreviewCamera");
            if (previewCamera == null)
            {
                throw new MissingReferenceException("S02_PreviewCamera was not found in SceneNew.");
            }

            const int width = 1280;
            const int height = 720;
            RenderTexture renderTexture = new RenderTexture(width, height, 24);
            Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;

            previewCamera.targetTexture = renderTexture;
            previewCamera.Render();
            RenderTexture.active = renderTexture;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();

            string previewPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../SceneNewPreview.png"));
            File.WriteAllBytes(previewPath, image.EncodeToPNG());

            previewCamera.targetTexture = null;
            RenderTexture.active = previous;
            Object.DestroyImmediate(renderTexture);
            Object.DestroyImmediate(image);
            Debug.Log("SceneNew preview rendered to: " + previewPath);
        }

        private static void BuildSceneNew()
        {
            EnsureFolders();
            CreateMaterials();

            Scene previousScene = SceneManager.GetActiveScene();
            bool buildAdditively = previousScene.IsValid() && !string.IsNullOrEmpty(previousScene.path);
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                buildAdditively ? NewSceneMode.Additive : NewSceneMode.Single);
            scene.name = "SceneNew";
            SceneManager.SetActiveScene(scene);

            GameObject sceneRoot = CreateRoot("SceneNew_40sqm_LivingRoom");
            Transform architecture = CreateGroup("A00_Architecture", sceneRoot.transform);
            Transform furniture = CreateGroup("F00_Furniture", sceneRoot.transform);
            Transform decoration = CreateGroup("D00_Decoration", sceneRoot.transform);
            Transform lighting = CreateGroup("L00_Lighting", sceneRoot.transform);
            Transform system = CreateGroup("S00_SceneSystem", sceneRoot.transform);

            BuildArchitecture(architecture);
            BuildFurniture(furniture);
            BuildDecoration(decoration);
            BuildLighting(lighting);
            BuildSceneSystem(system);

            SetLayerRecursively(architecture.gameObject, "Environment");
            SetLayerRecursively(furniture.gameObject, "Furniture");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings();

            if (buildAdditively && previousScene.IsValid() && previousScene.isLoaded)
            {
                SceneManager.SetActiveScene(previousScene);
                EditorSceneManager.CloseScene(scene, true);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("SceneNew generated: 40 sqm living room with surface-matched furniture colliders and reusable XR rig.");
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "LivingRoom30");
            EnsureFolder("Assets/LivingRoom30", "Materials");
            EnsureFolder("Assets/LivingRoom30", "Prefabs");
            EnsureFolder("Assets/LivingRoom30", "Scripts");
            EnsureFolder("Assets/LivingRoom30", "Editor");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static void CreateMaterials()
        {
            wallMaterial = GetOrCreateMaterial("M01_Wall_WarmWhite", new Color(0.91f, 0.88f, 0.81f), 0f, 0.12f);
            floorMaterial = GetOrCreateMaterial("M02_Floor_LightOak", new Color(0.62f, 0.42f, 0.24f), 0f, 0.22f);
            fabricBeigeMaterial = GetOrCreateMaterial("M03_Fabric_WarmBeige", new Color(0.67f, 0.55f, 0.43f), 0f, 0.08f);
            fabricGreenMaterial = GetOrCreateMaterial("M04_Fabric_ForestGreen", new Color(0.10f, 0.27f, 0.21f), 0f, 0.10f);
            walnutMaterial = GetOrCreateMaterial("M05_Wood_Walnut", new Color(0.29f, 0.14f, 0.07f), 0f, 0.24f);
            metalBlackMaterial = GetOrCreateMaterial("M06_Metal_MatteBlack", new Color(0.035f, 0.04f, 0.045f), 0.65f, 0.32f);
            screenMaterial = GetOrCreateMaterial("M07_TVScreen_Black", new Color(0.008f, 0.012f, 0.016f), 0.15f, 0.78f);
            glassMaterial = GetOrCreateMaterial("M08_Glass_Clear", new Color(0.56f, 0.72f, 0.76f, 0.30f), 0f, 0.82f, true);
            rugMaterial = GetOrCreateMaterial("M09_Rug_Cream", new Color(0.78f, 0.70f, 0.58f), 0f, 0.04f);
            curtainMaterial = GetOrCreateMaterial("M10_Curtain_OffWhite", new Color(0.88f, 0.84f, 0.75f), 0f, 0.06f);
            ceramicMaterial = GetOrCreateMaterial("M11_Ceramic_WarmGray", new Color(0.38f, 0.34f, 0.30f), 0f, 0.42f);
            leafMaterial = GetOrCreateMaterial("M12_Leaf_Green", new Color(0.09f, 0.32f, 0.13f), 0f, 0.12f);
            accentMaterial = GetOrCreateMaterial("M13_Accent_Terracotta", new Color(0.58f, 0.20f, 0.09f), 0f, 0.18f);
        }

        private static Material GetOrCreateMaterial(string name, Color color, float metallic, float smoothness, bool transparent = false)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);

            if (transparent && material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3f);
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = (int)RenderQueue.Transparent;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BuildArchitecture(Transform parent)
        {
            CreateBox("A01_Floor", parent, new Vector3(0f, -0.05f, 0f), new Vector3(7.2f, 0.1f, 5.6f), floorMaterial);
            CreateBox("A02_Ceiling", parent, new Vector3(0f, 2.75f, 0f), new Vector3(7.5f, 0.1f, 5.9f), wallMaterial);
            CreateBox("A03_Wall_North", parent, new Vector3(0f, 1.35f, 2.875f), new Vector3(7.5f, 2.7f, 0.15f), wallMaterial);

            Transform south = CreateGroup("A04_Wall_South", parent);
            CreateBox("A04a_Wall_South_Left", south, new Vector3(-3.40f, 1.35f, -2.875f), new Vector3(0.40f, 2.7f, 0.15f), wallMaterial);
            CreateBox("A04b_Wall_South_Right", south, new Vector3(0.65f, 1.35f, -2.875f), new Vector3(5.90f, 2.7f, 0.15f), wallMaterial);
            CreateBox("A04c_Wall_South_Header", south, new Vector3(-2.75f, 2.4f, -2.875f), new Vector3(0.9f, 0.6f, 0.15f), wallMaterial);

            Transform east = CreateGroup("A05_Wall_East", parent);
            CreateBox("A05a_Wall_East_South", east, new Vector3(3.675f, 1.35f, -2.50f), new Vector3(0.15f, 2.7f, 0.60f), wallMaterial);
            CreateBox("A05b_Wall_East_North", east, new Vector3(3.675f, 1.35f, 0.75f), new Vector3(0.15f, 2.7f, 4.10f), wallMaterial);
            CreateBox("A05c_Wall_East_Header", east, new Vector3(3.675f, 2.4f, -1.75f), new Vector3(0.15f, 0.6f, 0.9f), wallMaterial);

            Transform west = CreateGroup("A06_Wall_West", parent);
            CreateBox("A06a_Wall_West_South", west, new Vector3(-3.675f, 1.35f, -1.775f), new Vector3(0.15f, 2.7f, 2.05f), wallMaterial);
            CreateBox("A06b_Wall_West_North", west, new Vector3(-3.675f, 1.35f, 2.325f), new Vector3(0.15f, 2.7f, 0.95f), wallMaterial);
            CreateBox("A06c_Wall_West_Sill", west, new Vector3(-3.675f, 0.375f, 0.55f), new Vector3(0.15f, 0.75f, 2.6f), wallMaterial);
            CreateBox("A06d_Wall_West_Header", west, new Vector3(-3.675f, 2.475f, 0.55f), new Vector3(0.15f, 0.45f, 2.6f), wallMaterial);

            BuildEntryDoor(parent);
            BuildInteriorDoor(parent);
            BuildWindow(parent);
        }

        private static void BuildEntryDoor(Transform parent)
        {
            Transform door = CreateGroup("A07_Door_Entry", parent);
            door.localPosition = new Vector3(-3.20f, 0f, -2.80f);
            door.localRotation = Quaternion.Euler(0f, -72f, 0f);
            CreateBox("A07a_DoorPanel", door, new Vector3(0.45f, 1.05f, 0f), new Vector3(0.9f, 2.1f, 0.045f), walnutMaterial);
            CreateCylinder("A07b_DoorHandle", door, new Vector3(0.80f, 1.05f, -0.055f), new Vector3(0.035f, 0.05f, 0.035f), metalBlackMaterial, Quaternion.Euler(90f, 0f, 0f));
        }

        private static void BuildInteriorDoor(Transform parent)
        {
            Transform door = CreateGroup("A08_Door_Interior", parent);
            door.localPosition = new Vector3(3.60f, 0f, -2.20f);
            door.localRotation = Quaternion.Euler(0f, 58f, 0f);
            CreateBox("A08a_DoorPanel", door, new Vector3(-0.425f, 1.05f, 0f), new Vector3(0.85f, 2.1f, 0.045f), wallMaterial);
            CreateCylinder("A08b_DoorHandle", door, new Vector3(-0.75f, 1.05f, -0.055f), new Vector3(0.035f, 0.05f, 0.035f), metalBlackMaterial, Quaternion.Euler(90f, 0f, 0f));
        }

        private static void BuildWindow(Transform parent)
        {
            Transform window = CreateGroup("A09_Window_Main", parent);
            CreateBox("A09a_WindowGlass", window, new Vector3(-3.62f, 1.5f, 0.55f), new Vector3(0.035f, 1.5f, 2.6f), glassMaterial, false);
            CreateBox("A09b_Frame_Bottom", window, new Vector3(-3.60f, 0.75f, 0.55f), new Vector3(0.09f, 0.08f, 2.7f), wallMaterial);
            CreateBox("A09c_Frame_Top", window, new Vector3(-3.60f, 2.25f, 0.55f), new Vector3(0.09f, 0.08f, 2.7f), wallMaterial);
            CreateBox("A09d_Frame_Centre", window, new Vector3(-3.60f, 1.5f, 0.55f), new Vector3(0.09f, 1.5f, 0.06f), wallMaterial);
        }

        private static void BuildFurniture(Transform parent)
        {
            BuildSofa(parent);
            BuildArmchair(parent, "F02", "Armchair Left", "F02_Armchair_Left", new Vector3(-2.20f, 0f, 0.28f), 28f);
            BuildArmchair(parent, "F03", "Armchair Right", "F03_Armchair_Right", new Vector3(2.20f, 0f, 0.28f), -28f);
            BuildCoffeeTable(parent);
            BuildTvConsole(parent);
            BuildTelevision(parent);
            BuildSideTable(parent, "F07", "Side Table Left", "F07_SideTable_Left", new Vector3(-1.48f, 0f, -0.78f));
            BuildSideTable(parent, "F08", "Side Table Right", "F08_SideTable_Right", new Vector3(1.48f, 0f, -0.78f));
            BuildDisplayCabinet(parent);
            BuildStorageCabinet(parent);
            BuildBookcase(parent);
        }

        private static void BuildSofa(Transform parent)
        {
            Transform root = CreateFurnitureRoot(parent, "F01", "Three-seat Sofa", "F01_Sofa_ThreeSeat", new Vector3(0f, 0f, -0.78f), 0f, 1.05f);
            CreateBox("Frame", root, new Vector3(0f, 0.25f, 0f), new Vector3(2.3f, 0.25f, 0.82f), fabricBeigeMaterial);
            CreateBox("Back", root, new Vector3(0f, 0.61f, -0.38f), new Vector3(2.3f, 0.60f, 0.18f), fabricBeigeMaterial);
            CreateBox("Arm_Left", root, new Vector3(-1.06f, 0.52f, 0f), new Vector3(0.18f, 0.58f, 0.82f), fabricBeigeMaterial);
            CreateBox("Arm_Right", root, new Vector3(1.06f, 0.52f, 0f), new Vector3(0.18f, 0.58f, 0.82f), fabricBeigeMaterial);
            for (int i = 0; i < 3; i++)
            {
                float x = -0.70f + i * 0.70f;
                CreateBox($"Seat_Cushion_{i + 1:00}", root, new Vector3(x, 0.43f, 0.08f), new Vector3(0.66f, 0.16f, 0.62f), fabricBeigeMaterial);
                CreateBox($"Back_Cushion_{i + 1:00}", root, new Vector3(x, 0.69f, -0.23f), new Vector3(0.66f, 0.43f, 0.16f), fabricBeigeMaterial);
            }
            AddFourLegs(root, 0.96f, 0.31f, 0.10f);
        }

        private static void BuildArmchair(Transform parent, string id, string englishName, string objectName, Vector3 position, float rotationY)
        {
            Transform root = CreateFurnitureRoot(parent, id, englishName, objectName, position, rotationY, 1.05f);
            CreateBox("Frame", root, new Vector3(0f, 0.25f, 0f), new Vector3(0.82f, 0.24f, 0.76f), fabricGreenMaterial);
            CreateBox("Seat_Cushion", root, new Vector3(0f, 0.43f, 0.06f), new Vector3(0.54f, 0.17f, 0.56f), fabricGreenMaterial);
            CreateBox("Back", root, new Vector3(0f, 0.64f, -0.33f), new Vector3(0.82f, 0.58f, 0.18f), fabricGreenMaterial);
            CreateBox("Arm_Left", root, new Vector3(-0.35f, 0.52f, 0f), new Vector3(0.12f, 0.52f, 0.72f), fabricGreenMaterial);
            CreateBox("Arm_Right", root, new Vector3(0.35f, 0.52f, 0f), new Vector3(0.12f, 0.52f, 0.72f), fabricGreenMaterial);
            AddFourLegs(root, 0.31f, 0.29f, 0.10f);
        }

        private static void BuildCoffeeTable(Transform parent)
        {
            Transform root = CreateFurnitureRoot(parent, "F04", "Coffee Table", "F04_CoffeeTable", new Vector3(0f, 0f, 0.38f), 0f, 0.68f);
            CreateBox("TableTop", root, new Vector3(0f, 0.38f, 0f), new Vector3(1.10f, 0.08f, 0.60f), walnutMaterial);
            AddFourTableLegs(root, 0.46f, 0.21f, 0.34f);
        }

        private static void BuildTvConsole(Transform parent)
        {
            Transform root = CreateFurnitureRoot(parent, "F05", "TV Console", "F05_TVConsole", new Vector3(0f, 0f, 2.50f), 0f, 0.78f);
            CreateBox("CabinetBody", root, new Vector3(0f, 0.31f, 0f), new Vector3(2.0f, 0.42f, 0.42f), walnutMaterial);
            for (int i = 0; i < 3; i++)
            {
                CreateBox($"Door_{i + 1:00}", root, new Vector3(-0.66f + i * 0.66f, 0.31f, -0.216f), new Vector3(0.62f, 0.34f, 0.025f), wallMaterial);
            }
            AddFourTableLegs(root, 0.88f, 0.15f, 0.10f);
        }

        private static void BuildTelevision(Transform parent)
        {
            Transform root = CreateFurnitureRoot(parent, "F06", "Television 65 Inch", "F06_Television_65Inch", new Vector3(0f, 0f, 2.46f), 0f, 1.58f);
            CreateBox("TV_Frame", root, new Vector3(0f, 0.995f, 0f), new Vector3(1.50f, 0.89f, 0.08f), metalBlackMaterial);
            CreateBox("TV_Screen", root, new Vector3(0f, 0.995f, -0.046f), new Vector3(1.42f, 0.81f, 0.012f), screenMaterial);
            CreateBox("TV_Stand", root, new Vector3(0f, 0.535f, 0f), new Vector3(0.48f, 0.03f, 0.24f), metalBlackMaterial);
            CreateBox("TV_Neck", root, new Vector3(0f, 0.57f, 0f), new Vector3(0.08f, 0.08f, 0.08f), metalBlackMaterial);
        }

        private static void BuildSideTable(Transform parent, string id, string englishName, string objectName, Vector3 position)
        {
            Transform root = CreateFurnitureRoot(parent, id, englishName, objectName, position, 0f, 0.70f);
            CreateCylinder("RoundTop", root, new Vector3(0f, 0.47f, 0f), new Vector3(0.45f, 0.03f, 0.45f), walnutMaterial, null, true);
            CreateCylinder("CentreLeg", root, new Vector3(0f, 0.24f, 0f), new Vector3(0.055f, 0.22f, 0.055f), metalBlackMaterial, null, true);
            CreateCylinder("Foot", root, new Vector3(0f, 0.025f, 0f), new Vector3(0.30f, 0.025f, 0.30f), metalBlackMaterial, null, true);
        }

        private static void BuildDisplayCabinet(Transform parent)
        {
            Transform root = CreateFurnitureRoot(parent, "F09", "Display Cabinet", "F09_DisplayCabinet", new Vector3(-2.85f, 0f, 2.53f), 0f, 1.90f);
            CreateBox("CabinetBody", root, new Vector3(0f, 0.80f, 0f), new Vector3(1.10f, 1.60f, 0.38f), walnutMaterial);
            CreateBox("GlassDoor_Left", root, new Vector3(-0.275f, 0.88f, -0.205f), new Vector3(0.52f, 1.30f, 0.025f), glassMaterial);
            CreateBox("GlassDoor_Right", root, new Vector3(0.275f, 0.88f, -0.205f), new Vector3(0.52f, 1.30f, 0.025f), glassMaterial);
            CreateBox("Shelf_01", root, new Vector3(0f, 0.52f, -0.02f), new Vector3(0.98f, 0.035f, 0.32f), walnutMaterial);
            CreateBox("Shelf_02", root, new Vector3(0f, 1.02f, -0.02f), new Vector3(0.98f, 0.035f, 0.32f), walnutMaterial);
        }

        private static void BuildStorageCabinet(Transform parent)
        {
            Transform root = CreateFurnitureRoot(parent, "F10", "Storage Cabinet", "F10_StorageCabinet", new Vector3(3.32f, 0f, 1.35f), -90f, 1.10f);
            CreateBox("CabinetBody", root, new Vector3(0f, 0.46f, 0f), new Vector3(1.30f, 0.72f, 0.40f), walnutMaterial);
            CreateBox("Door_Left", root, new Vector3(-0.32f, 0.46f, -0.212f), new Vector3(0.60f, 0.62f, 0.025f), wallMaterial);
            CreateBox("Door_Right", root, new Vector3(0.32f, 0.46f, -0.212f), new Vector3(0.60f, 0.62f, 0.025f), wallMaterial);
            AddFourTableLegs(root, 0.55f, 0.14f, 0.10f);
        }

        private static void BuildBookcase(Transform parent)
        {
            Transform root = CreateFurnitureRoot(parent, "F11", "Bookcase", "F11_Bookcase", new Vector3(1.85f, 0f, 2.60f), 0f, 2.06f);
            CreateBox("Side_Left", root, new Vector3(-0.46f, 0.90f, 0f), new Vector3(0.08f, 1.80f, 0.32f), walnutMaterial);
            CreateBox("Side_Right", root, new Vector3(0.46f, 0.90f, 0f), new Vector3(0.08f, 1.80f, 0.32f), walnutMaterial);
            CreateBox("Top", root, new Vector3(0f, 1.77f, 0f), new Vector3(1.00f, 0.06f, 0.32f), walnutMaterial);
            CreateBox("Bottom", root, new Vector3(0f, 0.06f, 0f), new Vector3(1.00f, 0.12f, 0.32f), walnutMaterial);
            CreateBox("Back", root, new Vector3(0f, 0.90f, 0.145f), new Vector3(0.92f, 1.68f, 0.03f), wallMaterial);

            float[] shelfHeights = { 0.45f, 0.88f, 1.31f };
            for (int shelfIndex = 0; shelfIndex < shelfHeights.Length; shelfIndex++)
            {
                float shelfY = shelfHeights[shelfIndex];
                CreateBox($"Shelf_{shelfIndex + 1:00}", root, new Vector3(0f, shelfY, 0f), new Vector3(0.92f, 0.055f, 0.30f), walnutMaterial);

                for (int bookIndex = 0; bookIndex < 5; bookIndex++)
                {
                    float height = 0.22f + ((bookIndex + shelfIndex) % 3) * 0.035f;
                    float x = -0.31f + bookIndex * 0.145f;
                    Material bookMaterial = (bookIndex + shelfIndex) % 2 == 0 ? accentMaterial : fabricGreenMaterial;
                    CreateBox(
                        $"Book_{shelfIndex + 1:00}_{bookIndex + 1:00}",
                        root,
                        new Vector3(x, shelfY + 0.028f + height * 0.5f, -0.08f),
                        new Vector3(0.09f, height, 0.15f),
                        bookMaterial,
                        false);
                }
            }
        }

        private static void BuildDecoration(Transform parent)
        {
            CreateBox("D01_AreaRug", parent, new Vector3(0f, 0.012f, 0.25f), new Vector3(3.60f, 0.024f, 2.55f), rugMaterial, false);

            Transform lamp = CreateGroup("D02_FloorLamp", parent);
            lamp.localPosition = new Vector3(-1.65f, 0f, 2.25f);
            CreateCylinder("Base", lamp, new Vector3(0f, 0.035f, 0f), new Vector3(0.34f, 0.035f, 0.34f), metalBlackMaterial);
            CreateCylinder("Pole", lamp, new Vector3(0f, 0.83f, 0f), new Vector3(0.035f, 0.78f, 0.035f), metalBlackMaterial);
            CreateCylinder("Shade", lamp, new Vector3(0f, 1.58f, 0f), new Vector3(0.48f, 0.22f, 0.48f), curtainMaterial);

            Transform tableLamp = CreateGroup("D03_TableLamp", parent);
            tableLamp.localPosition = new Vector3(1.48f, 0.50f, -0.78f);
            CreateCylinder("Base", tableLamp, new Vector3(0f, 0.04f, 0f), new Vector3(0.18f, 0.04f, 0.18f), ceramicMaterial);
            CreateCylinder("Stem", tableLamp, new Vector3(0f, 0.22f, 0f), new Vector3(0.025f, 0.18f, 0.025f), metalBlackMaterial);
            CreateCylinder("Shade", tableLamp, new Vector3(0f, 0.43f, 0f), new Vector3(0.28f, 0.18f, 0.28f), curtainMaterial);

            CreateBox("D04_Curtain_Left", parent, new Vector3(-3.52f, 1.48f, -0.63f), new Vector3(0.09f, 1.62f, 0.24f), curtainMaterial, false);
            CreateBox("D05_Curtain_Right", parent, new Vector3(-3.52f, 1.48f, 1.73f), new Vector3(0.09f, 1.62f, 0.24f), curtainMaterial, false);

            BuildPlant(parent, "D06_Plant_Tall", new Vector3(3.00f, 0f, 2.20f), 1.25f);
            BuildPlant(parent, "D07_Plant_Small", new Vector3(3.30f, 0.84f, 1.12f), 0.42f);

            CreateBox("D08_WallArt_01", parent, new Vector3(3.60f, 1.68f, 0.05f), new Vector3(0.045f, 0.72f, 0.92f), accentMaterial, false);
            CreateBox("D09_WallArt_02", parent, new Vector3(3.60f, 1.68f, 1.15f), new Vector3(0.045f, 0.72f, 0.82f), fabricGreenMaterial, false);

            Transform books = CreateGroup("D10_Books_Set", parent);
            books.localPosition = new Vector3(-2.85f, 1.08f, 2.30f);
            for (int i = 0; i < 5; i++)
            {
                Material bookMaterial = i % 2 == 0 ? accentMaterial : curtainMaterial;
                CreateBox($"Book_{i + 1:00}", books, new Vector3(-0.18f + i * 0.09f, 0.10f, 0f), new Vector3(0.065f, 0.20f + i * 0.015f, 0.18f), bookMaterial, false);
            }

            Transform cushions = CreateGroup("D11_Cushions_Set", parent);
            cushions.localPosition = new Vector3(0f, 0f, -0.78f);
            CreateBox("Cushion_Left", cushions, new Vector3(-0.67f, 0.67f, 0.03f), new Vector3(0.38f, 0.38f, 0.13f), accentMaterial, false);
            CreateBox("Cushion_Right", cushions, new Vector3(0.67f, 0.67f, 0.03f), new Vector3(0.38f, 0.38f, 0.13f), fabricGreenMaterial, false);
            CreateBox("D12_ThrowBlanket", parent, new Vector3(0.56f, 0.535f, -0.67f), new Vector3(0.56f, 0.035f, 0.58f), curtainMaterial, false);
        }

        private static void BuildPlant(Transform parent, string name, Vector3 position, float height)
        {
            Transform plant = CreateGroup(name, parent);
            plant.localPosition = position;
            CreateCylinder("Pot", plant, new Vector3(0f, height * 0.16f, 0f), new Vector3(height * 0.28f, height * 0.16f, height * 0.28f), ceramicMaterial);
            CreateCylinder("Stem", plant, new Vector3(0f, height * 0.53f, 0f), new Vector3(height * 0.025f, height * 0.34f, height * 0.025f), walnutMaterial);
            for (int i = 0; i < 7; i++)
            {
                float angle = i * 51.4f;
                float radians = angle * Mathf.Deg2Rad;
                Vector3 leafPosition = new Vector3(Mathf.Cos(radians) * height * 0.15f, height * (0.62f + (i % 3) * 0.10f), Mathf.Sin(radians) * height * 0.15f);
                GameObject leaf = CreateSphere($"Leaf_{i + 1:00}", plant, leafPosition, new Vector3(height * 0.22f, height * 0.10f, height * 0.11f), leafMaterial);
                leaf.transform.localRotation = Quaternion.Euler(0f, -angle, 22f);
            }
        }

        private static void BuildLighting(Transform parent)
        {
            GameObject sunObject = new GameObject("L01_DirectionalLight_Window");
            sunObject.transform.SetParent(parent, false);
            sunObject.transform.localRotation = Quaternion.Euler(42f, -58f, 0f);
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1.0f, 0.91f, 0.78f);
            sun.intensity = 0.58f;
            sun.shadows = LightShadows.Soft;

            CreatePointLight("L02_CeilingLight_Main", parent, new Vector3(0f, 2.48f, 0f), new Color(1f, 0.84f, 0.66f), 1.45f, 7.2f);
            CreatePointLight("L03_CeilingLight_TV", parent, new Vector3(0f, 2.46f, 1.70f), new Color(1f, 0.82f, 0.62f), 0.72f, 4.5f);
            CreateCylinder("L04_CeilingFixture", parent, new Vector3(0f, 2.64f, 0f), new Vector3(0.48f, 0.06f, 0.48f), curtainMaterial);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.32f, 0.29f);
        }

        private static void BuildSceneSystem(Transform parent)
        {
            GameObject start = new GameObject("S01_PlayerStart");
            start.transform.SetParent(parent, false);
            start.transform.localPosition = new Vector3(-2.70f, 0f, -2.42f);
            start.transform.localRotation = Quaternion.Euler(0f, 20f, 0f);

            GameObject cameraObject = new GameObject("S02_PreviewCamera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.localPosition = new Vector3(-2.85f, 1.64f, -2.32f);
            cameraObject.transform.LookAt(new Vector3(0f, 0.92f, 0.42f));
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 68f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 60f;
            AudioListener previewListener = cameraObject.AddComponent<AudioListener>();
            previewListener.enabled = false;

            GameObject xrRigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(XRReviewRigPrefabPath);
            if (xrRigPrefab == null)
            {
                Debug.LogWarning("XR review rig prefab is missing. Run Tools/SceneNew/Create or Update XR Review Rig Prefab before rebuilding SceneNew.");
                return;
            }

            GameObject xrRig = PrefabUtility.InstantiatePrefab(xrRigPrefab, parent) as GameObject;
            if (xrRig == null)
            {
                throw new MissingReferenceException("XR_ReviewRig prefab could not be instantiated.");
            }

            xrRig.name = "S03_XR_ReviewRig";
            xrRig.transform.localPosition = start.transform.localPosition;
            xrRig.transform.localRotation = start.transform.localRotation;

            CharacterController body = xrRig.GetComponentInChildren<CharacterController>(true);
            if (body != null)
            {
                body.height = 1.75f;
                body.radius = 0.22f;
                body.center = new Vector3(0f, 0.875f, 0f);
                body.skinWidth = 0.02f;
                body.stepOffset = 0.20f;
            }

            GameObject simulatorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(XRDeviceSimulatorPrefabPath);
            if (simulatorPrefab != null)
            {
                GameObject simulator = PrefabUtility.InstantiatePrefab(simulatorPrefab, parent) as GameObject;
                if (simulator != null)
                {
                    simulator.name = "S04_XR_DeviceSimulator_EditorOnly";
                    simulator.tag = "EditorOnly";
                    simulator.transform.localPosition = Vector3.zero;
                    simulator.transform.localRotation = Quaternion.identity;
                }
            }
            else
            {
                Debug.LogWarning("XR Device Simulator prefab was not found; headset play remains available.");
            }
        }

        private static Transform CreateFurnitureRoot(Transform parent, string id, string englishName, string objectName, Vector3 position, float rotationY, float labelHeight)
        {
            Transform root = CreateGroup(objectName, parent);
            root.localPosition = position;
            root.localRotation = Quaternion.Euler(0f, rotationY, 0f);

            FurnitureInfo info = root.gameObject.AddComponent<FurnitureInfo>();
            info.Configure(id, englishName);

            GameObject label = new GameObject($"Label_{id}_{englishName.Replace(' ', '_')}");
            label.transform.SetParent(root, false);
            label.transform.localPosition = new Vector3(0f, labelHeight, 0f);
            TextMesh text = label.AddComponent<TextMesh>();
            text.text = id + "\n" + englishName;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = 0.08f;
            text.fontSize = 48;
            text.color = new Color(0.12f, 0.08f, 0.05f);
            label.SetActive(false);

            return root;
        }

        private static void AddFourLegs(Transform root, float x, float z, float height)
        {
            Vector3[] positions =
            {
                new Vector3(-x, height * 0.5f, -z), new Vector3(x, height * 0.5f, -z),
                new Vector3(-x, height * 0.5f, z), new Vector3(x, height * 0.5f, z)
            };
            for (int i = 0; i < positions.Length; i++)
            {
                CreateBox($"Leg_{i + 1:00}", root, positions[i], new Vector3(0.055f, height, 0.055f), metalBlackMaterial);
            }
        }

        private static void AddFourTableLegs(Transform root, float x, float z, float height)
        {
            Vector3[] positions =
            {
                new Vector3(-x, height * 0.5f, -z), new Vector3(x, height * 0.5f, -z),
                new Vector3(-x, height * 0.5f, z), new Vector3(x, height * 0.5f, z)
            };
            for (int i = 0; i < positions.Length; i++)
            {
                CreateBox($"Leg_{i + 1:00}", root, positions[i], new Vector3(0.045f, height, 0.045f), metalBlackMaterial);
            }
        }

        private static void CreatePointLight(string name, Transform parent, Vector3 position, Color color, float intensity, float range)
        {
            GameObject lightObject = new GameObject(name);
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.localPosition = position;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.Soft;
        }

        private static GameObject CreateRoot(string name)
        {
            return new GameObject(name);
        }

        private static Transform CreateGroup(string name, Transform parent)
        {
            GameObject group = new GameObject(name);
            group.transform.SetParent(parent, false);
            return group.transform;
        }

        private static GameObject CreateBox(string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material, bool addCollider = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!addCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static GameObject CreateCylinder(string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material, Quaternion? localRotation = null, bool addMeshCollider = false)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.transform.localRotation = localRotation ?? Quaternion.identity;
            go.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (addMeshCollider)
            {
                MeshCollider collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
            }
            return go;
        }

        private static void SetLayerRecursively(GameObject root, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0)
            {
                Debug.LogWarning("SceneNew layer is missing: " + layerName);
                return;
            }

            foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
            {
                item.gameObject.layer = layer;
            }
        }

        private static GameObject CreateSphere(string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static void AddSceneToBuildSettings()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes.All(item => item.path != ScenePath))
            {
                EditorBuildSettings.scenes = scenes.Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) }).ToArray();
            }
        }
    }
}
