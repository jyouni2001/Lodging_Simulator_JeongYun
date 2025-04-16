using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

// RoomDetector의 인터페이스만 정의하여 참조 문제 해결
public interface IRoomDetector
{
    GameObject[] GetDetectedRooms();
    void DetectRooms();
}

public class AIAgent : MonoBehaviour
{
    // AI 이동 및 위치 관련 변수들
    private NavMeshAgent agent;                    // AI 이동을 제어하는 네비게이션 에이전트
    private Transform counterPosition;             // 카운터의 위치
    private static List<RoomInfo> roomList = new List<RoomInfo>();  // 동적 룸 정보 리스트
    private Transform spawnPoint;                  // AI 생성/소멸 지점
    private int currentRoomIndex = -1;            // 현재 사용 중인 방 번호 (-1은 미사용)
    private AISpawner spawner;                    // AISpawner 참조
    private float arrivalDistance = 0.5f;         // 도착 판정 거리 (AI 크기에 맞춤)

    // 대기열 관련 변수
    private bool isInQueue = false;
    private Vector3 targetQueuePosition;
    private bool isWaitingForService = false;

    // AI 상태 관련 변수들
    private AIState currentState = AIState.MovingToQueue;  // 현재 AI 상태
    private float counterWaitTime = 5f;           // 카운터에서 처리되는 시간
    private string currentDestination = "대기열로 이동 중";  // 현재 목적지 (UI 표시용)
    private bool isBeingServed = false;           // 현재 서비스를 받고 있는지 여부

    // 동시성 및 안전성 관련 변수들
    private static readonly object lockObject = new object();  // 스레드 동기화를 위한 잠금 객체
    private Coroutine wanderingCoroutine;         // 배회 코루틴 참조
    private Coroutine roomUseCoroutine;           // 방 사용 코루틴 참조
    private int maxRetries = 3;                   // 위치 찾기 최대 시도 횟수

    // 룸 정보 클래스
    private class RoomInfo
    {
        public Transform transform;
        public bool isOccupied;
        public float size;
        public GameObject gameObject;
        public string roomId;  // 방의 고유 ID 추가

        public RoomInfo(GameObject roomObj)
        {
            gameObject = roomObj;
            transform = roomObj.transform;
            isOccupied = false;
            
            var collider = roomObj.GetComponent<Collider>();
            if (collider != null)
            {
                size = collider.bounds.size.magnitude * 0.3f;
            }
            else
            {
                size = 2f;
                Debug.LogWarning($"Room {roomObj.name}에 Collider가 없습니다. 기본 크기(2)를 사용합니다.");
            }

            // 방의 고유 ID 생성 (위치 기반)
            Vector3 pos = roomObj.transform.position;
            roomId = $"Room_{pos.x:F0}_{pos.z:F0}";
            Debug.Log($"방 ID 생성: {roomId} at {pos}");
        }
    }

    // AI 상태 열거형
    private enum AIState
    {
        Wandering,           // 배회 중
        MovingToQueue,       // 대기열로 이동 중
        WaitingInQueue,      // 대기열에서 대기 중
        MovingToRoom,        // 배정된 방으로 이동 중
        UsingRoom,          // 방 사용 중
        ReportingRoom,      // 방 사용 완료 보고 중
        ReturningToSpawn    // 스폰 지점으로 돌아가는 중
    }

    // Room 정보 업데이트를 위한 델리게이트와 이벤트
    public delegate void RoomsUpdatedHandler(GameObject[] rooms);
    private static event RoomsUpdatedHandler OnRoomsUpdated;

    // 정적 변수 초기화를 위한 메서드 추가
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void InitializeStatics()
    {
        roomList.Clear();
        OnRoomsUpdated = null;
    }

