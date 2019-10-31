﻿using System;
using System.Collections;
using System.Collections.Generic;
using OptIn.Voxel.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
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
                Vector3Int gridPosition = VoxelHelper.To3DIndex(index, chunkSize);
                Voxel voxel = voxels[index];

                if (voxel.data == Voxel.VoxelType.Air)
                    return;

                for (int direction = 0; direction < 6; direction++)
                {
                    Vector3Int neighborPosition = gridPosition + VoxelGenerator.VoxelDirectionOffsets[direction];

                    if (!TransparencyCheck(neighborPosition))
                        continue;

                    AddTriangleByDirection(direction, gridPosition);
                }
            }

            bool TransparencyCheck(Vector3Int position)
            {
                if (!BoundaryCheck(position))
                    return true;

                int index = VoxelHelper.To1DIndex(position, chunkSize);
                return voxels[index].data == Voxel.VoxelType.Air;
            }

            bool BoundaryCheck(Vector3Int position)
            {
                return chunkSize > position.x && chunkSize > position.y && chunkSize > position.z && position.x >= 0 && position.y >= 0 && position.z >= 0;
            }

            void AddTriangleByDirection(int direction, Vector3Int gridPosition)
            {
                int numFace = counter.Increment();

                int numVertices = numFace * 4;
                for (int i = 0; i < 4; i++)
                {
                    vertices[numVertices + i] = VoxelGenerator.CubeVertices[VoxelGenerator.CubeFaces[i + direction * 4]] + gridPosition;
                    normals[numVertices + i] = VoxelGenerator.VoxelDirectionOffsets[direction];
                }

                int numTriangles = numFace * 6;
                for (int i = 0; i < 6; i++)
                {
                    triangles[numTriangles + i] = VoxelGenerator.CubeIndices[direction * 6 + i] + numVertices;
                }
            }
        }

        public static void GenerateByGreedyMeshing(Voxel[,,] voxels, int chunkSize, bool enableJob, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
        {
            vertices.Clear();
            triangles.Clear();
            normals.Clear();

            if (enableJob)
            {
                
            }
            else
            {
                int[,] heightMap = new int[chunkSize, chunkSize];
                for (int direction = 0; direction < 6; direction++)
                {
                    for (int x = 0; x < chunkSize; x++)
                    {
                        for (int y = 0; y < chunkSize; y++)
                        {
                            heightMap[x, y] = -1;
                            for (int z = chunkSize - 1; z >= 0; z--)
                            {
                                Vector3Int gridPosition = new Vector3Int(x, y, z);
                                Voxel voxel = voxels[x, y, z];

                                if (voxel.data == Voxel.VoxelType.Block)
                                {
                                    heightMap[x, y] = z;
                                    break;
                                }
                            }
                        }
                    }

                    for (int x = 0; x < chunkSize; x++)
                    {
                        for (int y = 0; y < chunkSize;)
                        {
                            int map = heightMap[x, y];

                            if (map == -1)
                            {
                                y++;
                                continue;
                            }

                            int height;
                            for (height = 1; y + height < chunkSize && heightMap[x, y + height] == map; height++) {}

                            bool done = false;

                            int width;
                            for (width = 1; width + x < chunkSize; width++)
                            {
                                for (int dy = y; dy < height; dy++)
                                {
                                    if (heightMap[x + width, dy] != map)
                                    {
                                        done = true;
                                        break;
                                    }
                                }

                                if (done)
                                {
                                    break;
                                }
                            }
                        }
                    }
                }
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

        public static void CompleteCullingJob(NativeVoxelData data, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
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

        public static void GenerateByCulling(Voxel[,,] voxels, int chunkSize, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
        {
            vertices.Clear();
            triangles.Clear();
            normals.Clear();

            for (int x = 0; x < chunkSize; x += 1)
            {
                for (int y = 0; y < chunkSize; y += 1)
                {
                    for (int z = 0; z < chunkSize; z += 1)
                    {
                        Vector3Int gridPosition = new Vector3Int(x, y, z);
                        Voxel voxel = voxels[x, y, z];

                        if (voxel.data == Voxel.VoxelType.Air)
                            continue;

                        for (int direction = 0; direction < 6; direction++)
                        {
                            Vector3Int neighborPosition = gridPosition + VoxelDirectionOffsets[direction];

                            if (!TransparencyCheck(voxels, neighborPosition, chunkSize))
                                continue;

                            AddTriangleByDirection(direction, gridPosition, vertices, normals, triangles);
                        }
                    }
                }
            }
        }

        static bool BoundaryCheck(int chunkSize, Vector3Int position)
        {
            return chunkSize > position.x && chunkSize > position.y && chunkSize > position.z && position.x >= 0 && position.y >= 0 && position.z >= 0;
        }

        static bool TransparencyCheck(Voxel[,,] voxels, Vector3Int position, int chunkSize)
        {
            if (!BoundaryCheck(chunkSize, position))
                return true;

            return voxels[position.x, position.y, position.z].data == Voxel.VoxelType.Air;
        }

        static void AddTriangleByDirection(int direction, Vector3Int gridPosition, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
        {
            AddTriangleByDirection(direction, gridPosition, 1, vertices, normals, triangles);
        }

        static void AddTriangleByDirection(int direction, Vector3Int gridPosition, float scale, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
        {
            int numTriangles = vertices.Count;
            for (int i = direction * 6; i < (direction + 1) * 6; i++)
            {
                triangles.Add((CubeIndices[i] + numTriangles));
            }

            for (int i = 0; i < 4; i++)
            {
                vertices.Add(CubeVertices[CubeFaces[i + direction * 4]] * scale + gridPosition);
                normals.Add(VoxelDirectionOffsets[direction]);
            }
        }

        public static readonly Vector3Int[] VoxelDirectionOffsets =
        {
            new Vector3Int(0, 0, 1), // front
            new Vector3Int(0, 0, -1), // back
            new Vector3Int(0, 1, 0), // top
            new Vector3Int(0, -1, 0), // bottom
            new Vector3Int(-1, 0, 0), // left
            new Vector3Int(1, 0, 0), // right
        };

        public static readonly Vector3[] CubeVertices = {new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(0, 1, 1)};

        public static readonly int[] CubeFaces =
        {
            2, 3, 6, 7, // front
            0, 1, 4, 5, // back
            4, 5, 6, 7, // top
            0, 1, 2, 3, // bottom
            0, 3, 4, 7, // left
            1, 2, 5, 6, // right
        };

        public static readonly int[] CubeIndices =
        {
            0, 3, 1,                        
            0, 2, 3, //face front
            0, 2, 1, 
            1, 2, 3, //face back
            0, 2, 1, 
            0, 3, 2, //face top
            0, 1, 2, 
            0, 2, 3, //face bottom
            0, 1, 2, 
            1, 3, 2, //face left
            0, 2, 3, 
            0, 3, 1, //face right
        };
    }

}