using UnityEngine;

namespace Combat
{
    public enum AttackHitDetectionMode
    {
        Auto = 0,
        TriggerOnly = 1,
        Expanded = 2,
        ExpandedAndSweep = 3
    }

    [System.Serializable]
    public struct AttackHitWindow
    {
        [Header("▶ 판정 시작 시점 (정규화 0~1)")]
        [Range(0f, 1f)] public float startNormalized;

        [Header("▶ 판정 종료 시점 (정규화 0~1)")]
        [Range(0f, 1f)] public float endNormalized;

        [Header("▶ 데미지 배수 (0 이하면 기본 1배 유지)")]
        public float damageMultiplier;

        [Header("▶ 히트 타입 (None이면 공격 기본값 유지)")]
        public HitType hitType;

        public bool Contains(float normalizedTime)
        {
            float start = Mathf.Min(startNormalized, endNormalized);
            float end = Mathf.Max(startNormalized, endNormalized);
            return normalizedTime >= start && normalizedTime <= end;
        }

        public float ResolveDamage(float baseDamage)
        {
            float multiplier = damageMultiplier > 0f ? damageMultiplier : 1f;
            return Mathf.Max(0f, baseDamage * multiplier);
        }

        public HitType ResolveHitType(HitType fallback)
        {
            return hitType != HitType.None ? hitType : fallback;
        }
    }

    // ▶ 입력 종류(약/강) — 기획서 입력 스킴과 동일
    public enum AttackInput { Light, Heavy }

    // ▶ 분기 테이블 엔트리 (입력 타입 -> 다음 공격 데이터)
    [System.Serializable]
    public struct ComboNextEntry
    {
        [Header("▶ 어떤 입력으로 다음 타로 갈지 (약/강)")]
        public AttackInput input;

        [Header("▶ 해당 입력 시 이어질 다음 공격 데이터")]
        public AttackData next;
    }

    [CreateAssetMenu(menuName = "Combat/Attack Data", fileName = "AttackData")]
    public class AttackData : ScriptableObject
    {
        enum AttackAutoProfile
        {
            Unknown = 0,
            LightOpener = 1,
            LightChain = 2,
            LightFinisher = 3,
            HeavyOpener = 4,
            HeavyChain = 5,
            HeavyFinisher = 6
        }

        [Header("▶ 애니메이터 스테이트명 (Action 레이어의 State 이름과 일치)")]
        public string stateName;

        [Header("▶ 재생 레이어 인덱스 (공격 레이어, 보통 1)")]
        public int animatorLayer = 1;

        [Header("▶ 모션 재생 속도 배수 (1=기본, 1.1~1.2로 체감 가속)")]
        public float playSpeed = 1.0f;

        [Header("▶ 히트 타이밍(정규화 0~1) — AE_Hit() 동기화용")]
        [Range(0f, 1f)] public float hitTime = 0.35f;

        [Header("▶ 캔슬 시작/종료(정규화 0~1) — 이 구간 입력 시 다음 타로 전이")]
        [Range(0f, 1f)] public float cancelStart = 0.5f;
        [Range(0f, 1f)] public float cancelEnd   = 0.9f;

        [Header("▶ 기본 데미지 (밸런싱용)")]
        public float baseDamage = 10f;

        [Header("▶ 히트 타입 (None이면 히트박스 기본값 유지)")]
        public HitType hitType = HitType.Normal;

        [Header("▶ 히트 윈도우 (비우면 hitTime 기반 단일 윈도우로 추론 가능)")]
        public AttackHitWindow[] hitWindows;

        [Header("▶ 히트 윈도우 튜닝 (음수면 자동 추천값 사용)")]
        public float hitWindowLeadNormalized = -1f;
        public float hitWindowTailNormalized = -1f;

        [Header("▶ 판정 모드 (Auto면 공격 종류 기준 추천값 사용)")]
        public AttackHitDetectionMode hitDetectionMode = AttackHitDetectionMode.Auto;

        [Header("▶ 히트박스 튜닝 (음수면 히트박스 기본값 유지)")]
        public float hitboxExpandedPadding = -1f;
        public float hitboxMeshPaddingScale = -1f;
        public float hitboxScanInterval = -1f;
        public float hitboxOneShotWindow = -1f;

        [Header("▶ 디버그 라벨 (예: L1, L2_in_H, H4_in_LLLHH 등)")]
        public string variantTag;

        [Header("▶ 입력별 다음 타(분기 테이블)")]
        public ComboNextEntry[] nextByInput;

        public HitType ResolveHitType(HitType fallback)
        {
            return hitType != HitType.None ? hitType : fallback;
        }