    void Start()
    {
        // 필수 컴포넌트 체크
        agent = GetComponent<NavMeshAgent>();
        if (agent == null)
        {
            Debug.LogError($"AI {gameObject.name}: NavMeshAgent 컴포넌트가 없습니다.");
            Destroy(gameObject);
            return;
        }
        
        // 필수 오브젝트 찾기 (태그 기반으로 변경)
        GameObject counter = GameObject.FindGameObjectWithTag("Counter");
        GameObject spawn = GameObject.FindGameObjectWithTag("Spawn");
            
        if (counter == null || spawn == null)
        {
            Debug.LogError($"AI {gameObject.name}: 필수 오브젝트(Counter 또는 Spawn)를 찾을 수 없습니다. 태그가 올바르게 설정되어 있는지 확인하세요.");
            Destroy(gameObject);
            return;
        }

        counterPosition = counter.transform;
        spawnPoint = spawn.transform;
        
        // 룸 초기화 (첫 번째 AI만 초기화하도록)
        lock (lockObject)
        {
            if (roomList.Count == 0)
            {
                InitializeRooms();
                
                // 이벤트 구독 설정
                if (OnRoomsUpdated == null)
                {
                    OnRoomsUpdated += UpdateRoomList;
                }
            }
        }

        // NavMesh 영역 확인
        if (NavMesh.GetAreaFromName("Ground") == 0)
        {
            Debug.LogWarning($"AI {gameObject.name}: Ground NavMesh 영역이 설정되지 않았습니다.");
        }

        // 초기 상태 설정 - 랜덤 배회 또는 대기열로 이동
        if (Random.value < 0.3f)
        {
            currentState = AIState.Wandering;
            currentDestination = "배회 중";
            wanderingCoroutine = StartCoroutine(WanderingBehavior());
            Debug.Log($"AI {gameObject.name}: 배회를 시작합니다.");
        }
        else
        {
            currentState = AIState.MovingToQueue;
            currentDestination = "대기열로 이동 중";
            StartCoroutine(QueueBehavior()); // 바로 대기열 행동 시작
            Debug.Log($"AI {gameObject.name}: 대기열로 이동을 시작합니다.");
        }
    }

    // 룸 초기화 (태그 기반으로 변경)
    private void InitializeRooms()
    {
        roomList.Clear();
        Debug.Log($"AI {gameObject.name}: 룸 초기화 시작");
        
        // RoomDetector를 통해 방 찾기
        var roomDetectors = GameObject.FindObjectsByType<RoomDetector>(FindObjectsSortMode.None);
        if (roomDetectors != null && roomDetectors.Length > 0)
        {
            foreach (var detector in roomDetectors)
            {
                // 룸 감지 요청
                detector.ScanForRooms();
                
                // 이벤트 구독
                detector.OnRoomsUpdated += (rooms) => {
                    if (rooms != null && rooms.Length > 0)
                    {
                        UpdateRoomList(rooms);
                    }
                };
            }
            
            Debug.Log($"AI {gameObject.name}: RoomDetector를 통해 방 감지를 시작했습니다.");
        }
        else
        {
            // RoomDetector가 없는 경우 태그로 직접 찾기
            GameObject[] taggedRooms = GameObject.FindGameObjectsWithTag("Room");
            foreach (GameObject room in taggedRooms)
            {
                if (!roomList.Any(r => r.gameObject == room))
                {
                    roomList.Add(new RoomInfo(room));
                }
            }
            
            Debug.Log($"AI {gameObject.name}: 태그로 {roomList.Count}개의 방을 찾았습니다.");
        }
        
        // 결과 로깅
        if (roomList.Count == 0)
        {
            Debug.LogError($"AI {gameObject.name}: Room을 찾을 수 없습니다! Room 태그가 올바르게 설정되어 있는지 확인하세요.");
        }
        else
        {
            Debug.Log($"AI {gameObject.name}: {roomList.Count}개의 Room이 초기화되었습니다.");
        }
    }

    // 룸 리스트 업데이트 (새로 감지된 룸 반영)
    public static void UpdateRoomList(GameObject[] newRooms)
    {
        if (newRooms == null || newRooms.Length == 0)
            return;
            
        lock (lockObject)
        {
            bool isUpdated = false;
            HashSet<string> processedRoomIds = new HashSet<string>();
            List<RoomInfo> updatedRoomList = new List<RoomInfo>();

            // 새로운 방들을 처리
            foreach (GameObject room in newRooms)
            {
                if (room != null)
                {
                    RoomInfo newRoom = new RoomInfo(room);
                    
                    // 이미 처리된 ID인지 확인
                    if (!processedRoomIds.Contains(newRoom.roomId))
                    {
                        processedRoomIds.Add(newRoom.roomId);
                        
                        // 기존 방 찾기
                        var existingRoom = roomList.FirstOrDefault(r => r.roomId == newRoom.roomId);
                        if (existingRoom != null)
                        {
                            // 기존 방의 상태를 유지하면서 업데이트
                            newRoom.isOccupied = existingRoom.isOccupied;
                            updatedRoomList.Add(newRoom);
                            Debug.Log($"방 업데이트: {newRoom.roomId}");
                        }
                        else
                        {
                            // 새로운 방 추가
                            updatedRoomList.Add(newRoom);
                            isUpdated = true;
                            Debug.Log($"새로운 방 추가: {newRoom.roomId}");
                        }
                    }
                }
            }

            // 룸 리스트 교체
            if (updatedRoomList.Count > 0)
            {
                roomList = updatedRoomList;
                Debug.Log($"룸 리스트 업데이트 완료. 총 {roomList.Count}개의 방이 있습니다.");
                foreach (var room in roomList)
                {
                    Debug.Log($"- 방 ID: {room.roomId}, 사용 중: {room.isOccupied}");
                }
            }
        }
    }

