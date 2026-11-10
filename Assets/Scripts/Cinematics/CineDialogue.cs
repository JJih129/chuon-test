using UnityEngine;

/// <summary>
/// 시네마틱 대사 조회 순서: ① 메인 대사 테이블(DialogueTable_KO, 리드가 키를 병합하면 이쪽이 우선)
/// ② 시네마틱 전용 테이블 Resources/Cinematics/DialogueTable_Cine_KO (반복 14 신규 대사, 메인 테이블과 같은 형식)
/// ③ 클립에 저장된 폴백 문구. 표시 시점에만 호출한다.
/// </summary>
public static class CineDialogue
{
    public const string CineTableResourcePath = "Cinematics/DialogueTable_Cine_KO";

    static DialogueTable s_cineTable;
    static bool s_loadAttempted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_cineTable = null;
        s_loadAttempted = false;
    }

    public static DialogueTable CineTable
    {
        get
        {
            if (s_cineTable == null && !s_loadAttempted)
            {
                s_loadAttempted = true;
                s_cineTable = Resources.Load<DialogueTable>(CineTableResourcePath);
            }
            return s_cineTable;
        }
    }

    /// <summary>key 로 화자·문구를 덮어쓴다. 찾았으면 true. 문구 안의 " / " 는 줄바꿈으로 바꾼다.</summary>
    public static bool Resolve(string key, ref string speaker, ref string text)
    {
        bool found = false;
        if (!string.IsNullOrEmpty(key))
        {
            if (DialogueTable.TryGet(key, out DialogueTable.Entry entry))
            {
                found = true;
                if (!string.IsNullOrEmpty(entry.speaker)) speaker = entry.speaker;
                text = entry.text;
            }
            else
            {
                DialogueTable cine = CineTable;
                if (cine != null && cine.TryGetEntry(key, out entry))
                {
                    found = true;
                    if (!string.IsNullOrEmpty(entry.speaker)) speaker = entry.speaker;
                    text = entry.text;
                }
            }
        }

        if (!string.IsNullOrEmpty(text) && text.IndexOf(" / ", System.StringComparison.Ordinal) >= 0)
            text = text.Replace(" / ", "\n");
        return found;
    }
}
