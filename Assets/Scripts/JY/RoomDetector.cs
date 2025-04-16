using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using JY;

public class RoomDetector : MonoBehaviour
{
    [SerializeField] private PlacementSystem placementSystem;
    [SerializeField] private Grid grid;
    [SerializeField] private bool enableDebugLogs = true;
    [SerializeField] private int minWalls = 4;
    [SerializeField] private int minDoors = 1;
    [SerializeField] private int minBeds = 1;
    [SerializeField] private float scanInterval = 2f;
    [SerializeField] private LayerMask roomElementsLayer;

    private Dictionary<Vector3Int, RoomCell> roomGrid = new Dictionary<Vector3Int, RoomCell>();
    private List<RoomInfo> detectedRooms = new List<RoomInfo>();
    private HashSet<string> existingRoomIds = new HashSet<string>();
    private bool isInitialized = false;

    public delegate void RoomUpdateHandler(GameObject[] rooms);
    public event RoomUpdateHandler OnRoomsUpdated;

    public class RoomCell
    {
        public bool isFloor;
        public bool isWall;
        public bool isDoor;
        public bool isBed;
        public Vector3Int position;
        public List<GameObject> objects = new List<GameObject>();
    }

    public class RoomInfo
    {
        public List<Vector3Int> floorCells = new List<Vector3Int>();
        public List<GameObject> walls = new List<GameObject>();
        public List<GameObject> doors = new List<GameObject>();
        public List<GameObject> beds = new List<GameObject>();
        public Bounds bounds;
        public Vector3 center;
        public string roomId;
        public GameObject gameObject;

        public bool isValid(int minWalls, int minDoors, int minBeds)
        {
            return walls.Count >= minWalls && doors.Count >= minDoors && beds.Count >= minBeds;
        }
    }

    private void Start()
    {
        InitializeComponents();
        if (isInitialized)
        {
            InvokeRepeating(nameof(ScanForRooms), 1f, scanInterval);
        }
    }

    private void InitializeComponents()
    {
        placementSystem = placementSystem ?? FindObjectOfType<PlacementSystem>();
        grid = grid ?? FindObjectOfType<Grid>();

        if (placementSystem == null || grid == null)
        {
            DebugLog("필수 컴포넌트 누락!", true);
            isInitialized = false;
            return;
        }

        isInitialized = true;
    }

    private void DebugLog(string message, bool isError = false)
    {
        if (enableDebugLogs)
        {
            if (isError)
                Debug.LogError(message);
            else
                Debug.Log(message);
        }
    }

    public void ScanForRooms()
    {
        if (!isInitialized)
        {
            DebugLog("RoomDetector가 초기화되지 않았습니다.", true);
            return;
        }

        DebugLog("방 스캔 시작");
        UpdateGridFromScene();

        List<RoomInfo> newRooms = new List<RoomInfo>();
        HashSet<Vector3Int> visitedCells = new HashSet<Vector3Int>();

        foreach (var cell in roomGrid)
        {
            if (cell.Value.isFloor && !visitedCells.Contains(cell.Key))
            {
                RoomInfo room = FloodFillRoom(cell.Key, visitedCells);
                if (room != null)
                {
                    bool isRoomValid = room.isValid(minWalls, minDoors, minBeds);
                    DebugLog($"방 검증 결과:\n" +
                            $"위치: {room.center}\n" +
                            $"벽 수: {room.walls.Count} (최소 {minWalls}개 필요)\n" +
                            $"문 수: {room.doors.Count} (최소 {minDoors}개 필요)\n" +
                            $"침대 수: {room.beds.Count} (최소 {minBeds}개 필요)\n" +
                            $"유효성: {isRoomValid}");
                    
                    if (isRoomValid)
                    {
                        string roomId = $"Room_{room.center.x:F0}_{room.center.z:F0}";
                        room.roomId = roomId;
                        newRooms.Add(room);
                        DebugLog($"새로운 방 추가됨: {roomId}");
                    }
                }
            }
        }

        DebugLog($"스캔 결과:\n" +
                 $"새로 감지된 방: {newRooms.Count}개\n" +
                 $"기존 감지된 방: {detectedRooms.Count}개");

        if (newRooms.Count > 0)
        {
            DebugLog($"{newRooms.Count}개의 새로운 방이 감지되어 업데이트를 시작합니다.");
            UpdateRooms(newRooms);
        }
        else
        {
            DebugLog("감지된 새로운 방이 없습니다.");
        }
    }

