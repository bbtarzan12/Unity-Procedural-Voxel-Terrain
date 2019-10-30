using UnityEngine;

namespace OptIn.Voxel
{
    public static class VoxelHelper
    {
        public static Vector3Int To3DIndex(int index, Vector3Int chunkSize)
        {
            return new Vector3Int {z = index % chunkSize.z, y = (index / chunkSize.z) % chunkSize.y, x = index / (chunkSize.y * chunkSize.z)};
        }

        public static int To1DIndex(Vector3Int index, Vector3Int chunkSize)
        {
            return index.z + index.y * chunkSize.z + index.x * chunkSize.z * chunkSize.y;
        }

        public static Vector3Int WorldToChunk(Vector3 worldPosition, Vector3Int chunkSize)
        {
            return new Vector3Int {x = Mathf.FloorToInt(worldPosition.x / chunkSize.x), y = Mathf.FloorToInt(worldPosition.y / chunkSize.y), z = Mathf.FloorToInt(worldPosition.z / chunkSize.z)};
        }

        public static Vector3 ChunkToWorld(Vector3Int chunkPosition, Vector3Int chunkSize)
        {
            return chunkPosition * chunkSize;
        }
    }
}