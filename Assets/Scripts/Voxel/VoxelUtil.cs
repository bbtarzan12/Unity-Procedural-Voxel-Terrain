using Unity.Mathematics;
using UnityEngine;

namespace OptIn.Voxel
{
    public static class VoxelUtil
    {
        public static int3 To3DIndex(int index, int chunkSize)
        {
            return new int3 {z = index % chunkSize, y = (index / chunkSize) % chunkSize, x = index / (chunkSize * chunkSize)};
        }

        public static int To1DIndex(int3 index, int chunkSize)
        {
            return index.z + index.y * chunkSize + index.x * chunkSize * chunkSize;
        }

        public static Vector3Int WorldToChunk(Vector3 worldPosition, int chunkSize)
        {
            return new Vector3Int {x = Mathf.FloorToInt(worldPosition.x / chunkSize), y = Mathf.FloorToInt(worldPosition.y / chunkSize), z = Mathf.FloorToInt(worldPosition.z / chunkSize)};
        }

        public static Vector3 ChunkToWorld(Vector3Int chunkPosition, int chunkSize)
        {
            return chunkPosition * chunkSize;
        }

        public static Vector3 ToVector3(int3 v) => new Vector3(v.x, v.y, v.z);

        public static int3 ToInt3(Vector3Int v) => new int3(v.x, v.y, v.z);
    }
}