    // AIAgent 내에서 RoomDetector 감지된 룸 정보를 업데이트할 수 있도록 하는 공개 메서드
    public static void NotifyRoomsUpdated(GameObject[] rooms)
    {
        if (OnRoomsUpdated != null)
        {
            OnRoomsUpdated(rooms);
        }
    }

    void Update()
    {
        if (!agent.isOnNavMesh)
        {
            Debug.LogWarning($"AI {gameObject.name}: NavMesh에서 벗어남");
            ReturnToPool();
            return;
        }

        switch (currentState)
        {
            case AIState.Wandering:
                // 배회 중에는 WanderingBehavior 코루틴이 처리
                break;
            
            case AIState.MovingToQueue:
            case AIState.WaitingInQueue:
                // 대기열에서의 동작은 QueueBehavior 코루틴이 처리
                break;

            case AIState.MovingToRoom:
                if (!agent.pathPending && agent.remainingDistance < arrivalDistance)
                {
                    StartCoroutine(UseRoom());
                }
                break;

            case AIState.UsingRoom:
                // 방 사용은 UseRoom 코루틴이 처리
                break;

            case AIState.ReportingRoom:
                // 방 사용 완료 보고는 ReportRoomVacancy 코루틴이 처리
                break;

            case AIState.ReturningToSpawn:
                if (!agent.pathPending && agent.remainingDistance < arrivalDistance)
                {
                    Debug.Log($"AI {gameObject.name}: 스폰 지점 도착, 풀로 반환됩니다.");
                    ReturnToPool();
                }
                break;
        }
    }

    private IEnumerator QueueBehavior()
    {
        // 대기열 합류 요청 (방 배정/사용완료 보고 모두 동일 대기열 사용)
        if (!CounterManager.Instance.TryJoinQueue(this))
        {
            // 대기열이 가득 찼을 때 처리
            if (currentRoomIndex == -1)
            {
                // 방 배정이 필요한 경우, 50% 확률로 배회 또는 퇴장
                if (Random.value < 0.5f)
                {
                    TransitionToState(AIState.Wandering);
                    wanderingCoroutine = StartCoroutine(WanderingBehavior());
                }
                else
                {
                    TransitionToState(AIState.ReturningToSpawn);
                    agent.SetDestination(spawnPoint.position);
                }
            }
            else
            {
                // 방 사용 완료 보고인 경우, 잠시 후 다시 시도
                yield return new WaitForSeconds(Random.Range(1f, 3f));
                StartCoroutine(QueueBehavior());
            }
            yield break;
        }

        isInQueue = true;
        TransitionToState(AIState.WaitingInQueue);

        // 서비스 받을 순서가 될 때까지 대기
        while (isInQueue)
        {
            // 목적지에 도착했는지 확인
            if (!agent.pathPending && agent.remainingDistance < arrivalDistance)
            {
                if (CounterManager.Instance.CanReceiveService(this))
                {
                    CounterManager.Instance.StartService(this);
                    isWaitingForService = true;
                    
                    // 서비스가 완료될 때까지 대기
                    while (isWaitingForService)
                    {
                        yield return new WaitForSeconds(0.1f);
                    }
                    
                    // 서비스 완료 후 처리 - AI의 현재 상태에 따라 다른 처리
                    if (currentRoomIndex != -1)
                    {
                        // 방 사용 완료 보고인 경우
                        roomList[currentRoomIndex].isOccupied = false;
                        currentRoomIndex = -1;
                        TransitionToState(AIState.ReturningToSpawn);
                        agent.SetDestination(spawnPoint.position);
                    }
                    else
                    {
                        // 방 배정 요청인 경우
                        if (TryAssignRoom())
                        {
                            TransitionToState(AIState.MovingToRoom);
                            agent.SetDestination(roomList[currentRoomIndex].transform.position);
                        }
                        else
                        {
                            // 빈 방이 없을 때 50% 확률로 배회 또는 퇴장
                            if (Random.value < 0.5f)
                            {
                                TransitionToState(AIState.Wandering);
                                wanderingCoroutine = StartCoroutine(WanderingBehavior());
                            }
                            else
                            {
                                TransitionToState(AIState.ReturningToSpawn);
                                agent.SetDestination(spawnPoint.position);
                            }
                        }
                    }
                    break;
                }
            }
            yield return new WaitForSeconds(0.1f);
        }
    }

