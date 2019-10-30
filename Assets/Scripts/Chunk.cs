using System.Collections;
using System.Collections.Generic;
using OptIn.Voxel;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
public class Chunk : MonoBehaviour
{
    TerrainGenerator generator;
    Voxel[,,] voxels;
    Vector3Int chunkPosition;
    bool dirty;

    List<Vector3> vertices = new List<Vector3>();
    List<Vector3> normals = new List<Vector3>();
    List<int> triangles = new List<int>();
    
    // Mesh
    Mesh mesh;
    MeshFilter meshFilter;
    MeshRenderer meshRenderer;
    MeshCollider meshCollider;

    public bool Dirty => dirty;
    
    
    
    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();
        mesh = new Mesh {indexFormat = IndexFormat.UInt32};
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
        
        voxels = new Voxel[generator.ChunkSize.x, generator.ChunkSize.y, generator.ChunkSize.z];
        NoiseGenerator.Generate(voxels, chunkPosition, generator.ChunkSize, generator.EnableJob);
        dirty = true;
    }

    public void UpdateMesh()
    {
        if (dirty == false)
            return;
        
        VoxelGenerator.GenerateByCulling(voxels, generator.ChunkSize, generator.EnableJob, vertices, normals, triangles);

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetNormals(normals);
        
        mesh.RecalculateNormals();
        
        //meshCollider.sharedMesh = mesh;

        dirty = false;
    }
}
