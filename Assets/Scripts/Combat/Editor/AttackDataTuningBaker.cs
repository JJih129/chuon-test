using Combat;
using UnityEditor;
using UnityEngine;

public static class AttackDataTuningBaker
{
    const float DefaultLead = 0.06f;
    const float DefaultTail = 0.12f;
    const float DefaultExpandedPadding = 0f;
    const float DefaultMeshPaddingScale = 0.6f;
    const float DefaultScanInterval = 0.06f;
    const float DefaultOneShotWindow = 0.2f;

    [MenuItem("Tools/Combat/Bake Recommended Attack Tuning")]
    public static void BakeRecommendedAttackTuning()
    {
        string[] guids = AssetDatabase.FindAssets("t:AttackData", new[] { "Assets/Attack_data" });
        int changedCount = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AttackData attackData = AssetDatabase.LoadAssetAtPath<AttackData>(path);
            if (attackData == null)
                continue;

            bool changed = false;
            Undo.RecordObject(attackData, "Bake Recommended Attack Tuning");

            if (!attackData.HasDefinedHitWindows &&
                attackData.TryBuildFallbackHitWindow(DefaultLead, DefaultTail, out AttackHitWindow fallbackWindow))
            {
                attackData.hitWindows = new[] { fallbackWindow };
                changed = true;
            }

            float recommendedLead = attackData.ResolveHitWindowLeadNormalized(DefaultLead);
            if (attackData.hitWindowLeadNormalized < 0f)
            {
                attackData.hitWindowLeadNormalized = recommendedLead;
                changed = true;
            }

            float recommendedTail = attackData.ResolveHitWindowTailNormalized(DefaultTail);
            if (attackData.hitWindowTailNormalized < 0f)
            {
                attackData.hitWindowTailNormalized = recommendedTail;
                changed = true;
            }

            if (attackData.hitboxExpandedPadding < 0f)
            {
                attackData.hitboxExpandedPadding = attackData.ResolveHitboxExpandedPadding(DefaultExpandedPadding);
                changed = true;
            }

            if (attackData.hitboxMeshPaddingScale < 0f)
            {
                attackData.hitboxMeshPaddingScale = attackData.ResolveHitboxMeshPaddingScale(DefaultMeshPaddingScale);
                changed = true;
            }

            if (attackData.hitboxScanInterval < 0f)
            {
                attackData.hitboxScanInterval = attackData.ResolveHitboxScanInterval(DefaultScanInterval);
                changed = true;
            }

            if (attackData.hitboxOneShotWindow < 0f)
            {
                attackData.hitboxOneShotWindow = attackData.ResolveHitboxOneShotWindow(DefaultOneShotWindow);
                changed = true;
            }

            if (!changed)
                continue;

            EditorUtility.SetDirty(attackData);
            changedCount++;
        }

        if (changedCount > 0)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[AttackDataTuningBaker] Baked {changedCount} AttackData assets.");
    }
}
