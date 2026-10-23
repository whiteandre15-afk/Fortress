using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Fortress.EditorTools
{
    /// Builds the game for Mac and Windows into Builds/. Development builds keep unlimited mode on.
    /// Command line: `-executeMethod Fortress.EditorTools.BuildPlayers.All`.
    public static class BuildPlayers
    {
        [MenuItem("Fortress/Build Mac + Windows")]
        public static void All()
        {
            bool ok = Build(BuildTarget.StandaloneOSX, "Builds/Mac/Fortress.app")
                      & Build(BuildTarget.StandaloneWindows64, "Builds/Windows/Fortress.exe");
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool Build(BuildTarget target, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Fortress/Scenes/Main.unity" },
                locationPathName = path,
                target = target,
                options = BuildOptions.Development,
            });
            Debug.Log($"Build {target}: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalErrors} errors");
            return report.summary.result == BuildResult.Succeeded;
        }
    }
}
