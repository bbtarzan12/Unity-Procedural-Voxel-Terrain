using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace OptIn.Voxel
{
    public unsafe struct VoxelLight
    {
        public fixed float ambient[24];

        public bool CompareFace(VoxelLight other, int direction)
        {
            for (int i = 0; i < 4; i++)
            {
                if (ambient[direction * 4 + i] != other.ambient[direction * 4 + i])
                {
                    return false;
                }
            }

            return true;
        }
    }

    public static class VoxelLightBuilder
    {
        
        [BurstCompile]
        struct VoxelAOJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Voxel> voxels;
            [ReadOnly] public int chunkSize;

            [WriteOnly] public NativeArray<VoxelLight> lightDatas;
            
            
            public unsafe void Execute(int index)
            {
                int3 gridPosition = VoxelUtil.To3DIndex(index, chunkSize);

                Voxel voxel = voxels[index];
                
                VoxelLight voxelLight = new VoxelLight();

                if (voxel.data == Voxel.VoxelType.Air)
                {
                    lightDatas[index] = voxelLight;
                    return;
                }

                for (int direction = 0; direction < 6; direction++)
                {
                    int3 neighborPosition = gridPosition + VoxelMeshBuilder.VoxelDirectionOffsets[direction];
                    if (VoxelMeshBuilder.TransparencyCheck(voxels, neighborPosition, chunkSize))
                        continue;
                    
                    // Todo : Code Cleanup!!
                    
                    int3 down = gridPosition;
                    int3 left = gridPosition;
                    int3 top  = gridPosition;
                    int3 right= gridPosition;
                    int3 leftDownCorner = gridPosition;
                    int3 topLeftCorner = gridPosition;
                    int3 topRightCorner = gridPosition;
                    int3 rightDownCorner = gridPosition;

                    down[VoxelMeshBuilder.DirectionAlignedY[direction]] -= 1;
                    left[VoxelMeshBuilder.DirectionAlignedX[direction]] -= 1;
                    top[VoxelMeshBuilder.DirectionAlignedY[direction]] += 1;
                    right[VoxelMeshBuilder.DirectionAlignedX[direction]] += 1;

                    leftDownCorner[VoxelMeshBuilder.DirectionAlignedX[direction]] -= 1;
                    leftDownCorner[VoxelMeshBuilder.DirectionAlignedY[direction]] -= 1;
            
                    topLeftCorner[VoxelMeshBuilder.DirectionAlignedX[direction]] -= 1;
                    topLeftCorner[VoxelMeshBuilder.DirectionAlignedY[direction]] += 1;
            
                    topRightCorner[VoxelMeshBuilder.DirectionAlignedX[direction]] += 1;
                    topRightCorner[VoxelMeshBuilder.DirectionAlignedY[direction]] += 1;
                
                    rightDownCorner[VoxelMeshBuilder.DirectionAlignedX[direction]] += 1;
                    rightDownCorner[VoxelMeshBuilder.DirectionAlignedY[direction]] -= 1;  
                    
                    int3* neighbors = stackalloc int3[] {down, leftDownCorner, left, topLeftCorner, top, topRightCorner, right, rightDownCorner};

                    for (int i = 0; i < 8; i++)
                    {
                        neighbors[i][VoxelMeshBuilder.DirectionAlignedZ[direction]] += (direction % 2 == 0) ? 1 : -1;
                    }

                    for (int i = 0; i < 4; i++)
                    {
                        bool side1 = VoxelMeshBuilder.TransparencyCheck(voxels, neighbors[AONeighborOffsets[i * 3]], chunkSize);
                        bool corner = VoxelMeshBuilder.TransparencyCheck(voxels, neighbors[AONeighborOffsets[i * 3 + 1]], chunkSize);
                        bool side2 = VoxelMeshBuilder.TransparencyCheck(voxels, neighbors[AONeighborOffsets[i * 3 + 2]], chunkSize);
                        
                        if (side1 && side2)
                        {
                            voxelLight.ambient[i + direction * 4] = 0f;
                        }
                        else
                        {
                            voxelLight.ambient[i + direction * 4] = ((side1 ? 0 : 1) + (side2 ? 0 : 1) + (corner ? 0 : 1)) / 3.0f;
                        }
                    }
                }

                lightDatas[index] = voxelLight;
            }
        }
        
        public static NativeArray<VoxelLight> GenerateLightData(NativeArray<Voxel> voxels, int chunkSize)
        {
            NativeArray<VoxelLight> nativeLightData = new NativeArray<VoxelLight>(chunkSize * chunkSize * chunkSize, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            VoxelAOJob aoJob = new VoxelAOJob
            {
                voxels = voxels,
                chunkSize = chunkSize,
                lightDatas = nativeLightData
            };

            JobHandle jobHandle = aoJob.Schedule(nativeLightData.Length, 32);
            jobHandle.Complete();
            return nativeLightData;
        }
        
        public static readonly int[] AONeighborOffsets =
        {
            0, 1, 2,
            6, 7, 0,
            2, 3, 4,
            4, 5, 6,
        };
    }
}