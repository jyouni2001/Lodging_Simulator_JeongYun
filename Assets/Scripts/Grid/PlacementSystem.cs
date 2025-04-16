using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class PlacementSystem : MonoBehaviour
{
    // 싱글톤 인스턴스
    private static PlacementSystem _instance;

    public static PlacementSystem Instance
    {
        get
        {
            if (_instance is null)
            {
                _instance = FindFirstObjectByType<PlacementSystem>();
                if (_instance is null)
                {
                    Debug.LogError("PlacementSystem 인스턴스가 씬에 존재하지 않습니다. PlacementSystem 오브젝트를 추가해주세요.");
                }
            }
            return _instance;
        }
    }

    [SerializeField] private GameObject mouseIndicator;
    [SerializeField] private GameObject cellIndicatorPrefab;
    private List<GameObject> cellIndicators = new List<GameObject>();

    [Header("컴포넌트")]
    [SerializeField] private InputManager inputManager;
    [SerializeField] public ObjectPlacer objectPlacer;
    [SerializeField] private Grid grid;
    public ObjectsDatabaseSO database;
    [SerializeField] private GameObject previewObject;

    [Header("그리드 관련")]
    [SerializeField] private List<GameObject> gridVisualization;
    [SerializeField] private List<Bounds> planeBounds;

    [Header("플레인 리스트")]
    [SerializeField] private List<GameObject> plane;
    [SerializeField] private List<GameObject> plane2f;
    [SerializeField] private List<GameObject> plane3f;
    [SerializeField] private List<GameObject> plane4f;

    private int selectedObjectIndex = -1;
    public GridData floorData, furnitureData, wallData;
    private Renderer previewRenderer;
    private Vector3Int gridPosition;
    private Quaternion previewRotation = Quaternion.identity;

    [Header("땅 구매 버튼")]
    [SerializeField] private Button purchaseButton;
    [SerializeField] private Button purchase2FButton;
    private int currentPurchaseLevel = 1;

    private bool FloorLock = false;
    [SerializeField] private GridData selectedData;
    private void Awake()
    {
        // 싱글톤 인스턴스 설정
        if (_instance is not null && _instance != this)
        {
            Debug.LogWarning("이미 PlacementSystem 인스턴스가 존재합니다. 이 인스턴스를 파괴합니다.");
            Destroy(gameObject);
            return;
        }
        _instance = this;

        // 씬 전환 시 파괴되지 않도록 설정 (선택 사항)
        // DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        StopPlacement();
        InitailizeGridDatas();
        InitializeGridBounds();
        InitializePlane();

        database.InitializeDictionary();
        
        if (purchaseButton != null)
        {
            purchaseButton.onClick.AddListener(PurchaseNextLand);
        }

        if (purchase2FButton != null)
        {
            purchase2FButton.onClick.AddListener(PurchaseOtherFloor);
        }

    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            FloorLock = true;
            Debug.Log($"증축 시스템 해금 상태 = {FloorLock}");
        }

        if (selectedObjectIndex < 0) return;

        IndicatorPos();
        PreviewObjectFunc();
        DragPlacement();
    }

    #region 플레인 초기화

    /// <summary>
    /// 시작 시 그리드를 전부 비활성화시켜 보이지않도록 한다.
    /// </summary>
    public void InitializePlane()
    {
        foreach (GameObject gridVisual in gridVisualization)
        {
            gridVisual.SetActive(false);
        }
    }
    #endregion

    #region 인디케이터 위치

    /// <summary>
    /// 마우스와 셀 인디케이터의 좌표를 조절한다.
    /// </summary>
    private void IndicatorPos()
    {
        Vector3 mousePosition = inputManager.GetSelectedMapPosition();
        mouseIndicator.transform.position = mousePosition;
        gridPosition = grid.WorldToCell(mousePosition);
        UpdateCellIndicators();
    }

    /// <summary>
    /// 마우스의 위치를 통해 인디케이터의 좌표를 실시간으로 변경하고, 미리보기 중일 때 또한 변경 되도록 한다.
    /// </summary>
    private void UpdateCellIndicators()
    {
        if (selectedObjectIndex < 0 || selectedObjectIndex >= database.objectsData.Count) return;

        Vector2Int objectSize = database.objectsData[selectedObjectIndex].Size;
        int requiredIndicators = objectSize.x * objectSize.y;

        // 최대 인디케이터 수 제한
        const int maxIndicators = 50; // 적절한 값으로 설정
        while (cellIndicators.Count < requiredIndicators && cellIndicators.Count < maxIndicators)
        {
            GameObject newIndicator = Instantiate(cellIndicatorPrefab, transform);
            cellIndicators.Add(newIndicator);
            if (previewRenderer is null)
            {
                previewRenderer = newIndicator.GetComponentInChildren<Renderer>();
                if (previewRenderer is null)
                {
                    Debug.LogError("cellIndicatorPrefab에 Renderer가 없습니다!");
                }
            }
        }
        for (int i = 0; i < cellIndicators.Count; i++)
        {
            if (i < requiredIndicators)
            {
                cellIndicators[i].SetActive(true);
                List<Vector3Int> positions = floorData.CalculatePosition(gridPosition, objectSize, previewRotation, grid);
                if (i < positions.Count)
                {
                    cellIndicators[i].transform.position = grid.GetCellCenterWorld(positions[i]) - new Vector3(0, .46f, 0);
                    cellIndicators[i].transform.rotation = Quaternion.Euler(90, 0, 0);
                }
            }
            else
            {
                cellIndicators[i].SetActive(false);
            }
        }
    }
    #endregion

    #region 그리드 데이터 초기화

    /// <summary>
    /// 첫 그리드 데이터를 초기화한다.
    /// </summary>
    private void InitailizeGridDatas()
    {
        floorData = new GridData();
        furnitureData = new GridData();
        wallData = new GridData();
    }
    #endregion

    #region 그리드 건설반경 초기화

    /// <summary>
    /// 그리드 건설 반경을 초기화하는 함수
    /// </summary>
    private void InitializeGridBounds()
    {
        if (plane == null || plane.Count == 0)
        {
            Debug.LogWarning("plane 리스트가 null이거나 비어 있습니다.");
            return;
        }

        planeBounds.Clear();

        if (plane[0] != null)
        {
            Renderer firstPlaneRenderer = plane[0].GetComponent<Renderer>();
            if (firstPlaneRenderer != null)
            {
                Bounds rendererBounds = firstPlaneRenderer.bounds;
                rendererBounds.Expand(new Vector3(0, 1, 0));
                planeBounds.Add(rendererBounds);
            }
        }
    }
    #endregion

    #region UpdateGridBounds
    /// <summary>
    /// 그리드 반경에 따라 건축 가능한 지역 바운드 설정
    /// </summary>
    private void UpdateGridBounds()
    {
        foreach (GameObject planeRend in plane)
        {
            if (planeRend != null && planeRend.activeSelf)
            {
                Renderer planeRenderer = planeRend.GetComponent<Renderer>();
                if (planeRenderer != null)
                {
                    Bounds rendererBounds = planeRenderer.bounds;
                    rendererBounds.Expand(new Vector3(0, 1, 0));

                    bool alreadyExists = planeBounds.Exists(b => b.center == rendererBounds.center && b.size == rendererBounds.size);
                    if (!alreadyExists)
                    {
                        planeBounds.Add(rendererBounds);
                        gridVisualization.Add(planeRend);
                    }
                }
            }
        }

        if (FloorLock)
        {
            foreach (GameObject planeRend in plane2f)
            {
                if (planeRend != null && planeRend.activeSelf)
                {
                    Renderer planeRenderer = planeRend.GetComponent<Renderer>();
                    if (planeRenderer != null)
                    {
                        Bounds rendererBounds = planeRenderer.bounds;
                        rendererBounds.Expand(new Vector3(0, 1, 0));

                        bool alreadyExists = planeBounds.Exists(b => b.center == rendererBounds.center && b.size == rendererBounds.size);
                        if (!alreadyExists)
                        {
                            planeBounds.Add(rendererBounds);
                            gridVisualization.Add(planeRend);
                        }
                    }
                }
            }
        }
    }
    #endregion

    #region 건축 시작
    public void StartPlacement(int ID)
    {
        StopPlacement();
        StopDeleteMode();

        selectedObjectIndex = database.objectsData.FindIndex(data => data.ID == ID);
        if (selectedObjectIndex < 0)
        {
            Debug.LogError($"No Id Found{ID}");
            return;
        }

        CreatePreview();
        inputManager.OnClicked += PlaceStructure;
        inputManager.OnExit += StopPlacement;
        UpdateCellIndicators(); // 즉시 셀 인디케이터 업데이트
    }
    #endregion

    #region 미리보기
    public void CreatePreview()
    {
        if (selectedObjectIndex < 0 || selectedObjectIndex >= database.objectsData.Count) return;

        if (previewObject != null)
        {
            Destroy(previewObject);
        }

        previewObject = Instantiate(database.objectsData[selectedObjectIndex].Prefab);
        ApplyPreviewMaterial(previewObject);
    }

    private void ApplyPreviewMaterial(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        MaterialPropertyBlock propBlock = new MaterialPropertyBlock();

        foreach (Renderer renderer in renderers)
        {
            renderer.GetPropertyBlock(propBlock);
            propBlock.SetColor("_Color", new Color(1f, 1f, 1f, 0.5f));
            renderer.SetPropertyBlock(propBlock);
        }
    }
    #endregion

    #region 오브젝트 배치
    private void PlaceStructure()
    {
        if (inputManager.IsPointerOverUI())
        {
            return;
        }

        Vector3 mousePosition = inputManager.GetSelectedMapPosition();
        Vector3Int gridPosition = grid.WorldToCell(mousePosition);

        bool placementValidity = CheckPlacementValidity(gridPosition, selectedObjectIndex, previewRotation);
        if (!placementValidity) return;

        Vector3 worldPosition = grid.GetCellCenterWorld(gridPosition);
        int index = objectPlacer.PlaceObject(database.objectsData[selectedObjectIndex].Prefab, worldPosition, previewRotation);

        selectedData = GetSelectedGridData();

        bool isWall = database.objectsData[selectedObjectIndex].IsWall;
        selectedData.AddObjectAt( // <<< previewRotation이 전달되는지 확인
            gridPosition,
            database.objectsData[selectedObjectIndex].Size,
            database.objectsData[selectedObjectIndex].ID,
            index,
            database.objectsData[selectedObjectIndex].kindIndex,
            previewRotation, // <<< 여기!
            grid,
            isWall
        );

        if (inputManager.hit.transform is not null) Debug.Log($"현재 설치된 오브젝트 : {index}, 선택한 오브젝트 : {inputManager.hit.transform.name}");
    }
    #endregion

    #region 점유상태 확인
    private bool CheckPlacementValidity(Vector3Int gridPosition, int selectedObjectIndex, Quaternion rotation)
    {
        ObjectData objectToPlace = database.objectsData[selectedObjectIndex];
        bool placingWall = objectToPlace.IsWall;
        GameObject prefab = database.objectsData[selectedObjectIndex].Prefab;

        // 1. 배치 영역 확인 (planeBounds) - 기존 로직 유지
        List<Vector3Int> positionsToCheck = floorData.CalculatePosition(gridPosition, objectToPlace.Size, rotation, grid);
        foreach (Vector3Int pos in positionsToCheck)
        {
            Vector3 worldPos = grid.GetCellCenterWorld(pos);
            bool isWithinBounds = planeBounds.Any(bound => bound.Contains(worldPos));
            if (!isWithinBounds)
            {
                Debug.Log($"그리드 반경을 벗어남: {pos}");
                return false;
            }
        }

        if (objectToPlace.kindIndex == 0)
        {
            // 바닥 플로어를 배치하려는 경우, 해당 위치에 이미 다른 바닥 플로어가 있는지 확인
            if (!floorData.CanPlaceObjectAt(gridPosition, objectToPlace.Size, rotation, grid, placingWall))
            {
                Debug.Log($"배치 불가: 해당 위치에 이미 바닥 플로어가 존재합니다. 위치: {gridPosition}");
                return false;
            }
            return true;
        }

        if (!placingWall) // 가구를 설치하려는 경우
        {
            // 가구의 실제 월드 좌표 기준 크기와 중심을 계산
            
            GameObject tempObject = Instantiate(prefab, grid.GetCellCenterWorld(gridPosition), rotation);
        
            // 콜라이더를 가져옴
            Collider objCollider = tempObject.GetComponent<Collider>();
            if (objCollider is null)
            {
                objCollider = tempObject.GetComponentInChildren<Collider>();
            }
        
            if (objCollider is null)
            {
                Debug.LogWarning($"오브젝트 {prefab.name}에 콜라이더가 없습니다. 충돌 검사를 건너뜁니다.");
                Destroy(tempObject);
                return furnitureData.CanPlaceObjectAt(gridPosition, objectToPlace.Size, rotation, grid, placingWall);
            }
        
            // 충돌 검사를 위해 콜라이더는 유지하고 렌더러는 비활성화
            Renderer[] renderers = tempObject.GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in renderers)
            {
                renderer.enabled = false;
            }
        
            // 마진 값 조정 (더 작은 값으로 검사 영역 축소)
            float collisionMargin = 0.05f; // 5cm 마진
        
            Vector3 colliderCenter = objCollider.bounds.center;
            Vector3 colliderSize = objCollider.bounds.size - new Vector3(collisionMargin, collisionMargin, collisionMargin);
        
            // 디버그 시각화 - 실제 검사 영역 확인용
            Debug.DrawLine(colliderCenter - colliderSize/2, colliderCenter + colliderSize/2, Color.red, 2f);
        
            // 벽 레이어만 검사
            int wallLayerMask = LayerMask.GetMask("Wall");
        
            // 더 정확한 충돌 검사 방법 - 다중 Raycast 사용
            bool collision = false;
        
            // 콜라이더 경계 상자의 8개 코너에서 레이캐스트
            Vector3[] corners = new Vector3[8];
            corners[0] = new Vector3(-1, -1, -1);
            corners[1] = new Vector3(1, -1, -1);
            corners[2] = new Vector3(-1, 1, -1);
            corners[3] = new Vector3(1, 1, -1);
            corners[4] = new Vector3(-1, -1, 1);
            corners[5] = new Vector3(1, -1, 1);
            corners[6] = new Vector3(-1, 1, 1);
            corners[7] = new Vector3(1, 1, 1);
        
            // 콜라이더 경계에서 중심으로 레이캐스트
            foreach (Vector3 cornerDir in corners)
            {
                Vector3 startPos = colliderCenter + Vector3.Scale(cornerDir, colliderSize / 2f);
                Vector3 direction = -cornerDir.normalized;
                float distance = colliderSize.magnitude / 2f;
            
                Debug.DrawRay(startPos, direction * distance, Color.yellow, 2f);
            
                if (Physics.Raycast(startPos, direction, distance, wallLayerMask))
                {
                    collision = true;
                    Debug.DrawRay(startPos, direction * distance, Color.red, 2f);
                    break;
                }
            }
        
            // 중심에서 6방향으로 추가 레이캐스트
            if (!collision)
            {
                Vector3[] directions = new Vector3[] 
                {
                    Vector3.right, Vector3.left, Vector3.up, 
                    Vector3.down, Vector3.forward, Vector3.back
                };
            
                foreach (Vector3 dir in directions)
                {
                    float distance = Vector3.Scale(colliderSize / 2f, new Vector3(
                        Mathf.Abs(dir.x) > 0.01f ? 1 : 0,
                        Mathf.Abs(dir.y) > 0.01f ? 1 : 0,
                        Mathf.Abs(dir.z) > 0.01f ? 1 : 0
                    )).magnitude;
                
                    Debug.DrawRay(colliderCenter, dir * distance, Color.yellow, 2f);
                
                    if (Physics.Raycast(colliderCenter, dir, distance, wallLayerMask))
                    {
                        collision = true;
                        Debug.DrawRay(colliderCenter, dir * distance, Color.red, 2f);
                        break;
                    }
                }
            }
        
            // 충돌이 감지되면 배치 불가
            if (collision)
            {
                Debug.Log($"배치 불가: 가구가 벽과 충돌했습니다. 위치: {gridPosition}");
                Destroy(tempObject);
                return false;
            }
        
            // 오버랩박스로 마지막 확인 (더 작은 크기로)
            Collider[] hitColliders = Physics.OverlapBox(
                colliderCenter,         
                colliderSize / 2.1f,     // 더 작은 영역으로 검사
                rotation,               
                wallLayerMask          
            );
        
            // 충돌 감지 시 로그 출력 및 배치 금지
            if (hitColliders.Length > 0)
            {
                Debug.Log($"배치 불가: 가구가 벽과 충돌했습니다. 벽 개수: {hitColliders.Length}");
                foreach (Collider hit in hitColliders)
                {
                    Debug.Log($"- 충돌한 벽 오브젝트: {hit.gameObject.name}");
                }
                Destroy(tempObject);
                return false;
            }
        
            // 임시 오브젝트 제거
            Destroy(tempObject);
        
            // GridData 기반 충돌 체크 - 다른 가구와의 충돌 확인
            if (!furnitureData.CanPlaceObjectAt(gridPosition, objectToPlace.Size, rotation, grid, placingWall))
            {
                Debug.Log($"배치 불가: 해당 위치에 가구가 이미 존재합니다. 위치: {gridPosition}");
                return false;
            }
        }
        
        if (placingWall)
        {
            // 1. 기존 다른 벽과의 충돌 검사
            if (!wallData.CanPlaceObjectAt(gridPosition, objectToPlace.Size, rotation, grid, placingWall))
            {
                Debug.Log($"벽 배치 불가: 같은 각도의 벽 충돌 at {gridPosition}");
                return false;
            }

            // 2. 벽과 가구 간 충돌 검사 (Raycast 기반)
            GameObject tempObject = Instantiate(prefab, grid.GetCellCenterWorld(gridPosition), rotation);

            Collider wallCollider = tempObject.GetComponent<Collider>();
            if (wallCollider == null)
            {
                wallCollider = tempObject.GetComponentInChildren<Collider>();
            }

            if (wallCollider != null)
            {
                // 충돌 검사를 위해 렌더러 비활성화
                Renderer[] renderers = tempObject.GetComponentsInChildren<Renderer>();
                foreach (Renderer renderer in renderers)
                {
                    renderer.enabled = false;
                }

                // 가구 레이어 마스크 설정
                int furnitureLayerMask = LayerMask.GetMask("Furniture");

                // 벽 콜라이더의 중심과 크기
                Vector3 colliderCenter = wallCollider.bounds.center;
                Vector3 colliderSize = wallCollider.bounds.size;

                // 벽의 바닥 위치 계산 (Raycast 발사 위치)
                Vector3 wallBottomCenter = colliderCenter - tempObject.transform.up * (colliderSize.y / 5f);

                // 디버그 시각화: 콜라이더 범위 및 발사 위치 표시
                Debug.DrawLine(colliderCenter - colliderSize/2, colliderCenter + colliderSize/2, Color.blue, 2f); // 콜라이더 범위
                Debug.DrawLine(wallBottomCenter - new Vector3(0.1f, 0, 0.1f), wallBottomCenter + new Vector3(0.1f, 0, 0.1f), Color.green, 2f); // 발사 위치


                Vector3 leftRaycastOrigin1 = wallBottomCenter - tempObject.transform.forward * 1; // 왼쪽 첫 번째 위치
                Vector3 rightRaycastOrigin2 = wallBottomCenter + tempObject.transform.forward * 1; // 오른쪽 두 번째 위치
                
                Debug.Log($"leftRaycastOrigin1 : {leftRaycastOrigin1}, rightRaycastOrigin2 : {rightRaycastOrigin2}");
                
                // Raycast 방향 설정 (벽의 로컬 아래 방향)
                Vector3 raycastDirection = -tempObject.transform.up;

                // Raycast 거리 계산: 벽 바닥에서 그리드 바닥까지의 거리
                float distance = Mathf.Abs(wallBottomCenter.y - grid.GetCellCenterWorld(gridPosition).y) + 1f; // 바닥까지 거리 + 여유분

                // 각 위치에서 Raycast 발사
                Vector3[] raycastOrigins = new Vector3[]
                {
                    leftRaycastOrigin1, rightRaycastOrigin2
                };

                bool collision = false;
                foreach (Vector3 origin in raycastOrigins)
                {
                    Debug.DrawRay(origin, raycastDirection * distance, Color.yellow, 2f); // 디버그: 노란색으로 Raycast 경로 표시

                    if (Physics.Raycast(origin, raycastDirection, distance, furnitureLayerMask))
                    {
                        collision = true;
                        Debug.DrawRay(origin, raycastDirection * distance, Color.red, 2f); // 디버그: 충돌 시 빨간색으로 표시
                        Debug.Log($"벽 배치 불가: Raycast로 가구와 충돌 감지됨 at {gridPosition}, 방향: {raycastDirection}, 발사 위치: {origin}");
                        break;
                    }
                }

                // 충돌이 감지되면 벽 설치 차단
                if (collision)
                {
                    Destroy(tempObject);
                    return false;
                }
            }

            Destroy(tempObject);
        }
        
        // 모든 검사를 통과하면 배치 가능
        return true;
    }
    #endregion

    #region 건축 종료
    private void StopPlacement()
    {
        selectedObjectIndex = -1;

        foreach (GameObject indicator in cellIndicators)
        {
            indicator.SetActive(false);
        }

        inputManager.OnClicked -= PlaceStructure;
        inputManager.OnExit -= StopPlacement;

        if (previewObject != null)
        {
            Destroy(previewObject);
            previewObject = null;
        }
    }
    #endregion    

    #region 프리뷰 변경
    private void PreviewObjectFunc()
    {
        bool placementValidity = CheckPlacementValidity(gridPosition, selectedObjectIndex, previewRotation);

        if (previewRenderer is not null)
        {
            previewRenderer.material.color = placementValidity ? Color.white : Color.red;
        }
        else
        {
            Debug.LogWarning("previewRenderer가 null입니다.");
        }
        
        if (isDragging)
        {
            Vector3Int dragEndPosition = grid.WorldToCell(inputManager.GetSelectedMapPosition());
            UpdateDragPreview(dragStartPosition, dragEndPosition);
        }
        else if (previewObject is not null)
        {
            previewObject.transform.position = grid.GetCellCenterWorld(gridPosition);
            previewObject.transform.rotation = previewRotation;
            Renderer[] renderers = previewObject.GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in renderers)
            {
                renderer.material.color = placementValidity ? new Color(1f, 1f, 1f, 0.5f) : new Color(1f, 0f, 0f, 0.5f);
            }
        }
        
        if (previewRenderer is not null)
        {
            previewRenderer.material.color = placementValidity ? new Color(1f, 1f, 1f, 0.5f) : new Color(1f, 0f, 0f, 0.5f);
        }
        
        if (Input.GetKeyDown(KeyCode.R) && !isDragging)
        {
            previewRotation = Quaternion.Euler(0, previewRotation.eulerAngles.y + 90, 0);
            if (previewObject != null) previewObject.transform.rotation = previewRotation;
            UpdateCellIndicators();
        }
        
        /*if (Input.GetKeyDown(KeyCode.R))
        {
            previewRotation = Quaternion.Euler(0, previewRotation.eulerAngles.y + 90, 0);
            previewObject.transform.rotation = previewRotation;
            UpdateCellIndicators();
        }*/

        foreach (GameObject indicator in cellIndicators)
        {
            if (indicator.activeSelf)
            {
                Renderer renderer = indicator.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = placementValidity ? new Color(1f, 1f, 1f, 0.35f) : new Color(1f, 0f, 0f, 0.5f);

                }
            }
        }
    }
    #endregion

    #region 버튼 기능(테스트 기능, 후에 삭제)
    public void ResizeMesh()
    {
        UpdateGridBounds();
    }
    #endregion

    #region 땅 구매

    /// <summary>
    /// 땅을 구매하는 버튼을 통해 실행되는 함수
    /// 버튼을 클릭하면 그리드 플레인이 활성화 + 그리드 건설 반경이 확대되며, 4번(최대 횟수) 클릭 시 버튼이 비활성화 된다.
    /// </summary>
    public void PurchaseNextLand()
    {
        currentPurchaseLevel++;
        Debug.Log(currentPurchaseLevel);

        ActivatePlanesByLevel(currentPurchaseLevel);
        UpdateGridBounds();

        if (currentPurchaseLevel >= 4)
        {
            Debug.Log("모든 땅이 구매되었습니다!");
            if (purchaseButton != null)
            {
                purchaseButton.gameObject.SetActive(false);
            }
            return;
        }

        Debug.Log($"현재 구매 단계: {currentPurchaseLevel}, 활성화된 Plane 수: {gridVisualization.Count}");
    }

    /// <summary>
    /// 플레인의 이름에서 뽑아낸 정수와 반환된 정수의 값이 같을 때, 그리드 플레인을 활성화한다. 
    /// </summary>
    /// <param name="level"></param>
    private void ActivatePlanesByLevel(int level)
    {
        foreach (GameObject planeObj in plane)
        {
            string planeName = planeObj.name;
            int planeLevel = ExtractLevelFromPlaneName(planeName);

            if (planeLevel == level)
            {
                planeObj.SetActive(true);
                Debug.Log($"활성화된 Plane: {planeName}");
            }
        }
    }

    /// <summary>
    /// 플레인의 이름에서 _를 기준으로 숫자를 뽑아내어 반환한다.
    /// </summary>
    /// <param name="planeName"></param>
    /// <returns></returns>
    private int ExtractLevelFromPlaneName(string planeName)
    {
        string[] parts = planeName.Split('_');
        if (parts.Length > 1 && int.TryParse(parts[1], out int level))
        {
            return level;
        }
        Debug.LogWarning($"Plane 이름에서 레벨을 추출할 수 없습니다: {planeName}");
        return 0;
    }
    #endregion

    #region 2층 구매

    // 땅이 전부 구매된 후에 2층 해금이 풀림
    // 해금 해제 후에는 2층의 땅을 모두 구입할 필요가 없기 때문에 전부다 비활성화 후 한꺼번에 활성화가 가능
    // 활성화가 된 후에 그리드 바운드가 업데이트 되어야만 함
    // 사라질 때 함께 사라지도록 테스트 필요
    // 오브젝트 활성화 후 그리드바운드만 작동되도록 테스트 = 성공
    // 그러면 ActivatePlanesByLevel 함수가 필요 없음
    // 따라서 2층을 구매하면 리스트들이 전부 활성화가 되며, 업데이트 그리드 바운드 함수가 실행되도록 테스트 시작 = 성공
    // 성공함으로서 2층 해금 시, 건축모드에서는 2층 그리드가 활성화가 됌
    // 조건은 무조건 땅을 모두 구매한 후에 해금 되도록 설정


    /// <summary>
    /// 버튼의 기능으로서, 함수가 실행되면 2층 플레인이 모두 활성화가 되며, 그리드 건설 반경이 확대된다.
    /// </summary>
    private void PurchaseOtherFloor()
    {
        if (!FloorLock) return;

        if (currentPurchaseLevel < 4)
        {
            Debug.LogWarning("모든 땅을 구매한 후에만 2층을 구매할 수 있습니다.");
            return;
        }

        foreach (GameObject planeObj in plane2f)
        {
            planeObj.SetActive(true);
        }
        UpdateGridBounds();
    }

    #endregion

    #region 건설 상태
    /// <summary>
    /// 건설모드가 되었을 때, 건설UI와 그리드를 활성화한다.
    /// </summary>
    public void EnterBuildMode()
    {
        inputManager.isBuildMode = true;
        inputManager.BuildUI.SetActive(true);

        foreach (GameObject gridVisual in gridVisualization)
        {
            gridVisual.SetActive(true);
        }

        Debug.Log("건설 상태 진입: BuildUI와 Grid 활성화");
    }

    /// <summary>
    /// 건설모드에서 해제 되었을 때, 건설UI와 그리드를 비활성화한다.
    /// </summary>
    public void ExitBuildMode()
    {
        inputManager.isBuildMode = false;
        StopPlacement();
        inputManager.BuildUI.SetActive(false);

        foreach (GameObject gridVisual in gridVisualization)
        {
            gridVisual.SetActive(false);
        }

        Debug.Log("건설 상태 종료: BuildUI와 Grid 비활성화");
    }

    #endregion
    
    #region 드래그 건축 
    
    private bool isDragging = false;
    private Vector3Int dragStartPosition;

    void DragPlacement()
    {
        // 드래그 시작
        if (Input.GetMouseButtonDown(0) && !inputManager.IsPointerOverUI())
        {
            isDragging = true;
            dragStartPosition = grid.WorldToCell(inputManager.GetSelectedMapPosition());
        }

        // 드래그 종료 및 배치
        if (Input.GetMouseButtonUp(0) && isDragging)
        {
            isDragging = false;
            Vector3Int dragEndPosition = grid.WorldToCell(inputManager.GetSelectedMapPosition());
            PlaceLineStructure(dragStartPosition, dragEndPosition);
        }
    }
    
    private void PlaceLineStructure(Vector3Int startPos, Vector3Int endPos)
    {
        if (selectedObjectIndex < 0) return;

        Vector2Int objectSize = database.objectsData[selectedObjectIndex].Size;
        bool isWall = database.objectsData[selectedObjectIndex].IsWall;
        selectedData = GetSelectedGridData();

        // X축 또는 Z축 중 더 긴 방향으로만 배치 (간단한 직선 드래그)
        int dx = Mathf.Abs(endPos.x - startPos.x);
        int dz = Mathf.Abs(endPos.z - startPos.z);
        bool alongX = dx >= dz;

        int stepCount = alongX ? dx + 1 : dz + 1;
        Vector3Int stepDirection = alongX ? new Vector3Int((int)Mathf.Sign(endPos.x - startPos.x), 0, 0) 
            : new Vector3Int(0, 0, (int)Mathf.Sign(endPos.z - startPos.z));

        Vector3Int currentPos = startPos;
        for (int i = 0; i < stepCount; i++)
        {
            if (CheckPlacementValidity(currentPos, selectedObjectIndex, previewRotation))
            {
                Vector3 worldPosition = grid.GetCellCenterWorld(currentPos);
                int index = objectPlacer.PlaceObject(database.objectsData[selectedObjectIndex].Prefab, worldPosition, previewRotation);
                selectedData.AddObjectAt(currentPos, objectSize, database.objectsData[selectedObjectIndex].ID, index, 
                    database.objectsData[selectedObjectIndex].kindIndex, previewRotation, grid, isWall);
            }
            currentPos += stepDirection;
        }
    }
    
    private GridData GetSelectedGridData()
    {
        if (selectedObjectIndex < 0 || selectedObjectIndex >= database.objectsData.Count) return null; // 예외 처리 추가

        switch (database.objectsData[selectedObjectIndex].kindIndex)
        {
            case 0: return floorData;
            case 1: return furnitureData;
            case 2: return wallData;
            default:
                Debug.LogWarning($"Unknown kindIndex: {database.objectsData[selectedObjectIndex].kindIndex}");
                return furnitureData; // 기본값 또는 null 반환 등 결정 필요
        }
    }
    
    private void UpdateDragPreview(Vector3Int startPos, Vector3Int endPos)
    {
        Vector2Int objectSize = database.objectsData[selectedObjectIndex].Size;
        int dx = Mathf.Abs(endPos.x - startPos.x);
        int dz = Mathf.Abs(endPos.z - startPos.z);
        bool alongX = dx >= dz;
        int stepCount = alongX ? dx + 1 : dz + 1;
        Vector3Int stepDirection = alongX ? new Vector3Int((int)Mathf.Sign(endPos.x - startPos.x), 0, 0) 
            : new Vector3Int(0, 0, (int)Mathf.Sign(endPos.z - startPos.z));

        int requiredIndicators = stepCount * objectSize.x * objectSize.y;
        while (cellIndicators.Count < requiredIndicators)
        {
            GameObject newIndicator = Instantiate(cellIndicatorPrefab, transform);
            cellIndicators.Add(newIndicator);
        }

        Vector3Int currentPos = startPos;
        int indicatorIndex = 0;
        for (int i = 0; i < stepCount; i++)
        {
            List<Vector3Int> positions = floorData.CalculatePosition(currentPos, objectSize, previewRotation, grid);
            foreach (Vector3Int pos in positions)
            {
                if (indicatorIndex < cellIndicators.Count)
                {
                    cellIndicators[indicatorIndex].SetActive(true);
                    cellIndicators[indicatorIndex].transform.position = grid.GetCellCenterWorld(pos) - new Vector3(0, 0.46f, 0);
                    cellIndicators[indicatorIndex].transform.rotation = Quaternion.Euler(90, 0, 0);
                    indicatorIndex++;
                }
            }
            currentPos += stepDirection;
        }

        for (int i = indicatorIndex; i < cellIndicators.Count; i++)
        {
            cellIndicators[i].SetActive(false);
        }
    }

    #endregion

    #region 삭제 모드

    public bool isDeleteMode = false;

    public void StartDeleteMode()
    {
        StopPlacement(); // 기존 배치 모드 종료
        isDeleteMode = true;
        inputManager.OnClicked += DeleteStructure; // 클릭 시 삭제 함수 호출
        inputManager.OnExit += StopDeleteMode;
        Debug.Log("삭제 모드 시작");
    }

    public void StopDeleteMode()
    {
        isDeleteMode = false;
        inputManager.OnClicked -= DeleteStructure;
        inputManager.OnExit -= StopDeleteMode;
        Debug.Log("삭제 모드 종료");
    }

    private void DeleteStructure()
    {
        if (inputManager.IsPointerOverUI())
            return;

        GameObject clickedObject = inputManager.GetClickedObject();

        Debug.Log($"삭제하려는 오브젝트 : {clickedObject}");

        if (clickedObject is null)
        {
            Debug.Log("삭제할 오브젝트가 없습니다.");
            return;
        }

        // ObjectPlacer에서 오브젝트 인덱스 찾기
        int objectIndex = objectPlacer.GetObjectIndex(clickedObject);
        Debug.Log($"삭제하려는 오브젝트의 인덱스 : {objectIndex}");
        if (objectIndex < 0)
        {
            Debug.LogWarning("클릭한 오브젝트가 ObjectPlacer에 등록되지 않음.");
            return;
        }

        // 적절한 GridData 선택
        GridData selectedData = FindGridDataByObjectIndex(objectIndex);
        Debug.Log($"삭제하려는 오브젝트 그리드 데이터 : {selectedData}");
        if (selectedData is null)
        {
            Debug.LogWarning("해당 오브젝트의 GridData를 찾을 수 없음.");
            return;
        }

        // GridData에서 데이터 제거
        if (selectedData.RemoveObjectByIndex(objectIndex))
        {
            // ObjectPlacer에서 오브젝트 제거
            objectPlacer.RemoveObject(objectIndex);
            Debug.Log($"오브젝트 삭제 완료: 인덱스 {objectIndex}");
        }
        else
        {
            Debug.LogWarning("GridData에서 오브젝트 데이터를 제거하지 못함.");
        }
    }

    private GridData FindGridDataByObjectIndex(int objectIndex)
    {
        // floorData 확인
        if (floorData.placedObjects.Any(kvp => kvp.Value.Any(data => data.PlacedObjectIndex == objectIndex)))
            return floorData;

        // furnitureData 확인
        if (furnitureData.placedObjects.Any(kvp => kvp.Value.Any(data => data.PlacedObjectIndex == objectIndex)))
            return furnitureData;

        // wallData 확인
        if (wallData.placedObjects.Any(kvp => kvp.Value.Any(data => data.PlacedObjectIndex == objectIndex)))
            return wallData;

        return null;
    }

    #endregion
}