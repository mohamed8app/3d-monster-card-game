#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.UI;
using MonsterCardGame.Core;
using MonsterCardGame.UI;

namespace MonsterCardGame.Editor
{
    public class BattleSceneBuilder
    {
        [MenuItem("Monster Game/🚀 بناء ساحة المعركة التلقائية (Setup Scene)")]
        public static void BuildScene()
        {
            var newScene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, 
                UnityEditor.SceneManagement.NewSceneMode.Single);

            // بناء ساحة المعركة التفاعلية كاملة
            GameBootstrapper.AutoInitializeGame();

            // حفظ المشهد في Assets/Scenes/MainBattleScene.unity
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }

            string scenePath = "Assets/Scenes/MainBattleScene.unity";
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(newScene, scenePath);

            Debug.Log($"🎉 تم بناء وحفظ مشهد اللعبة في: {scenePath}");

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("نجاح ساحة المعركة", "تم بناء ساحة الـ 7 خانات ولوحة القائد واليد والأزرار بنجاح!\n\nاضغط الآن على زر (▶️ Play) لتلعب مباشرة!", "ممتاز");
            }
        }

        [MenuItem("Monster Game/📦 بناء اللعبة كملف تنفيذي للويندوز (Build Windows Game .EXE)")]
        public static void BuildWindowsExe()
        {
            Debug.Log("🚀 بدء بناء ملف اللعبة التنفيذي للويندوز (MonsterCardGame.exe)...");

            BuildScene();

            string buildFolder = "D:/deck game/Game_Windows_Exe";
            if (!System.IO.Directory.Exists(buildFolder))
            {
                System.IO.Directory.CreateDirectory(buildFolder);
            }

            string exePath = buildFolder + "/MonsterCardGame.exe";
            string[] scenes = new string[] { "Assets/Scenes/MainBattleScene.unity" };

            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
            buildPlayerOptions.scenes = scenes;
            buildPlayerOptions.locationPathName = exePath;
            buildPlayerOptions.target = BuildTarget.StandaloneWindows64;
            buildPlayerOptions.options = BuildOptions.None;

            BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"🎉 تم بناء اللعبة بنجاح تام! الحجم: {summary.totalSize} بايت. المسار: {exePath}");
            }
            else
            {
                Debug.LogError($"❌ فشل بناء اللعبة: {summary.result}");
            }
        }
    }
}
#endif
