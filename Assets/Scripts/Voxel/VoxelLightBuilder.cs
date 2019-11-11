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
            [ReadOnly] public int3 chunkSize;

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
                    int3 neighborPosition = gridPosition + VoxelUtil.VoxelDirectionOffsets[direction];
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

                    down[VoxelUtil.DirectionAlignedY[direction]] -= 1;
                    left[VoxelUtil.DirectionAlignedX[direction]] -= 1;
                    top[VoxelUtil.DirectionAlignedY[direction]] += 1;
                    right[VoxelUtil.DirectionAlignedX[direction]] += 1;

                    leftDownCorner[VoxelUtil.DirectionAlignedX[direction]] -= 1;
                    leftDownCorner[VoxelUtil.DirectionAlignedY[direction]] -= 1;
            
                    topLeftCorner[VoxelUtil.DirectionAlignedX[direction]] -= 1;
                    topLeftCorner[VoxelUtil.DirectionAlignedY[direction]] += 1;
            
                    topRightCorner[VoxelUtil.DirectionAlignedX[direction]] += 1;
                    topRightCorner[VoxelUtil.DirectionAlignedY[direction]] += 1;
                
                    rightDownCorner[VoxelUtil.DirectionAlignedX[direction]] += 1;
                    rightDownCorner[VoxelUtil.DirectionAlignedY[direction]] -= 1;  
                    
                    int3* neighbors = stackalloc int3[] {down, leftDownCorner, left, topLeftCorner, top, topRightCorner, right, rightDownCorner};

                    for (int i = 0; i < 8; i++)
                    {
                        neighbors[i][VoxelUtil.DirectionAlignedZ[direction]] += (direction % 2 == 0) ? 1 : -1;
                    }

                    for (int i = 0; i < 4; i++)
                    {
                        bool side1 = VoxelMeshBuilder.TransparencyCheck(voxels, neighbors[VoxelUtil.AONeighborOffsets[i * 3]], chunkSize);
                        bool corner = VoxelMeshBuilder.TransparencyCheck(voxels, neighbors[VoxelUtil.AONeighborOffsets[i * 3 + 1]], chunkSize);
                        bool side2 = VoxelMeshBuilder.TransparencyCheck(voxels, neighbors[VoxelUtil.AONeighborOffsets[i * 3 + 2]], chunkSize);
                        
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
        
        public static NativeArray<VoxelLight> GenerateLightData(NativeArray<Voxel> voxels, int3 chunkSize)
        {
            NativeArray<VoxelLight> nativeLightData = new NativeArray<VoxelLight>(chunkSize.x * chunkSize.y * chunkSize.z, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

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
    }
}