using UnityEngine;
using UnityEngine.Splines; // 스플라인 패키지 네임스페이스

public class SplineFollower : MonoBehaviour
{
    [SerializeField] private SplineContainer spline; // 스플라인 참조
    [SerializeField] private float speed = 1f; // 이동 속도
    [SerializeField] private bool loop = true; // 반복 여부

    private float distance = 0f; // 스플라인 상의 현재 위치 (0~1)
    private float splineLength; // 스플라인의 총 길이

    void Start()
    {
        if (spline == null)
        {
            Debug.LogError("SplineContainer가 지정되지 않았습니다!");
            return;
        }
        
        // 스플라인의 총 길이 계산
        splineLength = spline.Spline.GetLength();
    }

    void Update()
    {
        if (spline == null) return;

        // 시간에 따라 거리 증가
        distance += speed * Time.deltaTime / splineLength;

        // 루프 설정
        if (loop)
        {
            if (distance > 1f)
                distance -= 1f;
        }
        else
        {
            distance = Mathf.Clamp01(distance);
        }

        // 스플라인 상의 위치 계산
        Vector3 position = spline.EvaluatePosition(distance);
        Vector3 direction = spline.EvaluateTangent(distance);

        // 객체 위치와 회전 업데이트
        transform.position = position;
        transform.rotation = Quaternion.LookRotation(direction);
    }
}