    private void UpdateRooms(List<RoomInfo> newRooms)
    {
        foreach (var room in detectedRooms)
        {
            if (room.gameObject != null)
            {
                GameObject.Destroy(room.gameObject);
            }
        }

        detectedRooms = newRooms;
        foreach (var room in detectedRooms)
        {
            CreateRoomGameObject(room);
        }

        if (detectedRooms.Count > 0)
        {
            DebugLog($"총 {detectedRooms.Count}개의 방이 감지됨");
            OnRoomsUpdated?.Invoke(detectedRooms.Select(r => r.gameObject).ToArray());
        }
        else
        {
            DebugLog("감지된 방이 없음");
        }
    }

    private void UpdateGridFromScene()
    {
        roomGrid.Clear();
        DebugLog("그리드 업데이트 시작");

        // 각 태그별로 오브젝트 검색 및 부모 오브젝트 위치 사용
        ProcessTaggedObjects("Floor", (obj) => {
            Vector3Int gridPosition = GetParentGridPosition(obj);
            if (!roomGrid.ContainsKey(gridPosition))
            {
                roomGrid[gridPosition] = new RoomCell { position = gridPosition, objects = new List<GameObject>() };
            }
            roomGrid[gridPosition].isFloor = true;
            roomGrid[gridPosition].objects.Add(obj.transform.parent?.gameObject ?? obj);
        });

        ProcessTaggedObjects("Wall", (obj) => {
            Vector3Int gridPosition = GetParentGridPosition(obj);
            if (!roomGrid.ContainsKey(gridPosition))
            {
                roomGrid[gridPosition] = new RoomCell { position = gridPosition, objects = new List<GameObject>() };
            }
            roomGrid[gridPosition].isWall = true;
            roomGrid[gridPosition].objects.Add(obj.transform.parent?.gameObject ?? obj);
        });

        ProcessTaggedObjects("Door", (obj) => {
            Vector3Int gridPosition = GetParentGridPosition(obj);
            if (!roomGrid.ContainsKey(gridPosition))
            {
                roomGrid[gridPosition] = new RoomCell { position = gridPosition, objects = new List<GameObject>() };
            }
            roomGrid[gridPosition].isDoor = true;
            roomGrid[gridPosition].objects.Add(obj.transform.parent?.gameObject ?? obj);
        });

        ProcessTaggedObjects("Bed", (obj) => {
            Vector3Int gridPosition = GetParentGridPosition(obj);
            if (!roomGrid.ContainsKey(gridPosition))
            {
                roomGrid[gridPosition] = new RoomCell { position = gridPosition, objects = new List<GameObject>() };
            }
            roomGrid[gridPosition].isBed = true;
            roomGrid[gridPosition].objects.Add(obj.transform.parent?.gameObject ?? obj);
        });

        // 그리드 정보 출력
        foreach (var cell in roomGrid)
        {
            DebugLog($"Grid Cell {cell.Key}:\n" +
                     $"Floor: {cell.Value.isFloor}\n" +
                     $"Wall: {cell.Value.isWall}\n" +
                     $"Door: {cell.Value.isDoor}\n" +
                     $"Bed: {cell.Value.isBed}\n" +
                     $"Objects: {cell.Value.objects.Count}");
        }
    }

    private Vector3Int GetParentGridPosition(GameObject obj)
    {
        // 부모 오브젝트의 위치를 사용하되, 부모가 없으면 자신의 위치 사용
        Vector3 worldPosition = obj.transform.parent != null ? 
            obj.transform.parent.position : 
            obj.transform.position;
        
        Vector3Int gridPosition = grid.WorldToCell(worldPosition);
        DebugLog($"오브젝트 위치 변환: {obj.name}\n" +
                 $"World Position: {worldPosition}\n" +
                 $"Grid Position: {gridPosition}");
                 
        return gridPosition;
    }

    private void ProcessTaggedObjects(string tag, System.Action<GameObject> processor)
    {
        var taggedObjects = GameObject.FindGameObjectsWithTag(tag);
        DebugLog($"{tag} 태그 오브젝트 수: {taggedObjects.Length}");
        
        foreach (var obj in taggedObjects)
        {
            if (obj == null) continue;
            processor(obj);
        }
    }

