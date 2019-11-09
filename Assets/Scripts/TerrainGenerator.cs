using System.Collections;
using System.Collections.Generic;
using OptIn.Voxel;
using Priority_Queue;
using UnityEngine;

public class TerrainGenerator : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] int chunkSize = 32;
    [SerializeField] Vector3Int chunkSpawnSize = Vector3Int.one * 3;
    [SerializeField] Material chunkMaterial;
    [SerializeField] int maxGenerateChunksInFrame = 5;
    [SerializeField] VoxelMeshBuilder.SimplifyingMethod simplifyingMethod;

    class ChunkNode : FastPriorityQueueNode
    {
        public Vector3Int chunkPosition;
    } 
    
    Dictionary<Vector3Int, Chunk> chunks = new Dictionary<Vector3Int, Chunk>();
    Vector3Int lastTargetChunkPosition = new Vector3Int(int.MinValue, int.MaxValue, int.MinValue);
    FastPriorityQueue<ChunkNode> generateChunkQueue = new FastPriorityQueue<ChunkNode>(10000);

    public int ChunkSize => chunkSize;
    public Material ChunkMaterial => chunkMaterial;
    public VoxelMeshBuilder.SimplifyingMethod SimplifyingMethod => simplifyingMethod;

    void Update()
    {
        GenerateChunkByTargetPosition();
        UpdateChunkMesh();
    }

    void LateUpdate()
    {
        ProcessGenerateChunkQueue();
    }

    void GenerateChunkByTargetPosition()
    {
        if (target == null)
            return;
        
        Vector3Int targetPosition = VoxelUtil.WorldToChunk(target.position, chunkSize);

        if (lastTargetChunkPosition == targetPosition)
            return;

        foreach (ChunkNode chunkNode in generateChunkQueue)
        {
            Vector3Int deltaPosition = targetPosition - chunkNode.chunkPosition;
            if (chunkSpawnSize.x < Mathf.Abs(deltaPosition.x) || chunkSpawnSize.y < Mathf.Abs(deltaPosition.y) || chunkSpawnSize.z < Mathf.Abs(deltaPosition.z))
            {
                generateChunkQueue.Remove(chunkNode);
                continue;
            }
            
            generateChunkQueue.UpdatePriority(chunkNode, (targetPosition - chunkNode.chunkPosition).sqrMagnitude);
        }

        for (int x = targetPosition.x - chunkSpawnSize.x; x <= targetPosition.x + chunkSpawnSize.x; x++)
        {
            for (int y = targetPosition.y - chunkSpawnSize.y; y <= targetPosition.y + chunkSpawnSize.y; y++)
            {
                for (int z = targetPosition.z - chunkSpawnSize.z; z <= targetPosition.z + chunkSpawnSize.z; z++)
                {
                    Vector3Int chunkPosition = new Vector3Int(x, y, z);
                    if (chunks.ContainsKey(chunkPosition))
                        continue;

                    ChunkNode newNode = new ChunkNode {chunkPosition = chunkPosition};
                    
                    if(generateChunkQueue.Contains(newNode))
                        continue;
                    
                    generateChunkQueue.Enqueue(newNode, (targetPosition - chunkPosition).sqrMagnitude);
                }
            }
        }

        lastTargetChunkPosition = targetPosition;
    }
    
    void UpdateChunkMesh()
    {
        foreach (Chunk chunk in chunks.Values)
        {
            if (!chunk.Dirty)
                continue;

            chunk.UpdateMesh();
        }
    }

    void ProcessGenerateChunkQueue()
    {
        int numChunks = 0;
        while (generateChunkQueue.Count != 0)
        {
            if (numChunks >= maxGenerateChunksInFrame)
                return;

            Vector3Int chunkPosition = generateChunkQueue.Dequeue().chunkPosition;
            GenerateChunk(chunkPosition);
            numChunks++;
        }
    }

    Chunk GenerateChunk(Vector3Int chunkPosition)
    {
        if (chunks.ContainsKey(chunkPosition))
            return chunks[chunkPosition];

        GameObject chunkGameObject = new GameObject(chunkPosition.ToString());
        chunkGameObject.transform.SetParent(transform);
        chunkGameObject.transform.position = VoxelUtil.ChunkToWorld(chunkPosition, chunkSize);

        Chunk newChunk = chunkGameObject.AddComponent<Chunk>();
        StartCoroutine(newChunk.Init(chunkPosition, this));

        chunks.Add(chunkPosition, newChunk);
        return newChunk;
    }
}
