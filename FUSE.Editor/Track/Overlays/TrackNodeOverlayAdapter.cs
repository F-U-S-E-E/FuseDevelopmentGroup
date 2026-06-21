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
        private Mesh _sphereMesh;
        private Material _material;

        Color normal = new Color(0.8f, 0.5f, 0, 0.65f);
        Color selected = new Color(0.6f, 0.5f, 0.2f, 0.65f);

        public TrackNodeOverlayAdapter(Material overrideMaterial)
        {
            _material = overrideMaterial;
        }

        public Mesh GetOverlayMesh(object Entity, object FuseData)
        {
            // Create a simple sphere mesh if not cached
            if (_sphereMesh == null)
            {
                _sphereMesh = CreateSphereMesh(0.6f, 16, 16);
            }
            return _sphereMesh;
        }

        public Material GetOverlayMaterial(object Entity, object FuseData)
        {
            // Allow override, otherwise return null to use default wireframe
            if (FuseEditor.Instance.EntitySelection.IsEntitySelected(FuseData, ((TrackNode)Entity).id))
            {
                _material.color = selected;
            }
            else
            {
                _material.color = normal;
            }
            return _material;
        }

        public Bounds GetObjectBounds(object Entity, object FuseData)
        {
            FuseNode node = FuseData as FuseNode;
            var bounds = new Bounds(node.Position, Vector3.one);

            return bounds;
        }

        /// <summary>
        /// Creates a simple UV sphere mesh at runtime.
        /// </summary>
        private static Mesh CreateSphereMesh(float radius, int latitudeBands, int longitudeBands)
        {
            var mesh = new Mesh();
            var vertices = new Vector3[(latitudeBands + 1) * (longitudeBands + 1)];
            var triangles = new int[latitudeBands * longitudeBands * 6];

            for (int lat = 0; lat <= latitudeBands; lat++)
            {
                float latAngle = Mathf.PI * lat / latitudeBands;
                float sinLat = Mathf.Sin(latAngle);
                float cosLat = Mathf.Cos(latAngle);

                for (int lon = 0; lon <= longitudeBands; lon++)
                {
                    float lonAngle = 2 * Mathf.PI * lon / longitudeBands;
                    float sinLon = Mathf.Sin(lonAngle);
                    float cosLon = Mathf.Cos(lonAngle);

                    int idx = lat * (longitudeBands + 1) + lon;
                    vertices[idx] = new Vector3(
                        radius * sinLat * cosLon,
                        radius * cosLat,
                        radius * sinLat * sinLon
                    );
                }
            }

            int triIdx = 0;
            for (int lat = 0; lat < latitudeBands; lat++)
            {
                for (int lon = 0; lon < longitudeBands; lon++)
                {
                    int a = lat * (longitudeBands + 1) + lon;
                    int b = a + 1;
                    int c = a + longitudeBands + 1;
                    int d = c + 1;

                    triangles[triIdx++] = a;
                    triangles[triIdx++] = c;
                    triangles[triIdx++] = b;

                    triangles[triIdx++] = b;
                    triangles[triIdx++] = c;
                    triangles[triIdx++] = d;
                }
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}
