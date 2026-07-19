using Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace FUSE.Editor.Resources
{
    class MeshGenerator
    {
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

        public static Mesh CreateCubicMeshAlongPath(Vector3[] points, Vector3 offset)
        {
            if (points == null || points.Length < 2)
            {
                return null;
            }

            var mesh = new Mesh();
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            float cubeSize = 0.075f; // Half-width of the cube cross-section

            // For each segment between consecutive points
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector3 p1 = points[i] - offset;
                Vector3 p2 = points[i + 1] - offset;
                Vector3 direction = (p2 - p1).normalized;

                // Calculate perpendicular vectors for the cube cross-section
                Vector3 right = Vector3.Cross(direction, Vector3.up).normalized;
                if (right.magnitude < 0.01f)
                {
                    right = Vector3.Cross(direction, Vector3.right).normalized;
                }
                Vector3 up = Vector3.Cross(right, direction).normalized;

                // Create 8 vertices for the cube at this segment
                int baseVertexIndex = vertices.Count;

                // Front face
                vertices.Add(p1 - right * cubeSize - up * cubeSize);
                vertices.Add(p1 + right * cubeSize - up * cubeSize);
                vertices.Add(p1 + right * cubeSize + up * cubeSize);
                vertices.Add(p1 - right * cubeSize + up * cubeSize);

                // Back face
                vertices.Add(p2 - right * cubeSize - up * cubeSize);
                vertices.Add(p2 + right * cubeSize - up * cubeSize);
                vertices.Add(p2 + right * cubeSize + up * cubeSize);
                vertices.Add(p2 - right * cubeSize + up * cubeSize);

                // Create triangles for the 6 faces of the cube segment
                // Front face
                triangles.Add(baseVertexIndex + 0);
                triangles.Add(baseVertexIndex + 1);
                triangles.Add(baseVertexIndex + 2);
                triangles.Add(baseVertexIndex + 0);
                triangles.Add(baseVertexIndex + 2);
                triangles.Add(baseVertexIndex + 3);

                // Back face
                triangles.Add(baseVertexIndex + 6);
                triangles.Add(baseVertexIndex + 5);
                triangles.Add(baseVertexIndex + 4);
                triangles.Add(baseVertexIndex + 7);
                triangles.Add(baseVertexIndex + 6);
                triangles.Add(baseVertexIndex + 4);

                // Top face
                triangles.Add(baseVertexIndex + 3);
                triangles.Add(baseVertexIndex + 2);
                triangles.Add(baseVertexIndex + 6);
                triangles.Add(baseVertexIndex + 3);
                triangles.Add(baseVertexIndex + 6);
                triangles.Add(baseVertexIndex + 7);

                // Bottom face
                triangles.Add(baseVertexIndex + 4);
                triangles.Add(baseVertexIndex + 5);
                triangles.Add(baseVertexIndex + 1);
                triangles.Add(baseVertexIndex + 4);
                triangles.Add(baseVertexIndex + 1);
                triangles.Add(baseVertexIndex + 0);

                // Left face
                triangles.Add(baseVertexIndex + 4);
                triangles.Add(baseVertexIndex + 0);
                triangles.Add(baseVertexIndex + 3);
                triangles.Add(baseVertexIndex + 4);
                triangles.Add(baseVertexIndex + 3);
                triangles.Add(baseVertexIndex + 7);

                // Right face
                triangles.Add(baseVertexIndex + 1);
                triangles.Add(baseVertexIndex + 5);
                triangles.Add(baseVertexIndex + 6);
                triangles.Add(baseVertexIndex + 1);
                triangles.Add(baseVertexIndex + 6);
                triangles.Add(baseVertexIndex + 2);
            }

            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        public static Mesh CreateBezierRailMesh(BezierCurve bezierCurve, int curveResolution, float gaugeInside, float gaugeHeadWidth, Vector3 offset, float RAIL_TOP_HEIGHT, float RAIL_BOTTOM_HEIGHT)
        {
            var mesh = new Mesh();
            mesh.name = "BezierRailMesh";

            // Sample points along the Bezier curve
            var curvePoints = new Vector3[curveResolution];
            var curveTangents = new Vector3[curveResolution];
            var curveUps = new Vector3[curveResolution];
            var curveRights = new Vector3[curveResolution];

            // Calculate rail positioning
            float railCenterDistance = gaugeInside / 2f;  // Distance from centerline to each rail's inside face

            for (int i = 0; i < curveResolution; i++)
            {
                float t = i / (curveResolution - 1f);

                // Evaluate Bezier curve at parameter t
                curvePoints[i] = bezierCurve.GetPoint(t) - offset;

                // Get direction (tangent) from the curve
                curveTangents[i] = bezierCurve.GetDirection(t);

                // Interpolate up vector between start and end
                curveUps[i] = Vector3.Lerp(bezierCurve._up0, bezierCurve._up3, t);

                // Calculate perpendicular (right) vector
                curveRights[i] = Vector3.Cross(curveTangents[i], curveUps[i]).normalized;
            }

            // Each rail has 4 vertices per curve point (inside-top, outside-top, inside-bottom, outside-bottom)
            // With 2 rails, that's 8 vertices per curve point
            int verticesPerPoint = 8;
            var vertices = new Vector3[curveResolution * verticesPerPoint];
            var triangles = new int[(curveResolution - 1) * 24 * 2];  // 24 triangles per segment (top, bottom, 4 sides), 2 rails
            var uv = new Vector2[curveResolution * verticesPerPoint];

            // Generate vertices for each point along the curve
            for (int curveIdx = 0; curveIdx < curveResolution; curveIdx++)
            {
                Vector3 point = curvePoints[curveIdx];
                Vector3 right = curveRights[curveIdx];
                Vector3 up = curveUps[curveIdx];
                Vector3 down = -up;

                // Rail 1 (left side: negative right direction)
                Vector3 rail1Center = point - right * railCenterDistance;
                vertices[curveIdx * verticesPerPoint + 0] = rail1Center + up * RAIL_TOP_HEIGHT;                              // inside-top
                vertices[curveIdx * verticesPerPoint + 1] = rail1Center - right * gaugeHeadWidth + up * RAIL_TOP_HEIGHT;    // outside-top
                vertices[curveIdx * verticesPerPoint + 2] = rail1Center + down * RAIL_BOTTOM_HEIGHT;                         // inside-bottom
                vertices[curveIdx * verticesPerPoint + 3] = rail1Center - right * gaugeHeadWidth + down * RAIL_BOTTOM_HEIGHT; // outside-bottom

                // Rail 2 (right side: positive right direction)
                Vector3 rail2Center = point + right * railCenterDistance;
                vertices[curveIdx * verticesPerPoint + 4] = rail2Center + up * RAIL_TOP_HEIGHT;                              // inside-top
                vertices[curveIdx * verticesPerPoint + 5] = rail2Center + right * gaugeHeadWidth + up * RAIL_TOP_HEIGHT;    // outside-top
                vertices[curveIdx * verticesPerPoint + 6] = rail2Center + down * RAIL_BOTTOM_HEIGHT;                         // inside-bottom
                vertices[curveIdx * verticesPerPoint + 7] = rail2Center + right * gaugeHeadWidth + down * RAIL_BOTTOM_HEIGHT; // outside-bottom

                // Set UVs
                for (int v = 0; v < verticesPerPoint; v++)
                {
                    uv[curveIdx * verticesPerPoint + v] = new Vector2((v % 2) / 1f, curveIdx / (float)(curveResolution - 1));
                }
            }

            // Generate triangles connecting segments
            int triIdx = 0;
            for (int curveIdx = 0; curveIdx < curveResolution - 1; curveIdx++)
            {
                int currentBase = curveIdx * verticesPerPoint;
                int nextBase = (curveIdx + 1) * verticesPerPoint;

                // =========================
                // RAIL 1 (left side)
                // =========================

                // Top face (inside-top to outside-top)
                triangles[triIdx++] = currentBase + 0;
                triangles[triIdx++] = nextBase + 0;
                triangles[triIdx++] = currentBase + 1;
                triangles[triIdx++] = currentBase + 1;
                triangles[triIdx++] = nextBase + 0;
                triangles[triIdx++] = nextBase + 1;

                // Bottom face (inside-bottom to outside-bottom)
                triangles[triIdx++] = currentBase + 2;
                triangles[triIdx++] = currentBase + 3;
                triangles[triIdx++] = nextBase + 2;
                triangles[triIdx++] = currentBase + 3;
                triangles[triIdx++] = nextBase + 3;
                triangles[triIdx++] = nextBase + 2;

                // Inside face (inside-top to inside-bottom)
                triangles[triIdx++] = currentBase + 0;
                triangles[triIdx++] = currentBase + 2;
                triangles[triIdx++] = nextBase + 0;
                triangles[triIdx++] = nextBase + 0;
                triangles[triIdx++] = currentBase + 2;
                triangles[triIdx++] = nextBase + 2;

                // Outside face (outside-top to outside-bottom)
                triangles[triIdx++] = currentBase + 1;
                triangles[triIdx++] = nextBase + 1;
                triangles[triIdx++] = currentBase + 3;
                triangles[triIdx++] = currentBase + 3;
                triangles[triIdx++] = nextBase + 1;
                triangles[triIdx++] = nextBase + 3;

                // =========================
                // RAIL 2 (right side)
                // =========================

                // Top face (inside-top to outside-top)
                triangles[triIdx++] = currentBase + 4;
                triangles[triIdx++] = currentBase + 5;
                triangles[triIdx++] = nextBase + 4;
                triangles[triIdx++] = currentBase + 5;
                triangles[triIdx++] = nextBase + 5;
                triangles[triIdx++] = nextBase + 4;

                // Bottom face (inside-bottom to outside-bottom)
                triangles[triIdx++] = currentBase + 6;
                triangles[triIdx++] = nextBase + 6;
                triangles[triIdx++] = currentBase + 7;
                triangles[triIdx++] = currentBase + 7;
                triangles[triIdx++] = nextBase + 6;
                triangles[triIdx++] = nextBase + 7;

                // Inside face (inside-top to inside-bottom)
                triangles[triIdx++] = currentBase + 4;
                triangles[triIdx++] = nextBase + 4;
                triangles[triIdx++] = currentBase + 6;
                triangles[triIdx++] = currentBase + 6;
                triangles[triIdx++] = nextBase + 4;
                triangles[triIdx++] = nextBase + 6;

                // Outside face (outside-top to outside-bottom)
                triangles[triIdx++] = currentBase + 5;
                triangles[triIdx++] = currentBase + 7;
                triangles[triIdx++] = nextBase + 5;
                triangles[triIdx++] = nextBase + 5;
                triangles[triIdx++] = currentBase + 7;
                triangles[triIdx++] = nextBase + 7;
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uv;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        public static Mesh CombineMeshesWithOffsetAndRotation(Mesh mesh1, Mesh mesh2, Vector3 offset, Quaternion rotation)
        {
            // Combine both meshes with transform
            CombineInstance[] combineInstances = new CombineInstance[2];

            combineInstances[0].mesh = mesh1;
            combineInstances[0].transform = Matrix4x4.identity;

            // Apply offset and rotation via transform matrix
            combineInstances[1].mesh = mesh2;
            combineInstances[1].transform = Matrix4x4.TRS(offset, rotation, Vector3.one);

            Mesh combinedMesh = new Mesh();
            combinedMesh.name = "CombinedMesh";
            combinedMesh.CombineMeshes(combineInstances);

            return combinedMesh;
        }

        public static Mesh CreateSolidTriangleMesh(Vector3 center, Quaternion rotation, float size)
        {
            var mesh = new Mesh();
            mesh.name = "SolidTriangleMesh";

            // Create triangular prism vertices - tapered at front, wide at back, 3D top to bottom
            var vertices = new Vector3[]
            {
                // Top vertices
                new Vector3(0, size * 0.1f, -size * 0.5f),     // 0: Front center top (tapered)
                new Vector3(-size, size * 0.1f, size * 0.5f),  // 1: Back-left top
                new Vector3(size, size * 0.1f, size * 0.5f),   // 2: Back-right top

                // Bottom vertices
                new Vector3(0, -size * 0.1f, -size * 0.5f),    // 3: Front center bottom (tapered)
                new Vector3(-size, -size * 0.1f, size * 0.5f), // 4: Back-left bottom
                new Vector3(size, -size * 0.1f, size * 0.5f)   // 5: Back-right bottom
            };

            // Apply rotation to each vertex and add center offset
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = rotation * vertices[i] + center;
            }

            var triangles = new int[]
            {
                // Top face
                0, 1, 2,
                // Bottom face
                3, 5, 4,
                // Front-left side face
                0, 3, 1,
                1, 3, 4,
                // Front-right side face
                0, 2, 3,
                2, 5, 3,
                // Back face
                1, 5, 2,
                1, 4, 5
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}
