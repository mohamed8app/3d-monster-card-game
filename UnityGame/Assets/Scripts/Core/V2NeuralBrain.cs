using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using MonsterCardGame.Data;

namespace MonsterCardGame.Core
{
    /// <summary>
    /// Pure-C# Zero-Dependency Inference Engine for Arena_Lab_V2's trained PyTorch FighterNet
    /// (OBS_DIM = 220 -> Linear(256) -> LayerNorm -> ReLU -> Linear(128) -> LayerNorm -> ReLU -> Policy(62) + Value(1)).
    /// Runs exact float32 inference natively inside Unity in < 0.2 ms per decision!
    /// </summary>
    public class V2NeuralBrain
    {
        public const int ExpectedObsDim = 220;
        public const int ExpectedActionDim = 62;

        public string ModelId { get; private set; }
        public int ObsDim { get; private set; }
        public int Hidden1 { get; private set; }
        public int Hidden2 { get; private set; }
        public int ActionDim { get; private set; }
        public bool LoadedFromLiveArenaLab { get; private set; }

        private float[] fc1W, fc1B;
        private float[] ln1W, ln1B;
        private float[] fc2W, fc2B;
        private float[] ln2W, ln2B;
        private float[] polW, polB;
        private float[] valW, valB;

        // Reusable inference scratchpads (zero GC per move)
        private float[] h1Buf;
        private float[] h2Buf;
        private float[] logitsBuf;

        private static readonly Dictionary<string, V2NeuralBrain> BrainCache =
            new Dictionary<string, V2NeuralBrain>(StringComparer.OrdinalIgnoreCase);

        private static string cachedLiveArenaDir = null;
        private static bool liveArenaDirSearched = false;

        private static readonly Dictionary<string, int> ElementIndexMap =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Fire"] = 0, ["Water"] = 1, ["Nature"] = 2, ["Electric"] = 3, ["Ice"] = 4,
                ["Earth"] = 5, ["Dark"] = 6, ["Light"] = 7, ["Air"] = 8, ["Desert"] = 9
            };

        public static void ClearCache()
        {
            BrainCache.Clear();
            liveArenaDirSearched = false;
            cachedLiveArenaDir = null;
        }

        /// <summary>
        /// Automatically locates the live `Arena_Lab_V2` training directory on disk relative to
        /// the Unity Editor project OR the standalone Windows .EXE (`Game_Windows_Exe/MonsterCardGame.exe`)
        /// so Unity ALWAYS plays against the newest trained neural brains & settled decks without rebuilding!
        /// </summary>
        public static string FindLiveArenaLabV2Directory()
        {
            if (liveArenaDirSearched) return cachedLiveArenaDir;
            liveArenaDirSearched = true;

            var candidates = new List<string>();
            try
            {
                string dataPath = Application.dataPath;
                if (!string.IsNullOrEmpty(dataPath))
                {
                    candidates.Add(Path.GetFullPath(Path.Combine(dataPath, "../../Arena_Lab_V2")));
                    candidates.Add(Path.GetFullPath(Path.Combine(dataPath, "../Arena_Lab_V2")));
                    candidates.Add(Path.GetFullPath(Path.Combine(dataPath, "../../../Arena_Lab_V2")));
                }
            }
            catch { }

            candidates.Add(@"D:\deck game\Arena_Lab_V2");

            foreach (var dir in candidates)
            {
                if (!string.IsNullOrEmpty(dir) &&
                    Directory.Exists(dir) &&
                    Directory.Exists(Path.Combine(dir, "saved_models")))
                {
                    cachedLiveArenaDir = dir;
                    Debug.Log($"🔗 Live Arena_Lab_V2 Directory Linked: {cachedLiveArenaDir}");
                    return cachedLiveArenaDir;
                }
            }

            cachedLiveArenaDir = null;
            return null;
        }

