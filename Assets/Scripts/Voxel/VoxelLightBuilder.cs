using System;
using System.Collections;
using System.Collections.Generic;
using OptIn.Voxel.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
        public class NativeLightData
        {
            public NativeArray<VoxelLight> nativeLightData;
            
            NativeArray<Voxel> nativeVoxelsWithNeighbor;
            NativeHashMap<int3, int> nativeNeighborHashMap;

            public int frameCount;
            public JobHandle jobHandle;

            public NativeLightData(int3 chunkSize)
            {
                nativeLightData = new NativeArray<VoxelLight>(chunkSize.x * chunkSize.y * chunkSize.z, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            }
            
            ~NativeLightData()
            {
                jobHandle.Complete();
                Dispose();
            }

            public IEnumerator ScheduleLightingJob(List<Voxel[]> neighborVoxels, int3 chunkPosition, int3 chunkSize, int numNeighbor, bool argent = false)
            {
                nativeNeighborHashMap = new NativeHashMap<int3, int>(neighborVoxels.Count, Allocator.TempJob);

                int voxelIndex = 0;
                int numNeighbors = 0;
                for (int x = chunkPosition.x - numNeighbor; x <= chunkPosition.x + numNeighbor; x++)
                {
                    for (int y = chunkPosition.y - numNeighbor; y <= chunkPosition.y + numNeighbor; y++)
                    {
                        for (int z = chunkPosition.z - numNeighbor; z <= chunkPosition.z + numNeighbor; z++)
                        {
                            int3 neighborChunkPosition = new int3(x, y, z);
                            if (neighborVoxels[voxelIndex] == null)
                            {
                                nativeNeighborHashMap.TryAdd(neighborChunkPosition, -1);
                            }
                            else
                            {
                                nativeNeighborHashMap.TryAdd(neighborChunkPosition, numNeighbors);
                                numNeighbors += 1;
                            }

                            voxelIndex += 1;
                        }
                    }
                }
                int numVoxels = chunkSize.x * chunkSize.y * chunkSize.z;
                
                nativeVoxelsWithNeighbor = new NativeArray<Voxel>(numNeighbors * numVoxels, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                voxelIndex = 0;
                for (int x = chunkPosition.x - numNeighbor; x <= chunkPosition.x + numNeighbor; x++)
                {
                    for (int y = chunkPosition.y - numNeighbor; y <= chunkPosition.y + numNeighbor; y++)
                    {
                        for (int z = chunkPosition.z - numNeighbor; z <= chunkPosition.z + numNeighbor; z++)
                        {
                            int3 neighborChunkPosition = new int3(x, y, z);
                            if (nativeNeighborHashMap[neighborChunkPosition] != -1)
                            {
                                NativeArray<Voxel>.Copy(neighborVoxels[voxelIndex], 0, nativeVoxelsWithNeighbor, nativeNeighborHashMap[neighborChunkPosition] * numVoxels, numVoxels);
                            }
                            voxelIndex += 1;
                        }
                    }
                }

                VoxelAOJob aoJob = new VoxelAOJob
                {
                    voxelsWithNeighbor = nativeVoxelsWithNeighbor,
                    neighborHashMap = nativeNeighborHashMap,
                    chunkPosition = chunkPosition,
                    chunkSize = chunkSize,
                    lightDatas = nativeLightData
                };

                jobHandle = aoJob.Schedule(nativeLightData.Length, 32);
                JobHandle.ScheduleBatchedJobs();

                frameCount = 0;
                yield return new WaitUntil(() =>
                {
                    frameCount++;
                    return jobHandle.IsCompleted || frameCount >= 3 || argent;
                });
                
                jobHandle.Complete();
            }
            
            public void Dispose()
            {
                if (nativeLightData.IsCreated)
                    nativeLightData.Dispose();

                if (nativeVoxelsWithNeighbor.IsCreated)
                    nativeVoxelsWithNeighbor.Dispose();

                if (nativeNeighborHashMap.IsCreated)
                    nativeNeighborHashMap.Dispose();
            }
        }

        [BurstCompile]
        struct VoxelAOJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Voxel> voxelsWithNeighbor;
            [ReadOnly] public int3 chunkSize;
            [ReadOnly] public int3 chunkPosition;
            [ReadOnly] public NativeHashMap<int3, int> neighborHashMap;

            [WriteOnly] public NativeArray<VoxelLight> lightDatas;
            
            
            public unsafe void Execute(int index)
            {
                int3 gridPosition = VoxelUtil.To3DIndex(index, chunkSize);

                NativeSlice<Voxel> voxels = voxelsWithNeighbor.Slice(neighborHashMap[chunkPosition] * chunkSize.x * chunkSize.y * chunkSize.z, chunkSize.x * chunkSize.y * chunkSize.z);
                
                Voxel voxel = voxels[index];
                
                VoxelLight voxelLight = new VoxelLight();

                if (voxel.data == Voxel.VoxelType.Air)
                {
                    lightDatas[index] = voxelLight;
                    return;
                }

                for (int direction = 0; direction < 6; direction++)
                {
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
                        neighbors[i][VoxelUtil.DirectionAlignedZ[direction]] += VoxelUtil.DirectionAlignedSign[direction];
                    }

                    for (int i = 0; i < 4; i++)
                    {
                        bool side1 = TransparencyCheck(voxels, neighbors[VoxelUtil.AONeighborOffsets[i * 3]]);
                        bool corner = TransparencyCheck(voxels, neighbors[VoxelUtil.AONeighborOffsets[i * 3 + 1]]);
                        bool side2 = TransparencyCheck(voxels, neighbors[VoxelUtil.AONeighborOffsets[i * 3 + 2]]);
                        
                        if (side1 && side2)
                        {
                            voxelLight.ambient[i + direction * 4] = 0f;
                        }
                        else
                        {
                            voxelLight.ambient[i + direction * 4] = ((side1 ? 0f : 1f) + (side2 ? 0f : 1f) + (corner ? 0f : 1f)) / 3.0f;
                        }
                    }
                }

                lightDatas[index] = voxelLight;
            }
            
            bool TransparencyCheck(NativeSlice<Voxel> voxels, int3 gridPosition)
            {
                if (VoxelUtil.BoundaryCheck(gridPosition, chunkSize))
                {
                    return voxels[VoxelUtil.To1DIndex(gridPosition, chunkSize)].data != Voxel.VoxelType.Air;
                }
                else
                {
                    int3 worldGridPosition = gridPosition + chunkPosition * chunkSize;
                    int3 neighborChunkPosition = VoxelUtil.WorldToChunk(worldGridPosition, chunkSize);

                    if (neighborHashMap.TryGetValue(neighborChunkPosition, out int voxelIndex))
                    {
                        if (voxelIndex == -1)
                            return false;
                    
                        int3 position = VoxelUtil.WorldToGrid(worldGridPosition, neighborChunkPosition, chunkSize);
                        NativeSlice<Voxel> neighborVoxels = voxelsWithNeighbor.Slice(voxelIndex * chunkSize.x * chunkSize.y * chunkSize.z, chunkSize.x * chunkSize.y * chunkSize.z);
                        return neighborVoxels[VoxelUtil.To1DIndex(position, chunkSize)].data != Voxel.VoxelType.Air;
                    }

                    return false;   
                }
            }
        }
    }
}