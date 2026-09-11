#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Game.Character.EditorTools
{
    /// <summary>
    /// Override Controller 工作流工具(TR-1.2,2026-07-30 建立)。
    ///
    /// 原则:一份主 Animator Controller(DefaultCharacterController)作逻辑骨架,
    /// 各角色用 AnimatorOverrideController 换 Clip,禁止再整份复制 473KB Controller。
    ///
    /// 能力:
    ///  1. 从"现有复制版 Controller"迁移为 Override(自动按 Clip 名比对差异);
    ///  2. 从 CharacterAnimSetAsset 同步 Override 映射(新角色量产路径);
    ///  3. 同步检查:主 Controller 变更后验证 Override 覆盖是否仍然有效。
    /// </summary>
    public static class OverrideControllerTool
    {
        public const string MasterControllerPath =
            "Assets/_Game/character/Common/DefaultCharacterController.controller";

        // ─────────────────────────────────────────────────────────────
        //  迁移能力(无菜单):新角色从"现有复制版 Controller"提取 Override 时调用
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 以 master 为骨架,把 source(现有复制版 Controller)的差异化 Clip 提取为 Override。
        /// 比对规则:按 Clip 名匹配;source 与 master 同名不同引用的 Clip 加入映射。
        /// </summary>
        public static AnimatorOverrideController CreateOverrideFromExisting(
            AnimatorController master, AnimatorController source,
            string folder, string name, out string report)
        {
            var sb = new StringBuilder();
            if (master == null || source == null)
            {
                report = "[OverrideTool] master/source 为空。";
                return null;
            }

            // master 全部 Clip 按名索引(同名多份时保留第一份并记录)
            var masterByName = new Dictionary<string, AnimationClip>();
            foreach (var clip in master.animationClips)
            {
                if (clip == null) continue;
                if (!masterByName.ContainsKey(clip.name))
                    masterByName.Add(clip.name, clip);
            }

            // source 中引用但 master 没有的 Clip(无法 Override 锚定)
            var missing = new List<string>();
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            var seen = new HashSet<AnimationClip>();

            foreach (var srcClip in source.animationClips)
            {
                if (srcClip == null || seen.Contains(srcClip)) continue;
                seen.Add(srcClip);

                AnimationClip masterClip;
                if (!masterByName.TryGetValue(srcClip.name, out masterClip))
                {
                    missing.Add(srcClip.name);
                    continue;
                }
                if (masterClip != srcClip)
                    overrides.Add(new KeyValuePair<AnimationClip, AnimationClip>(masterClip, srcClip));
            }

            var ov = new AnimatorOverrideController(master);
            ov.name = name;
            ov.ApplyOverrides(overrides);
            int stateMappings = SyncFromController(ov, source);

            string path = $"{folder}/{name}.overrideController";
            AssetDatabase.DeleteAsset(path); // 重跑幂等
            AssetDatabase.CreateAsset(ov, path);
            AssetDatabase.SaveAssets();

            sb.AppendLine($"[OverrideTool] 迁移完成 → {path}");
            sb.AppendLine($"  Override 映射 {overrides.Count + stateMappings} 条(master {master.animationClips.Length} Clips, source {source.animationClips.Length} Clips)");
            sb.AppendLine($"  按 Animator 状态/BlendTree 自动补齐 {stateMappings} 条");
            if (missing.Count > 0)
            {
                sb.AppendLine($"  ⚠ source 独有 Clip(主 Controller 无锚点,不会生效,需人工处理) {missing.Count} 条:");
                foreach (var m in missing.Distinct()) sb.AppendLine($"    - {m}");
            }
            report = sb.ToString();
            return ov;
        }

        /// <summary>
        /// 按主 Controller 与角色 Controller 的 Animator 状态/BlendTree 结构补齐 Override。
        /// 这是 Locomotion、Jump、Falling、Turn、Crouch 等没有写入 AnimSet 的动画的自动填充路径。
        /// </summary>
        public static int SyncFromController(AnimatorOverrideController ov, AnimatorController source)
        {
            if (ov == null || source == null) return 0;
            var master = ov.runtimeAnimatorController as AnimatorController;
            if (master == null) return 0;

            var current = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            ov.GetOverrides(current);
            var map = new Dictionary<AnimationClip, AnimationClip>();
            // 种子映射。注意 ApplyOverrides 只会覆盖/清除列表中显式给出的键：
            // 因此指向 __preview__ 预览 Clip 的条目必须显式置 null（而不是从 map 移除），
            // 才能清除该锚点的 Override，让状态回退到主控制器原始共享 Clip。
            // 预览 Clip 是导入器内部对象，运行时无有效动画数据（无根位移），序列化为目标即坏。
            foreach (var kv in current)
            {
                if (kv.Key == null) continue;
                map[kv.Key] = IsPreviewClip(kv.Value) ? null : kv.Value;
            }

            var masterEntries = new List<StateMotionEntry>();
            var sourceEntries = new List<StateMotionEntry>();
            foreach (var layer in master.layers)
                CollectStateMotionEntries(layer.stateMachine, masterEntries);
            foreach (var layer in source.layers)
                CollectStateMotionEntries(layer.stateMachine, sourceEntries);

            int changed = 0;
            for (int i = 0; i < masterEntries.Count; i++)
            {
                var masterEntry = masterEntries[i];
                int occurrence = 0;
                for (int j = 0; j < i; j++)
                    if (masterEntries[j].name == masterEntry.name) occurrence++;

                var sourceEntry = sourceEntries
                    .Where((entry, index) => entry.name == masterEntry.name
                        && sourceEntries.Take(index).Count(previous => previous.name == masterEntry.name) == occurrence)
                    .FirstOrDefault();
                if (sourceEntry == null) continue;

                int count = Mathf.Min(masterEntry.clips.Count, sourceEntry.clips.Count);
                for (int clipIndex = 0; clipIndex < count; clipIndex++)
                {
                    var masterClip = masterEntry.clips[clipIndex];
                    var sourceClip = sourceEntry.clips[clipIndex];
                    if (masterClip == null || sourceClip == null || masterClip == sourceClip) continue;
                    if (!map.TryGetValue(masterClip, out var currentClip) || currentClip != sourceClip)
                    {
                        map[masterClip] = sourceClip;
                        changed++;
                    }
                }
            }

            ov.ApplyOverrides(map.Select(kv => new KeyValuePair<AnimationClip, AnimationClip>(kv.Key, kv.Value)).ToList());
            EditorUtility.SetDirty(ov);
            return changed;
        }

        private const string SharedAnimationRoot = "Assets/_Game/Animations";

        private sealed class AnimationClipCandidate
        {
            public AnimationClip clip;
            public string path;
            public int priority;
            public HashSet<string> keys = new HashSet<string>();
        }

        /// <summary>
        /// 从角色专属动画目录和共享动画目录填充 Override。
        /// 角色目录优先，共享目录作为缺省；技能 AnimSet 在调用方随后覆盖这些推断结果。
        /// </summary>
        public static int SyncFromAnimationDirectories(
            AnimatorOverrideController ov,
            string characterFolder,
            string sharedFolder = SharedAnimationRoot)
        {
            if (ov == null) return 0;

            var roots = new List<string>();
            if (!string.IsNullOrEmpty(characterFolder) && AssetDatabase.IsValidFolder(characterFolder))
                roots.Add(characterFolder);
            if (!string.IsNullOrEmpty(sharedFolder)
                && AssetDatabase.IsValidFolder(sharedFolder)
                && !roots.Contains(sharedFolder))
                roots.Add(sharedFolder);
            if (roots.Count == 0) return 0;

            var candidates = CollectAnimationCandidates(roots);
            if (candidates.Count == 0) return 0;

            var current = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            ov.GetOverrides(current);
            var map = new Dictionary<AnimationClip, AnimationClip>();
            // 种子映射。注意 ApplyOverrides 只会覆盖/清除列表中显式给出的键：
            // 因此指向 __preview__ 预览 Clip 的条目必须显式置 null（而不是从 map 移除），
            // 才能清除该锚点的 Override，让状态回退到主控制器原始共享 Clip。
            // 预览 Clip 是导入器内部对象，运行时无有效动画数据（无根位移），序列化为目标即坏。
            foreach (var kv in current)
            {
                if (kv.Key == null) continue;
                map[kv.Key] = IsPreviewClip(kv.Value) ? null : kv.Value;
            }

            var master = ov.runtimeAnimatorController as AnimatorController;
            if (master == null) return 0;

            var entries = new List<StateMotionEntry>();
            foreach (var layer in master.layers)
                CollectStateMotionEntries(layer.stateMachine, entries);

            int changed = 0;
            foreach (var entry in entries)
            {
                foreach (var anchor in entry.clips)
                {
                    if (anchor == null) continue;
                    var candidate = FindBestCandidate(entry.name, anchor.name, candidates, anchor);
                    bool strictAction = IsStrictActionState(entry.name);
                    bool directional = IsDirectionalMotionName(anchor.name);
                    bool crouchLoco = IsCrouchMotionName(anchor.name);
                    if (strictAction || directional || crouchLoco)
                    {
                        // 翻越/攀爬/梯子动作 + 八向移动/原地转身动画不能接受泛化候选：
                        // 找不到同语义/同方向 Clip 时必须清空旧 Override，回退主 Controller 的有效共享 Clip。
                        if (candidate == null || candidate.clip == null || candidate.clip == anchor)
                        {
                            if (map.TryGetValue(anchor, out var stale) && stale != null)
                            {
                                map[anchor] = null;
                                changed++;
                            }
                            continue;
                        }
                    }
                    if (candidate == null || candidate.clip == null || candidate.clip == anchor)
                        continue;
                    if (!map.TryGetValue(anchor, out var currentClip) || currentClip != candidate.clip)
                    {
                        map[anchor] = candidate.clip;
                        changed++;
                    }
                }
            }

            foreach (var kv in current)
            {
                if (kv.Key == null || map.ContainsKey(kv.Key)) continue;
                var candidate = FindBestCandidate(string.Empty, kv.Key.name, candidates, kv.Key);
                if (candidate == null || candidate.clip == null || candidate.clip == kv.Key)
                {
                    // 方向性幽灵锚点（主 Controller 已不含但旧 Override 仍残留）匹配不到时同样清空，
                    // 避免历史错误映射（如 WalkRight→Walk）被永久保留。
                    if (IsDirectionalMotionName(kv.Key.name) && kv.Value != null)
                    {
                        map[kv.Key] = null;
                        changed++;
                    }
                    continue;
                }
                map[kv.Key] = candidate.clip;
                changed++;
            }

            ov.ApplyOverrides(map.Select(kv => new KeyValuePair<AnimationClip, AnimationClip>(kv.Key, kv.Value)).ToList());
            EditorUtility.SetDirty(ov);
            return changed;
        }

        /// <summary>判断是否为 Unity 导入器自动生成的 __preview__ 预览 Clip（不应作为 Override 目标）。</summary>
        internal static bool IsPreviewClip(AnimationClip clip)
        {
            return clip != null && clip.name.StartsWith("__preview__", System.StringComparison.Ordinal);
        }

        private static List<AnimationClipCandidate> CollectAnimationCandidates(List<string> roots)
        {
            var result = new List<AnimationClipCandidate>();
            var seenPaths = new HashSet<string>();
            var seenClips = new HashSet<AnimationClip>();
            for (int priority = 0; priority < roots.Count; priority++)
            {
                string root = roots[priority];
                var assetGuids = new HashSet<string>(AssetDatabase.FindAssets("t:Model", new[] { root }));
                foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { root }))
                    assetGuids.Add(guid);

                foreach (var guid in assetGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!seenPaths.Add(path)) continue;
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        var clip = asset as AnimationClip;
                        // 跳过 Unity 导入器自动生成的 __preview__ 预览 Clip：
                        // 它们是编辑器内部对象，运行时无有效动画数据（无根位移），
                        // 一旦作为 Override 目标被序列化，动作状态将播不出正确动画。
                        if (clip == null || IsPreviewClip(clip)) continue;
                        if (!seenClips.Add(clip)) continue;
                        var candidate = new AnimationClipCandidate
                        {
                            clip = clip,
                            path = path,
                            priority = priority
                        };
                        AddCandidateKeys(candidate, clip.name);
                        AddCandidateKeys(candidate, System.IO.Path.GetFileNameWithoutExtension(path));
                        result.Add(candidate);
                    }
                }
            }
            return result;
        }

        private static void AddCandidateKeys(AnimationClipCandidate candidate, string value)
        {
            if (candidate == null || string.IsNullOrEmpty(value)) return;
            string normalized = NormalizeAnimationKey(value);
            if (!string.IsNullOrEmpty(normalized)) candidate.keys.Add(normalized);
            int at = value.LastIndexOf('@');
            if (at >= 0 && at + 1 < value.Length)
            {
                string suffix = NormalizeAnimationKey(value.Substring(at + 1));
                if (!string.IsNullOrEmpty(suffix)) candidate.keys.Add(suffix);
            }
        }

        private static AnimationClipCandidate FindBestCandidate(
            string stateName,
            string anchorName,
            List<AnimationClipCandidate> candidates,
            AnimationClip anchor)
        {
            // 方向性动画（八向移动 / 原地转身）必须精确同名匹配，
            // 禁止泛化到基础 walk/run 动画（否则侧移/后退/转身全部退化成单向前进动画）。
            if (IsDirectionalMotionName(anchorName))
            {
                string exactKey = NormalizeAnimationKey(anchorName);
                return candidates
                    .Where(c => c.clip != null && c.clip != anchor && c.keys.Contains(exactKey))
                    .OrderBy(c => c.priority)
                    .ThenBy(c => c.path, System.StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
            }

            foreach (var alias in BuildAnimationAliases(stateName, anchorName))
            {
                var match = candidates
                    .Where(c => c.clip != null && c.clip != anchor && c.keys.Contains(alias))
                    .OrderBy(c => c.priority)
                    .ThenBy(c => c.path, System.StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (match != null) return match;
            }
            return null;
        }

        private static bool IsStrictActionState(string stateName)
        {
            if (string.IsNullOrEmpty(stateName)) return false;
            switch (NormalizeAnimationKey(stateName))
            {
                case "stepup":
                case "jumpover":
                case "climbup":
                case "enterladderbottom":
                case "enterladdertop":
                case "exitladderbottom":
                case "exitladdertop":
                case "climbladder":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 判断 Clip 名是否为"方向性"动画（八向移动 / 原地转身）。
        /// 这类动画必须精确同名匹配，禁止泛化到基础 walk/run 动画。
        /// </summary>
        private static bool IsDirectionalMotionName(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return false;
            string n = NormalizeAnimationKey(clipName);

            // 原地转身：turn 开头（TurnOnSpotRightA / Turn_90_R 等）
            if (n.StartsWith("turn", System.StringComparison.Ordinal))
                return true;

            // 八向移动：walk/run/crouch 基础移动带方向后缀
            bool locoBase = n.Contains("walk") || n.Contains("run") || n.Contains("crouch");
            if (!locoBase) return false;
            return n.Contains("right") || n.Contains("left")
                || n.Contains("back") || n.Contains("forward");
        }

        /// <summary>
        /// 判断 Clip 名是否为蹲伏基础动画（Crouch_Walk / Crouch_Idle 等）。
        /// 蹲伏动画同样禁止泛化到站立动画，缺省时应回退主控制器共享蹲伏 Clip。
        /// </summary>
        private static bool IsCrouchMotionName(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return false;
            return NormalizeAnimationKey(clipName).Contains("crouch");
        }

        private static IEnumerable<string> BuildAnimationAliases(string stateName, string anchorName)
        {
            var aliases = new List<string>();
            bool strictAction = IsStrictActionState(stateName);
            // 交互动作优先状态语义，不能先用 anchorName 的泛化键（如 jump）命中 jumpOnSpot。
            if (strictAction)
                AddAnimationAlias(aliases, stateName);
            else
                AddAnimationAlias(aliases, anchorName);
            if (!strictAction)
                AddAnimationAlias(aliases, stateName);

            string combined = NormalizeAnimationKey($"{stateName}_{anchorName}");
            AddAnimationAlias(aliases, combined);

            string source = $"{anchorName} {stateName}".ToLowerInvariant();
            // 蹲伏基础动画（Crouch_Walk / Crouch_Idle 等）禁止泛化到站立 walk/run/idle：
            // 角色缺蹲伏动画时应回退主控制器的共享蹲伏 Clip，而不是被 "walk" 别名命中站立走路。
            bool crouchMotion = source.Contains("crouch");
            if (source.Contains("idle") && !crouchMotion)
            {
                AddAnimationAlias(aliases, "idle");
                AddAnimationAlias(aliases, "idle01");
            }
            if ((source.Contains("run") || source.Contains("locomotion")) && !crouchMotion)
            {
                AddAnimationAlias(aliases, "run");
                AddAnimationAlias(aliases, "running");
                AddAnimationAlias(aliases, "runfast");
            }
            if (source.Contains("walk") && !crouchMotion) AddAnimationAlias(aliases, "walk");
            if (source.Contains("death") || source.Contains("dead"))
            {
                AddAnimationAlias(aliases, "death");
                AddAnimationAlias(aliases, "death01");
            }
            if (source.Contains("fall")) AddAnimationAlias(aliases, "falling");
            if (source.Contains("land"))
            {
                AddAnimationAlias(aliases, "landlow");
                AddAnimationAlias(aliases, "landhigh");
                AddAnimationAlias(aliases, "land");
                AddAnimationAlias(aliases, "hardlanding");
            }
            if (source.Contains("jump") && !strictAction)
            {
                AddAnimationAlias(aliases, "jump");
                AddAnimationAlias(aliases, "jumponspot");
            }
            if (source.Contains("roll"))
            {
                AddAnimationAlias(aliases, "roll");
                AddAnimationAlias(aliases, "rollshort");
                AddAnimationAlias(aliases, "shortroll");
            }
            if (source.Contains("enterladderbottom"))
            {
                AddAnimationAlias(aliases, "ladderenterbottom");
                AddAnimationAlias(aliases, "enterbottom");
            }
            if (source.Contains("enterladdertop"))
            {
                AddAnimationAlias(aliases, "ladderentertop");
                AddAnimationAlias(aliases, "entertop");
            }
            if (source.Contains("exitladderbottom"))
            {
                AddAnimationAlias(aliases, "ladderexitbottom");
                AddAnimationAlias(aliases, "exitbottom");
            }
            if (source.Contains("exitladdertop"))
            {
                AddAnimationAlias(aliases, "ladderexittop");
                AddAnimationAlias(aliases, "exittop");
            }
            if (source.Contains("climbladder"))
            {
                AddAnimationAlias(aliases, "ladderclimb");
                AddAnimationAlias(aliases, "climb");
            }
            if (source.Contains("attack"))
            {
                AddAnimationAlias(aliases, "attack");
                AddAnimationAlias(aliases, "attack01");
            }
            if (source.Contains("skillq")) AddAnimationAlias(aliases, "spell01");
            if (source.Contains("skillw")) AddAnimationAlias(aliases, "spell02");
            if (source.Contains("skille")) AddAnimationAlias(aliases, "spell03");
            if (source.Contains("skillr")) AddAnimationAlias(aliases, "spell04");
            return aliases;
        }

        private static void AddAnimationAlias(List<string> aliases, string value)
        {
            string key = NormalizeAnimationKey(value);
            if (!string.IsNullOrEmpty(key) && !aliases.Contains(key)) aliases.Add(key);
        }

        private static string NormalizeAnimationKey(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var chars = value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
            return new string(chars);
        }

        // ─────────────────────────────────────────────────────────────
        //  量产:从 AnimSet 同步 Override 映射(TR-1.2 新角色路径)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 把 CharacterAnimSetAsset 中的全部 Clip 同步进 Override。
        /// 优先按动画状态名建立映射，兼容角色 Clip 名与 Default Controller 不同的情况；
        /// 再按 Clip 名兜底。返回新增/更新条数。
        /// </summary>
        public static int SyncFromAnimSet(AnimatorOverrideController ov, CharacterAnimSetAsset animSet)
        {
            if (ov == null || animSet == null) return 0;
            var master = ov.runtimeAnimatorController as AnimatorController;
            if (master == null) return 0;

            var current = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            ov.GetOverrides(current);
            var map = new Dictionary<AnimationClip, AnimationClip>();
            // 种子映射。注意 ApplyOverrides 只会覆盖/清除列表中显式给出的键：
            // 因此指向 __preview__ 预览 Clip 的条目必须显式置 null（而不是从 map 移除），
            // 才能清除该锚点的 Override，让状态回退到主控制器原始共享 Clip。
            // 预览 Clip 是导入器内部对象，运行时无有效动画数据（无根位移），序列化为目标即坏。
            foreach (var kv in current)
            {
                if (kv.Key == null) continue;
                map[kv.Key] = IsPreviewClip(kv.Value) ? null : kv.Value;
            }

            int changed = 0;
            foreach (var sequence in EnumerateSequences(animSet))
                changed += SyncSequenceByStateName(master, sequence, map);

            var masterByName = new Dictionary<string, AnimationClip>();
            foreach (var clip in master.animationClips)
                if (clip != null && !masterByName.ContainsKey(clip.name))
                    masterByName.Add(clip.name, clip);
            foreach (var clip in CollectAnimSetClips(animSet))
            {
                if (clip == null || !masterByName.TryGetValue(clip.name, out var masterClip) || masterClip == clip)
                    continue;
                if (!map.TryGetValue(masterClip, out var currentClip) || currentClip != clip)
                {
                    map[masterClip] = clip;
                    changed++;
                }
            }

            ov.ApplyOverrides(map.Select(kv => new KeyValuePair<AnimationClip, AnimationClip>(kv.Key, kv.Value)).ToList());
            EditorUtility.SetDirty(ov);
            return changed;
        }

        /// <summary>
        /// 攻击/技能 Override 的权威映射路径：遍历主控制器所有层的 Attack_N / Skill_X 状态，
        /// 以状态【当前 motion】为锚点映射到角色 AnimSet 对应 Clip。
        /// 通用"按 Clip 名匹配"在同名 Clip 多份/历史幽灵锚点残留时会对错锚（实测导致普攻播原角色动画或不播），
        /// 此遍以状态为准，必须在通用同步之后执行。
        /// </summary>
        public static int SyncSkillStatesFromAnimSet(AnimatorOverrideController ov, CharacterAnimSetAsset animSet)
        {
            if (ov == null || animSet == null) return 0;
            var master = ov.runtimeAnimatorController as AnimatorController;
            if (master == null) return 0;

            var current = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            ov.GetOverrides(current);
            var map = new Dictionary<AnimationClip, AnimationClip>();
            foreach (var kv in current)
            {
                if (kv.Key == null) continue;
                map[kv.Key] = IsPreviewClip(kv.Value) ? null : kv.Value;
            }

            int changed = 0;
            var entries = new List<StateMotionEntry>();
            foreach (var layer in master.layers)
                CollectStateMotionEntries(layer.stateMachine, entries);

            foreach (var entry in entries)
            {
                AnimationClip target = ResolveSkillStateTargetClip(entry.name, animSet);
                if (target == null) continue;
                foreach (var anchor in entry.clips)
                {
                    if (anchor == null || anchor == target) continue;
                    if (!map.TryGetValue(anchor, out var cur) || cur != target)
                    {
                        map[anchor] = target;
                        changed++;
                    }
                }
            }

            if (changed > 0)
            {
                ov.ApplyOverrides(map.Select(kv => new KeyValuePair<AnimationClip, AnimationClip>(kv.Key, kv.Value)).ToList());
                EditorUtility.SetDirty(ov);
            }
            return changed;
        }

        /// <summary>按状态名解析 AnimSet 目标 Clip：Attack_N → attacks[N-1]；Skill_Q/W/E/R → skills[0..3]。</summary>
        private static AnimationClip ResolveSkillStateTargetClip(string stateName, CharacterAnimSetAsset animSet)
        {
            if (string.IsNullOrEmpty(stateName)) return null;
            if (stateName.StartsWith("Attack_", System.StringComparison.Ordinal))
            {
                if (int.TryParse(stateName.Substring(7), out int n) && n >= 1
                    && animSet.attacks != null && n <= animSet.attacks.Count)
                    return FirstClipOfSequence(animSet.attacks[n - 1]);
                return null;
            }
            int skillIdx = stateName switch
            {
                "Skill_Q" => 0,
                "Skill_W" => 1,
                "Skill_E" => 2,
                "Skill_R" => 3,
                _ => -1,
            };
            if (skillIdx >= 0 && animSet.skills != null && skillIdx < animSet.skills.Count)
                return FirstClipOfSequence(animSet.skills[skillIdx]);
            return null;
        }

        private static AnimationClip FirstClipOfSequence(CharacterAnimSequence sequence)
        {
            if (sequence == null) return null;
            if (sequence.segments != null)
                foreach (var clip in sequence.segments)
                    if (clip != null) return clip;
            if (sequence.leadIn != null) return sequence.leadIn;
            return sequence.leadOut;
        }

        private static IEnumerable<CharacterAnimSequence> EnumerateSequences(CharacterAnimSetAsset animSet)
        {
            if (animSet.attacks != null)
                foreach (var sequence in animSet.attacks)
                    if (sequence != null) yield return sequence;
            if (animSet.skills != null)
                foreach (var sequence in animSet.skills)
                    if (sequence != null) yield return sequence;
        }

        private static int SyncSequenceByStateName(
            AnimatorController master,
            CharacterAnimSequence sequence,
            Dictionary<AnimationClip, AnimationClip> map)
        {
            if (string.IsNullOrEmpty(sequence.id)) return 0;
            var entries = new List<StateMotionEntry>();
            foreach (var layer in master.layers)
                CollectStateMotionEntries(layer.stateMachine, entries);

            var exact = entries.Where(e => e.name == sequence.id).ToList();
            var leadIn = entries.Where(e => e.name == sequence.id + "_LeadIn").ToList();
            var leadOut = entries.Where(e => e.name == sequence.id + "_LeadOut").ToList();
            var segments = entries
                .Where(e => e.name.StartsWith(sequence.id + "_", System.StringComparison.Ordinal)
                    && e.name != sequence.id + "_LeadIn"
                    && e.name != sequence.id + "_LeadOut")
                .OrderBy(e => e.name, System.StringComparer.Ordinal)
                .ToList();

            int changed = 0;
            var targetSegments = sequence.segments ?? new List<AnimationClip>();
            var firstSegment = targetSegments.FirstOrDefault(c => c != null);
            if (firstSegment != null)
                foreach (var entry in exact)
                    changed += AddMotionOverrides(entry.clips, firstSegment, map);

            if (sequence.leadIn != null)
                foreach (var entry in leadIn)
                    changed += AddMotionOverrides(entry.clips, sequence.leadIn, map);
            if (sequence.leadOut != null)
                foreach (var entry in leadOut)
                    changed += AddMotionOverrides(entry.clips, sequence.leadOut, map);

            for (int i = 0; i < segments.Count && i < targetSegments.Count; i++)
            {
                var target = targetSegments[i];
                if (target != null)
                    changed += AddMotionOverrides(segments[i].clips, target, map);
            }
            return changed;
        }

        private sealed class StateMotionEntry
        {
            public string name;
            public List<AnimationClip> clips = new List<AnimationClip>();
        }

        private static void CollectStateMotionEntries(AnimatorStateMachine stateMachine, List<StateMotionEntry> entries)
        {
            if (stateMachine == null) return;
            foreach (var state in stateMachine.states)
            {
                var entry = new StateMotionEntry { name = state.state.name };
                CollectMotionClips(state.state.motion, entry.clips);
                entries.Add(entry);
            }
            foreach (var child in stateMachine.stateMachines)
                CollectStateMotionEntries(child.stateMachine, entries);
        }

        private static void CollectMotionClips(Motion motion, List<AnimationClip> clips)
        {
            if (motion is AnimationClip clip)
            {
                clips.Add(clip);
                return;
            }
            if (motion is BlendTree tree)
            {
                foreach (var child in tree.children)
                    CollectMotionClips(child.motion, clips);
            }
        }

        private static int AddMotionOverrides(
            List<AnimationClip> anchors,
            AnimationClip target,
            Dictionary<AnimationClip, AnimationClip> map)
        {
            int changed = 0;
            foreach (var anchor in anchors)
            {
                if (anchor == null || target == null || anchor == target) continue;
                if (!map.TryGetValue(anchor, out var current) || current != target)
                {
                    map[anchor] = target;
                    changed++;
                }
            }
            return changed;
        }

        private static IEnumerable<AnimationClip> CollectAnimSetClips(CharacterAnimSetAsset animSet)
        {
            foreach (var seq in animSet.attacks)
            {
                if (seq == null) continue;
                if (seq.leadIn != null) yield return seq.leadIn;
                if (seq.leadOut != null) yield return seq.leadOut;
                if (seq.segments == null) continue;
                foreach (var clip in seq.segments)
                    if (clip != null) yield return clip;
            }
            foreach (var seq in animSet.skills)
            {
                if (seq == null) continue;
                if (seq.leadIn != null) yield return seq.leadIn;
                if (seq.leadOut != null) yield return seq.leadOut;
                if (seq.segments == null) continue;
                foreach (var clip in seq.segments)
                    if (clip != null) yield return clip;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  同步检查:验证 Override 的锚点在主 Controller 中仍然存在
        // ─────────────────────────────────────────────────────────────

        /// <summary>校验单个角色 Override：必须有主骨架、有效锚点和至少一条实际映射。</summary>
        public static bool ValidateOverride(AnimatorOverrideController ov, out string report)
        {
            var sb = new StringBuilder();
            if (ov == null)
            {
                report = "Override Controller 为空";
                return false;
            }

            var master = ov.runtimeAnimatorController as AnimatorController;
            if (master == null)
            {
                report = "Override 的主 Controller 丢失或类型不正确";
                return false;
            }
            string masterPath = AssetDatabase.GetAssetPath(master);
            if (!string.Equals(masterPath, MasterControllerPath, System.StringComparison.OrdinalIgnoreCase))
            {
                report = $"Override 主骨架不是 DefaultCharacterController（当前：{masterPath}）";
                return false;
            }

            var masterClips = new HashSet<AnimationClip>(master.animationClips);
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            ov.GetOverrides(overrides);
            int orphan = overrides.Count(kv => kv.Key != null && !masterClips.Contains(kv.Key));
            int effective = overrides.Count(kv => kv.Key != null && kv.Value != null && kv.Key != kv.Value);
            if (orphan > 0)
                sb.Append($"存在 {orphan} 条无效映射锚点；");
            if (effective == 0)
                sb.Append("没有有效的角色动画映射；");

            bool valid = orphan == 0 && effective > 0;
            if (valid)
                sb.Append($"校验通过（主骨架 {master.name}，有效映射 {effective} 条）");
            report = sb.ToString();
            return valid;
        }

        [MenuItem("Tools/Character Kit/Maintenance/Auto-Fill Current Player Override")]
        public static void AutoFillCurrentPlayerOverride()
        {
            string prefabPath = EditorPrefs.GetString("Test09.CharacterKit.PlayerPrefabPath", "");
            var prefab = string.IsNullOrEmpty(prefabPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var animator = prefab != null ? prefab.GetComponent<Animator>() : null;
            var ov = animator != null ? animator.runtimeAnimatorController as AnimatorOverrideController : null;
            if (ov == null)
            {
                Debug.LogWarning("[OverrideTool] 当前玩家 Prefab 没有 AnimatorOverrideController。");
                return;
            }

            string folder = System.IO.Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            string backupFolder = $"{folder}/_Backups";
            AnimatorController source = null;
            foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { backupFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var candidate = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (candidate == null) continue;
                string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                if (fileName.IndexOf("Locomotion", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || fileName.IndexOf("Controller", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    source = candidate;
                    break;
                }
            }
            if (source == null)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { backupFolder }))
                {
                    source = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(guid));
                    if (source != null) break;
                }
            }
            int changed = SyncFromController(ov, source);
            int directoryMappings = SyncFromAnimationDirectories(ov, folder);
            changed += directoryMappings;
            // 权威兜底：攻击/技能状态按状态名显式映射（修复通用名匹配对错锚的问题）
            string characterName = System.IO.Path.GetFileName(folder);
            var animSet = AssetDatabase.LoadAssetAtPath<CharacterAnimSetAsset>($"{folder}/AnimSet_{characterName}.asset");
            if (animSet != null)
                changed += SyncSkillStatesFromAnimSet(ov, animSet);
            AssetDatabase.SaveAssets();
            var master = ov.runtimeAnimatorController as AnimatorController;
            var masterEntries = new List<StateMotionEntry>();
            var sourceEntries = new List<StateMotionEntry>();
            if (master != null)
                foreach (var layer in master.layers) CollectStateMotionEntries(layer.stateMachine, masterEntries);
            if (source != null)
                foreach (var layer in source.layers) CollectStateMotionEntries(layer.stateMachine, sourceEntries);
            var masterNames = string.Join(", ", masterEntries.Select(e => e.name).Distinct().Take(30));
            var sourceNames = string.Join(", ", sourceEntries.Select(e => e.name).Distinct().Take(30));
            Debug.Log($"[OverrideTool] 当前玩家 Override 自动填充完成：{changed} 条状态/BlendTree 映射。源 Controller：{(source != null ? source.name : "未找到")}；Master States={masterEntries.Count} [{masterNames}]；Source States={sourceEntries.Count} [{sourceNames}]");
        }

        [MenuItem("Tools/Character Kit/Maintenance/Validate Override Controllers")]
        public static void ValidateAllOverrides()
        {
            var guids = AssetDatabase.FindAssets("t:AnimatorOverrideController");
            var sb = new StringBuilder();
            int total = 0, broken = 0;
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ov = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
                if (ov == null) continue;
                var master = ov.runtimeAnimatorController as AnimatorController;
                if (master == null)
                {
                    sb.AppendLine($"[OverrideTool] {path}: ❌ 主 Controller 丢失");
                    broken++;
                    continue;
                }
                var masterClips = new HashSet<AnimationClip>(master.animationClips);
                var ovList = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                ov.GetOverrides(ovList);
                int orphan = ovList.Count(kv => kv.Key != null && !masterClips.Contains(kv.Key));
                total++;
                if (orphan > 0)
                {
                    broken++;
                    sb.AppendLine($"[OverrideTool] {path}: ⚠ {orphan} 条映射锚点已不在主 Controller 中");
                }
            }
            if (total == 0) sb.AppendLine("[OverrideTool] 项目中暂无 Override Controller。");
            else if (broken == 0) sb.AppendLine($"[OverrideTool] 校验通过:{total} 个 Override 全部有效。");
            Debug.Log(sb.ToString());
        }
    }
}
#endif
