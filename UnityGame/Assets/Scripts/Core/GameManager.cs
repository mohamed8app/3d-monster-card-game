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
        ActionPhase,        // إنزال الوحوش وإطلاق السحر والبيئة والتضحية
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

        [Header("Match Statistics")]
        public int playerSpellsCastCount = 0;

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
                string wrappedJson = "{\"monsters\":" + json + "}";
                var wrapper = JsonUtility.FromJson<MonsterDatabaseWrapper>(wrappedJson);
                allMonsters = wrapper.monsters;
                if (allMonsters != null)
                {
                    foreach (var m in allMonsters)
                    {
                        if (m != null) m.name = m.id.ToString();
                    }
                }
                Debug.Log($"✅ تم تحميل {allMonsters.Count} وحشاً من قاعدة البيانات بنجاح!");
            }
        }

        public void StartGame()
        {
            // بناء مجموعة تكتيكية متوازنة مكونة من 30 بطاقة:
            // 20 وحش + 6 بطاقات سحر + 4 بطاقات بيئة
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

            isPlayerTurn = true;
            currentTurn = 1;
            activeEnvironment = null;

            Debug.Log($"🎲 تبدأ المعركة الملحمية! اللاعب البادئ: {humanPlayer.playerName}");

            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.UpdateUI();
            }

            StartTurn();
        }

        public List<CardData> GenerateDeck(int count)
        {
            var deck = new List<CardData>();
            if (allMonsters == null || allMonsters.Count == 0) return deck;

            // 1. إضافة 20 وحش عشوائي من الـ 150 وحش
            int monsterCount = Mathf.Min(20, count);
            for (int i = 0; i < monsterCount; i++)
            {
                var randomMonster = allMonsters[UnityEngine.Random.Range(0, allMonsters.Count)];
                deck.Add(randomMonster);
            }

            // 2. إضافة 6 بطاقات سحرية منوعة
            for (int i = 0; i < 6; i++)
            {
                deck.Add(SpellDatabase.GetRandomSpell());
            }

            // 3. إضافة 4 بطاقات بيئة ومناخ تكتيكية
            for (int i = 0; i < 4; i++)
            {
                deck.Add(EnvironmentCardData.GetRandomEnvironment());
            }

            // خلط إضافي للتوزيع
            for (int i = 0; i < deck.Count; i++)
            {
                int r = UnityEngine.Random.Range(i, deck.Count);
                var temp = deck[i];
                deck[i] = deck[r];
                deck[r] = temp;
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
                aiOpponent.PlayTurn(currentTurn, activeEnvironment, EndTurn);
            }
        }

        #region Spell & Environment Mechanisms

        public bool CastSpell(PlayerState caster, PlayerState target, CardData spellCard)
        {
            if (spellCard == null || spellCard.cardType != CardType.Spell) return false;
            if (spellCard.cost > caster.currentPower)
            {
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.LogCombatMessage($"❌ الباور غير كافٍ لإلقاء تعويذة [{spellCard.name}]! (المطلوب: {spellCard.cost}⚡، المتاح: {caster.currentPower}⚡)");
                }
                return false;
            }

            // خصم التكلفة ونقل البطاقة للمقبرة
            caster.currentPower -= spellCard.cost;
            caster.hand.Remove(spellCard);
            caster.graveyard.Add(spellCard);

            if (caster == humanPlayer)
            {
                playerSpellsCastCount++;
            }

            // إشعار وبانر بصري فاخر للتعويذة
            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.ShowSpellBanner(spellCard, caster.playerName);
                BattleUIManager.Instance.LogCombatMessage($"🔮 أطلق [{caster.playerName}] تعويذة [{spellCard.name}] ({spellCard.cost}⚡): {spellCard.description}");
            }

            // تنفيذ التأثير الفعلي حسب نوع التعويذة
            switch (spellCard.spellType)
            {
                case SpellType.DirectDamage:
                    ExecuteDirectDamageSpell(caster, target, spellCard.spellPower);
                    break;

                case SpellType.BoardAOE:
                    ExecuteBoardAOESpell(target, spellCard.spellPower);
                    break;

                case SpellType.LeaderHeal:
                    caster.HealLeader(spellCard.spellPower);
                    // ترميم دروع وحوش الحلفاء بالساحة
                    foreach (var slot in caster.slots)
                    {
                        if (slot != null && slot.IsOccupied)
                        {
                            slot.currentMonster.HealDefense(12);
                        }
                    }
                    if (BattleUIManager.Instance != null)
                    {
                        var leaderTransform = BattleUIManager.Instance.GetLeaderTransform(caster == humanPlayer);
                        if (leaderTransform != null)
                        {
                            StartCoroutine(BattleAnimations.AnimateFloatingHeal(BattleUIManager.Instance.transform, leaderTransform.position, spellCard.spellPower));
                        }
                    }
                    break;

                case SpellType.BoardBuff:
                    foreach (var slot in caster.slots)
                    {
                        if (slot != null && slot.IsOccupied)
                        {
                            slot.currentMonster.AddBonusDef(spellCard.spellPower);
                        }
                    }
                    if (BattleUIManager.Instance != null)
                    {
                        BattleUIManager.Instance.LogCombatMessage($"🛡️ عززت التعويذة دفاع وحوش [{caster.playerName}] بـ +{spellCard.spellPower} DEF!");
                    }
                    break;

                case SpellType.FreezeBoard:
                    foreach (var slot in target.slots)
                    {
                        if (slot != null && slot.IsOccupied)
                        {
                            slot.currentMonster.canAttack = false;
                            slot.currentMonster.TakeDamage(spellCard.spellPower);
                        }
                    }
                    if (BattleUIManager.Instance != null)
                    {
                        BattleUIManager.Instance.LogCombatMessage($"❄️ تم تجميد وشل حركة كافة وحوش الخصم للدور القادم مع {spellCard.spellPower} ضرر صقيع!");
                    }
                    break;

                case SpellType.DrawAndEnergy:
                    caster.DrawCard();
                    caster.DrawCard();
                    if (spellCard.spellPower > 0)
                    {
                        target.TakeLeaderDamage(spellCard.spellPower);
                    }
                    break;

                case SpellType.LifeDrain:
                    target.TakeLeaderDamage(spellCard.spellPower);
                    caster.HealLeader(spellCard.spellPower);
                    if (BattleUIManager.Instance != null)
                    {
                        var leaderTransform = BattleUIManager.Instance.GetLeaderTransform(target == humanPlayer);
                        if (leaderTransform != null)
                        {
                            BattleUIManager.Instance.SpawnDamageNumber(leaderTransform.position, spellCard.spellPower, true);
                        }
                    }
                    break;

                case SpellType.ManaTheft:
                    if (target.manaCapacity > 1)
                    {
                        target.manaCapacity = Mathf.Max(1, target.manaCapacity - 1);
                        if (target.currentPower > target.manaCapacity) target.currentPower = target.manaCapacity;
                    }
                    if (caster.manaCapacity < PlayerState.MaxManaCap)
                    {
                        caster.manaCapacity++;
                        caster.currentPower++;
                    }
                    if (BattleUIManager.Instance != null)
                    {
                        BattleUIManager.Instance.LogCombatMessage($"🔮 سلب [{caster.playerName}] 1 مانا دائمة من سقف مانا الخصم!");
                    }
                    break;

                case SpellType.ExecuteStrongest:
                    ExecuteStrongestEnemy(target);
                    break;

                case SpellType.ArcaneBlast:
                    ExecuteArcaneBlastSpell(target, spellCard.spellPower);
                    break;
            }

            CheckGameOver();
            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.UpdateUI();
            }
            return true;
        }

        private void ExecuteDirectDamageSpell(PlayerState caster, PlayerState target, int damage)
        {
            // يبحث عن أقوى وحش لدى الخصم لإصابته، وإن لم يجد يضرب القائد مباشرة
            BoardSlot bestTargetSlot = null;
            int maxAtk = -1;

            foreach (var slot in target.slots)
            {
                if (slot != null && slot.IsOccupied)
                {
                    if (slot.currentMonster.baseAtk > maxAtk)
                    {
                        maxAtk = slot.currentMonster.baseAtk;
                        bestTargetSlot = slot;
                    }
                }
            }

            if (bestTargetSlot != null)
            {
                bestTargetSlot.currentMonster.TakeDamage(damage);
                int sIdx = bestTargetSlot.slotIndex;
                bool isPlayerSlot = (target == humanPlayer);
                var slotView = BattleUIManager.Instance != null ? BattleUIManager.Instance.GetSlotView(sIdx, isPlayerSlot) : null;
                if (slotView != null && BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.TriggerHitShake(slotView.transform, Color.red);
                    BattleUIManager.Instance.SpawnDamageNumber(slotView.transform.position, damage, false);
                }

                if (!bestTargetSlot.currentMonster.IsAlive)
                {
                    target.graveyard.Add(bestTargetSlot.currentMonster.data);
                    bestTargetSlot.ClearSlot();
                }
            }
            else
            {
                target.TakeLeaderDamage(damage);
                if (BattleUIManager.Instance != null)
                {
                    var leaderTransform = BattleUIManager.Instance.GetLeaderTransform(target == humanPlayer);
                    if (leaderTransform != null)
                    {
                        BattleUIManager.Instance.TriggerHitShake(leaderTransform, Color.red);
                        BattleUIManager.Instance.SpawnDamageNumber(leaderTransform.position, damage, true);
                    }
                    BattleUIManager.Instance.ShakeScreen();
                }
            }
        }

        private void ExecuteBoardAOESpell(PlayerState target, int damage)
        {
            bool anyHit = false;
            foreach (var slot in target.slots)
            {
                if (slot != null && slot.IsOccupied)
                {
                    anyHit = true;
                    slot.currentMonster.TakeDamage(damage);
                    int sIdx = slot.slotIndex;
                    bool isPlayerSlot = (target == humanPlayer);
                    var slotView = BattleUIManager.Instance != null ? BattleUIManager.Instance.GetSlotView(sIdx, isPlayerSlot) : null;
                    if (slotView != null && BattleUIManager.Instance != null)
                    {
                        BattleUIManager.Instance.TriggerHitShake(slotView.transform, Color.yellow);
                        BattleUIManager.Instance.SpawnDamageNumber(slotView.transform.position, damage, false);
                    }

                    if (!slot.currentMonster.IsAlive)
                    {
                        target.graveyard.Add(slot.currentMonster.data);
                        slot.ClearSlot();
                    }
                }
            }

            // إن لم تكن هناك وحوش يذهب نصف الضرر للقائد
            if (!anyHit)
            {
                target.TakeLeaderDamage(damage);
                if (BattleUIManager.Instance != null)
                {
                    var leaderTransform = BattleUIManager.Instance.GetLeaderTransform(target == humanPlayer);
                    if (leaderTransform != null)
                    {
                        BattleUIManager.Instance.SpawnDamageNumber(leaderTransform.position, damage, true);
                    }
                }
            }
        }

        private void ExecuteStrongestEnemy(PlayerState target)
        {
            BoardSlot strongest = null;
            int highestThreat = -1;

            foreach (var slot in target.slots)
            {
                if (slot != null && slot.IsOccupied)
                {
                    int threat = slot.currentMonster.baseAtk * 2 + slot.currentMonster.currentDef;
                    if (threat > highestThreat)
                    {
                        highestThreat = threat;
                        strongest = slot;
                    }
                }
            }

            if (strongest != null)
            {
                string monsterName = strongest.currentMonster.data.name;
                target.graveyard.Add(strongest.currentMonster.data);
                strongest.ClearSlot();
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.LogCombatMessage($"💀 لعنة الإبادة السحيقة أعدمت [{monsterName}] فوراً إلى المقبرة!");
                }
            }
            else
            {
                target.TakeLeaderDamage(30);
            }
        }

        private void ExecuteArcaneBlastSpell(PlayerState target, int totalDamage)
        {
            int occupiedCount = 0;
            foreach (var slot in target.slots)
            {
                if (slot != null && slot.IsOccupied) occupiedCount++;
            }

            if (occupiedCount > 0)
            {
                int dmgPerMonster = Mathf.Max(8, totalDamage / (occupiedCount + 1));
                foreach (var slot in target.slots)
                {
                    if (slot != null && slot.IsOccupied)
                    {
                        slot.currentMonster.TakeDamage(dmgPerMonster);
                        if (!slot.currentMonster.IsAlive)
                        {
                            target.graveyard.Add(slot.currentMonster.data);
                            slot.ClearSlot();
                        }
                    }
                }
                target.TakeLeaderDamage(dmgPerMonster);
            }
            else
            {
                target.TakeLeaderDamage(totalDamage);
            }
            if (BattleUIManager.Instance != null) BattleUIManager.Instance.ShakeScreen();
        }

        public bool PlayEnvironmentCard(PlayerState player, CardData envCard)
        {
            if (envCard == null || envCard.cardType != CardType.Environment) return false;

            if (envCard.cost > player.currentPower)
            {
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.LogCombatMessage($"❌ الباور غير كافٍ لتفعيل بطاقة المناخ [{envCard.name}]! (المطلوب: {envCard.cost}⚡، المتاح: {player.currentPower}⚡)");
                }
                return false;
            }

            player.currentPower -= envCard.cost;
            player.hand.Remove(envCard);
            player.graveyard.Add(envCard);

            if (activeEnvironment != null && activeEnvironment.buffedElement == envCard.element)
            {
                // تكديس نفس البيئة: ترقية المستوى وتجديد المدة
                activeEnvironment.UpgradeLevel();
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.ShowEnvironmentBanner(activeEnvironment, player.playerName);
                    BattleUIManager.Instance.LogCombatMessage($"⚡ تصاعدت قوة بيئة [{activeEnvironment.name}] إلى المستوى {activeEnvironment.level} (مدتها: {activeEnvironment.turnsRemaining} جولات)!");
                }
            }
            else
            {
                // استبدال البيئة ببيئة جديدة مستوى 1
                activeEnvironment = new EnvironmentCardData(envCard.element, 1);
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.ShowEnvironmentBanner(activeEnvironment, player.playerName);
                    BattleUIManager.Instance.LogCombatMessage($"🌍 غيّر [{player.playerName}] مناخ الساحة إلى: [{activeEnvironment.name}] (المستوى 1)! تعزز {activeEnvironment.buffedElement} بنسبة +30%.");
                }
            }

            if (BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.UpdateEnvironmentVisuals();
                BattleUIManager.Instance.UpdateUI();
            }

            return true;
        }

        #endregion

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

            // إدارة مؤقت البيئة والمناخ في نهاية كل دورة
            if (activeEnvironment != null)
            {
                bool expired = activeEnvironment.TickTurn();
                if (expired)
                {
                    string oldName = activeEnvironment.name;
                    activeEnvironment = null;
                    if (BattleUIManager.Instance != null)
                    {
                        BattleUIManager.Instance.LogCombatMessage($"☀️ انقشعت عاصفة [{oldName}] وعادت أرض المعركة للمناخ الطبيعي المستقر.");
                        BattleUIManager.Instance.ShowBanner("☀️ عودة المناخ الطبيعي المستقر", new Color(0.7f, 0.85f, 1f));
                        BattleUIManager.Instance.UpdateEnvironmentVisuals();
                    }
                }
            }

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
            if (currentPhase == GamePhase.GameOver) return;

            if (!aiPlayer.IsAlive)
            {
                currentPhase = GamePhase.GameOver;
                Debug.Log("🏆 مبروك! لقد هزمت قائد الخصم وانتصرت في المعركة!");
                string envName = activeEnvironment != null ? activeEnvironment.name : "مناخ مستقر ☀️";
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.ShowGameOverScreen(true, currentTurn, humanPlayer.currentHp, aiPlayer.currentHp,
                        humanPlayer.monstersSummonedTotal, playerSpellsCastCount, envName, humanPlayer.manaCapacity);
                }
            }
            else if (!humanPlayer.IsAlive)
            {
                currentPhase = GamePhase.GameOver;
                Debug.Log("💀 سقط قائدك! لقد خسرت المعركة، حظاً أوفر في الجولة القادمة.");
                string envName = activeEnvironment != null ? activeEnvironment.name : "مناخ مستقر ☀️";
                if (BattleUIManager.Instance != null)
                {
                    BattleUIManager.Instance.ShowGameOverScreen(false, currentTurn, humanPlayer.currentHp, aiPlayer.currentHp,
                        humanPlayer.monstersSummonedTotal, playerSpellsCastCount, envName, humanPlayer.manaCapacity);
                }
            }
        }

        public void RestartGame()
        {
            Debug.Log("🔄 بدء معركة تكتيكية جديدة من الصفر...");

            for (int i = 0; i < humanPlayer.slots.Length; i++)
            {
                if (humanPlayer.slots[i] != null) humanPlayer.slots[i].ClearSlot();
                if (aiPlayer.slots[i] != null) aiPlayer.slots[i].ClearSlot();
            }

            playerSpellsCastCount = 0;
            activeEnvironment = null;

            if (BattleUIManager.Instance != null)
            {
                if (BattleUIManager.Instance.gameOverPanel != null)
                {
                    BattleUIManager.Instance.gameOverPanel.SetActive(false);
                }
                BattleUIManager.Instance.DeselectHandCards();
            }

            StartGame();
        }
    }
}
