using System;
using System.Collections.Generic;
using UnityEngine;
using MonsterCardGame.Data;
using MonsterCardGame.UI;

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
            if (monstersJsonFile == null)
            {
                monstersJsonFile = Resources.Load<TextAsset>("monsters_150");
            }

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

            // يبدأ اللاعب البشري الدور الأول دائماً لضمان ظهور وحوشه فوراً وتفاعله معها
            isPlayerTurn = true;
            currentTurn = 1;

            Debug.Log($"🎲 تبدأ المباراة! اللاعب البادئ: {humanPlayer.playerName}");

            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.UpdateUI();
            }

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

            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.UpdateUI();
            }

            if (isPlayerTurn)
            {
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.ShowBanner($"🟢 دورك الآن (الجولة {currentTurn})", new Color(0.2f, 0.95f, 0.5f));
                }
            }
            else
            {
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.ShowBanner($"🔴 دور الخصم (يفكر...)", new Color(1f, 0.35f, 0.35f));
                }
                // دور الذكاء الاصطناعي
                aiOpponent.PlayTurn(currentTurn, activeEnvironment, EndTurn);
            }
        }

        public void OnPlayerExecuteAttackButton()
        {
            if (!isPlayerTurn || currentPhase != GamePhase.ActionPhase) return;

            bool hasReadyAttackers = false;
            foreach (var slot in humanPlayer.slots)
            {
                if (slot != null && slot.IsOccupied && slot.currentMonster != null && slot.currentMonster.canAttack)
                {
                    if (slot.currentMonster.GetEffectiveAtk(activeEnvironment) > 0)
                    {
                        hasReadyAttackers = true;
                        break;
                    }
                }
            }

            if (!hasReadyAttackers)
            {
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.LogCombatMessage("⚠️ لا توجد وحوش جاهزة للهجوم حالياً بالساحة!");
                }
                return;
            }

            currentPhase = GamePhase.CombatPhase;

            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.ShowBanner("⚔️ مرحلة الهجوم الميداني!", new Color(1f, 0.85f, 0.2f));
            }

            StartCoroutine(CombatSystem.Instance.ExecuteCombatPhaseAnimated(humanPlayer, aiPlayer, activeEnvironment, () =>
            {
                CheckGameOver();
                if (currentPhase != GamePhase.GameOver)
                {
                    EndTurn();
                }
            }));
        }

        public void OnPlayerEndTurn()
        {
            if (!isPlayerTurn || currentPhase != GamePhase.ActionPhase) return;

            // فحص ما إذا كان لدى وحوش اللاعب هجمات جاهزة بالساحة
            bool hasReadyAttackers = false;
            foreach (var slot in humanPlayer.slots)
            {
                if (slot != null && slot.IsOccupied && slot.currentMonster != null && slot.currentMonster.canAttack)
                {
                    if (slot.currentMonster.GetEffectiveAtk(activeEnvironment) > 0)
                    {
                        hasReadyAttackers = true;
                        break;
                    }
                }
            }

            if (hasReadyAttackers)
            {
                Debug.Log("⚔️ إنهاء الدور مع هجوم تلقائي للوحوش الجاهزة!");
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.LogCombatMessage("⚔️ تنفيذ هجوم وحوشك الميداني تلقائياً قبل إنهاء الدور...");
                    BattleUIManager.Instance.ShowBanner("⚔️ هجوم ميداني تلقائي!", new Color(1f, 0.85f, 0.2f));
                }

                currentPhase = GamePhase.CombatPhase;

                StartCoroutine(CombatSystem.Instance.ExecuteCombatPhaseAnimated(humanPlayer, aiPlayer, activeEnvironment, () =>
                {
                    CheckGameOver();
                    if (currentPhase != GamePhase.GameOver)
                    {
                        EndTurn();
                    }
                }));
            }
            else
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
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.UpdateUI();
                }
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
