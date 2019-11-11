using Unity.Mathematics;
using UnityEngine;

namespace OptIn.Voxel
{
    public static class VoxelUtil
    {
        public static int3 To3DIndex(int index, int3 chunkSize)
        {
            return new int3 {z = index % chunkSize.z, y = (index / chunkSize.z) % chunkSize.y, x = index / (chunkSize.y * chunkSize.z)};
        }

        public static int To1DIndex(int3 index, int3 chunkSize)
        {
            return index.z + index.y * chunkSize.z + index.x * chunkSize.y * chunkSize.z;
        }

        public static int To1DIndex(Vector3Int index, Vector3Int chunkSize)
        {
            return To1DIndex(new int3(index.x, index.y, index.z), new int3(chunkSize.x, chunkSize.y, chunkSize.z));
        }

        public static Vector3Int WorldToChunk(Vector3 worldPosition, Vector3Int chunkSize)
        {
            return new Vector3Int {x = Mathf.FloorToInt(worldPosition.x / chunkSize.x), y = Mathf.FloorToInt(worldPosition.y / chunkSize.y), z = Mathf.FloorToInt(worldPosition.z / chunkSize.z)};
        }

        public static Vector3 ChunkToWorld(Vector3Int chunkPosition, Vector3Int chunkSize)
        {
            return chunkPosition * chunkSize;
        }

        public static Vector3 GridToWorld(Vector3Int gridPosition, Vector3Int chunkPosition, Vector3Int chunkSize)
        {
            return ChunkToWorld(chunkPosition, chunkSize) + gridPosition;
        }
        
        public static Vector3Int WorldToGrid(Vector3 worldPosition)
        {
            return new Vector3Int
            {
                x = Mathf.RoundToInt(worldPosition.x),
                y = Mathf.RoundToInt(worldPosition.y),
                z = Mathf.RoundToInt(worldPosition.z)
            };
        }
        
        public static bool BoundaryCheck(int3 chunkSize, int3 position)
        {
            return chunkSize.x > position.x && chunkSize.y > position.y && chunkSize.z > position.z && position.x >= 0 && position.y >= 0 && position.z >= 0;
        }
        
        public static bool BoundaryCheck(Vector3Int chunkSize, Vector3Int position)
        {
            return chunkSize.x > position.x && chunkSize.y > position.y && chunkSize.z > position.z && position.x >= 0 && position.y >= 0 && position.z >= 0;
        }

        public static Vector3 ToVector3(int3 v) => new Vector3(v.x, v.y, v.z);

        public static int3 ToInt3(Vector3Int v) => new int3(v.x, v.y, v.z);

        public static int InvertDirection(int direction)
        {
            int axis = direction / 2; // 0(+x,-x), 1(+y,-y), 2(+z,-z)
            int invDirection = Mathf.Abs(direction - (axis * 2 + 1)) + (axis * 2);
            
            /*
                direction    x0    abs(x0)    abs(x) + axis * 2 => invDirection
                0            -1    1          1  
                1            0     0          0
                2            -1    1          3
                3            0     0          2
                4            -1    1          5
                5            0     0          4
             */

            return invDirection;
        }
        
        public static int Mod(int x, int m) 
        {
            int r = x%m;
            return r<0 ? r+m : r;
        }
        
        public static readonly int[] DirectionAlignedX = { 2, 2, 0, 0, 0, 0 };
        public static readonly int[] DirectionAlignedY = { 1, 1, 2, 2, 1, 1 };
        public static readonly int[] DirectionAlignedZ = { 0, 0, 1, 1, 2, 2 };

        public static readonly int3[] VoxelDirectionOffsets =
        {
            new int3(1, 0, 0), // right
            new int3(-1, 0, 0), // left
            new int3(0, 1, 0), // top
            new int3(0, -1, 0), // bottom
            new int3(0, 0, 1), // front
            new int3(0, 0, -1), // back
        };

        public static readonly float3[] CubeVertices =
        {
            new float3(0, 0, 0),
            new float3(1, 0, 0),
            new float3(1, 0, 1),
            new float3(0, 0, 1), 
            new float3(0, 1, 0), 
            new float3(1, 1, 0),
            new float3(1, 1, 1),
            new float3(0, 1, 1)
        };

        public static readonly int[] CubeFaces =
        {
            1, 2, 5, 6, // right
            3, 0, 7, 4, // left
            4, 5, 7, 6, // top
            1, 0, 2, 3, // bottom
            2, 3, 6, 7, // front
            0, 1, 4, 5, // back
        };

        public static readonly float2[] CubeUVs =
        {
            new float2(0, 0), new float2(1.0f, 0), new float2(0, 1.0f), new float2(1.0f, 1.0f)    
        };

        public static readonly int[] CubeIndices =
        {
            0, 3, 1, 
            0, 2, 3, //face right
            0, 2, 1, 
            1, 2, 3, //face left
            0, 3, 1, 
            0, 2, 3, //face top
            0, 2, 1, 
            1, 2, 3, //face bottom
            0, 3, 1,                        
            0, 2, 3, //face front
            0, 2, 1, 
            1, 2, 3, //face back
        };
        
        public static readonly int[] CubeFlipedIndices =
        {
            0, 2, 1, 
            1, 2, 3, //face right
            0, 3, 1, 
            0, 2, 3, //face left
            0, 2, 1, 
            1, 2, 3, //face top
            0, 3, 1, 
            0, 2, 3, //face bottom
            0, 2, 1,                        
            1, 2, 3, //face front
            0, 3, 1, 
            0, 2, 3, //face back
        };
        
        public static readonly int[] AONeighborOffsets =
        {
            0, 1, 2,
            6, 7, 0,
            2, 3, 4,
            4, 5, 6,
        };
    }
}