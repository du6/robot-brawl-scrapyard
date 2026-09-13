using UnityEngine;

namespace RobotBrawl.Phase0
{
    // Only runtime-created meshes belong here. Primitive/shared asset meshes
    // are deliberately never registered or destroyed.
    public sealed class WorldMeshOwner : MonoBehaviour
    {
        public Mesh ownedMesh;
        void OnDestroy()
        {
            if (ownedMesh != null) Destroy(ownedMesh);
            ownedMesh = null;
        }
        public static void Own(GameObject owner, Mesh mesh)
        {
            owner.AddComponent<WorldMeshOwner>().ownedMesh = mesh;
        }
    }
}