    private bool TryAssignRoom()
    {
        lock (lockObject)
        {
            // 사용 가능한 방 목록 생성
            var availableRooms = new List<int>();
            for (int i = 0; i < roomList.Count; i++)
            {
                if (!roomList[i].isOccupied)
                {
                    availableRooms.Add(i);
                }
            }

            if (availableRooms.Count == 0)
            {
                Debug.Log($"AI {gameObject.name}: 사용 가능한 방이 없습니다.");
                return false;
            }

            // 랜덤하게 방 선택
            int randomIndex = Random.Range(0, availableRooms.Count);
            int selectedRoomIndex = availableRooms[randomIndex];

            // 다시 한번 점유 상태 확인 (다른 AI가 선점했을 수 있음)
            if (!roomList[selectedRoomIndex].isOccupied)
            {
                roomList[selectedRoomIndex].isOccupied = true;
                currentRoomIndex = selectedRoomIndex;
                Debug.Log($"AI {gameObject.name}: 방 {selectedRoomIndex + 1}번이 배정되었습니다.");
                return true;
            }
            else
            {
                Debug.Log($"AI {gameObject.name}: 방 {selectedRoomIndex + 1}번이 이미 사용 중입니다.");
                return false;
            }
        }
    }

    private void TransitionToState(AIState newState)
    {
        // 이전 상태의 코루틴 정리
        CleanupCoroutines();
        
        // 상태 전환 전 정리
        if (currentState == AIState.UsingRoom)
        {
            isBeingServed = false;
        }
        
        currentState = newState;
        currentDestination = GetStateDescription(newState);
        
        Debug.Log($"AI {gameObject.name}: 상태가 {newState}로 변경되었습니다.");
        
        // 새로운 상태에 맞는 동작 시작
        switch (newState)
        {
            case AIState.Wandering:
                wanderingCoroutine = StartCoroutine(WanderingBehavior());
                break;
            case AIState.MovingToRoom:
                if (currentRoomIndex != -1)
                {
                    agent.SetDestination(roomList[currentRoomIndex].transform.position);
                }
                break;
            case AIState.ReturningToSpawn:
                agent.SetDestination(spawnPoint.position);
                break;
        }
    }

    private string GetStateDescription(AIState state)
    {
        switch (state)
        {
            case AIState.Wandering:
                return "배회 중";
            case AIState.MovingToQueue:
                return "대기열로 이동 중";
            case AIState.WaitingInQueue:
                return "대기열에서 대기 중";
            case AIState.MovingToRoom:
                return $"방 {currentRoomIndex + 1}번으로 이동 중";
            case AIState.UsingRoom:
                return "방 사용 중";
            case AIState.ReportingRoom:
                return "방 사용 완료 보고 중";
            case AIState.ReturningToSpawn:
                return "퇴장하는 중";
            default:
                return "알 수 없는 상태";
        }
    }

