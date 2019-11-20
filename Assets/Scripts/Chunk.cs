using System;
using System.Collections;
using System.Collections.Generic;
using OptIn.Voxel;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
public class Chunk : MonoBehaviour
{
    TerrainGenerator generator;
    Vector3Int chunkPosition;
    Vector3Int chunkSize;
    
    bool initialized;
    bool dirty;
    bool argent;
    Voxel[] voxels;
    Coroutine meshUpdator;

    // Mesh
    Mesh mesh;
    MeshFilter meshFilter;
    MeshRenderer meshRenderer;
    MeshCollider meshCollider;

    public event Func<bool> CanUpdate;

    NoiseGenerator.NativeVoxelData voxelData;
    VoxelLightBuilder.NativeLightData lightData;
    VoxelMeshBuilder.NativeMeshData meshData;

    public bool Dirty => dirty;
    public bool Updating => meshUpdator != null;
    public bool Initialized => initialized;
    public Voxel[] Voxels => voxels;

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();
        mesh = new Mesh {indexFormat = IndexFormat.UInt32};
        CanUpdate = () => true;
    }

    void OnDestroy()
    {
        voxelData?.jobHandle.Complete();
        voxelData?.Dispose();
        meshData?.jobHandle.Complete();
        meshData?.Dispose();
        lightData?.jobHandle.Complete();
        lightData?.Dispose();
    }

    void Start()
    {
        meshFilter.mesh = mesh;
    }
    
    public void Init(Vector3Int position, TerrainGenerator parent)
    {
        chunkPosition = position;
        generator = parent;

        meshRenderer.material = generator.ChunkMaterial;
        chunkSize = generator.ChunkSize;

        StartCoroutine(nameof(InitUpdator));
    }

    IEnumerator InitUpdator()
    {
        int numVoxels = chunkSize.x * chunkSize.y * chunkSize.z;
        voxels =  new Voxel[numVoxels];
        voxelData = new NoiseGenerator.NativeVoxelData(VoxelUtil.ToInt3(chunkSize));
        yield return voxelData.Generate(voxels, VoxelUtil.ToInt3(chunkPosition), VoxelUtil.ToInt3(chunkSize));
        dirty = true;
        initialized = true;
    }

    void Update()
    {
        if (!initialized)
            return;
        
        if (Updating)
            return;

        if (!dirty)
            return;

        if (CanUpdate == null || !CanUpdate())
            return;

        meshUpdator = StartCoroutine(nameof(UpdateMesh));
    }

    IEnumerator UpdateMesh()
    {
        if(Updating)
            yield break;

        if (!generator.CanUpdate)
            yield break;

        generator.UpdatingChunks++;
        
        int3 chunkSizeInt3 = VoxelUtil.ToInt3(chunkSize);

        List<Voxel[]> neighborVoxels = generator.GetNeighborVoxels(chunkPosition, 1);
        
        lightData?.Dispose();
        lightData = new VoxelLightBuilder.NativeLightData(chunkSizeInt3);
        yield return lightData.ScheduleLightingJob(neighborVoxels, VoxelUtil.ToInt3(chunkPosition), chunkSizeInt3, 1, argent);
        meshData?.Dispose();
        meshData = new VoxelMeshBuilder.NativeMeshData(VoxelUtil.ToInt3(chunkSize));
        yield return meshData.ScheduleMeshingJob(voxels, lightData, VoxelUtil.ToInt3(chunkSize), generator.SimplifyingMethod, argent);
        
        meshData.GetMeshInformation(out int verticeSize, out int indicesSize);
        
        if (verticeSize > 0 && indicesSize > 0)
        {
            mesh.Clear();
            mesh.SetVertices(meshData.nativeVertices, 0, verticeSize);
            mesh.SetNormals(meshData.nativeNormals, 0, verticeSize);
            mesh.SetColors(meshData.nativeColors, 0, verticeSize);
            mesh.SetUVs(0, meshData.nativeUVs, 0, verticeSize);
            mesh.SetIndices(meshData.nativeIndices, 0, indicesSize, MeshTopology.Triangles, 0);

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            if(argent)
                SetSharedMesh(mesh);
            else
                VoxelColliderBuilder.Instance.Enqueue(this, mesh);
        }
        
        lightData.Dispose();
        meshData.Dispose();
        dirty = false;
        argent = false;
        gameObject.layer = LayerMask.NameToLayer("Voxel");
        meshUpdator = null;
        generator.UpdatingChunks--;
    }

    public void SetSharedMesh(Mesh bakedMesh)
    {
        meshCollider.sharedMesh = bakedMesh;
    }

    public bool GetVoxel(Vector3Int gridPosition, out Voxel voxel)
    {
        if (!initialized)
        {
            voxel = Voxel.Empty;
            return false;
        }
        
        if (!VoxelUtil.BoundaryCheck(gridPosition, chunkSize))
        {
            voxel = Voxel.Empty;
            return false;
        }

        voxel = voxels[VoxelUtil.To1DIndex(gridPosition, chunkSize)];
        return true;
    }

    public bool SetVoxel(Vector3Int gridPosition, Voxel.VoxelType type)
    {
        if (!initialized)
        {
            return false;
        }
        
        if (!VoxelUtil.BoundaryCheck(gridPosition, chunkSize))
        {
            return false;
        }

        voxels[VoxelUtil.To1DIndex(gridPosition, chunkSize)].data = type;
        dirty = true;
        argent = true;
        return true;
    }

    public void NeighborChunkIsChanged()
    {
        dirty = true;
        argent = true;
    }

    void OnDrawGizmos()
    {
        if (!initialized)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position + new Vector3(chunkSize.x / 2f, chunkSize.y / 2f, chunkSize.z / 2f), chunkSize);
        }
        else if (initialized && dirty)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireCube(transform.position + new Vector3(chunkSize.x / 2f, chunkSize.y / 2f, chunkSize.z / 2f), chunkSize);
        }
    }
}