        public float ResolveHitboxExpandedPadding(float fallback)
        {
            if (hitboxExpandedPadding >= 0f)
                return hitboxExpandedPadding;

            float recommended = GetRecommendedExpandedPadding();
            if (recommended >= 0f)
                return recommended;

            return Mathf.Max(0f, fallback);
        }

        public float ResolveHitboxMeshPaddingScale(float fallback)
        {
            if (hitboxMeshPaddingScale >= 0f)
                return Mathf.Clamp(hitboxMeshPaddingScale, 0.1f, 1f);

            float recommended = GetRecommendedMeshPaddingScale();
            if (recommended >= 0f)
                return Mathf.Clamp(recommended, 0.1f, 1f);

            return Mathf.Clamp(fallback, 0.1f, 1f);
        }

        public float ResolveHitboxScanInterval(float fallback)
        {
            if (hitboxScanInterval >= 0f)
                return hitboxScanInterval;

            float recommended = GetRecommendedScanInterval();
            if (recommended >= 0f)
                return recommended;

            return Mathf.Max(0f, fallback);
        }

        public float ResolveHitboxOneShotWindow(float fallback)
        {
            if (hitboxOneShotWindow >= 0f)
                return hitboxOneShotWindow;

            float recommended = GetRecommendedOneShotWindow();
            if (recommended >= 0f)
                return recommended;

            return Mathf.Max(0f, fallback);
        }

        public bool HasDefinedHitWindows => hitWindows != null && hitWindows.Length > 0;

        public bool TryGetActiveHitWindow(float normalizedTime, out AttackHitWindow hitWindow, out int index)
        {
            if (hitWindows != null)
            {
                for (int i = 0; i < hitWindows.Length; i++)
                {
                    if (!hitWindows[i].Contains(normalizedTime))
                        continue;

                    hitWindow = hitWindows[i];
                    index = i;
                    return true;
                }
            }

            hitWindow = default;
            index = -1;
            return false;
        }

        public bool TryBuildFallbackHitWindow(float leadNormalized, float tailNormalized, out AttackHitWindow hitWindow)
        {
            float start = Mathf.Clamp01(hitTime - ResolveHitWindowLeadNormalized(leadNormalized));
            float end = Mathf.Clamp01(hitTime + ResolveHitWindowTailNormalized(tailNormalized));
            if (end <= start + 0.0001f)
            {
                hitWindow = default;
                return false;
            }

            hitWindow = new AttackHitWindow
            {
                startNormalized = start,
                endNormalized = end,
                damageMultiplier = 1f,
                hitType = HitType.None
            };
            return true;
        }

        public float ResolveHitWindowLeadNormalized(float fallback)
        {
            if (hitWindowLeadNormalized >= 0f)
                return hitWindowLeadNormalized;

            float recommended = GetRecommendedWindowLead();
            if (recommended >= 0f)
                return recommended;

            return Mathf.Max(0f, fallback);
        }

        public float ResolveHitWindowTailNormalized(float fallback)
        {
            if (hitWindowTailNormalized >= 0f)
                return hitWindowTailNormalized;

            float recommended = GetRecommendedWindowTail();
            if (recommended >= 0f)
                return recommended;

            return Mathf.Max(0f, fallback);
        }

        public bool ResolveUseExpandedHitDetection(bool fallback)
        {
            switch (ResolveHitDetectionMode())
            {
                case AttackHitDetectionMode.TriggerOnly:
                    return false;
                case AttackHitDetectionMode.Expanded:
                case AttackHitDetectionMode.ExpandedAndSweep:
                    return true;
                default:
                    return fallback;
            }
        }

        public bool ResolveUseSweepHitDetection(bool fallback)
        {
            switch (ResolveHitDetectionMode())
            {
                case AttackHitDetectionMode.TriggerOnly:
                case AttackHitDetectionMode.Expanded:
                    return false;
                case AttackHitDetectionMode.ExpandedAndSweep:
                    return true;
                default:
                    return fallback;
            }
        }

        public AttackHitDetectionMode ResolveHitDetectionMode()
        {
            if (hitDetectionMode != AttackHitDetectionMode.Auto)
                return hitDetectionMode;

            switch (InferAutoProfile())
            {
                case AttackAutoProfile.LightOpener:
                case AttackAutoProfile.LightChain:
                    return AttackHitDetectionMode.Expanded;
                case AttackAutoProfile.LightFinisher:
                case AttackAutoProfile.HeavyOpener:
                case AttackAutoProfile.HeavyChain:
                case AttackAutoProfile.HeavyFinisher:
                    return AttackHitDetectionMode.ExpandedAndSweep;
                default:
                    return AttackHitDetectionMode.Expanded;
            }
        }

