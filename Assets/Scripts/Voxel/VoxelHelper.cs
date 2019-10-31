using UnityEngine;

namespace OptIn.Voxel
{
    public static class VoxelHelper
    {
        public static Vector3Int To3DIndex(int index, int chunkSize)
        {
            return new Vector3Int {z = index % chunkSize, y = (index / chunkSize) % chunkSize, x = index / (chunkSize * chunkSize)};
        }

        public static int To1DIndex(Vector3Int index, int chunkSize)
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
    }
}