        public static V2NeuralBrain LoadForModel(string modelId, bool forceLiveReload = false)
        {
            if (string.IsNullOrEmpty(modelId)) return null;
            if (!forceLiveReload && BrainCache.TryGetValue(modelId, out var cached)) return cached;

            // 1. PRIORITY: Load LIVE latest trained neural brain directly from Arena_Lab_V2/saved_models/<modelId>/brain_unity.bytes
            string arenaDir = FindLiveArenaLabV2Directory();
            if (!string.IsNullOrEmpty(arenaDir))
            {
                string livePath = Path.Combine(arenaDir, "saved_models", modelId, "brain_unity.bytes");
                if (File.Exists(livePath))
                {
                    try
                    {
                        byte[] raw = File.ReadAllBytes(livePath);
                        if (raw != null && raw.Length >= 32)
                        {
                            var liveBrain = ParseBinary(modelId, raw);
                            liveBrain.LoadedFromLiveArenaLab = true;
                            BrainCache[modelId] = liveBrain;
                            Debug.Log($"🧠 [LIVE Arena_Lab_V2] Loaded latest Neural Brain [{modelId}] ({liveBrain.ObsDim}->{liveBrain.Hidden1}->{liveBrain.Hidden2}->{liveBrain.ActionDim}) from {livePath}");
                            return liveBrain;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"⚠️ Could not read live brain {livePath}: {ex.Message}. Falling back to embedded Resources.");
                    }
                }
            }

            // 2. FALLBACK: Load embedded Resources/V2Brains/<modelId>.bytes
            var asset = Resources.Load<TextAsset>($"V2Brains/{modelId}");
            if (asset == null || asset.bytes == null || asset.bytes.Length < 32)
            {
                Debug.LogWarning($"⚠️ V2NeuralBrain binary not found for [{modelId}]");
                return null;
            }

            try
            {
                var brain = ParseBinary(modelId, asset.bytes);
                brain.LoadedFromLiveArenaLab = false;
                BrainCache[modelId] = brain;
                Debug.Log($"🧠 [Embedded] Loaded Neural Brain [{modelId}] ({brain.ObsDim}->{brain.Hidden1}->{brain.Hidden2}->{brain.ActionDim})");
                return brain;
            }
            catch (Exception ex)
            {
                Debug.LogError($"❌ Failed to parse V2NeuralBrain [{modelId}]: {ex.Message}");
                return null;
            }
        }

        private static float[] ReadFloatArray(BinaryReader br, int count)
        {
            var arr = new float[count];
            byte[] raw = br.ReadBytes(count * 4);
            Buffer.BlockCopy(raw, 0, arr, 0, raw.Length);
            return arr;
        }

