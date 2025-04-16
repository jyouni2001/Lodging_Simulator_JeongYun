using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace JY
{
    public class PaymentSystem : MonoBehaviour
    {
        [System.Serializable]
        private class PaymentInfo
        {
            public string aiName;
            public int amount;
            public string roomID;
            public bool isPaid;

            public PaymentInfo(string aiName, int amount, string roomID)
            {
                this.aiName = aiName;
                this.amount = amount;
                this.roomID = roomID;
                this.isPaid = false;
            }
        }

        [SerializeField] private List<PaymentInfo> paymentQueue = new List<PaymentInfo>();
        
        public void AddPayment(string aiName, int amount, string roomID)
        {
            paymentQueue.Add(new PaymentInfo(aiName, amount, roomID));
            Debug.Log($"새로운 결제 등록: {aiName}, 방 {roomID}, {amount}원");
        }
        
        public int ProcessPayment(string aiName)
        {
            int totalAmount = 0;
            List<PaymentInfo> aiPayments = paymentQueue.FindAll(p => p.aiName == aiName && !p.isPaid);
            
            foreach (var payment in aiPayments)
            {
                totalAmount += payment.amount;
                payment.isPaid = true;
                Debug.Log($"결제 처리: {payment.aiName}, 방 {payment.roomID}, {payment.amount}원");
            }
            
            // 처리된 결제 제거
            paymentQueue.RemoveAll(p => p.isPaid);
            
            return totalAmount;
        }
        
        public bool HasUnpaidPayments(string aiName)
        {
            return paymentQueue.Exists(p => p.aiName == aiName && !p.isPaid);
        }
        
        public int GetTotalUnpaidAmount(string aiName)
        {
            return paymentQueue
                .Where(p => p.aiName == aiName && !p.isPaid)
                .Sum(p => p.amount);
        }
    }
} 