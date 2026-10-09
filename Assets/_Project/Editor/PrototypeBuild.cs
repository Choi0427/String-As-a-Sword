using System;
using System.IO;
using System.Linq;
using StringAsASword.Core;
using StringAsASword.Presentation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StringAsASword.Editor
{
    public static class PrototypeBuild
    {
        [MenuItem("String As a Sword/Prepare Windows Build")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new BuildFailedException("Stop Play Mode before preparing a build.");
            var active = SceneManager.GetActiveScene();
            if (active.isDirty) throw new BuildFailedException("Save the open scene before preparing a build.");
            if (active.path != PrototypeSceneSetup.ScenePath)
                EditorSceneManager.OpenScene(PrototypeSceneSetup.ScenePath);
            var view = UnityEngine.Object.FindAnyObjectByType<PrototypeView>();
            var game = UnityEngine.Object.FindAnyObjectByType<PrototypeGame>();
            if (view == null || game == null || game.Song == null || game.Stage == null)
                throw new BuildFailedException("Prototype scene, song or stage references are missing.");
            var material = PrototypeSceneSetup.EnsureBoardMaterial();
            if (view.BoardMaterial != material)
            {
                var data = new SerializedObject(view);
                data.FindProperty("boardMaterial").objectReferenceValue = material;
                data.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            }
            AssetDatabase.SaveAssets();
        }

        public static string BuildWindows(string outputDirectory)
        {
            Prepare();
            string absolute = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(absolute);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { PrototypeSceneSetup.ScenePath },
                locationPathName = Path.Combine(absolute, "String As a Sword.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            var summary = report.summary;
            var messages = report.steps.SelectMany(step => step.messages)
                .Where(message => message.type == LogType.Warning || message.type == LogType.Error)
                .Select(message => message.type + ": " + message.content).ToArray();
            Directory.CreateDirectory("Logs/Build");
            File.WriteAllText("Logs/Build/windows-build-report.json", JsonUtility.ToJson(new BuildSummary
            {
                result = summary.result.ToString(), output = summary.outputPath,
                errors = summary.totalErrors, warnings = summary.totalWarnings,
                seconds = summary.totalTime.TotalSeconds, bytes = summary.totalSize, messages = messages
            }, true));
            if (summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"Windows build failed with {summary.totalErrors} errors. See Logs/Build/windows-build-report.json.");
            Debug.Log($"Windows build succeeded: {summary.outputPath} ({summary.totalErrors} errors, {summary.totalWarnings} warnings)");
            return summary.outputPath;
        }

        public static void BuildFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-prototypeBuildOutput");
            if (index < 0 || index + 1 >= args.Length) throw new BuildFailedException("Provide -prototypeBuildOutput <directory>.");
            BuildWindows(args[index + 1]);
        }

        [Serializable] private sealed class BuildSummary
        {
            public string result, output;
            public int errors, warnings;
            public double seconds;
            public ulong bytes;
            public string[] messages;
        }
    }

    // Applies to ordinary Editor builds too, so a missing reference fails before shipping a broken player.
    public sealed class PrototypeSceneBuildValidator : IProcessSceneWithReport
    {
        public int callbackOrder => 0;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null) return;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var view in root.GetComponentsInChildren<PrototypeView>(true))
                if (view.BoardMaterial == null || view.BoardMaterial.shader == null)
                    throw new BuildFailedException("Prototype board material is missing. Run String As a Sword > Prepare Windows Build.");
        }
    }
}
