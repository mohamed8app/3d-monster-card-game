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
            // 1. إنشاء مدير اللعبة والنظام القتالي
            var gameManagerObj = new GameObject("GameManager");
            var gm = gameManagerObj.AddComponent<GameManager>();
            var combatSystem = gameManagerObj.AddComponent<CombatSystem>();

            var playerObj = new GameObject("HumanPlayer");
            playerObj.transform.SetParent(gameManagerObj.transform);
            var humanPlayer = playerObj.AddComponent<PlayerState>();
            humanPlayer.playerName = "أنت (البطل)";

            var aiObj = new GameObject("AIOpponent");
            aiObj.transform.SetParent(gameManagerObj.transform);
            var aiPlayer = aiObj.AddComponent<PlayerState>();
            aiPlayer.playerName = "الخصم (الذكاء الاصطناعي)";
            var aiBrain = aiObj.AddComponent<AIOpponent>();
            aiBrain.aiPlayer = aiPlayer;
            aiBrain.humanPlayer = humanPlayer;

            gm.humanPlayer = humanPlayer;
            gm.aiPlayer = aiPlayer;
            gm.aiOpponent = aiBrain;

            // 2. إنشاء الخانات الـ 7 لكل لاعب
            for (int i = 0; i < 7; i++)
            {
                var pSlotObj = new GameObject($"Player_Slot_{i}");
                pSlotObj.transform.SetParent(playerObj.transform);
                var pSlot = pSlotObj.AddComponent<BoardSlot>();
                pSlot.Initialize(i, true);
                humanPlayer.slots[i] = pSlot;

                var eSlotObj = new GameObject($"Enemy_Slot_{i}");
                eSlotObj.transform.SetParent(aiObj.transform);
                var eSlot = eSlotObj.AddComponent<BoardSlot>();
                eSlot.Initialize(i, false);
                aiPlayer.slots[i] = eSlot;
            }

            // 3. ربط ملف الجيسون
            var jsonAsset = Resources.Load<TextAsset>("monsters_150");
            gm.monstersJsonFile = jsonAsset;

            Debug.Log("🎉 تم بناء مشهد اللعبة وربط الخانات والأنظمة بنجاح تام!");
            EditorUtility.DisplayDialog("نجاح", "تم إنشاء ساحة المعركة وربط الخانات واللاعبين والذكاء الاصطناعي بنجاح!\nاضغط Play لتجربة اللعبة.", "ممتاز");
        }
    }
}
#endif
