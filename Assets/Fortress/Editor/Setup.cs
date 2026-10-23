using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fortress.EditorTools
{
    /// One-time project setup, safe to re-run: URP with shadows and post-processing, the shared
    /// materials the game copies at runtime, the "Pieces" layer, and the main scene.
    /// Run from the command line with `-executeMethod Fortress.EditorTools.Setup.Run`.
    public static class Setup
    {
        const string Root = "Assets/Fortress";
        const string Resources = Root + "/Resources";

        [MenuItem("Fortress/Run Setup")]
        public static void Run()
        {
            Directory.CreateDirectory(Resources + "/Materials");
            Directory.CreateDirectory(Root + "/Rendering");
            Directory.CreateDirectory(Root + "/Scenes");
            AssetDatabase.Refresh();

            SetUpPipeline();
            SetUpMaterials();
            WorldSetup.Run();
            SetUpPostProcessing();
            SetUpLayers();
            SetUpScene();
            PlayerSettings.companyName = "Fortress Games";
            PlayerSettings.productName = "Fortress";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            AssetDatabase.SaveAssets();
            Debug.Log("Fortress setup done");
        }

        static void SetUpPipeline()
        {
            string rendererPath = Root + "/Rendering/Renderer.asset", pipelinePath = Root + "/Rendering/Pipeline.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (!renderer)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
                // Soft contact shadows in corners and under things.
                var ambientOcclusion = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ambientOcclusion.name = "Ambient Occlusion";
                AssetDatabase.AddObjectToAsset(ambientOcclusion, renderer);
                renderer.rendererFeatures.Add(ambientOcclusion);
                EditorUtility.SetDirty(renderer);
            }
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (!pipeline)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, pipelinePath);
            }
            pipeline.shadowDistance = 120;
            pipeline.shadowCascadeCount = 4;
            pipeline.msaaSampleCount = 4;
            pipeline.supportsHDR = true;
            // Settings without a public setter, by their serialized names.
            var serialized = new SerializedObject(pipeline);
            serialized.FindProperty("m_SoftShadowsSupported").boolValue = true;
            serialized.FindProperty("m_MainLightShadowmapResolution").intValue = 4096;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            for (int level = 0; level < QualitySettings.names.Length; level++)
            {
                QualitySettings.SetQualityLevel(level, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, false);
        }

        static Material SaveMaterial(string name, Shader shader, System.Action<Material> configure)
        {
            string path = $"{Resources}/Materials/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            configure(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// Turns a URP Lit/Unlit/Particles material see-through with ordinary alpha blending.
        static void MakeTransparent(Material material)
        {
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        static void SetUpMaterials()
        {
            SaveMaterial("Lit", Shader.Find("Universal Render Pipeline/Lit"), material =>
            {
                material.SetColor("_BaseColor", Color.white);
                material.enableInstancing = true;
            });
            SaveMaterial("Ghost", Shader.Find("Universal Render Pipeline/Unlit"), material =>
            {
                MakeTransparent(material);
                material.SetColor("_BaseColor", new Color(0.3f, 0.9f, 0.45f, 0.45f));
                material.SetFloat("_Cull", 0);
            });
            SaveMaterial("Particle", Shader.Find("Universal Render Pipeline/Particles/Unlit"), material =>
            {
                MakeTransparent(material);
                material.SetColor("_BaseColor", Color.white);
            });
        }

        static void SetUpPostProcessing()
        {
            string path = Resources + "/PostProfile.asset";
            // Rebuilt every run so changes to the grade below always land.
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1);
            bloom.intensity.Override(0.25f);
            bloom.scatter.Override(0.65f);
            var colour = profile.Add<ColorAdjustments>(true);
            colour.postExposure.Override(0.25f);
            colour.contrast.Override(14);
            colour.saturation.Override(4);
            var balance = profile.Add<WhiteBalance>(true);
            balance.temperature.Override(6);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.2f);
            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.12f);
            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);
        }

        /// Names layer 8 "Pieces" (built pieces) and 9 "Held" (what the player holds).
        static void SetUpLayers()
        {
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            tags.FindProperty("layers").GetArrayElementAtIndex(8).stringValue = "Pieces";
            // Held tools and arms, drawn by their own camera on top of the world.
            tags.FindProperty("layers").GetArrayElementAtIndex(9).stringValue = "Held";
            tags.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetUpScene()
        {
            string path = Root + "/Scenes/Main.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            WorldSetup.BuildTerrain();
            var game = new GameObject("Game");
            game.AddComponent<Fortress.Game.Game>();
            EditorSceneManager.SaveScene(scene, path);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
        }
    }
}
