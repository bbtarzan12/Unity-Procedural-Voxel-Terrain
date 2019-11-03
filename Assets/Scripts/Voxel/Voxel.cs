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
        public class NativeVoxelData
        {
            public NativeArray<Vector3> nativeVertices;
            public NativeArray<Vector3> nativeNormals;
            public NativeArray<int> nativeTriangles;
            public NativeCounter counter;
            public JobHandle jobHandle;

            public NativeVoxelData(int chunkSize)
            {
                nativeVertices = new NativeArray<Vector3>(12 * chunkSize * chunkSize * chunkSize, Allocator.TempJob);
                nativeNormals = new NativeArray<Vector3>(12 * chunkSize * chunkSize * chunkSize, Allocator.TempJob);
                nativeTriangles = new NativeArray<int>(18 * chunkSize * chunkSize * chunkSize, Allocator.TempJob);
                counter = new NativeCounter(Allocator.TempJob);
            }

            public void Dispose()
            {
                nativeVertices.Dispose();
                nativeNormals.Dispose();
                nativeTriangles.Dispose();
                counter.Dispose();
            }
        }

        [BurstCompile]
        struct VoxelMeshingJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Voxel> voxels;
            [ReadOnly] public int chunkSize;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<Vector3> vertices;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<Vector3> normals;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<int> triangles;

            [WriteOnly] public NativeCounter.Concurrent counter;

            public void Execute(int index)
            {
                int3 gridPosition = VoxelUtil.To3DIndex(index, chunkSize);
                Voxel voxel = voxels[index];

                if (voxel.data == Voxel.VoxelType.Air)
                    return;

                for (int direction = 0; direction < 6; direction++)
                {
                    int3 neighborPosition = gridPosition + VoxelUtil.ToInt3(VoxelDirectionOffsets[direction]);

                    if (TransparencyCheck(voxels, neighborPosition, chunkSize))
                        continue;

                    AddTriangleByDirection(direction, gridPosition, counter, vertices, normals, triangles);
                }
            }
        }

        [BurstCompile]
        struct VoxelGreedyMeshingOnlyHeightJob : IJob
        {
            [ReadOnly] public NativeArray<Voxel> voxels;
            [ReadOnly] public int chunkSize;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<Vector3> vertices;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<Vector3> normals;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<int> triangles;
            
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

                                int3 neighborPosition = gridPosition + VoxelUtil.ToInt3(VoxelDirectionOffsets[direction]);

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

                                AddQuadByDirection(direction, 1.0f, height, gridPosition, counter, vertices, normals, triangles);
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
            public NativeArray<Vector3> vertices;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<Vector3> normals;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<int> triangles;
            
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

                                int3 neighborPosition = gridPosition + VoxelUtil.ToInt3(VoxelDirectionOffsets[direction]);

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

                                AddQuadByDirection(direction, width, height, gridPosition, counter, vertices, normals, triangles);
                                y += height;
                            }
                        }
                        
                        hashMap.Clear();
                    }
                }

                hashMap.Dispose();
            }
        }

        public static NativeVoxelData ScheduleCullingJob(NativeArray<Voxel> voxels, int chunkSize)
        {
            NativeVoxelData data = new NativeVoxelData(chunkSize);

            VoxelMeshingJob voxelMeshingJob = new VoxelMeshingJob
            {
                voxels = voxels,
                chunkSize = chunkSize,
                vertices = data.nativeVertices,
                normals = data.nativeNormals,
                triangles = data.nativeTriangles,
                counter = data.counter.ToConcurrent(),
            };
            
            data.jobHandle = voxelMeshingJob.Schedule(voxels.Length, 32);
            JobHandle.ScheduleBatchedJobs();
            
            return data;
        }

        public static NativeVoxelData ScheduleGreedyOnlyHeightJob(NativeArray<Voxel> voxels, int chunkSize)
        {
            NativeVoxelData data = new NativeVoxelData(chunkSize);

            VoxelGreedyMeshingOnlyHeightJob voxelMeshingOnlyHeightJob = new VoxelGreedyMeshingOnlyHeightJob
            {
                voxels = voxels,
                chunkSize = chunkSize,
                vertices = data.nativeVertices,
                normals = data.nativeNormals,
                triangles = data.nativeTriangles,
                counter = data.counter.ToConcurrent(),
            };
            
            data.jobHandle = voxelMeshingOnlyHeightJob.Schedule();
            JobHandle.ScheduleBatchedJobs();
            
            return data;
        }
        
        public static NativeVoxelData ScheduleGreedyJob(NativeArray<Voxel> voxels, int chunkSize)
        {
            NativeVoxelData data = new NativeVoxelData(chunkSize);

            VoxelGreedyMeshingJob voxelMeshingOnlyHeightJob = new VoxelGreedyMeshingJob
            {
                voxels = voxels,
                chunkSize = chunkSize,
                vertices = data.nativeVertices,
                normals = data.nativeNormals,
                triangles = data.nativeTriangles,
                counter = data.counter.ToConcurrent(),
            };
            
            data.jobHandle = voxelMeshingOnlyHeightJob.Schedule();
            JobHandle.ScheduleBatchedJobs();
            
            return data;
        }
        
        public static void CompleteMeshingJob(NativeVoxelData data, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
        {
            data.jobHandle.Complete();

            if (data.counter.Count > 0)
            {
                int verticeSize = data.counter.Count * 4;
                int triangleSize = data.counter.Count * 6;

                NativeSlice<Vector3> nativeSliceVertices = new NativeSlice<Vector3>(data.nativeVertices, 0, verticeSize);
                NativeSlice<Vector3> nativeSliceNormal = new NativeSlice<Vector3>(data.nativeNormals, 0, verticeSize);
                NativeSlice<int> nativeSliceTriangles = new NativeSlice<int>(data.nativeTriangles, 0, triangleSize);

                vertices.NativeAddRange(nativeSliceVertices);
                triangles.NativeAddRange(nativeSliceTriangles);
                normals.NativeAddRange(nativeSliceNormal);
            }
            
            data.Dispose();
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

        static void AddQuadByDirection(int direction, float width, float height, int3 gridPosition, NativeCounter.Concurrent counter, NativeArray<Vector3> vertices, NativeArray<Vector3> normals, NativeArray<int> triangles)
        {
            int numFace = counter.Increment();
            
            int numVertices = numFace * 4;
            for (int i = 0; i < 4; i++)
            {
                Vector3 vertex = CubeVertices[CubeFaces[i + direction * 4]];

                vertex[DirectionAlignedX[direction]] *= width;
                vertex[DirectionAlignedY[direction]] *= height;
                
                vertices[numVertices + i] = vertex + VoxelUtil.ToVector3(gridPosition);
                normals[numVertices + i] = VoxelDirectionOffsets[direction];
            }

            int numTriangles = numFace * 6;
            for (int i = 0; i < 6; i++)
            {
                triangles[numTriangles + i] = CubeIndices[direction * 6 + i] + numVertices;
            }
        }

        static void AddTriangleByDirection(int direction, int3 gridPosition, NativeCounter.Concurrent counter, NativeArray<Vector3> vertices, NativeArray<Vector3> normals, NativeArray<int> triangles)
        {
            int numFace = counter.Increment();

            int numVertices = numFace * 4;
            for (int i = 0; i < 4; i++)
            {
                vertices[numVertices + i] = CubeVertices[CubeFaces[i + direction * 4]] + VoxelUtil.ToVector3(gridPosition);
                normals[numVertices + i] = VoxelDirectionOffsets[direction];
            }

            int numTriangles = numFace * 6;
            for (int i = 0; i < 6; i++)
            {
                triangles[numTriangles + i] = CubeIndices[direction * 6 + i] + numVertices;
            }
        }

        public static readonly int[] DirectionAlignedX = { 2, 2, 0, 0, 0, 0 };
        public static readonly int[] DirectionAlignedY = { 1, 1, 2, 2, 1, 1 };
        public static readonly int[] DirectionAlignedZ = { 0, 0, 1, 1, 2, 2 };
        
        public static readonly Vector3Int[] VoxelDirectionOffsets =
        {
            new Vector3Int(1, 0, 0), // right
            new Vector3Int(-1, 0, 0), // left
            new Vector3Int(0, 1, 0), // top
            new Vector3Int(0, -1, 0), // bottom
            new Vector3Int(0, 0, 1), // front
            new Vector3Int(0, 0, -1), // back
        };

        public static readonly Vector3[] CubeVertices =
        {
            new Vector3(0, 0, 0),
            new Vector3(1, 0, 0),
            new Vector3(1, 0, 1),
            new Vector3(0, 0, 1), 
            new Vector3(0, 1, 0), 
            new Vector3(1, 1, 0),
            new Vector3(1, 1, 1),
            new Vector3(0, 1, 1)
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