using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu]
public class ObjectsDatabaseSO : ScriptableObject
{
    public List<ObjectData> objectsData;
    private Dictionary<int, ObjectData> objectDataDictionary;

    public void InitializeDictionary()
    {
        objectDataDictionary = new Dictionary<int, ObjectData>();
        foreach (var data in objectsData)
        {
            if (!objectDataDictionary.ContainsKey(data.ID))
            {
                objectDataDictionary.Add(data.ID, data);
            }
        }
    }

    public ObjectData GetObjectData(int id)
    {
        if (objectDataDictionary == null)
        {
            InitializeDictionary();
        }
        return objectDataDictionary.TryGetValue(id, out ObjectData data) ? data : null;
    }
}

[Serializable]
public class ObjectData
{
    [field : SerializeField]
    public string Name { get; private set; }
    
    [field : SerializeField]
    public int ID { get; private set; }

    [field: SerializeField] 
    public Vector2Int Size { get; private set; } = Vector2Int.one;
    
    [field : SerializeField]
    public GameObject Prefab { get; private set; }

    [field : SerializeField]
    public bool IsWall { get; private set; }

    [field : SerializeField]
    public int kindIndex { get; private set; }

    [field : SerializeField]
    public int BasePrice { get; private set; }
}
