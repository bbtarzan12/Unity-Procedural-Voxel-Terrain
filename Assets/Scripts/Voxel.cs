using System;
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
        
        class VoxelGeneratorHelper : Singleton<VoxelGeneratorHelper>
        {
            public void Init()
            {
                Debug.Log($"Init {nameof(VoxelGeneratorHelper)} for Automatic Dispose of NativeArray");
            }
            
            void OnDestroy()
            {
                Dispose();
            }
        }

        [BurstCompile]
        struct VoxelMeshingJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Voxel> voxels;
            [ReadOnly] public Vector3Int chunkSize;

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
                return chunkSize.x > position.x && chunkSize.y > position.y && chunkSize.z > position.z && position.x >= 0 && position.y >= 0 && position.z >= 0;
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

        // Caching for performance
        static NativeArray<Vector3> nativeVertices;
        static NativeArray<Vector3> nativeNormals;
        static NativeArray<int> nativeTriangles;
        static NativeArray<Voxel> nativeVoxels;

        static bool isInitialized = false;

        static void Initialize(Vector3Int chunkSize)
        {
            if (isInitialized)
                return;

            nativeVertices = new NativeArray<Vector3>(12 * chunkSize.x * chunkSize.y * chunkSize.z, Allocator.Persistent);
            nativeNormals = new NativeArray<Vector3>(12 * chunkSize.x * chunkSize.y * chunkSize.z, Allocator.Persistent);
            nativeTriangles = new NativeArray<int>(18 * chunkSize.x * chunkSize.y * chunkSize.z, Allocator.Persistent);
            nativeVoxels = new NativeArray<Voxel>(chunkSize.x * chunkSize.y * chunkSize.z, Allocator.Persistent);

            isInitialized = true;
            
            VoxelGeneratorHelper.Instance.Init();
        }

        static void Dispose()
        {
            nativeVertices.Dispose();
            nativeNormals.Dispose();
            nativeTriangles.Dispose();
            nativeVoxels.Dispose();
        }

        public static void GenerateByCulling(Voxel[,,] voxels, Vector3Int chunkSize, bool enableJob, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
        {
            if (!isInitialized)
                Initialize(chunkSize);

            vertices.Clear();
            triangles.Clear();
            normals.Clear();

            if (enableJob)
            {
                NativeCounter counter = new NativeCounter(Allocator.TempJob);

                nativeVoxels.ManagedToNative(voxels);

                VoxelMeshingJob voxelMeshingJob = new VoxelMeshingJob
                {
                    voxels = nativeVoxels,
                    chunkSize = chunkSize,
                    vertices = nativeVertices,
                    normals = nativeNormals,
                    triangles = nativeTriangles,
                    counter = counter.ToConcurrent(),
                };

                JobHandle voxelMeshingJobHandle = voxelMeshingJob.Schedule(voxels.Length, 32);
                voxelMeshingJobHandle.Complete();

                if (counter.Count > 0)
                {
                    int verticeSize = counter.Count * 4;
                    int triangleSize = counter.Count * 6;

                    NativeSlice<Vector3> nativeSliceVertices = new NativeSlice<Vector3>(nativeVertices, 0, verticeSize);
                    NativeSlice<Vector3> nativeSliceNormal = new NativeSlice<Vector3>(nativeNormals, 0, verticeSize);
                    NativeSlice<int> nativeSliceTriangles = new NativeSlice<int>(nativeTriangles, 0, triangleSize);

                    vertices.NativeAddRange(nativeSliceVertices);
                    triangles.NativeAddRange(nativeSliceTriangles);
                    normals.NativeAddRange(nativeSliceNormal);
                }

                counter.Dispose();
            }
            else
            {
                for (int x = 0; x < chunkSize.x; x++)
                {
                    for (int y = 0; y < chunkSize.y; y++)
                    {
                        for (int z = 0; z < chunkSize.z; z++)
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
        }

        static bool BoundaryCheck(Vector3Int chunkSize, Vector3Int position)
        {
            return chunkSize.x > position.x && chunkSize.y > position.y && chunkSize.z > position.z && position.x >= 0 && position.y >= 0 && position.z >= 0;
        }

        static bool TransparencyCheck(Voxel[,,] voxels, Vector3Int position, Vector3Int chunkSize)
        {
            if (!BoundaryCheck(chunkSize, position))
                return true;

            return voxels[position.x, position.y, position.z].data == Voxel.VoxelType.Air;
        }

        static void AddTriangleByDirection(int direction, Vector3Int gridPosition, List<Vector3> vertices, List<Vector3> normals, List<int> triangles)
        {
            int numTriangles = vertices.Count;
            for (int i = direction * 6; i < (direction + 1) * 6; i++)
            {
                triangles.Add((CubeIndices[i] + numTriangles));
            }

            for (int i = 0; i < 4; i++)
            {
                vertices.Add(CubeVertices[CubeFaces[i + direction * 4]] + gridPosition);
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
            0, 3, 1, //face front                       
            0, 2, 3, 0, 2, 1, //face back
            1, 2, 3, 0, 2, 1, //face top
            0, 3, 2, 0, 1, 2, //face bottom
            0, 2, 3, 0, 1, 2, //face left
            1, 3, 2, 0, 2, 3, //face right
            0, 3, 1,
        };
    }

}