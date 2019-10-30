
using OptIn.Voxel;
using OptIn.Voxel.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

public static class NoiseGenerator
{

    class NoiseGeneratorHelper : Singleton<NoiseGeneratorHelper>
    {
        public void Init()
        {
            Debug.Log($"Init {nameof(NoiseGeneratorHelper)} for Automatic Dispose of NativeArray");
        }
            
        void OnDestroy()
        {
            Dispose();
        }
    }

    static void RandomVoxel(out Voxel voxel, Vector3Int worldPosition)
    {
        voxel = new Voxel();
        float density = -worldPosition.y;
        density += SimplexNoise.Noise.CalcPixel2DFractal(worldPosition.x, worldPosition.z, 0.003f, 1) * 25f;
        density += SimplexNoise.Noise.CalcPixel2DFractal(worldPosition.x, worldPosition.z, 0.03f, 3) * 5f;
        density += SimplexNoise.Noise.CalcPixel2DFractal(worldPosition.x, worldPosition.z, 0.09f, 5) * 1f;

        if (density > 0)
            voxel.data = Voxel.VoxelType.Block;
    }
    
    [BurstCompile]
    struct GenerateNoiseJob : IJobParallelFor
    {
        [ReadOnly] public Vector3Int chunkPosition;
        [ReadOnly] public Vector3Int chunkSize;
        
        [WriteOnly] public NativeArray<Voxel> voxels;

        public void Execute(int index)
        {
            Vector3Int gridPosition = VoxelHelper.To3DIndex(index, chunkSize);
            Vector3Int worldPosition = gridPosition + chunkPosition * chunkSize;
            RandomVoxel(out Voxel voxel, worldPosition);
            voxels[index] = voxel;
        }
    }

    static NativeArray<Voxel> nativeVoxels;

    static bool isInitialized = false;

    static void Initialize(Vector3Int chunkSize)
    {
        if (isInitialized)
            return;
        
        nativeVoxels = new NativeArray<Voxel>(chunkSize.x * chunkSize.y * chunkSize.z, Allocator.Persistent);

        NoiseGeneratorHelper.Instance.Init();
        
        isInitialized = true;
    }

    static void Dispose()
    {
        nativeVoxels.Dispose();
    }

    public static void Generate(Voxel[,,] voxels, Vector3Int chunkPosition, Vector3Int chunkSize, bool enableJob)
    {
        if (enableJob)
        {
            if(!isInitialized)
                Initialize(chunkSize);
            
            GenerateNoiseJob noiseJob = new GenerateNoiseJob
            {
                chunkPosition = chunkPosition,
                chunkSize = chunkSize,
                voxels = nativeVoxels
            };

            JobHandle noiseJobHandle = noiseJob.Schedule(nativeVoxels.Length, 32);
            noiseJobHandle.Complete();
            
            voxels.NativeToManaged(nativeVoxels);
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
                        Vector3Int worldPosition = gridPosition + chunkPosition * chunkSize;
                        RandomVoxel(out Voxel voxel, worldPosition);
                        voxels[x, y, z] = voxel;
                    }
                }
            }
        }
    }
    
}