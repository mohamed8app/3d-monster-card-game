#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using MonsterCardGame.Core;
using MonsterCardGame.UI;

namespace MonsterCardGame.Editor
{
    public class BattleSceneBuilder : EditorWindow
    {
        [MenuItem("Monster Game/🚀 بناء ساحة المعركة التلقائية (Setup Scene)")]
        public static void BuildScene()
        {
            // بناء ساحة المعركة التفاعلية كاملة
            GameBootstrapper.AutoInitializeGame();

            // حفظ المشهد في Assets/Scenes/MainBattleScene.unity
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }

            string scenePath = "Assets/Scenes/MainBattleScene.unity";
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene(), scenePath);

            Debug.Log($"🎉 تم بناء وحفظ مشهد اللعبة في: {scenePath}");
            EditorUtility.DisplayDialog("نجاح ساحة المعركة", "تم بناء ساحة الـ 7 خانات ولوحة القائد واليد والأزرار بنجاح!\n\nاضغط الآن على زر (▶️ Play) لتلعب مباشرة!", "ممتاز");
        }
    }
}
#endif
