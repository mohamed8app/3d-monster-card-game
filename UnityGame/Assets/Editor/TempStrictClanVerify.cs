#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MonsterCardGame.Core;
using MonsterCardGame.Data;
using MonsterCardGame.UI;
using MonsterCardGame.Utils;

namespace MonsterCardGame.Editor
{
    public static class TempStrictClanVerify
    {
        public static void VerifyAll()
        {
            int failures = 0;
            void Assert(bool cond, string msg)
            {
                if (cond) Debug.Log($"[PASS] {msg}");
                else { Debug.LogError($"[FAIL] {msg}"); failures++; }
            }

            EditorSceneManager.OpenScene("Assets/Scenes/MainBattleScene.unity", OpenSceneMode.Single);
            var gm = UnityEngine.Object.FindFirstObjectByType<GameManager>();
            var cs = UnityEngine.Object.FindFirstObjectByType<CombatSystem>();
            var ui = UnityEngine.Object.FindFirstObjectByType<BattleUIManager>();

            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            typeof(GameManager).GetMethod("Awake", flags)?.Invoke(gm, null);
            typeof(CombatSystem).GetMethod("Awake", flags)?.Invoke(cs, null);
            typeof(BattleUIManager).GetMethod("Awake", flags)?.Invoke(ui, null);
            typeof(GameManager).GetMethod("LoadMonsterDatabase", flags)?.Invoke(gm, null);
            typeof(GameManager).GetMethod("BuildCatalogIdIndex", flags)?.Invoke(gm, null);

            // 1. Turn-1 Opening Hand Guarantee Test via real StartGame()
            gm.StartGame();
            var h = gm.humanPlayer;
            var a = gm.aiPlayer;
            Assert(h.hand.Count == 5, $"Turn-1 Opening Hand size == 5 (got {h.hand.Count})");
            Assert(h.hand[0] != null && h.hand[0].cardType == CardType.Leader && MonsterArtLoader.HasCustomCardArt(h.hand[0]),
                $"Turn-1 hand[0] is guaranteed playable circular Commander Spirit: {h.hand[0]?.name} (#{h.hand[0]?.id})");
            Assert(h.hand[0] != null && h.hand[0].cost == 0,
                $"Turn-1 opening Spirit has awakening cost 0 for instant Turn-1 trial (cost={h.hand[0]?.cost})");

            var openingCard = h.hand[0];
            bool playedTurn1 = gm.PlayLeaderCard(h, openingCard);
            Assert(playedTurn1 && h.activeLeader == openingCard, "Player summoned opening Commander Spirit immediately on Turn 1 at 0 Mana");

            // 2. Strict Clan-Only Buff & Anti-Rival Debuff Tests
            MonsterInstance MakeInst(string tribe, string elem)
            {
                return new MonsterInstance(new CardData { id = 999, name = tribe, cardType = CardType.Monster, tribe = tribe, element = elem, role = "Balanced", atk = 20, def = 40 }, 1);
            }

            var alliedGhost = MakeInst("Ghosts", "Dark");
            var alliedDemonDark = MakeInst("Demons", "Dark"); // Same element Dark, DIFFERENT clan!
            var alliedWarrior = MakeInst("Warriors", "Fire");
            var alliedGiant = MakeInst("Giants", "Earth");
            var alliedMageIce = MakeInst("Mages", "Ice");      // Same element Ice as #468, DIFFERENT clan!

            h.slots[0].PlaceMonster(alliedGhost);
            h.slots[1].PlaceMonster(alliedDemonDark);
            h.slots[2].PlaceMonster(alliedWarrior);
            h.slots[3].PlaceMonster(alliedGiant);
            h.slots[4].PlaceMonster(alliedMageIce);

            // 451 Archlich Valkor (Ghosts/Dark): must buff Ghosts ONLY, zero leak to Demons/Dark
            h.activeLeader = gm.allLeaders.Find(l => l.id == 451);
            Assert(alliedGhost.GetEffectiveAtk(null) == 25, $"451 Ghosts +5 ATK: {alliedGhost.GetEffectiveAtk(null)} == 25");
            Assert(alliedDemonDark.GetEffectiveAtk(null) == 20, $"451 Demons(Dark) +0 ATK (zero element leak): {alliedDemonDark.GetEffectiveAtk(null)} == 20");
            Assert(alliedWarrior.GetEffectiveAtk(null) == 20, $"451 Warriors +0 ATK: {alliedWarrior.GetEffectiveAtk(null)} == 20");

            // 468 Basalt Matriarch Skaya (Giants/Ice): fortify Giants ONLY, zero leak to Mages/Ice; enemy non-Giants lose -3 ATK
            h.activeLeader = gm.allLeaders.Find(l => l.id == 468);
            int giantDefBefore = alliedGiant.currentDef;
            int mageDefBefore = alliedMageIce.currentDef;
            CommanderSpiritEffects.ApplyOnSummoned(h, a, h.activeLeader);
            Assert(alliedGiant.GetEffectiveAtk(null) == 25, $"468 Giants +5 ATK: {alliedGiant.GetEffectiveAtk(null)} == 25");
            Assert(alliedGiant.currentDef == giantDefBefore + 7, $"468 Giants +7 DEF on summon");
            Assert(alliedMageIce.GetEffectiveAtk(null) == 20 && alliedMageIce.currentDef == mageDefBefore,
                $"468 Mages(Ice) receives 0 ATK & 0 DEF (zero element/all-ally leak)");

            a.activeLeader = gm.allLeaders.Find(l => l.id == 468);
            Assert(CommanderSpiritEffects.GetEnemyDreadAtkPenalty(a, alliedWarrior) == 3, "468 applies -3 ATK dread to rival clan (Warriors)");
            Assert(CommanderSpiritEffects.GetEnemyDreadAtkPenalty(a, alliedGiant) == 0, "468 applies 0 dread penalty to Giants mirror");

            // 458 Astrea (Mages): skill discount strictly for Mages ONLY
            h.activeLeader = gm.allLeaders.Find(l => l.id == 458);
            Assert(CommanderSpiritEffects.GetSkillManaDiscount(h, alliedMageIce) == 1, "458 gives -1 Skill Mana to Mages");
            Assert(CommanderSpiritEffects.GetSkillManaDiscount(h, alliedGhost) == 0, "458 gives 0 Skill Mana discount to Ghosts");

            // 453 Malakor (Demons): burn-on-hit strictly for Demons ONLY
            h.activeLeader = gm.allLeaders.Find(l => l.id == 453);
            var enemyTarget = MakeInst("Elves", "Air");
            CommanderSpiritEffects.OnCombatHitDealt(h, alliedWarrior, a, enemyTarget, 15);
            Assert(enemyTarget.burnDmg == 0, "453 Warrior(Fire) hit does NOT burn (Demons only)");
            CommanderSpiritEffects.OnCombatHitDealt(h, alliedDemonDark, a, enemyTarget, 15);
            Assert(enemyTarget.burnDmg >= 4, "453 Demon hit applies Burn >= 4");

            // 461 Fenrir (Beasts): kill feast strictly for Beasts ONLY
            h.activeLeader = gm.allLeaders.Find(l => l.id == 461);
            int warBonusBefore = alliedWarrior.bonusAtk;
            CommanderSpiritEffects.OnMonsterDied(a, h, enemyTarget, alliedWarrior);
            Assert(alliedWarrior.bonusAtk == warBonusBefore, "461 non-Beast killer receives +0 ATK");
            var alliedBeast = MakeInst("Beasts", "Fire");
            CommanderSpiritEffects.OnMonsterDied(a, h, enemyTarget, alliedBeast);
            Assert(alliedBeast.bonusAtk == 4, "461 Beast killer receives +4 ATK");

            Debug.Log($"=== TEMP_STRICT_CLAN_VERIFY failures={failures} ===");
            EditorApplication.Exit(failures > 0 ? 1 : 0);
        }
    }
}
#endif
