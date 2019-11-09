﻿using System;
using System.Collections;
using System.Collections.Generic;
using OptIn.Voxel;
using Unity.Collections;
using Unity.Jobs;
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
    bool dirty;
    bool isUpdating;
    NativeArray<Voxel> voxels;

    // Mesh
    Mesh mesh;
    MeshFilter meshFilter;
    MeshRenderer meshRenderer;
    MeshCollider meshCollider;

    VoxelMeshBuilder.NativeMeshData meshData;
    JobHandle noiseJobHandle;

    public bool Dirty => dirty;

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();
        mesh = new Mesh {indexFormat = IndexFormat.UInt32};

        VoxelMeshBuilder.InitializeShaderParameter();
    }

    void OnDestroy()
    {
        noiseJobHandle.Complete();
        meshData?.jobHandle.Complete();
        meshData?.Dispose();
        voxels.Dispose();
    }

    void Start()
    {
        meshFilter.mesh = mesh;
    }
    
    public IEnumerator Init(Vector3Int position, TerrainGenerator parent)
    {
        chunkPosition = position;
        generator = parent;

        meshRenderer.material = generator.ChunkMaterial;

        voxels = new NativeArray<Voxel>(generator.ChunkSize * generator.ChunkSize * generator.ChunkSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        noiseJobHandle = NoiseGenerator.Generate(voxels, VoxelUtil.ToInt3(chunkPosition), generator.ChunkSize);
        yield return new WaitUntil(() => noiseJobHandle.IsCompleted);
        noiseJobHandle.Complete();
        dirty = true;
    }

    void LateUpdate()
    {
        if (isUpdating == false)
            return;

        if (meshData == null)
            return;
        
        meshData.CompleteMeshingJob(out int verticeSize, out int indicesSize);
        
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
            
            meshCollider.sharedMesh = mesh;
        }
        
        meshData.Dispose();
        isUpdating = false;
    }

    public void UpdateMesh()
    {
        if (dirty == false)
            return;

        meshData?.Dispose();
        meshData = new VoxelMeshBuilder.NativeMeshData(generator.ChunkSize);
        meshData.ScheduleMeshingJob(voxels, generator.ChunkSize, generator.SimplifyingMethod);
        
        isUpdating = true;
        dirty = false;
    }
}