        AttackAutoProfile InferAutoProfile()
        {
            string key = !string.IsNullOrWhiteSpace(stateName) ? stateName.Trim() : string.Empty;
            if (string.IsNullOrEmpty(key))
                key = !string.IsNullOrWhiteSpace(variantTag) ? variantTag.Trim() : string.Empty;
            if (string.IsNullOrEmpty(key))
                return AttackAutoProfile.Unknown;

            char opener = char.ToUpperInvariant(key[0]);
            bool isHeavy = opener == 'H';
            bool isLight = opener == 'L';
            int step = ExtractFirstNumber(key);
            bool variantFinisher = key.IndexOf("_B", System.StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("_C", System.StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("_D", System.StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("_E", System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (isHeavy)
            {
                if (variantFinisher || step >= 3)
                    return AttackAutoProfile.HeavyFinisher;
                if (step >= 2)
                    return AttackAutoProfile.HeavyChain;
                return AttackAutoProfile.HeavyOpener;
            }

            if (isLight)
            {
                if (variantFinisher || step >= 4)
                    return AttackAutoProfile.LightFinisher;
                if (step >= 2)
                    return AttackAutoProfile.LightChain;
                return AttackAutoProfile.LightOpener;
            }

            return AttackAutoProfile.Unknown;
        }

        float GetRecommendedWindowLead()
        {
            switch (InferAutoProfile())
            {
                case AttackAutoProfile.LightOpener:
                    return 0.08f;
                case AttackAutoProfile.LightChain:
                    return 0.09f;
                case AttackAutoProfile.LightFinisher:
                    return 0.10f;
                case AttackAutoProfile.HeavyOpener:
                    return 0.10f;
                case AttackAutoProfile.HeavyChain:
                    return 0.11f;
                case AttackAutoProfile.HeavyFinisher:
                    return 0.12f;
                default:
                    return 0.06f;
            }
        }

        float GetRecommendedWindowTail()
        {
            switch (InferAutoProfile())
            {
                case AttackAutoProfile.LightOpener:
                    return 0.14f;
                case AttackAutoProfile.LightChain:
                    return 0.17f;
                case AttackAutoProfile.LightFinisher:
                    return 0.20f;
                case AttackAutoProfile.HeavyOpener:
                    return 0.20f;
                case AttackAutoProfile.HeavyChain:
                    return 0.22f;
                case AttackAutoProfile.HeavyFinisher:
                    return 0.26f;
                default:
                    return 0.12f;
            }
        }

        float GetRecommendedExpandedPadding()
        {
            switch (InferAutoProfile())
            {
                case AttackAutoProfile.LightOpener:
                    return 0.18f;
                case AttackAutoProfile.LightChain:
                    return 0.22f;
                case AttackAutoProfile.LightFinisher:
                    return 0.28f;
                case AttackAutoProfile.HeavyOpener:
                    return 0.26f;
                case AttackAutoProfile.HeavyChain:
                    return 0.32f;
                case AttackAutoProfile.HeavyFinisher:
                    return 0.40f;
                default:
                    return -1f;
            }
        }

        float GetRecommendedMeshPaddingScale()
        {
            switch (InferAutoProfile())
            {
                case AttackAutoProfile.LightOpener:
                case AttackAutoProfile.LightChain:
                    return 0.65f;
                case AttackAutoProfile.LightFinisher:
                case AttackAutoProfile.HeavyOpener:
                case AttackAutoProfile.HeavyChain:
                case AttackAutoProfile.HeavyFinisher:
                    return 0.78f;
                default:
                    return -1f;
            }
        }

        float GetRecommendedScanInterval()
        {
            switch (InferAutoProfile())
            {
                case AttackAutoProfile.HeavyFinisher:
                    return 0.01f;
                case AttackAutoProfile.HeavyOpener:
                case AttackAutoProfile.HeavyChain:
                case AttackAutoProfile.LightFinisher:
                    return 0.012f;
                case AttackAutoProfile.LightOpener:
                case AttackAutoProfile.LightChain:
                    return 0.016f;
                default:
                    return -1f;
            }
        }

        float GetRecommendedOneShotWindow()
        {
            switch (InferAutoProfile())
            {
                case AttackAutoProfile.LightOpener:
                case AttackAutoProfile.LightChain:
                    return 0.11f;
                case AttackAutoProfile.LightFinisher:
                    return 0.14f;
                case AttackAutoProfile.HeavyOpener:
                case AttackAutoProfile.HeavyChain:
                    return 0.16f;
                case AttackAutoProfile.HeavyFinisher:
                    return 0.20f;
                default:
                    return -1f;
            }
        }

        static int ExtractFirstNumber(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            int value = 0;
            bool found = false;
            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsDigit(text[i]))
                {
                    if (found)
                        break;
                    continue;
                }

                found = true;
                value = value * 10 + (text[i] - '0');
            }

            return found ? value : 0;
        }
    }
}