    private IEnumerator UseRoom()
    {
        float roomUseTime = Random.Range(25f, 35f);  // 방 사용 시간을 25-35초 사이로 랜덤 설정
        float elapsedTime = 0f;
        bool stayInRoom = Random.value < 0.5f;  // 50% 확률로 방 안/밖 배회 결정
        float wanderRadius = 5f;  // 방 반경 기준 배회 범위

        if (currentRoomIndex < 0 || currentRoomIndex >= roomList.Count)
        {
            Debug.LogError($"AI {gameObject.name}: 잘못된 방 인덱스 {currentRoomIndex}, 방 사용을 중단합니다.");
            StartCoroutine(ReportRoomVacancy());
            yield break;
        }

        TransitionToState(AIState.UsingRoom);

        if (stayInRoom)
        {
            Debug.Log($"AI {gameObject.name}: 방 {currentRoomIndex + 1}번 안에서 배회합니다.");
            
            // 방 안에서 배회
            while (elapsedTime < roomUseTime && agent.isOnNavMesh)
            {
                Vector3 roomCenter = roomList[currentRoomIndex].transform.position;
                float roomSize = roomList[currentRoomIndex].size;
                Vector3 targetPos;

                if (TryGetValidPosition(roomCenter, roomSize, NavMesh.AllAreas, out targetPos))
                {
                    agent.SetDestination(targetPos);
                }
                else
                {
                    // 유효한 위치를 찾지 못했다면 방 밖에서 배회로 전환
                    Debug.LogWarning($"AI {gameObject.name}: 방 {currentRoomIndex + 1}번 안에서 유효한 위치를 찾지 못했습니다. 방 밖에서 배회합니다.");
                    stayInRoom = false;
                    break;
                }

                // 2-5초 기다렸다가 다음 위치로 이동
                yield return new WaitForSeconds(Random.Range(2f, 5f));
                elapsedTime += Random.Range(2f, 5f);
            }
        }

        if (!stayInRoom)
        {
            Debug.Log($"AI {gameObject.name}: 방 {currentRoomIndex + 1}번 밖에서 배회합니다.");
            
            // 방 밖에서 배회
            while (elapsedTime < roomUseTime && agent.isOnNavMesh)
            {
                Vector3 roomCenter = roomList[currentRoomIndex].transform.position;
                Vector3 randomPoint = roomCenter + Random.insideUnitSphere * wanderRadius;
                randomPoint.y = transform.position.y; // 높이 유지
                
                NavMeshHit hit;
                if (NavMesh.SamplePosition(randomPoint, out hit, wanderRadius, NavMesh.AllAreas))
                {
                    agent.SetDestination(hit.position);
                }
                
                // 3-7초 기다렸다가 다음 위치로 이동
                yield return new WaitForSeconds(Random.Range(3f, 7f));
                elapsedTime += Random.Range(3f, 7f);
            }
        }

        // 방 사용 완료
        Debug.Log($"AI {gameObject.name}: 방 {currentRoomIndex + 1}번 사용 완료, 보고를 위해 대기열로 이동합니다.");
        StartCoroutine(ReportRoomVacancy());
    }

    private IEnumerator ReportRoomVacancy()
    {
        TransitionToState(AIState.ReportingRoom);
        
        int reportingRoomIndex = currentRoomIndex; // 현재 방 번호 저장
        Debug.Log($"AI {gameObject.name}: 방 {reportingRoomIndex + 1}번 사용 완료를 보고합니다.");
        
        // 방 사용 완료 표시
        lock (lockObject)
        {
            if (reportingRoomIndex >= 0 && reportingRoomIndex < roomList.Count)
            {
                roomList[reportingRoomIndex].isOccupied = false;
                currentRoomIndex = -1;
                Debug.Log($"방 {reportingRoomIndex + 1}번이 비워졌습니다.");
            }
        }

        // 대기열 시스템을 통해 방 비움 보고
        StartCoroutine(QueueBehavior());
        
        yield break;
    }

    private void WanderOnGround()
    {
        NavMeshHit hit;
        Vector3 randomPoint = transform.position + Random.insideUnitSphere * 10f;
        int groundMask = NavMesh.GetAreaFromName("Ground");
        if (groundMask == 0)
        {
            Debug.LogError($"AI {gameObject.name}: Ground NavMesh 영역이 설정되지 않았습니다. 배회를 중단합니다.");
            return;
        }
        
        if (NavMesh.SamplePosition(randomPoint, out hit, 10f, groundMask))
        {
            agent.SetDestination(hit.position);
            Debug.Log($"AI {gameObject.name}: 새로운 배회 위치로 이동합니다.");
        }
    }

    private IEnumerator WanderingBehavior()
    {
        float wanderingTime = Random.Range(15f, 30f);
        float elapsedTime = 0f;
        
        while (currentState == AIState.Wandering && elapsedTime < wanderingTime)
        {
            WanderOnGround();
            float waitTime = Random.Range(3f, 7f);
            yield return new WaitForSeconds(waitTime);
            elapsedTime += waitTime;
        }
        
        // 배회 시간이 끝나면 스폰 지점으로 이동
        TransitionToState(AIState.ReturningToSpawn);
        agent.SetDestination(spawnPoint.position);
        Debug.Log($"AI {gameObject.name}: 배회 시간 종료, 퇴장합니다.");
    }

