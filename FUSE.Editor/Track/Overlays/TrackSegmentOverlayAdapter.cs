using Core;
using FUSE.Authoring.Data;
using FUSE.Editor.Overlays;
using Track;
using UnityEngine;

namespace FUSE.Editor.Track.Overlays
{
    /// <summary>
    /// Adapter that makes TrackSegment compatible with the overlay rendering system.
    /// Provides mesh and material data for rendering track segment previews.
    /// Segments are rendered as two rectangular rails following the segment's actual Bezier curve geometry.
    /// </summary>
    public class TrackSegmentOverlayAdapter : IOverlayRenderable
    {
        private Mesh _cachedMesh;
        public BezierCurve Curve;
        private static Material material_Normal;
        private static Material material_Selected;

        private Vector3 nodeAPos;
        private Vector3 nodeARot;

        private Vector3 nodeBPos;
        private Vector3 nodeBRot;

        // Rail dimensions (in game units)
        private const float RAIL_TOP_HEIGHT = 0.001f;     // Distance above curve centerline
        private const float RAIL_BOTTOM_HEIGHT = 0.1f;   // Distance below curve centerline

        public TrackSegmentOverlayAdapter()
        {
            if (material_Normal == null)
            {
                material_Normal = new(FuseOverlayManager.Instance.GetRenderer().WireframeMaterial);
                material_Normal.color = Color.cyan;
                material_Selected = new(FuseOverlayManager.Instance.GetRenderer().GhostMaterial);
                material_Selected.color = Color.green;
            }
        }

        public void UpdateIfNeeded(object Entity, object FuseData)
        {
            TrackSegment segment = Entity as TrackSegment;

            if (segment == null || FuseData == null)
            {
                return;
            }

            FuseSegment fuseSegment = FuseData as FuseSegment;

            TrackNode a = FUSE.Runtime.API.TrackAPI.GetNode(fuseSegment.StartNodeId);
            TrackNode b = FUSE.Runtime.API.TrackAPI.GetNode(fuseSegment.EndNodeId);

            // Get preview data if available (for pending edits)
            (Vector3 pos, Vector3 rot) nodeATransform = (a.transform.localPosition, a.transform.rotation.eulerAngles);
            (Vector3 pos, Vector3 rot) nodeBTransform = (b.transform.localPosition, b.transform.rotation.eulerAngles);

            OverlayPreviewData nodeAPreview = FuseOverlayManager.Instance.GetPreview(fuseSegment.StartNodeId);
            if (nodeAPreview != null && nodeAPreview.FuseData != null)
            {
                nodeATransform = (((FuseNode)nodeAPreview.FuseData).Position, ((FuseNode)nodeAPreview.FuseData).Rotation);
            }

            OverlayPreviewData nodeBPreview = FuseOverlayManager.Instance.GetPreview(fuseSegment.EndNodeId);
            if (nodeBPreview != null && nodeBPreview.FuseData != null)
            {
                nodeBTransform = (((FuseNode)nodeBPreview.FuseData).Position, ((FuseNode)nodeBPreview.FuseData).Rotation);
            }


            // Regenerate mesh if nodes have changed
            if (HaveNodesChanged(nodeATransform, nodeBTransform))
            {
                // Get the Bezier curve from the segment
                Curve = CreateBezier();
                int curveResolution = Mathf.RoundToInt(Curve.CalculateLength() * 8);
                curveResolution = Mathf.Max(4, curveResolution); // Ensure minimum resolution
                Mesh rail = CreateBezierRailMesh(Curve, curveResolution, 1.435f, 0.07f, segment.a.transform.localPosition);

                Mesh cheveron = TrackNodeOverlayAdapter.CreateChevronMesh(0.75f);

                _cachedMesh = CombineMeshesWithOffsetAndRotation(rail, cheveron, Curve.GetPoint(0.5f) - segment.a.transform.localPosition, Curve.GetRotation(0.5f));
            }

        }

