using OptIn.Voxel;
using UnityEngine;

public class VoxelController : MonoBehaviour
{
    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, 1 << LayerMask.NameToLayer("Voxel")))
            {
                TerrainGenerator.Instance.SetVoxel(hit.point - ray.direction * 0.01f, Voxel.VoxelType.Stone);
            }
        }
        
        if (Input.GetMouseButtonDown(1))
        {
            Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, 1 << LayerMask.NameToLayer("Voxel")))
            {
                TerrainGenerator.Instance.SetVoxel(hit.point + ray.direction * 0.01f, Voxel.VoxelType.Air);
            }
        }
    }
}