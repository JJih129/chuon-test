using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 내 모든 대사의 단일 데이터 소스 (Resources/Dialogue/DialogueTable_KO).
/// 호출부는 키로 읽고, 키가 없으면 기존 하드코딩 문구로 폴백한다.
/// 순차 대사: "prefix.0", "prefix.1", ... (GetSequence). 전투 짧은 대사(bark)는 barkGroups.
/// 대본 원문: Docs/Dialogue_Script_v2.md
/// </summary>
[CreateAssetMenu(fileName = "DialogueTable_KO", menuName = "ChuOn/Dialogue Table")]
public sealed class DialogueTable : ScriptableObject
{
    public const string DefaultResourcePath = "Dialogue/DialogueTable_KO";

    [Serializable]
    public sealed class Entry
    {
        public string key;
        public string speaker;
        [TextArea(1, 4)] public string text;
        [Tooltip("0 = 호출부 기본값(또는 글자 수 기반) 사용")]
        [Min(0f)] public float duration;
    }

    [Serializable]
    public sealed class BarkGroup
    {
        public string key;
        [Tooltip("같은 그룹 재생 간 최소 간격(초, 실시간)")]
        [Min(0f)] public float cooldown = 12f;
        [Tooltip("전역 쿨다운을 무시한다 (페이즈 전환 등 중요한 순간). 표시 중인 bark 는 교체한다.")]
        public bool priority;
        [Tooltip("세션(씬)당 한 번만")]
        public bool once;
        [Range(0f, 1f)] public float chance = 1f;
        [Tooltip("이 그룹 재생 직후 이어서 재생할 그룹 키 (예: Type-D 시스템 음성 → EGO)")]
        public string followUpGroup;
        [Min(0f)] public float followUpDelay = 1.2f;
        public Entry[] lines = Array.Empty<Entry>();
    }

    public Entry[] entries = Array.Empty<Entry>();
    public BarkGroup[] barkGroups = Array.Empty<BarkGroup>();

    Dictionary<string, Entry> _entryLookup;
    Dictionary<string, BarkGroup> _barkLookup;

    static DialogueTable s_active;
    static bool s_loadAttempted;

    /// <summary>현재 언어 테이블 (없으면 null → 모든 호출부가 폴백 문구 사용).</summary>
    public static DialogueTable Active
    {
        get
        {
            if (s_active == null && !s_loadAttempted)
            {
                s_loadAttempted = true;
                s_active = Resources.Load<DialogueTable>(DefaultResourcePath);
            }

            return s_active;
        }
    }

    public static void SetActive(DialogueTable table)
    {
        s_active = table;
        s_loadAttempted = true;
        if (table != null)
            table.Rebuild();
    }

    void OnEnable() => Rebuild();
    void OnValidate() => Rebuild();

    public void Rebuild()
    {
        _entryLookup = new Dictionary<string, Entry>(StringComparer.Ordinal);
        if (entries != null)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                Entry entry = entries[i];
                if (entry != null && !string.IsNullOrEmpty(entry.key))
                    _entryLookup[entry.key] = entry;
            }
        }

        _barkLookup = new Dictionary<string, BarkGroup>(StringComparer.Ordinal);
        if (barkGroups != null)
        {
            for (int i = 0; i < barkGroups.Length; i++)
            {
                BarkGroup group = barkGroups[i];
                if (group != null && !string.IsNullOrEmpty(group.key))
                    _barkLookup[group.key] = group;
            }
        }
    }

    public bool TryGetEntry(string key, out Entry entry)
    {
        entry = null;
        if (string.IsNullOrEmpty(key))
            return false;

        if (_entryLookup == null)
            Rebuild();

        return _entryLookup.TryGetValue(key, out entry) && entry != null && !string.IsNullOrEmpty(entry.text);
    }

    public bool TryGetBarkGroup(string key, out BarkGroup group)
    {
        group = null;
        if (string.IsNullOrEmpty(key))
            return false;

        if (_barkLookup == null)
            Rebuild();

        return _barkLookup.TryGetValue(key, out group) && group != null && group.lines != null && group.lines.Length > 0;
    }

    // ------------------------------------------------------------------ static helpers (null-safe)

    public static bool TryGet(string key, out Entry entry)
    {
        entry = null;
        DialogueTable table = Active;
        return table != null && table.TryGetEntry(key, out entry);
    }

    public static bool Has(string key) => TryGet(key, out _);

    public static string Text(string key, string fallback)
    {
        return TryGet(key, out Entry entry) ? entry.text : fallback;
    }

    public static string Speaker(string key, string fallback)
    {
        return TryGet(key, out Entry entry) && !string.IsNullOrEmpty(entry.speaker) ? entry.speaker : fallback;
    }

    public static float Duration(string key, float fallback)
    {
        return TryGet(key, out Entry entry) && entry.duration > 0f ? entry.duration : fallback;
    }

    /// <summary>키가 있으면 speaker/text/duration 을 덮어쓴다. 없으면 그대로 둔다.</summary>
    public static bool Resolve(string key, ref string speaker, ref string text, ref float duration)
    {
        if (!TryGet(key, out Entry entry))
            return false;

        if (!string.IsNullOrEmpty(entry.speaker))
            speaker = entry.speaker;
        text = entry.text;
        if (entry.duration > 0f)
            duration = entry.duration;
        else
            duration = Mathf.Max(duration, SuggestedDuration(text));
        return true;
    }

    /// <summary>"prefix.0", "prefix.1", ... 를 연속으로 찾는다 (최대 32줄). 하나도 없으면 빈 리스트.</summary>
    public static List<Entry> GetSequence(string prefix)
    {
        var result = new List<Entry>();
        DialogueTable table = Active;
        if (table == null || string.IsNullOrEmpty(prefix))
            return result;

        for (int i = 0; i < 32; i++)
        {
            if (!table.TryGetEntry(prefix + "." + i, out Entry entry))
                break;
            result.Add(entry);
        }

        return result;
    }

    /// <summary>글자 수 기반 표시 시간 (한 줄 짧은 대사 ≈1.6s, 두 줄 ≈3s).</summary>
    public static float SuggestedDuration(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 1.2f;

        int visible = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != ' ' && c != '\n' && c != '\r')
                visible++;
        }

        return Mathf.Clamp(0.95f + visible * 0.075f, 1.4f, 4.2f);
    }

    /// <summary>bark 그룹에서 직전과 다른 줄을 고른다.</summary>
    public static bool TryPickBark(string groupKey, int lastIndex, out BarkGroup group, out Entry line, out int index)
    {
        line = null;
        index = -1;
        group = null;
        DialogueTable table = Active;
        if (table == null || !table.TryGetBarkGroup(groupKey, out group))
            return false;

        int count = group.lines.Length;
        if (count == 1)
        {
            index = 0;
        }
        else
        {
            index = UnityEngine.Random.Range(0, count);
            if (index == lastIndex)
                index = (index + 1 + UnityEngine.Random.Range(0, count - 1)) % count;
        }

        line = group.lines[index];
        return line != null && !string.IsNullOrEmpty(line.text);
    }
}
