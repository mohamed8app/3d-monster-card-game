using System;
using System.Collections.Generic;
using UnityEngine;
using MonsterCardGame.Data;

namespace MonsterCardGame.Core
{
    public enum GamePhase
    {
        StartingCoinToss,   // قرعة البداية
        TurnStart,          // سحب الكروت والمانا
        ActionPhase,        // إنزال الوحوش والتضحية
        CombatPhase,        // مرحلة الهجوم في اتجاه واحد
        TurnEnd,            // تبديل الأدوار
        GameOver            // نهاية المباراة بفوز أو خسارة
    }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Players")]
        public PlayerState humanPlayer;
        public PlayerState aiPlayer;
        public AIOpponent aiOpponent;

        [Header("State")]
        public int currentTurn = 1;
        public bool isPlayerTurn = true;
        public GamePhase currentPhase = GamePhase.StartingCoinToss;
        public EnvironmentCardData activeEnvironment = null;

        [Header("Database")]
        public TextAsset monstersJsonFile;
        public List<MonsterData> allMonsters = new List<MonsterData>();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void Start()
        {
            LoadMonsterDatabase();
            StartGame();
        }

        private void LoadMonsterDatabase()
        {
            if (monstersJsonFile != null)
            {
                string json = monstersJsonFile.text;
                // إحاطة المصفوفة بغلاف لتحليلها بـ JsonUtility
                string wrappedJson = "{\"monsters\":" + json + "}";
                var wrapper = JsonUtility.FromJson<MonsterDatabaseWrapper>(wrappedJson);
                allMonsters = wrapper.monsters;
                Debug.Log($"✅ تم تحميل {allMonsters.Count} وحشاً من قاعدة البيانات بنجاح!");
            }
        }

        public void StartGame()
        {
            // بناء مجموعة عشوائية مكونة من 30 كارت لكل لاعب
            var deckA = GenerateDeck(30);
            var deckB = GenerateDeck(30);

            humanPlayer.InitializeDeck(deckA);
            aiPlayer.InitializeDeck(deckB);

            // سحب يد البداية (6 كروت لكل لاعب)
            for (int i = 0; i < 6; i++)
            {
                humanPlayer.DrawCard();
                aiPlayer.DrawCard();
            }

            // قرعة البداية (50/50)
            isPlayerTurn = UnityEngine.Random.value > 0.5f;
            currentTurn = 1;

            Debug.Log($"🎲 أجريت القرعة! اللاعب البادئ: {(isPlayerTurn ? humanPlayer.playerName : aiPlayer.playerName)}");
            StartTurn();
        }

        public List<MonsterData> GenerateDeck(int count)
        {
            var deck = new List<MonsterData>();
            if (allMonsters == null || allMonsters.Count == 0) return deck;

            for (int i = 0; i < count; i++)
            {
                var randomMonster = allMonsters[UnityEngine.Random.Range(0, allMonsters.Count)];
                deck.Add(randomMonster);
            }
            return deck;
        }

        public void StartTurn()
        {
            currentPhase = GamePhase.TurnStart;
            var activePlayer = isPlayerTurn ? humanPlayer : aiPlayer;
            activePlayer.StartTurn(currentTurn);

            currentPhase = GamePhase.ActionPhase;

            if (!isPlayerTurn)
            {
                // دور الذكاء الاصطناعي
                aiOpponent.PlayTurn(currentTurn, activeEnvironment, EndTurn);
            }
        }

        public void OnPlayerExecuteAttackButton()
        {
            if (!isPlayerTurn || currentPhase != GamePhase.ActionPhase) return;

            currentPhase = GamePhase.CombatPhase;
            CombatSystem.Instance.ExecuteCombatPhase(humanPlayer, aiPlayer, activeEnvironment);

            // فحص الفوز أو الخسارة
            CheckGameOver();

            if (currentPhase != GamePhase.GameOver)
            {
                EndTurn();
            }
        }

        public void EndTurn()
        {
            currentPhase = GamePhase.TurnEnd;
            CheckGameOver();

            if (currentPhase != GamePhase.GameOver)
            {
                isPlayerTurn = !isPlayerTurn;
                currentTurn++;
                StartTurn();
            }
        }

        private void CheckGameOver()
        {
            if (!aiPlayer.IsAlive)
            {
                currentPhase = GamePhase.GameOver;
                Debug.Log("🏆 مبروك! لقد هزمت قائد الخصم وانتصرت في المعركة!");
            }
            else if (!humanPlayer.IsAlive)
            {
                currentPhase = GamePhase.GameOver;
                Debug.Log("💀 سقط قائدك! لقد خسرت المعركة، حظاً أوفر في الجولة القادمة.");
            }
        }
    }
}
