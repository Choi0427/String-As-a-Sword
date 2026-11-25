using System.IO;
using System.Linq;
using StringAsASword.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StringAsASword.Editor
{
    /// <summary>Builds the MainMenu scene and puts it first in the build: menu → battle → ending → menu.</summary>
    public static class MainMenuSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/MainMenu.unity";
        public const string InvertMaterialPath = "Assets/_Project/Art/Materials/MenuInvertLeft.mat";
        public const string BackgroundPath = "Assets/_Project/Art/UI/Menu/MainMenu_Logo_BG.png";

        [MenuItem("String As a Sword/Build Main Menu Scene")]
        public static void Build()
        {
            var art = PrototypeSceneSetup.EnsureArtSet();
            var background = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundPath);
            if (art.mainMenuDark == null && background != null) { art.mainMenuDark = background; EditorUtility.SetDirty(art); }

            var material = AssetDatabase.LoadAssetAtPath<Material>(InvertMaterialPath);
            if (material == null)
            {
                var shader = Shader.Find("StringAsASword/UI/MenuInvertLeft");
                if (shader == null) throw new System.InvalidOperationException("MenuInvertLeft shader is missing.");
                material = new Material(shader) { name = "MenuInvertLeft" };
                AssetDatabase.CreateAsset(material, InvertMaterialPath);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera"; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.008f, .008f, .008f);
            camera.orthographic = true;
            var menu = new GameObject("Main Menu", typeof(RectTransform)).AddComponent<MainMenuView>();
            var serialized = new SerializedObject(menu);
            serialized.FindProperty("art").objectReferenceValue = art;
            serialized.FindProperty("invertMaterial").objectReferenceValue = material;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene, ScenePath);

            // Menu first so a build starts there; the battle scene stays playable on its own in the editor.
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[MainMenu] Built " + ScenePath + "; build order: " + string.Join(", ", scenes.Select(s => Path.GetFileNameWithoutExtension(s.path))));
        }
    }
}
