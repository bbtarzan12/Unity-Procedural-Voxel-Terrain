using System.Collections;
using OptIn.Voxel.Utils;
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
            NativeArray<Voxel> nativeVoxels;
            public NativeArray<float3> nativeVertices;
            public NativeArray<float3> nativeNormals;
            public NativeArray<int> nativeIndices;
            public NativeArray<float4> nativeUVs;
            public NativeArray<Color> nativeColors;
            public JobHandle jobHandle;
            NativeCounter counter;
            
            public NativeMeshData(int3 chunkSize)
            {
                int numVoxels = chunkSize.x * chunkSize.y * chunkSize.z;
                int maxVertices = 12 * numVoxels;
                int maxIndices = 18 * numVoxels;
                
                nativeVoxels = new NativeArray<Voxel>(numVoxels, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
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
                if (nativeVoxels.IsCreated)
                    nativeVoxels.Dispose();
                
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
            }

            public IEnumerator ScheduleMeshingJob(Voxel[] voxels, VoxelLightBuilder.NativeLightData lightData, int3 chunkSize, SimplifyingMethod method, bool argent = false)
            {
                nativeVoxels.CopyFrom(voxels);
                switch (method)
                {
                    case SimplifyingMethod.Culling:
                        ScheduleCullingJob(nativeVoxels, lightData, chunkSize);
                        break;
                    case SimplifyingMethod.GreedyOnlyHeight:
                        ScheduleGreedyOnlyHeightJob(nativeVoxels, lightData, chunkSize);
                        break;
                    case SimplifyingMethod.Greedy:
                        ScheduleGreedyJob(nativeVoxels, lightData, chunkSize);
                        break;
                    default:
                        ScheduleGreedyJob(nativeVoxels, lightData, chunkSize);
                        break;
                }
                
                int frameCount = lightData.frameCount;
                yield return new WaitUntil(() =>
                {
                    frameCount++;
                    return jobHandle.IsCompleted || frameCount >= 4 || argent;
                });
                
                jobHandle.Complete();
            }

            public void GetMeshInformation(out int verticeSize, out int indicesSize)
            {
                verticeSize = counter.Count * 4;
                indicesSize = counter.Count * 6;
            }
            
            void ScheduleCullingJob(NativeArray<Voxel> voxels, VoxelLightBuilder.NativeLightData lightData, int3 chunkSize)
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
                    lightData = lightData.nativeLightData,
                    counter = counter,
                };

                jobHandle = voxelCullingJob.Schedule();
                JobHandle.ScheduleBatchedJobs();
            }

            void ScheduleGreedyOnlyHeightJob(NativeArray<Voxel> voxels, VoxelLightBuilder.NativeLightData lightData, int3 chunkSize)
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
                    lightData = lightData.nativeLightData,
                    counter = counter,
                };

                jobHandle = voxelMeshingOnlyHeightJob.Schedule();
                JobHandle.ScheduleBatchedJobs();
            }

            void ScheduleGreedyJob(NativeArray<Voxel> voxels, VoxelLightBuilder.NativeLightData lightData, int3 chunkSize)
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
                    lightData = lightData.nativeLightData,
                    counter = counter,
                };

                jobHandle = voxelMeshingOnlyHeightJob.Schedule();
                JobHandle.ScheduleBatchedJobs();
            }
        }

        [BurstCompile]
        struct VoxelCullingParallelJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Voxel> voxels;
            [ReadOnly] public int3 chunkSize;
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

                    AddQuadByDirection(direction, voxel.data, lightData[index], 1.0f, 1.0f, gridPosition, counter.Increment(), vertices, normals, uvs, colors, indices);
                }
            }
        }
        
        [BurstCompile]
        struct VoxelCullingJob : IJob
        {
            [ReadOnly] public NativeArray<Voxel> voxels;
            [ReadOnly] public int3 chunkSize;
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

            [WriteOnly] public NativeCounter counter;

            public void Execute()
            {
                for (int x = 0; x < chunkSize.x; x++)
                {
                    for (int y = 0; y < chunkSize.y; y++)
                    {
                        for (int z = 0; z < chunkSize.z; z++)
                        {
                            int3 gridPosition = new int3(x, y, z);
                            int index = VoxelUtil.To1DIndex(gridPosition, chunkSize);

                            Voxel voxel = voxels[index];

                            if (voxel.data == Voxel.VoxelType.Air)
                                continue;

                            for (int direction = 0; direction < 6; direction++)
                            {
                                int3 neighborPosition = gridPosition + VoxelUtil.VoxelDirectionOffsets[direction];

                                if (TransparencyCheck(voxels, neighborPosition, chunkSize))
                                    continue;

                                AddQuadByDirection(direction, voxel.data, lightData[index], 1.0f, 1.0f, gridPosition, counter.Increment(), vertices, normals, uvs, colors, indices);
                            }
                        }
                    }
                }
            }
        }

        [BurstCompile]
        struct VoxelGreedyMeshingOnlyHeightJob : IJob
        {
            [ReadOnly] public NativeArray<Voxel> voxels;
            [ReadOnly] public int3 chunkSize;
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
            
            [WriteOnly] public NativeCounter counter;

            public void Execute()
            {
                for (int direction = 0; direction < 6; direction++)
                {
                    for (int depth = 0; depth < chunkSize[VoxelUtil.DirectionAlignedZ[direction]]; depth++)
                    {
                        for (int x = 0; x < chunkSize[VoxelUtil.DirectionAlignedX[direction]]; x++)
                        {
                            for (int y = 0; y < chunkSize[VoxelUtil.DirectionAlignedY[direction]];)
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
                                for (height = 1; height + y < chunkSize[VoxelUtil.DirectionAlignedY[direction]]; height++)
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

                                AddQuadByDirection(direction, voxel.data, light, 1.0f, height, gridPosition, counter.Increment(), vertices, normals, uvs, colors, indices);
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
            [ReadOnly] public int3 chunkSize;
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
            
            [WriteOnly] public NativeCounter counter;
            
            struct Empty {}

            public void Execute()
            {
                for (int direction = 0; direction < 6; direction++)
                {
                    NativeHashMap<int3, Empty> hashMap = new NativeHashMap<int3, Empty>(chunkSize[VoxelUtil.DirectionAlignedX[direction]] * chunkSize[VoxelUtil.DirectionAlignedY[direction]], Allocator.Temp);
                    for (int depth = 0; depth < chunkSize[VoxelUtil.DirectionAlignedZ[direction]]; depth++)
                    {
                        for (int x = 0; x < chunkSize[VoxelUtil.DirectionAlignedX[direction]]; x++)
                        {
                            for (int y = 0; y < chunkSize[VoxelUtil.DirectionAlignedY[direction]];)
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
                                for (height = 1; height + y < chunkSize[VoxelUtil.DirectionAlignedY[direction]]; height++)
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
                                for (width = 1; width + x < chunkSize[VoxelUtil.DirectionAlignedX[direction]]; width++)
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
                                
                                AddQuadByDirection(direction, voxel.data, light, width, height, gridPosition, counter.Increment(), vertices, normals, uvs, colors, indices);
                                y += height;
                            }
                        }
                        
                        hashMap.Clear();
                    }
                    hashMap.Dispose();
                }
            }
        }

        public static bool TransparencyCheck(NativeArray<Voxel> voxels, int3 position, int3 chunkSize)
        {
            if (!VoxelUtil.BoundaryCheck(position, chunkSize))
                return false;    

            return voxels[VoxelUtil.To1DIndex(position, chunkSize)].data != Voxel.VoxelType.Air;
        }

        static unsafe void AddQuadByDirection(int direction, Voxel.VoxelType data, VoxelLight voxelLight, float width, float height, int3 gridPosition, int numFace, NativeArray<float3> vertices, NativeArray<float3> normals, NativeArray<float4> uvs, NativeArray<Color> colors, NativeArray<int> indices)
        {
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