namespace OptIn.Voxel
{
    public struct Voxel
    {
        public enum VoxelType { Air, Grass, Dirt, Stone }

        public VoxelType data;
        
        public static Voxel Empty => new Voxel{data = VoxelType.Air};
    }
    
    
}