    private RoomInfo FloodFillRoom(Vector3Int startPos, HashSet<Vector3Int> visitedCells)
    {
        RoomInfo room = new RoomInfo();
        Queue<Vector3Int> queue = new Queue<Vector3Int>();
        queue.Enqueue(startPos);
        visitedCells.Add(startPos);

        Vector3Int minBounds = startPos;
        Vector3Int maxBounds = startPos;

        DebugLog($"방 탐색 시작 - 시작 위치: {startPos}");

        // 4방향 탐색
        Vector3Int[] directions = new Vector3Int[]
        {
            new Vector3Int(1, 0, 0),   // 오른쪽
            new Vector3Int(-1, 0, 0),  // 왼쪽
            new Vector3Int(0, 0, 1),   // 앞
            new Vector3Int(0, 0, -1)   // 뒤
        };

        HashSet<Vector3Int> roomCells = new HashSet<Vector3Int>();
        roomCells.Add(startPos);

        while (queue.Count > 0)
        {
            Vector3Int current = queue.Dequeue();
            
            if (!roomGrid.TryGetValue(current, out RoomCell currentCell))
            {
                DebugLog($"셀 없음: {current}");
                continue;
            }

            if (currentCell.isFloor)
            {
                room.floorCells.Add(current);
                minBounds = Vector3Int.Min(minBounds, current);
                maxBounds = Vector3Int.Max(maxBounds, current);

                // 주변 셀 검사
                foreach (var dir in directions)
                {
                    Vector3Int neighbor = current + dir;
                    
                    // 이웃 셀이 문인지 확인
                    bool isDoorBetween = false;
                    if (roomGrid.TryGetValue(neighbor, out RoomCell neighborCell))
                    {
                        if (neighborCell.isDoor)
                        {
                            isDoorBetween = true;
                            foreach (var obj in neighborCell.objects)
                            {
                                if (!room.doors.Contains(obj))
                                {
                                    room.doors.Add(obj);
                                    DebugLog($"문 발견: {neighbor}");
                                }
                            }
                        }
                    }

                    // 문이 있는 방향으로는 더 이상 진행하지 않음
                    if (!isDoorBetween)
                    {
                        // 벽 확인
                        if (roomGrid.TryGetValue(neighbor, out RoomCell wallCell) && wallCell.isWall)
                        {
                            foreach (var obj in wallCell.objects)
                            {
                                if (!room.walls.Contains(obj))
                                {
                                    room.walls.Add(obj);
                                    DebugLog($"벽 발견: {neighbor}");
                                }
                            }
                        }

                        // 침대 확인
                        if (roomGrid.TryGetValue(neighbor, out RoomCell bedCell) && bedCell.isBed)
                        {
                            foreach (var obj in bedCell.objects)
                            {
                                if (!room.beds.Contains(obj))
                                {
                                    room.beds.Add(obj);
                                    DebugLog($"침대 발견: {neighbor}");
                                }
                            }
                        }

                        // 바닥이 있고 아직 방문하지 않은 경우에만 큐에 추가
                        if (roomGrid.TryGetValue(neighbor, out RoomCell floorCell) && 
                            floorCell.isFloor && 
                            !visitedCells.Contains(neighbor))
                        {
                            queue.Enqueue(neighbor);
                            visitedCells.Add(neighbor);
                            roomCells.Add(neighbor);
                            DebugLog($"다음 바닥 탐색: {neighbor}");
                        }
                    }
                }
            }
        }

        if (room.floorCells.Count > 0)
        {
            Vector3 worldMin = grid.GetCellCenterWorld(minBounds);
            Vector3 worldMax = grid.GetCellCenterWorld(maxBounds);
            room.bounds = new Bounds();
            room.bounds.SetMinMax(worldMin, worldMax);
            room.center = room.bounds.center;
            room.roomId = $"Room_{room.center.x:F0}_{room.center.z:F0}";

            DebugLog($"방 감지 완료:\n" +
                     $"ID: {room.roomId}\n" +
                     $"중심점: {room.center}\n" +
                     $"바닥: {room.floorCells.Count}개\n" +
                     $"벽: {room.walls.Count}개\n" +
                     $"문: {room.doors.Count}개\n" +
                     $"침대: {room.beds.Count}개\n" +
                     $"유효성: {room.isValid(minWalls, minDoors, minBeds)}");

            return room;
        }

        DebugLog("유효한 방이 감지되지 않음");
        return null;
    }

    private bool AreRoomListsEqual(List<RoomInfo> list1, List<RoomInfo> list2)
    {
        if (list1.Count != list2.Count)
            return false;

        var sortedList1 = list1.OrderBy(r => r.center.x).ThenBy(r => r.center.z).ToList();
        var sortedList2 = list2.OrderBy(r => r.center.x).ThenBy(r => r.center.z).ToList();

        for (int i = 0; i < sortedList1.Count; i++)
        {
            if (!AreRoomsEqual(sortedList1[i], sortedList2[i]))
                return false;
        }
        return true;
    }

