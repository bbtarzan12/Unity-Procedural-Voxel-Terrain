﻿using System;
using System.Collections;
using System.Collections.Generic;
using OptIn.Voxel.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
 using Unity.Mathematics;
 using UnityEngine;

namespace OptIn.Voxel
{
    public struct Voxel
    {

        public enum VoxelType { Air, Block }

        public VoxelType data;
    }

    public static class VoxelGenerator
    {
        public enum SimplifyingMethod
        {
            Culling,
            GreedyOnlyHeight,
            Greedy
        };

        public class NativeMeshData
        {
            public NativeArray<float3> nativeVertices;
            public NativeArray<float3> nativeNormals;
            public NativeArray<int> nativeIndices;
            public NativeArray<float2> nativeUVs;
            public JobHandle jobHandle;
            NativeCounter counter;

            public NativeMeshData(int chunkSize)
            {
                nativeVertices = new NativeArray<float3>(12 * chunkSize * chunkSize * chunkSize, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                nativeNormals = new NativeArray<float3>(12 * chunkSize * chunkSize * chunkSize, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                nativeUVs = new NativeArray<float2>(12 * chunkSize * chunkSize * chunkSize, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                nativeIndices = new NativeArray<int>(18 * chunkSize * chunkSize * chunkSize, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                counter = new NativeCounter(Allocator.TempJob);
            }

            ~NativeMeshData()
            {
                jobHandle.Complete();
                Dispose();
            }

            public void Dispose()
            {
                if(nativeVertices.IsCreated)
                    nativeVertices.Dispose();
                
                if(nativeNormals.IsCreated)
                    nativeNormals.Dispose();
                
                if(nativeIndices.IsCreated)
                    nativeIndices.Dispose();
                
                if(counter.IsCreated)
                    counter.Dispose();

                if (nativeUVs.IsCreated)
                    nativeUVs.Dispose();
            }

            public void ScheduleMeshingJob(NativeArray<Voxel> voxels, int chunkSize, SimplifyingMethod method)
            {
                switch (method)
                {
                    case SimplifyingMethod.Culling:
                        ScheduleCullingJob(voxels, chunkSize);
                        break;
                    case SimplifyingMethod.GreedyOnlyHeight:
                        ScheduleGreedyOnlyHeightJob(voxels, chunkSize);
                        break;
                    case SimplifyingMethod.Greedy:
                        ScheduleGreedyJob(voxels, chunkSize);
                        break;
                    default:
                        ScheduleGreedyJob(voxels, chunkSize);
                        break;
                }
            }

            public void CompleteMeshingJob(out int verticeSize, out int indicesSize)
            {
                jobHandle.Complete();

                verticeSize = counter.Count * 4;
                indicesSize = counter.Count * 6;
            }

            void ScheduleCullingJob(NativeArray<Voxel> voxels, int chunkSize)
            {
                VoxelCullingJob voxelCullingJob = new VoxelCullingJob
                {
                    voxels = voxels,
                    chunkSize = chunkSize,
                    vertices = nativeVertices,
                    normals = nativeNormals,
                    uvs = nativeUVs,
                    indices = nativeIndices,
                    counter = counter.ToConcurrent(),
                };

                jobHandle = voxelCullingJob.Schedule(voxels.Length, 32);
                JobHandle.ScheduleBatchedJobs();
            }

            void ScheduleGreedyOnlyHeightJob(NativeArray<Voxel> voxels, int chunkSize)
            {
                VoxelGreedyMeshingOnlyHeightJob voxelMeshingOnlyHeightJob = new VoxelGreedyMeshingOnlyHeightJob
                {
                    voxels = voxels,
                    chunkSize = chunkSize,
                    vertices = nativeVertices,
                    normals = nativeNormals,
                    uvs = nativeUVs,
                    indices = nativeIndices,
                    counter = counter.ToConcurrent(),
                };

                jobHandle = voxelMeshingOnlyHeightJob.Schedule();
                JobHandle.ScheduleBatchedJobs();
            }

            void ScheduleGreedyJob(NativeArray<Voxel> voxels, int chunkSize)
            {
                VoxelGreedyMeshingJob voxelMeshingOnlyHeightJob = new VoxelGreedyMeshingJob
                {
                    voxels = voxels,
                    chunkSize = chunkSize,
                    vertices = nativeVertices,
                    normals = nativeNormals,
                    uvs = nativeUVs,
                    indices = nativeIndices,
                    counter = counter.ToConcurrent(),
                };

                jobHandle = voxelMeshingOnlyHeightJob.Schedule();
                JobHandle.ScheduleBatchedJobs();
            }
        }

        [BurstCompile]
        struct VoxelCullingJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Voxel> voxels;
            [ReadOnly] public int chunkSize;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<float3> vertices;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<float3> normals;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<float2> uvs;
            
            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<int> indices;

            [WriteOnly] public NativeCounter.Concurrent counter;

            public void Execute(int index)
            {
                int3 gridPosition = VoxelUtil.To3DIndex(index, chunkSize);
                Voxel voxel = voxels[index];

                if (voxel.data == Voxel.VoxelType.Air)
                    return;

                for (int direction = 0; direction < 6; direction++)
                {
                    int3 neighborPosition = gridPosition + VoxelDirectionOffsets[direction];

                    if (TransparencyCheck(voxels, neighborPosition, chunkSize))
                        continue;

                    AddQuadByDirection(direction, 1.0f, 1.0f, gridPosition, counter, vertices, normals, uvs, indices);
                }
            }
        }

        [BurstCompile]
        struct VoxelGreedyMeshingOnlyHeightJob : IJob
        {
            [ReadOnly] public NativeArray<Voxel> voxels;
            [ReadOnly] public int chunkSize;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<float3> vertices;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<float3> normals;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<float2> uvs;
            
            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<int> indices;
            
            [WriteOnly] public NativeCounter.Concurrent counter;

            public void Execute()
            {
                for (int direction = 0; direction < 6; direction++)
                {
                    for (int depth = 0; depth < chunkSize; depth++)
                    {
                        for (int x = 0; x < chunkSize; x++)
                        {
                            for (int y = 0; y < chunkSize;)
                            {
                                int3 gridPosition = new int3 {[DirectionAlignedX[direction]] = x, [DirectionAlignedY[direction]] = y, [DirectionAlignedZ[direction]] = depth};

                                Voxel voxel = voxels[VoxelUtil.To1DIndex(gridPosition, chunkSize)];

                                if (voxel.data == Voxel.VoxelType.Air)
                                {
                                    y++;
                                    continue;
                                }

                                int3 neighborPosition = gridPosition + VoxelDirectionOffsets[direction];

                                if (TransparencyCheck(voxels, neighborPosition, chunkSize))
                                {
                                    y++;
                                    continue;
                                }

                                int height;
                                for (height = 1; height + y < chunkSize; height++)
                                {
                                    int3 nextPosition = gridPosition;
                                    nextPosition[DirectionAlignedY[direction]] += height;

                                    Voxel nextVoxel = voxels[VoxelUtil.To1DIndex(nextPosition, chunkSize)];

                                    if (nextVoxel.data != voxel.data)
                                        break;
                                }

                                AddQuadByDirection(direction, 1.0f, height, gridPosition, counter, vertices, normals, uvs, indices);
                                y += height;
                            }
                        }
                    }
                }
            }
        }
        
        [BurstCompile]
        struct VoxelGreedyMeshingJob : IJob
        {
            [ReadOnly] public NativeArray<Voxel> voxels;
            [ReadOnly] public int chunkSize;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<float3> vertices;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<float3> normals;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<float2> uvs;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<int> indices;
            
            [WriteOnly] public NativeCounter.Concurrent counter;
            
            struct Empty {}

            public void Execute()
            {
                NativeHashMap<int3, Empty> hashMap = new NativeHashMap<int3, Empty>(chunkSize * chunkSize, Allocator.Temp);
                
                for (int direction = 0; direction < 6; direction++)
                {
                    for (int depth = 0; depth < chunkSize; depth++)
                    {
                        for (int x = 0; x < chunkSize; x++)
                        {
                            for (int y = 0; y < chunkSize;)
                            {
                                int3 gridPosition = new int3 {[DirectionAlignedX[direction]] = x, [DirectionAlignedY[direction]] = y, [DirectionAlignedZ[direction]] = depth};

                                Voxel voxel = voxels[VoxelUtil.To1DIndex(gridPosition, chunkSize)];

                                if (voxel.data == Voxel.VoxelType.Air)
                                {
                                    y++;
                                    continue;
                                }
                                
                                if (hashMap.ContainsKey(gridPosition))
                                {
                                    y++;
                                    continue;
                                }

                                int3 neighborPosition = gridPosition + VoxelDirectionOffsets[direction];

                                if (TransparencyCheck(voxels, neighborPosition, chunkSize))
                                {
                                    y++;
                                    continue;
                                }

                                hashMap.TryAdd(gridPosition, new Empty());

                                int height;
                                for (height = 1; height + y < chunkSize; height++)
                                {
                                    int3 nextPosition = gridPosition;
                                    nextPosition[DirectionAlignedY[direction]] += height;

                                    Voxel nextVoxel = voxels[VoxelUtil.To1DIndex(nextPosition, chunkSize)];

                                    if (nextVoxel.data != voxel.data || hashMap.ContainsKey(nextPosition))
                                        break;

                                    hashMap.TryAdd(nextPosition, new Empty());
                                }

                                bool isDone = false;
                                int width;
                                for (width = 1; width + x < chunkSize; width++)
                                {
                                    for (int dy = 0; dy < height; dy++)
                                    {
                                        int3 nextPosition = gridPosition;
                                        nextPosition[DirectionAlignedX[direction]] += width;
                                        nextPosition[DirectionAlignedY[direction]] += dy;

                                        Voxel nextVoxel = voxels[VoxelUtil.To1DIndex(nextPosition, chunkSize)];

                                        if (nextVoxel.data != voxel.data || hashMap.ContainsKey(nextPosition))
                                        {
                                            isDone = true;
                                            break;
                                        }
                                    }

                                    if (isDone)
                                    {
                                        break;
                                    }

                                    for (int dy = 0; dy < height; dy++)
                                    {
                                        int3 nextPosition = gridPosition;
                                        nextPosition[DirectionAlignedX[direction]] += width;
                                        nextPosition[DirectionAlignedY[direction]] += dy;
                                        hashMap.TryAdd(nextPosition, new Empty());
                                    }
                                }

                                AddQuadByDirection(direction, width, height, gridPosition, counter, vertices, normals, uvs, indices);
                                y += height;
                            }
                        }
                        
                        hashMap.Clear();
                    }
                }

                hashMap.Dispose();
            }
        }

        static bool BoundaryCheck(int chunkSize, int3 position)
        {
            return chunkSize > position.x && chunkSize > position.y && chunkSize > position.z && position.x >= 0 && position.y >= 0 && position.z >= 0;
        }

        static bool TransparencyCheck(NativeArray<Voxel> voxels, int3 position, int chunkSize)
        {
            if (!BoundaryCheck(chunkSize, position))
                return false;

            return voxels[VoxelUtil.To1DIndex(position, chunkSize)].data != Voxel.VoxelType.Air;
        }

        static void AddQuadByDirection(int direction, float width, float height, int3 gridPosition, NativeCounter.Concurrent counter, NativeArray<float3> vertices, NativeArray<float3> normals, NativeArray<float2> uvs, NativeArray<int> indices)
        {
            int numFace = counter.Increment();
            
            int numVertices = numFace * 4;
            for (int i = 0; i < 4; i++)
            {
                float3 vertex = CubeVertices[CubeFaces[i + direction * 4]];
                vertex[DirectionAlignedX[direction]] *= width;
                vertex[DirectionAlignedY[direction]] *= height;

                float2 uv = CubeUVs[i];
                uv.x *= width;
                uv.y *= height;
                
                vertices[numVertices + i] = vertex + gridPosition;
                normals[numVertices + i] = VoxelDirectionOffsets[direction];
                uvs[numVertices + i] = uv;
            }

            int numindices = numFace * 6;
            for (int i = 0; i < 6; i++)
            {
                indices[numindices + i] = CubeIndices[direction * 6 + i] + numVertices;
            }
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
            new float2(0,0.5f), new float2(0.5f, 0.5f), new float2(0,1), new float2(0.5f,1),    
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
    }

}