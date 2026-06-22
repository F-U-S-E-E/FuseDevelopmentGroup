using FUSE.Authoring.Data;
using FUSE.Editor.Overlays;
using Track;
using UnityEngine;

namespace FUSE.Editor.Track.Overlays
{
    /// <summary>
    /// Adapter that makes TrackNode compatible with the overlay rendering system.
    /// Provides mesh and material data for rendering track node previews.
    /// </summary>
    public class TrackNodeOverlayAdapter : IOverlayRenderable
    {
        private static Mesh _chevronMesh;
        private Material material_Normal;
        private Material material_Selected;

        public TrackNodeOverlayAdapter()
        {
            if (material_Normal == null)
            {
                material_Normal = new(FuseOverlayManager.Instance.GetRenderer().WireframeMaterial);
                material_Normal.color = Color.yellow;
                material_Selected = new(FuseOverlayManager.Instance.GetRenderer().GhostMaterial);
                material_Selected.color = Color.green;
            }
        }

        public Mesh GetOverlayMesh(object Entity, object FuseData)
        {
            // Get the static chevron mesh, creating it once on first access
            if (_chevronMesh == null)
            {
                _chevronMesh = CreateChevronMesh(0.5f);
            }
            return _chevronMesh;
        }

        public Material GetOverlayMaterial(object Entity, object FuseData)
        {
            // Allow override, otherwise return null to use default wireframe
            if (FuseEditor.Instance.EntitySelection.SelectedIds.Contains(((TrackNode)Entity).id))
            {
                return material_Selected;
            }
            else
            {
                return material_Normal;
            }
        }

        public Bounds GetObjectBounds(object Entity, object FuseData)
        {
            FuseNode node = FuseData as FuseNode;

            if (_chevronMesh == null)
            {
                _chevronMesh = CreateChevronMesh(0.5f);
            }

            return _chevronMesh.bounds;
        }

        /// <summary>
        /// Creates a chevron shape (two arms forming a V) pointing forward along the Z-axis.
        /// Tapers to a point at the front, opens wide at the back with a center notch.
        /// </summary>
        public static Mesh CreateChevronMesh(float size)
        {
            var mesh = new Mesh();

            // Chevron: two arms meeting at front tip, with a notch in the back middle
            var vertices = new Vector3[]
            {
                // Shared front tip (vertical edge)
                new Vector3(0f, 0.1f, 0.5f) * size,       // 0: front tip top
                new Vector3(0f, -0.1f, 0.5f) * size,      // 1: front tip bottom
                new Vector3(0f, 0.1f, 0.35f) * size,       // 2: back tip top
                new Vector3(0f, -0.1f, 0.35f) * size,      // 3: back tip bottom

                // Left arm back corners
                new Vector3(-0.45f, 0.1f, -0.4f) * size,  // 4 (2): left back outer top
                new Vector3(-0.45f, -0.1f, -0.4f) * size, // 5 (3): left back outer bottom
                new Vector3(-0.35f, 0.1f, -0.4f) * size,  // 6 (4): left back inner top
                new Vector3(-0.35f, -0.1f, -0.4f) * size, // 7 (5): left back inner bottom

                // Right arm back corners
                new Vector3(0.35f, 0.1f, -0.4f) * size,   // 8 (6): right back inner top
                new Vector3(0.35f, -0.1f, -0.4f) * size,  // 9 (7): right back inner bottom
                new Vector3(0.45f, 0.1f, -0.4f) * size,   // 10 (8): right back outer top
                new Vector3(0.45f, -0.1f, -0.4f) * size,  // 11 (9): right back outer bottom
            };

            var triangles = new int[]
            {
                // ===== LEFT ARM =====
                // Top face (front tip -> back inner -> back outer)
                0, 2, 6,
                0, 6, 4,
                // Bottom face
                1, 7, 3,
                1, 5, 7,
                // Outer side face (front tip to back-outer)
                0, 4, 1,
                1, 4, 5,
                // Inner side face (back tip to back-inner)
                2, 3, 6,
                6, 3, 7,
                // Back face (rectangle)
                4, 6, 7,
                4, 7, 5,

                // ===== RIGHT ARM =====
                // Top face (front tip -> back inner -> back outer)
                0, 8, 2,
                0, 10, 8,
                // Bottom face
                1, 3, 9,
                1, 9, 11,
                // Outer side face (front tip to back-outer)
                0, 1, 10,
                1, 11, 10,
                // Inner side face (back tip to back-inner)
                2, 8, 3,
                8, 9, 3,
                // Back face (rectangle)
                10, 9, 8,
                10, 11, 9,
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}
