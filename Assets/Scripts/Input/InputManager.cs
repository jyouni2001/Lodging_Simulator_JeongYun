using System;
using UnityEngine;
using UnityEngine.EventSystems;
using DG.Tweening; // DOTween 사용을 위해 추가

public class InputManager : MonoBehaviour
{
    [Header("컴포넌트")]
    [SerializeField] private PlacementSystem placementSystem;
    [SerializeField] private Camera cam;
    
    
    private Vector3 lastPosition;   // 마지막 좌표 변수
    private Vector3 uiShowPosition; // BuildUI가 보이는 위치
    private Vector3 uiHidePosition; // BuildUI가 숨겨진 위치
    private Tween   uiTween; // 현재 실행 중인 트윈 저장

    [Header("변수")]
    [SerializeField] private LayerMask placementLayermask;
    [SerializeField] private LayerMask batchedLayer;
    [SerializeField] private LayerMask objectLayer;
    public event Action OnClicked, OnExit;
    public GameObject   BuildUI;
    public RaycastHit   hit;
    public RaycastHit   hit2; 
    public bool         isBuildMode = false;
    public bool IsPointerOverUI() => EventSystem.current.IsPointerOverGameObject();

    private void Start()
    {
        // BuildUI의 초기 위치 설정
        if (BuildUI is not null)
        {
            // BuildUI의 RectTransform 사용
            RectTransform uiRect = BuildUI.GetComponent<RectTransform>();
            if (uiRect is not null)
            {
                // 현재 위치를 보이는 위치로 설정
                uiShowPosition = uiRect.anchoredPosition;

                // 숨겨진 위치는 Y축을 아래로 이동 (화면 아래로)
                uiHidePosition = uiShowPosition + new Vector3(0, -Screen.height, 0); // 화면 높이만큼 아래로

                // 초기 상태: BuildUI 숨김
                uiRect.anchoredPosition = uiHidePosition;
                BuildUI.SetActive(true); // 비활성화 대신 위치로 제어
            }
            else
            {
                Debug.LogError("BuildUI에 RectTransform이 없습니다!");
            }
        }
        else
        {
            Debug.LogError("BuildUI가 할당되지 않았습니다!");
        }
    }

    private void Update()
    {
        // B키로 건설 상태 토글
        if (Input.GetKeyDown(KeyCode.B))
        {
            isBuildMode = !isBuildMode;
            if (isBuildMode)
            {
                ShowBuildUI(); // BuildUI 애니메이션 실행
            }
            else
            {
                HideBuildUI(); // BuildUI 애니메이션 실행
                
            }
        }

        if (Input.GetMouseButtonDown(0))
        {
            OnClicked?.Invoke();
        }

        // ESC 키로 건설 상태 종료
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isBuildMode)
            {
                isBuildMode = false;
                placementSystem.ExitBuildMode();
                HideBuildUI();
            }
            OnExit?.Invoke();
        }

        // 기존 코드 유지
        if (Input.GetKeyDown(KeyCode.L) && isBuildMode)
        {
            if (placementSystem.isDeleteMode)
                placementSystem.StopDeleteMode();
            else
                placementSystem.StartDeleteMode();
        }
    }

    /// <summary>
    /// BuildUI를 위로 올리는 Dotween 애니메이션 코드
    /// </summary>
    private void ShowBuildUI()
    {
        if (BuildUI is null) return;

        // 기존 트윈이 있으면 종료
        if (uiTween is not null)
        {
            uiTween.Kill();
        }

        RectTransform uiRect = BuildUI.GetComponent<RectTransform>();
        if (uiRect is not null)
        {
            placementSystem.EnterBuildMode();
            // DOTween으로 Y축 이동 애니메이션
            uiTween = uiRect.DOAnchorPosY(uiShowPosition.y, 0.5f) // 0.5초 동안 이동
                .SetEase(Ease.OutQuad) // 부드러운 이징
                .OnComplete(() => uiTween = null); // 완료 시 트윈 변수 초기화
        }
    }

    /// <summary>
    /// BuildUI를 아래로 내리는 Dotween 애니메이션 코드
    /// </summary>
    private void HideBuildUI()
    {
        if (BuildUI is null) return;

        // 기존 트윈이 있으면 종료
        if (uiTween is not null)
        {
            uiTween.Kill();
        }

        RectTransform uiRect = BuildUI.GetComponent<RectTransform>();
        if (uiRect is not null)
        {
            // DOTween으로 Y축 이동 애니메이션
            uiTween = uiRect.DOAnchorPosY(uiHidePosition.y, 0.5f) // 0.5초 동안 이동
                .SetEase(Ease.InQuad) // 부드러운 이징
                .OnComplete(() =>
                {
                    uiTween = null;
                    placementSystem.ExitBuildMode();
                }); // 완료 시 트윈 변수 초기화
        }
    }

    /// <summary>
    /// 마우스를 통해 실시간으로 좌표를 반환한다.
    /// </summary>
    /// <returns></returns>
    public Vector3 GetSelectedMapPosition()
    {
        if (cam is null)
        {
            Debug.LogError("Camera가 할당되지 않았습니다!");
            return Vector3.zero;
        }
        
        Vector3 mousePos = Input.mousePosition;
        mousePos.z = cam.nearClipPlane;
        
        Ray ray = cam.ScreenPointToRay(mousePos);
        //RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 90, placementLayermask))
        {
            lastPosition = hit.point;
        }

        // 위 코드는 그리드에 맞게 건설할 수 있도록 그대로 둔다.
        // 아래 코드를 통해, 총 5층의 레이캐스트가 작동하도록 하고, 1~5층 각각의 레이어를 가진 오브젝트를 둔다.
        // 층수가 올라가는 버튼을 누를 때 마다 그 층외의 나머지 층 레이어는 전부 비활성화 시킨다.

        if (Physics.Raycast(ray, out hit2, 100, batchedLayer))
        {
            Debug.Log(hit2.collider.name);
            Debug.Log(hit2.transform.gameObject.layer);
        }
        
        return lastPosition;
    }

    public GameObject GetClickedObject()
    {
        if (cam is null)
        {
            Debug.LogError("Camera가 할당되지 않았습니다!");
            return null;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, objectLayer))
        {
            // 클릭한 오브젝트의 루트 오브젝트 반환
            GameObject clickedObject = hit.collider.gameObject;
            Debug.Log($"선택된 오브젝트 : {clickedObject}");
            return clickedObject.transform.root.gameObject;
        }
        return null;
    }
}
