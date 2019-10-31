
using OptIn.Voxel;
using OptIn.Voxel.Utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

public static class NoiseGenerator
{
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
        [ReadOnly] public int chunkSize;
        
        [WriteOnly] public NativeArray<Voxel> voxels;

        public void Execute(int index)
        {
            Vector3Int gridPosition = VoxelHelper.To3DIndex(index, chunkSize);
            Vector3Int worldPosition = gridPosition + chunkPosition * chunkSize;
            RandomVoxel(out Voxel voxel, worldPosition);
            voxels[index] = voxel;
        }
    }
    
    public static JobHandle Generate(NativeArray<Voxel>voxels, Vector3Int chunkPosition, int chunkSize)
    {
        GenerateNoiseJob noiseJob = new GenerateNoiseJob {chunkPosition = chunkPosition, chunkSize = chunkSize, voxels = voxels};
        JobHandle noiseJobHandle = noiseJob.Schedule(voxels.Length, 32);
        JobHandle.ScheduleBatchedJobs();

        return noiseJobHandle;
    }
    
    
}