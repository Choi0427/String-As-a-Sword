using System.IO;
using System.Linq;
using StringAsASword.Core;
using StringAsASword.Presentation;
using StringAsASword.Rhythm;
using StringAsASword.Stage;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StringAsASword.Editor
{
    public static class PrototypeSceneSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/RhythmPrototype.unity";
        public const string SongPath = "Assets/_Project/Data/Songs/GeomEulHyeon87.asset";
        public const string StagePath = "Assets/_Project/Data/Stages/FourPhasePrototype.asset";
        public const string BoardMaterialPath = "Assets/_Project/Art/Materials/PrototypeBoard.mat";

        public static Material EnsureBoardMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(BoardMaterialPath);
            if (material != null) return material;
            Directory.CreateDirectory("Assets/_Project/Art/Materials");
            AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new System.InvalidOperationException("URP Unlit shader is unavailable. Resolve the render pipeline before building.");
            material = new Material(shader) { name = "PrototypeBoard" };
            AssetDatabase.CreateAsset(material, BoardMaterialPath);
            return material;
        }

        [MenuItem("String As a Sword/Open Rhythm Prototype")]
        public static void OpenOrCreate()
        {
            if (File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return; }
            if (EditorSceneManager.GetActiveScene().isDirty)
                throw new System.InvalidOperationException("Save the current scene before creating the prototype.");
            Directory.CreateDirectory("Assets/_Project/Scenes");
            Directory.CreateDirectory("Assets/_Project/Data/Songs");
            Directory.CreateDirectory("Assets/_Project/Data/Stages");
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            if (font == null) throw new System.InvalidOperationException("Import TMP essentials before creating the prototype.");
            var song = AssetDatabase.LoadAssetAtPath<SongDefinition>(SongPath);
            if (song == null) { song = ScriptableObject.CreateInstance<SongDefinition>(); AssetDatabase.CreateAsset(song, SongPath); }
            var stage = AssetDatabase.LoadAssetAtPath<StageDefinition>(StagePath);
            if (stage == null) { stage = ScriptableObject.CreateInstance<StageDefinition>(); AssetDatabase.CreateAsset(stage, StagePath); }
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 5;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f,.043f,.052f);
            camera.transform.position = new Vector3(0,0,-10);
            var root = new GameObject("Rhythm Prototype");
            var game = root.AddComponent<PrototypeGame>();
            var gameData = new SerializedObject(game); gameData.FindProperty("song").objectReferenceValue = song; gameData.ApplyModifiedPropertiesWithoutUndo();
            gameData.FindProperty("stage").objectReferenceValue = stage; gameData.ApplyModifiedPropertiesWithoutUndo();
            var viewObject = new GameObject("Prototype UI",typeof(RectTransform),typeof(PrototypeView));
            viewObject.transform.SetParent(root.transform,false);
            var viewData = new SerializedObject(viewObject.GetComponent<PrototypeView>());
            viewData.FindProperty("font").objectReferenceValue = font; viewData.ApplyModifiedPropertiesWithoutUndo();
            viewData.FindProperty("boardMaterial").objectReferenceValue = EnsureBoardMaterial(); viewData.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene,ScenePath);
            EditorBuildSettings.scenes = new[] {new EditorBuildSettingsScene(ScenePath,true)}
                .Concat(EditorBuildSettings.scenes.Where(s=>s.path!=ScenePath)).ToArray();
            AssetDatabase.SaveAssets(); Selection.activeGameObject = root;
        }
    }
}
