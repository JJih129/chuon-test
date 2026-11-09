// 해상도 / 전체화면 저장·복원 (반복 13)
//  - 저장: w × h @ Hz + 전체화면 여부 (opt_res_w / opt_res_h / opt_res_hz / opt_fullscreen). 예전 인덱스 키(opt_resolution_index)는 호환용으로만 남는다.
//  - 복원: 플레이어 부팅 시 1회 (씬 로드마다 SetResolution 을 반복하지 않는다). 저장 해상도가 현재 모니터 목록에 없으면 건드리지 않는다.
//  - 에디터(Game 뷰)에서는 복원하지 않는다.
using UnityEngine;

public static class DisplayModeRuntime
{
    const string K_W = "opt_res_w";
    const string K_H = "opt_res_h";
    const string K_HZ = "opt_res_hz";
    const string K_FULLSCREEN = "opt_fullscreen";

    public static bool TryGetSaved(out int width, out int height, out int refreshHz, out bool fullscreen)
    {
        width = PlayerPrefs.GetInt(K_W, 0);
        height = PlayerPrefs.GetInt(K_H, 0);
        refreshHz = PlayerPrefs.GetInt(K_HZ, 0);
        fullscreen = PlayerPrefs.GetInt(K_FULLSCREEN, Screen.fullScreen ? 1 : 0) == 1;
        return width > 0 && height > 0;
    }

    public static void Save(int width, int height, int refreshHz, bool fullscreen)
    {
        PlayerPrefs.SetInt(K_W, width);
        PlayerPrefs.SetInt(K_H, height);
        PlayerPrefs.SetInt(K_HZ, refreshHz);
        PlayerPrefs.SetInt(K_FULLSCREEN, fullscreen ? 1 : 0);
    }

    public static int RefreshHz(Resolution r)
    {
        RefreshRate rr = r.refreshRateRatio;
        return rr.denominator != 0 ? Mathf.RoundToInt((float)rr.numerator / rr.denominator) : 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void RestoreAtBoot()
    {
        if (Application.isEditor)
            return;
        if (!TryGetSaved(out int w, out int h, out int hz, out bool fs))
            return;

        Resolution[] all = Screen.resolutions;
        int match = -1;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].width != w || all[i].height != h)
                continue;
            if (match < 0 || Mathf.Abs(RefreshHz(all[i]) - hz) < Mathf.Abs(RefreshHz(all[match]) - hz))
                match = i;
        }
        if (match < 0)
            return; // 모니터가 바뀌어 저장 해상도가 없다 → 플레이어 기본값 유지

        FullScreenMode mode = fs ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        if (Screen.width == w && Screen.height == h && Screen.fullScreenMode == mode)
            return;
        Screen.SetResolution(w, h, mode, all[match].refreshRateRatio);
    }
}