    private bool AreRoomsEqual(RoomInfo room1, RoomInfo room2)
    {
        if (room1.roomId != room2.roomId || room1.center != room2.center)
            return false;

        if (room1.walls.Count != room2.walls.Count ||
            room1.doors.Count != room2.doors.Count ||
            room1.beds.Count != room2.beds.Count ||
            room1.floorCells.Count != room2.floorCells.Count)
            return false;

        bool wallsEqual = room1.walls.All(w1 => room2.walls.Any(w2 => w2.GetInstanceID() == w1.GetInstanceID()));
        bool doorsEqual = room1.doors.All(d1 => room2.doors.Any(d2 => d2.GetInstanceID() == d1.GetInstanceID()));
        bool bedsEqual = room1.beds.All(b1 => room2.beds.Any(b2 => b2.GetInstanceID() == b1.GetInstanceID()));

        if (!wallsEqual || !doorsEqual || !bedsEqual)
            return false;

        var sortedFloors1 = room1.floorCells.OrderBy(v => v.x).ThenBy(v => v.z).ToList();
        var sortedFloors2 = room2.floorCells.OrderBy(v => v.x).ThenBy(v => v.z).ToList();
        
        for (int i = 0; i < sortedFloors1.Count; i++)
        {
            if (sortedFloors1[i] != sortedFloors2[i])
                return false;
        }

        return true;
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || !enableDebugLogs || detectedRooms == null)
            return;

        Gizmos.color = Color.yellow;
        Vector3 gridCenter = transform.position;
        Vector3 gridSize = new Vector3(100f, 10f, 100f);
        Gizmos.DrawWireCube(gridCenter, gridSize);

        foreach (var room in detectedRooms)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(room.bounds.center, room.bounds.size);

            foreach (var wall in room.walls)
            {
                if (wall != null)
                {
                    Gizmos.color = Color.blue;
                    Gizmos.DrawWireCube(wall.transform.position, Vector3.one);
                    Gizmos.DrawLine(wall.transform.position, room.bounds.center);
                }
            }

            foreach (var door in room.doors)
            {
                if (door != null)
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawWireSphere(door.transform.position, 0.5f);
                    Gizmos.DrawLine(door.transform.position, room.bounds.center);
                }
            }

            foreach (var bed in room.beds)
            {
                if (bed != null)
                {
                    Gizmos.color = Color.magenta;
                    Gizmos.DrawWireCube(bed.transform.position, new Vector3(1f, 0.5f, 2f));
                    Gizmos.DrawLine(bed.transform.position, room.bounds.center);
                }
            }

            UnityEditor.Handles.Label(room.bounds.center, 
                $"Room {room.roomId}\nWalls: {room.walls.Count}\n" +
                $"Doors: {room.doors.Count}\nBeds: {room.beds.Count}\n" +
                $"Valid: {room.isValid(minWalls, minDoors, minBeds)}");
        }
    }

    private void CreateRoomGameObject(RoomInfo room)
    {
        room.gameObject = new GameObject(room.roomId);
        room.gameObject.transform.position = room.center;
        room.gameObject.tag = "Room";

        // RoomContents 컴포넌트 추가 및 설정
        var roomContents = room.gameObject.AddComponent<RoomContents>();
        roomContents.roomID = room.roomId;
        roomContents.SetRoomBounds(room.bounds);

        BoxCollider roomCollider = room.gameObject.AddComponent<BoxCollider>();
        roomCollider.center = Vector3.zero;
        roomCollider.size = new Vector3(room.bounds.size.x, 3f, room.bounds.size.z);
        roomCollider.isTrigger = true;

        // 디버그용 시각화 오브젝트 생성
        var debugVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        debugVisual.transform.parent = room.gameObject.transform;
        debugVisual.transform.localPosition = Vector3.zero;
        debugVisual.transform.localScale = new Vector3(room.bounds.size.x, 0.1f, room.bounds.size.z);
        debugVisual.GetComponent<MeshRenderer>().material.color = new Color(1, 0, 0, 0.2f);
        debugVisual.GetComponent<BoxCollider>().enabled = false;

        DebugLog($"방 생성: {room.roomId}\n" +
                 $"- 위치: {room.center}\n" +
                 $"- 벽: {room.walls.Count}개\n" +
                 $"- 문: {room.doors.Count}개\n" +
                 $"- 침대: {room.beds.Count}개\n" +
                 $"- 바닥: {room.floorCells.Count}개");
    }
} 