        public Mesh GetOverlayMesh(object Entity, object FuseData)
        {
            UpdateIfNeeded(Entity, FuseData);

            return _cachedMesh;
        }

        private static Mesh CombineMeshesWithOffsetAndRotation(Mesh mesh1, Mesh mesh2, Vector3 offset, Quaternion rotation)
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

        public Material GetOverlayMaterial(object Entity, object FuseData)
        {
            TrackSegment segment = Entity as TrackSegment;

            // Check if the segment is selected
            if (segment != null && FuseEditor.Instance.EntitySelection.SelectedIds.Contains(segment.id))
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
            //UpdateIfNeeded(Entity, FuseData);
            if (_cachedMesh != null)
            {
                return new Bounds(Curve.GetPoint(0.5f) - ((TrackSegment)Entity).a.transform.localPosition, Vector3.one * 0.6f);
            }
            else
            {
                return new Bounds(Vector3.zero, Vector3.one);
            }
        }

        private BezierCurve CreateBezier()
        {
            Quaternion aRot = Quaternion.Euler(nodeARot);
            Quaternion bRot = Quaternion.Euler(nodeBRot);

            float d = (nodeAPos - nodeBPos).magnitude * BezierTangentFactorForTangents(aRot * Vector3.forward, bRot * Vector3.forward);
            Vector3 vector = TangentPointAlongSegment(nodeAPos, aRot, nodeBPos, bRot, d);
            Vector3 vector2 = TangentPointAlongSegment(nodeBPos, bRot, nodeAPos, aRot, d);
            return new BezierCurve(new Vector3[4] { nodeAPos, vector, vector2, nodeBPos }, aRot * Vector3.up, bRot * Vector3.up);
        }

        private static float BezierTangentFactorForTangents(Vector3 a, Vector3 b)
        {
            float num = Vector3.Angle(a, b);
            if (num > 90f)
            {
                num = 180f - num;
            }
            return Mathf.Lerp(0.35f, 0.41f, Mathf.InverseLerp(45f, 90f, num));
        }

        private Vector3 TangentPointAlongSegment(Vector3 thisPos, Quaternion thisRot, Vector3 otherPos, Quaternion otherRot, float d)
        {
            Vector3 vector = Transform(thisPos, thisRot, Vector3.forward);
            Vector3 vector2 = Transform(thisPos, thisRot, Vector3.back);
            float magnitude = (vector - otherPos).magnitude;
            float magnitude2 = (vector2 - otherPos).magnitude;
            float num = ((magnitude < magnitude2) ? d : (0f - d));
            return Transform(thisPos, thisRot, Vector3.forward * num);
        }

        private Vector3 Transform(Vector3 pos, Quaternion rot, Vector3 v)
        {
            return rot * v + pos;
        }

        private bool HaveNodesChanged((Vector3 pos, Vector3 rot) nodeA, (Vector3 pos, Vector3 rot) nodeB)
        {
            if (nodeA.pos != nodeAPos || nodeA.rot != nodeARot || nodeB.pos != nodeBPos || nodeB.rot != nodeBRot)
            {
                // Update cached transforms
                nodeAPos = nodeA.pos;
                nodeARot = nodeA.rot;
                nodeBPos = nodeB.pos;
                nodeBRot = nodeB.rot;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Creates a mesh with two rectangular rails following a Bezier curve.
        /// Rails are positioned symmetrically around the curve centerline.
        /// Top faces are 0.05 units above the curve, bottom faces are 0.1 units below.
        /// </summary>
        /// <param name="bezierCurve">The BezierCurve object defining the centerline path</param>
        /// <param name="curveResolution">Number of sample points along the curve</param>
        /// <param name="gaugeInside">Distance between inside faces of the two rails</param>
        /// <param name="gaugeHeadWidth">Width of each rail</param>
        /// <param name="offset">Offset to apply to the entire mesh</param>
        private static Mesh CreateBezierRailMesh(BezierCurve bezierCurve, int curveResolution, float gaugeInside, float gaugeHeadWidth, Vector3 offset)
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
    }
}