    // 안전한 위치 찾기 함수
    private bool TryGetValidPosition(Vector3 center, float radius, int layerMask, out Vector3 result)
    {
        result = center;
        float searchRadius = radius * 0.8f; // 방 크기의 80%만 사용하여 경계에 가까운 위치는 피함
        
        // 최대 시도 횟수만큼 반복
        for (int i = 0; i < maxRetries; i++)
        {
            // 방 중심에서 랜덤한 방향으로 위치 생성
            Vector2 randomCircle = Random.insideUnitCircle * searchRadius;
            Vector3 randomPoint = center + new Vector3(randomCircle.x, 0, randomCircle.y);
            
            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPoint, out hit, searchRadius, layerMask))
            {
                // 방 중심으로부터의 거리 확인
                float distanceFromCenter = Vector3.Distance(hit.position, center);
                if (distanceFromCenter <= searchRadius)
                {
                    result = hit.position;
                    return true;
                }
            }
        }
        return false;
    }

    // AISpawner 참조 설정 메서드
    public void SetSpawner(AISpawner spawnerRef)
    {
        spawner = spawnerRef;
    }

    // 풀로 반환하는 메서드
    private void ReturnToPool()
    {
        CleanupCoroutines();
        CleanupResources();
        
        if (spawner != null)
        {
            spawner.ReturnToPool(gameObject);
        }
        else
        {
            Debug.LogWarning($"AI {gameObject.name}: spawner 참조가 없어 Destroy됩니다.");
            Destroy(gameObject);
        }
    }

    // OnDisable과 OnDestroy에서 ReturnToPool 호출
    void OnDisable()
    {
        CleanupCoroutines();
        CleanupResources();
    }

    void OnDestroy()
    {
        CleanupCoroutines();
        CleanupResources();
    }

    private void CleanupCoroutines()
    {
        if (wanderingCoroutine != null)
        {
            StopCoroutine(wanderingCoroutine);
            wanderingCoroutine = null;
        }
        if (roomUseCoroutine != null)
        {
            StopCoroutine(roomUseCoroutine);
            roomUseCoroutine = null;
        }
    }

    private void CleanupResources()
    {
        // 현재 사용 중인 방이 있다면 비움 처리
        if (currentRoomIndex != -1)
        {
            lock (lockObject)
            {
                if (currentRoomIndex < roomList.Count)
                {
                    roomList[currentRoomIndex].isOccupied = false;
                    Debug.Log($"AI {gameObject.name} 리소스 정리: 방 {currentRoomIndex + 1}번 반환");
                }
                currentRoomIndex = -1;
            }
        }

        // 상태 초기화
        isBeingServed = false;
        isInQueue = false;
        isWaitingForService = false;

        // 대기열에서 제거
        if (CounterManager.Instance != null)
        {
            CounterManager.Instance.LeaveQueue(this);
        }
    }

    // UI 표시 함수
    void OnGUI()
    {
        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position);
        if (screenPos.z > 0)  // AI가 카메라 앞에 있을 때만 표시
        {
            Vector2 guiPosition = new Vector2(screenPos.x, Screen.height - screenPos.y);
            string displayText = currentDestination;
            GUI.Label(new Rect(guiPosition.x - 50, guiPosition.y - 50, 100, 40), displayText);
        }
    }

    // AI 초기화 메서드 추가
    public void InitializeAI()
    {
        // 상태 초기화
        currentState = AIState.MovingToQueue;
        currentDestination = "대기열로 이동 중";
        isBeingServed = false;
        isInQueue = false;
        isWaitingForService = false;
        currentRoomIndex = -1;

        // 위치 및 회전 초기화
        if (agent != null)
        {
            agent.ResetPath();
            
            // 초기 상태 설정 - 랜덤 배회 또는 대기열로 이동
            if (Random.value < 0.3f)
            {
                currentState = AIState.Wandering;
                currentDestination = "배회 중";
                wanderingCoroutine = StartCoroutine(WanderingBehavior());
                Debug.Log($"AI {gameObject.name}: 배회를 시작합니다.");
            }
            else
            {
                currentState = AIState.MovingToQueue;
                currentDestination = "대기열로 이동 중";
                StartCoroutine(QueueBehavior()); // 바로 대기열 행동 시작
                Debug.Log($"AI {gameObject.name}: 대기열로 이동을 시작합니다.");
            }
        }
    }

    void OnEnable()
    {
        InitializeAI();
    }

    // CounterManager에서 호출할 메서드들
    public void SetQueueDestination(Vector3 position)
    {
        targetQueuePosition = position;
        if (agent != null)
        {
            agent.SetDestination(position);
        }
    }

    // 서비스 완료 콜백
    public void OnServiceComplete()
    {
        isWaitingForService = false;
        isInQueue = false;
        CounterManager.Instance.LeaveQueue(this);
    }
} 