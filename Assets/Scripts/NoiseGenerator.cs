using OptIn.Voxel;
using OptIn.Voxel.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public static class NoiseGenerator
{
    static void RandomVoxel(out Voxel voxel, int3 worldPosition)
    {
        voxel = new Voxel();
        int density = -worldPosition.y;
        density += (int)(SimplexNoise.Noise.CalcPixel2DFractal(worldPosition.x, worldPosition.z, 0.003f, 1) * 25f);
        density += (int)(SimplexNoise.Noise.CalcPixel2DFractal(worldPosition.x, worldPosition.z, 0.03f, 3) * 5f);
        density += (int)(SimplexNoise.Noise.CalcPixel2DFractal(worldPosition.x, worldPosition.z, 0.09f, 5) * 1f);

        int level = 0;
        if (density >= level)
        {
            voxel.data = Voxel.VoxelType.Grass;
            level += 1;
        }

        if (density >= level)
        {
            voxel.data = Voxel.VoxelType.Dirt;
            level += (int) (SimplexNoise.Noise.CalcPixel2DFractal(worldPosition.x, worldPosition.z, 0.01f, 1) * 10f) + 3;
        }

        if (density >= level)
            voxel.data = Voxel.VoxelType.Stone;
    }
    
    [BurstCompile]
    struct GenerateNoiseJob : IJobParallelFor
    {
        [ReadOnly] public int3 chunkPosition;
        [ReadOnly] public int3 chunkSize;
        
        [WriteOnly] public NativeArray<Voxel> voxels;

        public void Execute(int index)
        {
            int3 gridPosition = VoxelUtil.To3DIndex(index, chunkSize);
            int3 worldPosition = gridPosition + chunkPosition * chunkSize;
            RandomVoxel(out Voxel voxel, worldPosition);
            voxels[index] = voxel;
        }
    }
    
    public static JobHandle Generate(NativeArray<Voxel>voxels, int3 chunkPosition, int3 chunkSize)
    {
        GenerateNoiseJob noiseJob = new GenerateNoiseJob {chunkPosition = chunkPosition, chunkSize = chunkSize, voxels = voxels};
        JobHandle noiseJobHandle = noiseJob.Schedule(voxels.Length, 32);
        JobHandle.ScheduleBatchedJobs();

        return noiseJobHandle;
    }
    
    
}