        private static V2NeuralBrain ParseBinary(string modelId, byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes))
            using (var br = new BinaryReader(ms))
            {
                byte[] magic = br.ReadBytes(4);
                if (magic[0] != 'V' || magic[1] != '2' || magic[2] != 'B' || magic[3] != 'R')
                    throw new InvalidDataException("Invalid V2BR neural checkpoint header.");

                uint version = br.ReadUInt32();
                int obsDim = (int)br.ReadUInt32();
                int h1 = (int)br.ReadUInt32();
                int h2 = (int)br.ReadUInt32();
                int actDim = (int)br.ReadUInt32();
                if (version != 1 || obsDim <= 0 || h1 <= 0 || h2 <= 0 || actDim <= 0)
                    throw new InvalidDataException($"Unsupported V2BR dimensions: v{version} {obsDim}/{h1}/{h2}/{actDim}");

                var b = new V2NeuralBrain
                {
                    ModelId = modelId,
                    ObsDim = obsDim,
                    Hidden1 = h1,
                    Hidden2 = h2,
                    ActionDim = actDim,
                    fc1W = ReadFloatArray(br, h1 * obsDim),
                    fc1B = ReadFloatArray(br, h1),
                    ln1W = ReadFloatArray(br, h1),
                    ln1B = ReadFloatArray(br, h1),
                    fc2W = ReadFloatArray(br, h2 * h1),
                    fc2B = ReadFloatArray(br, h2),
                    ln2W = ReadFloatArray(br, h2),
                    ln2B = ReadFloatArray(br, h2),
                    polW = ReadFloatArray(br, actDim * h2),
                    polB = ReadFloatArray(br, actDim),
                    valW = ReadFloatArray(br, h2),
                    valB = ReadFloatArray(br, 1),
                    h1Buf = new float[h1],
                    h2Buf = new float[h2],
                    logitsBuf = new float[actDim]
                };
                return b;
            }
        }

        private static void LinearLayerNormRelu(
            float[] input, int inDim,
            float[] weight, float[] bias,
            float[] lnW, float[] lnB,
            float[] output, int outDim)
        {
            float sum = 0f;
            for (int o = 0; o < outDim; o++)
            {
                int row = o * inDim;
                float acc = bias[o];
                for (int i = 0; i < inDim; i++)
                {
                    acc += weight[row + i] * input[i];
                }
                output[o] = acc;
                sum += acc;
            }

            float mean = sum / outDim;
            float varSum = 0f;
            for (int o = 0; o < outDim; o++)
            {
                float d = output[o] - mean;
                varSum += d * d;
            }
            float invStd = 1f / Mathf.Sqrt((varSum / outDim) + 1e-5f);

            for (int o = 0; o < outDim; o++)
            {
                float norm = (output[o] - mean) * invStd * lnW[o] + lnB[o];
                output[o] = norm > 0f ? norm : 0f;
            }
        }

        /// <summary>
        /// Runs exact FighterNet inference on obs[220] with legalMask[62] and picks the
        /// highest-probability legal action (with optional slight temperature sampling).
        /// </summary>
        public int SelectAction(
            float[] obs,
            bool[] legalMask,
            out float confidencePct,
            out float stateValue,
            bool deterministic = true)
        {
            LinearLayerNormRelu(obs, ObsDim, fc1W, fc1B, ln1W, ln1B, h1Buf, Hidden1);
            LinearLayerNormRelu(h1Buf, Hidden1, fc2W, fc2B, ln2W, ln2B, h2Buf, Hidden2);

            // Value head
            float val = valB[0];
            for (int i = 0; i < Hidden2; i++) val += valW[i] * h2Buf[i];
            stateValue = val;

            // Policy head + masking
            float maxLogit = float.NegativeInfinity;
            int bestAct = 0;
            for (int a = 0; a < ActionDim; a++)
            {
                if (a >= legalMask.Length || !legalMask[a])
                {
                    logitsBuf[a] = -1e9f;
                    continue;
                }
                int row = a * Hidden2;
                float acc = polB[a];
                for (int i = 0; i < Hidden2; i++) acc += polW[row + i] * h2Buf[i];
                logitsBuf[a] = acc;
                if (acc > maxLogit)
                {
                    maxLogit = acc;
                    bestAct = a;
                }
            }

            // Compute softmax confidence for telemetry / debugging
            float expSum = 0f;
            for (int a = 0; a < ActionDim; a++)
            {
                if (a < legalMask.Length && legalMask[a])
                    expSum += Mathf.Exp(Mathf.Clamp(logitsBuf[a] - maxLogit, -20f, 0f));
            }

            if (deterministic || expSum <= 0f)
            {
                confidencePct = expSum > 0f ? (100f / expSum) : 100f;
                return bestAct;
            }

            float r = UnityEngine.Random.value * expSum;
            float cum = 0f;
            for (int a = 0; a < ActionDim; a++)
            {
                if (a >= legalMask.Length || !legalMask[a]) continue;
                float p = Mathf.Exp(Mathf.Clamp(logitsBuf[a] - maxLogit, -20f, 0f));
                cum += p;
                if (cum >= r)
                {
                    confidencePct = 100f * p / expSum;
                    return a;
                }
            }

            confidencePct = 100f / Mathf.Max(1e-5f, expSum);
            return bestAct;
        }

        private static bool HasAnyPassiveKind(MonsterInstance u, params string[] kinds)
        {
            if (u == null || u.data == null || u.silencedTurns > 0) return false;
            var mops = V2CardMechanicsEngine.GetMonsterOps(u.data);
            if (mops == null || mops.passives == null) return false;
            for (int i = 0; i < mops.passives.Count; i++)
            {
                var k = mops.passives[i]?.kind;
                if (string.IsNullOrEmpty(k)) continue;
                for (int j = 0; j < kinds.Length; j++)
                {
                    if (string.Equals(k, kinds[j], StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        private static bool HasTaunt(MonsterInstance u)
        {
            if (u == null || u.silencedTurns > 0) return false;
            return HasAnyPassiveKind(u, "taunt", "taunt_reflect_pct", "taunt_evasion")
                || u.HasPassiveSubstr("Taunt") != null
                || u.HasPassiveSubstr("Vanguard") != null;
        }

        private static bool HasStealth(MonsterInstance u)
        {
            if (u == null || u.silencedTurns > 0) return false;
            return HasAnyPassiveKind(u, "stealth") || u.HasPassiveSubstr("Stealth") != null;
        }

        private static bool IsMultiStrikeOrAllAttack(MonsterInstance u)
        {
            if (u == null) return false;
            return u.doubleStrike || u.IsAllAttack || HasAnyPassiveKind(u, "double_strike", "all_attack");
        }

        private static bool IsDefenseOrImmune(MonsterInstance u)
        {
            if (u == null) return false;
            return HasAnyPassiveKind(u,
                "reflect", "reflect_flat", "reflect_pct", "taunt_reflect_pct",
                "evasion", "taunt_evasion", "spell_immune", "status_immune",
                "rooted_immune_regen", "undying_1def", "undying_once");
        }

        private static bool IsEngineOrAura(MonsterInstance u)
        {
            if (u == null) return false;
            return HasAnyPassiveKind(u,
                "turn_heal_aura", "turn_heal_def", "turn_heal_hp", "cleanse_heal_aura", "cleanse_aura",
                "spell_power_aura", "spell_ping", "skill_conduit", "cost_discount",
                "on_spell_grow", "on_spell_buff", "on_skill_grow", "on_death_grow", "death_growth",
                "on_death_dmg", "aura_weaken_opp", "hydra_split", "ascend", "grave_scaling", "pack_aura");
        }

        private static bool IsLeechOrOnHitDot(MonsterInstance u)
        {
            if (u == null) return false;
            return HasAnyPassiveKind(u,
                "life_leech", "on_hit_burn", "on_hit_poison", "on_hit_bleed", "enrage", "strip_buffs_hit");
        }

        private static bool IsCleaveOrPierce(MonsterInstance u)
        {
            if (u == null) return false;
            return HasAnyPassiveKind(u, "cleave", "bypass", "pierce");
        }

        private static int CountBoardUnits(PlayerState p)
        {
            int c = 0;
            if (p != null && p.slots != null)
            {
                for (int i = 0; i < p.slots.Length; i++)
                    if (p.slots[i] != null && p.slots[i].IsOccupied && p.slots[i].currentMonster != null && p.slots[i].currentMonster.IsAlive)
                        c++;
            }
            return c;
        }

        /// <summary>
        /// Builds the exact 220-float normalized observation vector mirroring `battle_engine.py::get_obs()`,
        /// including the 60 Pure-RL Passive/Active/Status indicator lights at indices 160..219.
        /// </summary>
        public static float[] BuildObservation220(
            PlayerState p,
            PlayerState opp,
            int currentTurn,
            EnvironmentCardData activeEnv)
        {
            var obs = new float[ExpectedObsDim];
            if (p == null || opp == null) return obs;

            int envIdx = -1;
            if (activeEnv != null)
            {
                string norm = EnvironmentCardData.NormalizeElement(activeEnv.element);
                if (!ElementIndexMap.TryGetValue(norm, out envIdx)) envIdx = -1;
            }

            // 0..19: Global scalars
            obs[0] = p.currentHp / 500f;
            obs[1] = opp.currentHp / 500f;
            obs[2] = p.currentMana / 10f;
            obs[3] = Mathf.Min(10, p.power) / 10f;
            obs[4] = Mathf.Min(10, opp.power) / 10f;
            obs[5] = (p.hand != null ? p.hand.Count : 0) / 7f;
            obs[6] = (opp.hand != null ? opp.hand.Count : 0) / 7f;
            obs[7] = (p.deck != null ? p.deck.Count : 0) / 50f;
            obs[8] = (opp.deck != null ? opp.deck.Count : 0) / 50f;
            obs[9] = (p.graveyard != null ? p.graveyard.Count : 0) / 30f;
            obs[10] = (opp.graveyard != null ? opp.graveyard.Count : 0) / 30f;
            obs[11] = Mathf.Min(1f, currentTurn / 40f);
            obs[12] = p.hasSacrificedThisTurn ? 1f : 0f;
            obs[13] = V2CardMechanicsEngine.IsLeaderActiveForPlayer(p) ? 1f : 0f;
            obs[14] = V2CardMechanicsEngine.IsCovenantActiveForPlayer(p) ? 1f : 0f;
            obs[15] = V2CardMechanicsEngine.IsLeaderActiveForPlayer(opp) ? 1f : 0f;
            obs[16] = V2CardMechanicsEngine.IsCovenantActiveForPlayer(opp) ? 1f : 0f;
            obs[17] = envIdx >= 0 ? (envIdx + 1) / 10f : 0f;
            obs[18] = activeEnv != null ? activeEnv.level / 3f : 0f;
            obs[19] = activeEnv != null ? Mathf.Min(1f, activeEnv.turnsRemaining / 10f) : 0f;

            // 20..73: Ally board (6 slots x 9 features)
            int idx = 20;
            for (int sl = 0; sl < 6; sl++)
            {
                var u = (p.slots != null && sl < p.slots.Length && p.slots[sl] != null && p.slots[sl].IsOccupied)
                    ? p.slots[sl].currentMonster : null;
                if (u != null && u.IsAlive)
                {
                    obs[idx] = 1f;
                    obs[idx + 1] = u.GetEffectiveAtk(activeEnv, p) / 35f;
                    obs[idx + 2] = u.currentDef / 60f;
                    obs[idx + 3] = (u.canAttack && u.frozenTurns <= 0) ? 1f : 0f;
                    obs[idx + 4] = HasTaunt(u) ? 1f : 0f;
                    obs[idx + 5] = u.IsBarrier ? 1f : 0f;
                    obs[idx + 6] = (u.frozenTurns > 0 || u.silencedTurns > 0) ? 1f : 0f;
                    obs[idx + 7] = (u.burnDmg + u.poisonDmg + u.bleedDmg) / 15f;
                    obs[idx + 8] = u.ActiveSkillCount / 2f;
                }
                idx += 9;
            }

            // 74..127: Enemy board (6 slots x 9 features)
            for (int sl = 0; sl < 6; sl++)
            {
                var u = (opp.slots != null && sl < opp.slots.Length && opp.slots[sl] != null && opp.slots[sl].IsOccupied)
                    ? opp.slots[sl].currentMonster : null;
                if (u != null && u.IsAlive)
                {
                    obs[idx] = 1f;
                    obs[idx + 1] = u.GetEffectiveAtk(activeEnv, opp) / 35f;
                    obs[idx + 2] = u.currentDef / 60f;
                    obs[idx + 3] = HasTaunt(u) ? 1f : 0f;
                    obs[idx + 4] = HasStealth(u) ? 1f : 0f;
                    obs[idx + 5] = u.IsBarrier ? 1f : 0f;
                    obs[idx + 6] = 0f; // spy
                    obs[idx + 7] = (u.burnDmg + u.poisonDmg + u.bleedDmg) / 15f;
                    obs[idx + 8] = (u.data != null ? u.data.cost : 1) / 7f;
                }
                idx += 9;
            }

            // 128..155: Hand cards (up to 7 x 4 features)
            int handCount = p.hand != null ? p.hand.Count : 0;
            for (int h = 0; h < 7; h++)
            {
                if (h < handCount && p.hand[h] != null)
                {
                    var c = p.hand[h];
                    float tc = 0.2f;
                    if (c.cardType == CardType.Spell) tc = 0.4f;
                    else if (c.cardType == CardType.Environment) tc = 0.6f;
                    else if (c.cardType == CardType.Leader) tc = 0.8f;
                    else if (c.cardType == CardType.Covenant) tc = 1.0f;

                    int effCost = p.GetEffectiveCardCost(c);
                    obs[idx] = tc;
                    obs[idx + 1] = effCost / 7f;
                    obs[idx + 2] = effCost <= p.currentMana ? 1f : 0f;
                    obs[idx + 3] = c.cardType == CardType.Monster
                        ? (c.atk + c.def) / 70f
                        : Mathf.Max(1, c.tier_num) / 7f;
                }
                idx += 4;
            }

            // 156..159: Summary
            obs[156] = CountBoardUnits(p) / 6f;
            obs[157] = CountBoardUnits(opp) / 6f;
            obs[158] = 0f;
            obs[159] = p.freeSkillBonus / 10f;

            // 160..177: Ally board passive lights (6 x 3)
            idx = 160;
            for (int sl = 0; sl < 6; sl++)
            {
                var u = (p.slots != null && sl < p.slots.Length && p.slots[sl] != null && p.slots[sl].IsOccupied)
                    ? p.slots[sl].currentMonster : null;
                if (u != null && u.IsAlive)
                {
                    obs[idx] = (IsMultiStrikeOrAllAttack(u) || IsCleaveOrPierce(u)) ? 1f : 0f;
                    obs[idx + 1] = IsEngineOrAura(u) ? 1f : 0f;
                    obs[idx + 2] = (IsDefenseOrImmune(u) || HasAnyPassiveKind(u, "life_leech")) ? 1f : 0f;
                }
                idx += 3;
            }

            // 178..219: Enemy board passive/active/status lights (6 x 7)
            for (int sl = 0; sl < 6; sl++)
            {
                var u = (opp.slots != null && sl < opp.slots.Length && opp.slots[sl] != null && opp.slots[sl].IsOccupied)
                    ? opp.slots[sl].currentMonster : null;
                if (u != null && u.IsAlive)
                {
                    obs[idx] = IsMultiStrikeOrAllAttack(u) ? 1f : 0f;
                    obs[idx + 1] = IsDefenseOrImmune(u) ? 1f : 0f;
                    obs[idx + 2] = IsLeechOrOnHitDot(u) ? 1f : 0f;
                    obs[idx + 3] = IsEngineOrAura(u) ? 1f : 0f;
                    obs[idx + 4] = IsCleaveOrPierce(u) ? 1f : 0f;
                    obs[idx + 5] = u.ActiveSkillCount / 2f;
                    obs[idx + 6] = (u.frozenTurns > 0 || u.silencedTurns > 0 || u.rootedTurns > 0) ? 1f : 0f;
                }
                idx += 7;
            }

            return obs;
        }

        /// <summary>
        /// Builds the exact 62-action legality mask mirroring `battle_engine.py::get_legal_mask()`.
        ///   0       : End Turn
        ///   1..7    : Sacrifice hand[h]
        ///   8..49   : Play hand[h] targeting slot s (8 + h * 6 + s)
        ///   50..61  : Activate slot s active skill k (50 + s * 2 + k)
        /// </summary>
        public static bool[] BuildLegalActionMask62(PlayerState p, PlayerState opp, GameManager gm)
        {
            var mask = new bool[ExpectedActionDim];
            mask[0] = true; // End Turn is always legal
            if (p == null || opp == null || gm == null) return mask;

            int handCount = Mathf.Min(7, p.hand != null ? p.hand.Count : 0);

            // 1..7: Sacrifice hand card h
            if (!p.hasSacrificedThisTurn && p.denyDrawSacTurns <= 0 && p.power < V2CardMechanicsEngine.GetPlayerManaCap(p))
            {
                for (int h = 0; h < handCount; h++)
                {
                    if (p.hand[h] != null) mask[1 + h] = true;
                }
            }

            // 8..49: Play hand[h] at slot s
            for (int h = 0; h < handCount; h++)
            {
                var c = p.hand[h];
                if (c == null) continue;
                int effCost = p.GetEffectiveCardCost(c);
                if (effCost > p.currentMana) continue;

                int baseAct = 8 + h * 6;
                if (c.cardType == CardType.Monster)
                {
                    if (!V2CardMechanicsEngine.CanPlayerSummonMonster(p, c, out _)) continue;
                    for (int sl = 0; sl < 6; sl++)
                    {
                        if (p.slots != null && sl < p.slots.Length && p.slots[sl] != null && !p.slots[sl].IsOccupied)
                            mask[baseAct + sl] = true;
                    }
                }
                else if (c.cardType == CardType.Spell)
                {
                    if (V2CardMechanicsEngine.IsCovenantActiveForPlayer(p))
                    {
                        var cov = V2CardMechanicsEngine.GetCovenantOps(p.activeCovenant);
                        if (cov != null && cov.ban_self_spells) continue;
                    }
                    if (!gm.IsSpellPlayable(p, opp, c, out _)) continue;
                    SpellDatabase.NormalizeSpellMetadata(c);
                    var tmode = SpellDatabase.GetTargetMode(c.id);
                    if (tmode == SpellTargetMode.Enemy)
                    {
                        bool anyTarget = false;
                        for (int sl = 0; sl < 6; sl++)
                        {
                            var es = opp.slots != null && sl < opp.slots.Length ? opp.slots[sl] : null;
                            if (es != null && es.IsOccupied && es.currentMonster != null && es.currentMonster.IsAlive &&
                                es.currentMonster.HasPassiveSubstr("Stealth") == null)
                            {
                                mask[baseAct + sl] = true;
                                anyTarget = true;
                            }
                        }
                        if (!anyTarget) mask[baseAct] = true;
                    }
                    else if (tmode == SpellTargetMode.Ally)
                    {
                        bool anyTarget = false;
                        for (int sl = 0; sl < 6; sl++)
                        {
                            var al = p.slots != null && sl < p.slots.Length ? p.slots[sl] : null;
                            if (al != null && al.IsOccupied && al.currentMonster != null && al.currentMonster.IsAlive)
                            {
                                mask[baseAct + sl] = true;
                                anyTarget = true;
                            }
                        }
                        if (!anyTarget) mask[baseAct] = true;
                    }
                    else
                    {
                        mask[baseAct] = true;
                    }
                }
                else if (c.cardType == CardType.Environment)
                {
                    mask[baseAct] = true;
                }
                else if (c.cardType == CardType.Leader)
                {
                    if (!p.leaderExorcised && (p.activeLeader == null || p.activeLeader.id != c.id))
                        mask[baseAct] = true;
                }
                else if (c.cardType == CardType.Covenant)
                {
                    if (!p.covenantDestroyed && p.activeCovenant == null)
                    {
                        var cops = V2CardMechanicsEngine.GetCovenantOps(c);
                        if (cops == null || cops.self_hp_init_loss <= 0 || p.currentHp > cops.self_hp_init_loss)
                            mask[baseAct] = true;
                    }
                }
            }

            // 50..61: Active skills on board slots 0..5, skill 0..1
            for (int sl = 0; sl < 6; sl++)
            {
                var bs = p.slots != null && sl < p.slots.Length ? p.slots[sl] : null;
                if (bs == null || !bs.IsOccupied || bs.currentMonster == null) continue;
                var m = bs.currentMonster;
                if (!m.IsAlive || m.frozenTurns > 0 || m.silencedTurns > 0 || !m.HasActiveSkill) continue;
                var mops = V2CardMechanicsEngine.GetMonsterOps(m.data);
                for (int k = 0; k < Mathf.Min(2, m.ActiveSkillCount); k++)
                {
                    if (m.IsActiveSkillUsed(k, p)) continue;
                    int skillCost = 1;
                    if (mops != null && mops.actives != null && k < mops.actives.Count && mops.actives[k] != null)
                        skillCost = Mathf.Max(0, mops.actives[k].cost);
                    if (p.freeSkillManaZero) skillCost = 0;
                    if (skillCost <= p.currentMana)
                        mask[50 + sl * 2 + k] = true;
                }
            }

            return mask;
        }
    }
}
