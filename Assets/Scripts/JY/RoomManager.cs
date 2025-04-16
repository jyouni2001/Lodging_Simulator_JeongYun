using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using JY;

/// <summary>
/// 방 관리 및 요금 청구를 담당하는 매니저 클래스
/// </summary>
public class RoomManager : MonoBehaviour
{
    [Header("Room Management")]
    [Tooltip("모든 방 내용물 관리 컴포넌트")]
    public List<RoomContents> allRooms = new List<RoomContents>();
    
    [Tooltip("방 결제 시스템 참조")]
    public PaymentSystem paymentSystem;
    
    [Header("Room Settings")]
    [Tooltip("방을 찾을 때 사용할 태그")]
    public string roomTag = "Room";
    
    [Header("Pricing")]
    [Tooltip("오늘의 방 요금 배율")]
    public float priceMultiplier = 1.0f;
    
    [Header("Debug")]
    [Tooltip("디버그 로그 표시")]
    public bool showDebug = true;
    
    [Tooltip("사용된 방 정보")]
    [SerializeField] private List<string> usedRoomLogs = new List<string>();
    
    [Tooltip("결제 내역")]
    [SerializeField] private List<string> paymentLogs = new List<string>();
    
    // 방 자동 검색
    private void Start()
    {
        FindAllRooms();
    }
    
    // 씬의 모든 방 검색
    public void FindAllRooms()
    {
        allRooms.Clear();
        
        // 태그로 방 찾기
        GameObject[] roomObjects = GameObject.FindGameObjectsWithTag(roomTag);
        foreach (GameObject roomObj in roomObjects)
        {
            RoomContents roomContents = roomObj.GetComponent<RoomContents>();
            if (roomContents != null)
            {
                allRooms.Add(roomContents);
            }
        }
        
        // 방 번호 할당
        for (int i = 0; i < allRooms.Count; i++)
        {
            if (string.IsNullOrEmpty(allRooms[i].roomID))
            {
                allRooms[i].roomID = (i + 101).ToString(); // 101, 102, 103...
            }
        }
        
        if (showDebug)
        {
            Debug.Log($"총 {allRooms.Count}개의 방이 감지되었습니다.");
            if (allRooms.Count == 0)
            {
                Debug.LogWarning($"'{roomTag}' 태그를 가진 방을 찾을 수 없습니다. 방 오브젝트에 태그가 설정되어 있는지 확인하세요.");
            }
        }
    }
    
    // 새로운 방이 생성되었을 때 호출
    public void RegisterNewRoom(RoomContents room)
    {
        if (room != null && !allRooms.Contains(room))
        {
            allRooms.Add(room);
            if (string.IsNullOrEmpty(room.roomID))
            {
                room.roomID = (allRooms.Count + 100).ToString();
            }
            if (showDebug)
            {
                Debug.Log($"새로운 방 {room.roomID}이(가) 등록되었습니다.");
            }
        }
    }
    
    // 방이 제거되었을 때 호출
    public void UnregisterRoom(RoomContents room)
    {
        if (room != null && allRooms.Contains(room))
        {
            allRooms.Remove(room);
            if (showDebug)
            {
                Debug.Log($"방 {room.roomID}이(가) 제거되었습니다.");
            }
        }
    }

    // AI가 방을 사용했을 때 호출
    public void ReportRoomUsage(string aiName, RoomContents room)
    {
        if (room == null) return;
        
        // 방이 이미 사용 중인지 확인
        if (room.IsRoomUsed)
        {
            if (showDebug)
                Debug.Log($"{aiName}가 이미 사용 중인 방 {room.roomID}에 접근했습니다.");
            return;
        }
        
        // 방 요금 계산 (방 가격 * 오늘의 배율)
        int finalPrice = Mathf.RoundToInt(room.UseRoom() * priceMultiplier);
        
        // 로그 추가
        string usageLog = $"{aiName}이(가) 방 {room.roomID}을(를) 사용: {finalPrice}원";
        usedRoomLogs.Add(usageLog);
        
        if (showDebug)
            Debug.Log(usageLog);
        
        // 결제 시스템에 요금 추가 (있는 경우)
        if (paymentSystem != null)
        {
            paymentSystem.AddPayment(aiName, finalPrice, room.roomID);
        }
    }
    
    // AI가 카운터에서 방 사용 요금을 지불할 때 호출
    public int ProcessRoomPayment(string aiName)
    {
        if (paymentSystem == null) return 0;
        
        int amount = paymentSystem.ProcessPayment(aiName);
        
        string paymentLog = $"{aiName}의 방 사용 요금 결제: {amount}원";
        paymentLogs.Add(paymentLog);
        
        if (showDebug)
            Debug.Log(paymentLog);
            
        return amount;
    }
    
    // 방 확인 및 업데이트
    public void UpdateRooms()
    {
        foreach (var room in allRooms)
        {
            room.UpdateRoomContents();
        }
    }
    
    // 특정 금액 범위 내의 방 찾기
    public List<RoomContents> FindRoomsInPriceRange(int minPrice, int maxPrice)
    {
        return allRooms.Where(r => !r.IsRoomUsed && r.TotalRoomPrice >= minPrice && r.TotalRoomPrice <= maxPrice).ToList();
    }
    
    // 사용 가능한 모든 방 찾기
    public List<RoomContents> GetAvailableRooms()
    {
        return allRooms.Where(r => !r.IsRoomUsed).ToList();
    }
} 