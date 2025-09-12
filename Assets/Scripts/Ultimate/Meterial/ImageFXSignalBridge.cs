using UnityEngine;

public class ImageFXSignalBridge : MonoBehaviour
{
    [SerializeField] Material mat;
    // 공통 믹스(곱/더하기)
    public void SetMix(float mul, float add)
    {
        var pdma = mat.GetVector("_PixDistMulAdd");
        pdma.z = mul; pdma.w = add;
        mat.SetVector("_PixDistMulAdd", pdma);
    }
    // HSVC
    public void SetHSVC(float hue, float sat, float val, float cont)
    {
        mat.SetVector("_HSVC", new Vector4(hue, sat, val, cont));
        // HSVC는 내부에서 컬러매트릭스로 변환되도록 커스텀 인스펙터가 있지만,
        // 런타임엔 셰이더에서 _HSVC 직접 읽어 처리하는 버전일 수도 있어 셰이더 구현에 맞춰 사용.
    }
    // 커널 예시(픽셀 스텝, 프리셋 강도)
    public void SetKernelStep(float pxStep)
    {
        var pdma = mat.GetVector("_PixDistMulAdd");
        pdma.x = pxStep; pdma.y = pxStep; // 스크립트가 x=y로 유지함
        mat.SetVector("_PixDistMulAdd", pdma);
    }
}
