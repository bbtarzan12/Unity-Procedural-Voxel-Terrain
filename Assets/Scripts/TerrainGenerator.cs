using System.Collections;
using System.Collections.Generic;
using OptIn.Voxel;
using UnityEngine;

public class TerrainGenerator : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] Vector3Int chunkSize = new Vector3Int(32, 32, 32);
    [SerializeField] Vector3Int chunkSpawnSize = Vector3Int.one * 3;
    [SerializeField] Material chunkMaterial;
    [SerializeField] bool enableJob;
    [SerializeField] int maxGenerateChunksInFrame = 5;
    [SerializeField] int maxUpdateChunksInFrame = 5;
    
    Dictionary<Vector3Int, Chunk> chunks = new Dictionary<Vector3Int, Chunk>();
    Vector3Int lastTargetChunkPosition = new Vector3Int(int.MinValue, int.MaxValue, int.MinValue);
    HashSet<Vector3Int> generateChunkSet = new HashSet<Vector3Int>();
    Queue<Vector3Int> generateChunkQueue = new Queue<Vector3Int>();
    Queue<Chunk> updateChunkQueue = new Queue<Chunk>();
    
    public Vector3Int ChunkSize => chunkSize;
    public bool EnableJob => enableJob;
    public Material ChunkMaterial => chunkMaterial;

    void Update()
    {
        GenerateChunkByTargetPosition();
        UpdateChunkMesh();
        
        ProcessGenerateChunkQueue();
        ProcessUpdateChunkQueue();
    }
    
    void GenerateChunkByTargetPosition()
    {
        if (target == null)
            return;
        
        Vector3Int chunkPosition = VoxelHelper.WorldToChunk(target.position, chunkSize);

        if (lastTargetChunkPosition == chunkPosition)
            return;

        for (int x = chunkPosition.x - chunkSpawnSize.x; x <= chunkPosition.x + chunkSpawnSize.x; x++)
        {
            for (int y = chunkPosition.y - chunkSpawnSize.y; y <= chunkPosition.y + chunkSpawnSize.y; y++)
            {
                for (int z = chunkPosition.z - chunkSpawnSize.z; z <= chunkPosition.z + chunkSpawnSize.z; z++)
                {
                    Vector3Int newChunkPosition = new Vector3Int(x, y, z);
                    if (chunks.ContainsKey(newChunkPosition))
                        continue;
                    
                    if(generateChunkSet.Contains(newChunkPosition))
                        continue;
                    
                    generateChunkQueue.Enqueue(newChunkPosition);
                    generateChunkSet.Add(newChunkPosition);
                }
            }
        }

        lastTargetChunkPosition = chunkPosition;
    }
    
    void UpdateChunkMesh()
    {
        foreach (Chunk chunk in chunks.Values)
        {
            if (!chunk.Dirty)
                continue;

            updateChunkQueue.Enqueue(chunk);
        }
    }

    void ProcessGenerateChunkQueue()
    {
        int numChunks = 0;
        while (generateChunkQueue.Count != 0)
        {
            if (numChunks >= maxGenerateChunksInFrame)
                return;

            Vector3Int chunkPosition = generateChunkQueue.Dequeue();
            GenerateChunk(chunkPosition);
            generateChunkSet.Remove(chunkPosition);
            numChunks++;
        }
    }
    
    void ProcessUpdateChunkQueue()
    {
        int numChunks = 0;
        while (updateChunkQueue.Count != 0)
        {
            if (numChunks >= maxUpdateChunksInFrame)
                return;
            
            Chunk chunk = updateChunkQueue.Dequeue();
            chunk.UpdateMesh();
        }
    }
    
    Chunk GenerateChunk(Vector3Int chunkPosition)
    {
        if (chunks.ContainsKey(chunkPosition))
            return chunks[chunkPosition];

        GameObject chunkGameObject = new GameObject(chunkPosition.ToString());
        chunkGameObject.transform.SetParent(transform);
        chunkGameObject.transform.position = VoxelHelper.ChunkToWorld(chunkPosition, chunkSize);

        Chunk newChunk = chunkGameObject.AddComponent<Chunk>();
        newChunk.Init(chunkPosition, this);

        chunks.Add(chunkPosition, newChunk);
        return newChunk;
    }
}
