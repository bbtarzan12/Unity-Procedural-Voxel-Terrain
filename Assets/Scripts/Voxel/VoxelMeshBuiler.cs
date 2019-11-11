using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace OptIn.Voxel
{
    public static class VoxelMeshBuilder
    {
        public static void InitializeShaderParameter()
        {
            Shader.SetGlobalInt("_AtlasX", AtlasSize.x);
            Shader.SetGlobalInt("_AtlasY", AtlasSize.y);
            Shader.SetGlobalVector("_AtlasRec", new Vector4(1.0f / AtlasSize.x, 1.0f / AtlasSize.y));
        }
        
        public static readonly int2 AtlasSize = new int2(8, 8);
        
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
            public NativeArray<float4> nativeUVs;
            public NativeArray<Color> nativeColors;
            NativeArray<VoxelLight> nativeLightData;
            public JobHandle jobHandle;
            NativeCounter counter;

            public NativeMeshData(int chunkSize)
            {
                int maxVertices = 12 * chunkSize * chunkSize * chunkSize;
                int maxIndices = 18 * chunkSize * chunkSize * chunkSize;
                
                nativeVertices = new NativeArray<float3>(maxVertices, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                nativeNormals = new NativeArray<float3>(maxVertices, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                nativeUVs = new NativeArray<float4>(maxVertices, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                nativeColors = new NativeArray<Color>(maxVertices, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                nativeIndices = new NativeArray<int>(maxIndices, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
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

                if (nativeColors.IsCreated)
                    nativeColors.Dispose();

                if (nativeLightData.IsCreated)
                    nativeLightData.Dispose();
            }

            public void ScheduleMeshingJob(NativeArray<Voxel> voxels, int chunkSize, SimplifyingMethod method)
            {
                nativeLightData = VoxelLightBuilder.GenerateLightData(voxels, chunkSize);
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
                    colors = nativeColors,
                    lightData = nativeLightData,
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
                    colors = nativeColors,
                    lightData = nativeLightData,
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
                    colors = nativeColors,
                    lightData = nativeLightData,
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
            [ReadOnly] public NativeArray<VoxelLight> lightData;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<float3> vertices;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<float3> normals;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<float4> uvs;
            
            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<int> indices;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<Color> colors;

            [WriteOnly] public NativeCounter.Concurrent counter;

            public void Execute(int index)
            {
                int3 gridPosition = VoxelUtil.To3DIndex(index, chunkSize);
                Voxel voxel = voxels[index];

                if (voxel.data == Voxel.VoxelType.Air)
                    return;

                for (int direction = 0; direction < 6; direction++)
                {
                    int3 neighborPosition = gridPosition + VoxelUtil.VoxelDirectionOffsets[direction];

                    if (TransparencyCheck(voxels, neighborPosition, chunkSize))
                        continue;

                    AddQuadByDirection(direction, voxel.data, lightData[index], 1.0f, 1.0f, gridPosition, counter, vertices, normals, uvs, colors, indices);
                }
            }
        }

        [BurstCompile]
        struct VoxelGreedyMeshingOnlyHeightJob : IJob
        {
            [ReadOnly] public NativeArray<Voxel> voxels;
            [ReadOnly] public int chunkSize;
            [ReadOnly] public NativeArray<VoxelLight> lightData;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<float3> vertices;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<float3> normals;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<float4> uvs;
            
            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<Color> colors;
            
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
                                int3 gridPosition = new int3
                                {
                                    [VoxelUtil.DirectionAlignedX[direction]] = x,
                                    [VoxelUtil.DirectionAlignedY[direction]] = y, 
                                    [VoxelUtil.DirectionAlignedZ[direction]] = depth
                                };

                                int index = VoxelUtil.To1DIndex(gridPosition, chunkSize);
                                
                                Voxel voxel = voxels[index];
                                VoxelLight light = lightData[index];
                                
                                if (voxel.data == Voxel.VoxelType.Air)
                                {
                                    y++;
                                    continue;
                                }

                                int3 neighborPosition = gridPosition + VoxelUtil.VoxelDirectionOffsets[direction];

                                if (TransparencyCheck(voxels, neighborPosition, chunkSize))
                                {
                                    y++;
                                    continue;
                                }

                                int height;
                                for (height = 1; height + y < chunkSize; height++)
                                {
                                    int3 nextPosition = gridPosition;
                                    nextPosition[VoxelUtil.DirectionAlignedY[direction]] += height;

                                    int nextIndex = VoxelUtil.To1DIndex(nextPosition, chunkSize);

                                    Voxel nextVoxel = voxels[nextIndex];
                                    VoxelLight nextLight = lightData[nextIndex];

                                    if (nextVoxel.data != voxel.data)
                                        break;

                                    if (!light.CompareFace(nextLight, direction))
                                        break;
                                }

                                AddQuadByDirection(direction, voxel.data, light, 1.0f, height, gridPosition, counter, vertices, normals, uvs, colors, indices);
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
            [ReadOnly] public NativeArray<VoxelLight> lightData;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<float3> vertices;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<float3> normals;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<float4> uvs;

            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<int> indices;
            
            [NativeDisableParallelForRestriction] [WriteOnly]
            public NativeArray<Color> colors;
            
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
                                int3 gridPosition = new int3 {[VoxelUtil.DirectionAlignedX[direction]] = x, [VoxelUtil.DirectionAlignedY[direction]] = y, [VoxelUtil.DirectionAlignedZ[direction]] = depth};

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
                                
                                int3 neighborPosition = gridPosition + VoxelUtil.VoxelDirectionOffsets[direction];

                                if (TransparencyCheck(voxels, neighborPosition, chunkSize))
                                {
                                    y++;
                                    continue;
                                }
                                
                                VoxelLight light = lightData[VoxelUtil.To1DIndex(gridPosition, chunkSize)];

                                hashMap.TryAdd(gridPosition, new Empty());

                                int height;
                                for (height = 1; height + y < chunkSize; height++)
                                {
                                    int3 nextPosition = gridPosition;
                                    nextPosition[VoxelUtil.DirectionAlignedY[direction]] += height;

                                    Voxel nextVoxel = voxels[VoxelUtil.To1DIndex(nextPosition, chunkSize)];
                                    VoxelLight nextLight = lightData[VoxelUtil.To1DIndex(nextPosition, chunkSize)];

                                    if (nextVoxel.data != voxel.data)
                                        break;

                                    if (!nextLight.CompareFace(light, direction))
                                        break;

                                    if (hashMap.ContainsKey(nextPosition))
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
                                        nextPosition[VoxelUtil.DirectionAlignedX[direction]] += width;
                                        nextPosition[VoxelUtil.DirectionAlignedY[direction]] += dy;

                                        Voxel nextVoxel = voxels[VoxelUtil.To1DIndex(nextPosition, chunkSize)];
                                        VoxelLight nextLight = lightData[VoxelUtil.To1DIndex(nextPosition, chunkSize)];

                                        if (nextVoxel.data != voxel.data || hashMap.ContainsKey(nextPosition) || !nextLight.CompareFace(light, direction))
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
                                        nextPosition[VoxelUtil.DirectionAlignedX[direction]] += width;
                                        nextPosition[VoxelUtil.DirectionAlignedY[direction]] += dy;
                                        hashMap.TryAdd(nextPosition, new Empty());
                                    }
                                }
                                
                                AddQuadByDirection(direction, voxel.data, light, width, height, gridPosition, counter, vertices, normals, uvs, colors, indices);
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

        public static bool TransparencyCheck(NativeArray<Voxel> voxels, int3 position, int chunkSize)
        {
            if (!BoundaryCheck(chunkSize, position))
                return false;    

            return voxels[VoxelUtil.To1DIndex(position, chunkSize)].data != Voxel.VoxelType.Air;
        }

        static unsafe void AddQuadByDirection(int direction, Voxel.VoxelType data, VoxelLight voxelLight, float width, float height, int3 gridPosition, NativeCounter.Concurrent counter, NativeArray<float3> vertices, NativeArray<float3> normals, NativeArray<float4> uvs, NativeArray<Color> colors, NativeArray<int> indices)
        {
            int numFace = counter.Increment();

            int numVertices = numFace * 4;
            for (int i = 0; i < 4; i++)
            {
                float3 vertex = VoxelUtil.CubeVertices[VoxelUtil.CubeFaces[i + direction * 4]];
                vertex[VoxelUtil.DirectionAlignedX[direction]] *= width;
                vertex[VoxelUtil.DirectionAlignedY[direction]] *= height;

                int atlasIndex = (int) data * 6 + direction;
                int2 atlasPosition = new int2 {x = atlasIndex % AtlasSize.x, y = atlasIndex / AtlasSize.x};

                float4 uv = new float4 {x = VoxelUtil.CubeUVs[i].x * width, y = VoxelUtil.CubeUVs[i].y * height, z = atlasPosition.x, w = atlasPosition.y};

                colors[numVertices + i] = new Color(0, 0, 0, voxelLight.ambient[i + direction * 4]);
                vertices[numVertices + i] = vertex + gridPosition;
                normals[numVertices + i] = VoxelUtil.VoxelDirectionOffsets[direction];
                uvs[numVertices + i] = uv;
            }

            int numindices = numFace * 6;
            for (int i = 0; i < 6; i++)
            {
                if (voxelLight.ambient[direction * 4] + voxelLight.ambient[direction * 4 + 3] < voxelLight.ambient[direction * 4 + 1] + voxelLight.ambient[direction * 4 + 2])
                {
                    indices[numindices + i] = VoxelUtil.CubeFlipedIndices[direction * 6 + i] + numVertices;
                }
                else
                {
                    indices[numindices + i] = VoxelUtil.CubeIndices[direction * 6 + i] + numVertices;
                }
            }
        }
    }
}