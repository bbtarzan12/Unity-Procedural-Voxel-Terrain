﻿using System;
using System.Collections;
using System.Collections.Generic;
using OptIn.Voxel;
using Unity.Collections;
using Unity.Jobs;
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

    List<Vector3> vertices = new List<Vector3>();
    List<Vector3> normals = new List<Vector3>();
    List<int> triangles = new List<int>();
    
    // Mesh
    Mesh mesh;
    MeshFilter meshFilter;
    MeshRenderer meshRenderer;
    MeshCollider meshCollider;

    VoxelGenerator.NativeVoxelData data;
    JobHandle noiseJobHandle;

    public bool Dirty => dirty;

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();
        mesh = new Mesh {indexFormat = IndexFormat.UInt32};
    }

    void OnDestroy()
    {
        noiseJobHandle.Complete();
        data?.jobHandle.Complete();
        data?.Dispose();
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
        
        voxels = new NativeArray<Voxel>(generator.ChunkSize * generator.ChunkSize * generator.ChunkSize, Allocator.Persistent);
        noiseJobHandle = NoiseGenerator.Generate(voxels, chunkPosition, generator.ChunkSize);
        yield return new WaitUntil(() => noiseJobHandle.IsCompleted);
        noiseJobHandle.Complete();
        dirty = true;
    }

    void LateUpdate()
    {
        if (isUpdating == false)
            return;

        VoxelGenerator.CompleteMeshingJob(data, vertices, normals, triangles);
        
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetNormals(normals);
        
        mesh.RecalculateNormals();
        
        //meshCollider.sharedMesh = mesh;

        data = null;
        isUpdating = false;
    }

    public void UpdateMesh()
    {
        if (dirty == false)
            return;

        data = VoxelGenerator.ScheduleMeshingJob(voxels, generator.ChunkSize, generator.SimplifyingMethod);
        
        isUpdating = true;
        dirty = false;
    }
}
