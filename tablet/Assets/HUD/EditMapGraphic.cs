using System.Collections.Generic;
using MartianMap.Edits;
using MartianMap.Terrain;
using UnityEngine;
using UnityEngine.UI;

namespace MartianMap.HUD
{
    /// <summary>Screen-space edit outlines and draft vertices. Does not fill or modify hazard rasters.</summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class EditMapGraphic : Graphic
    {
        private IReadOnlyList<LiveEntity> _entities = System.Array.Empty<LiveEntity>();
        private IReadOnlyList<FixedPoint> _draft = System.Array.Empty<FixedPoint>();
        private string _selected;
        private Matrix4x4 _view;
        private Rect _rect;

        public void SetGeometry(IReadOnlyList<LiveEntity> entities, IReadOnlyList<FixedPoint> draft, string selected)
        {
            _entities = entities;
            _draft = draft;
            _selected = selected;
            SetVerticesDirty();
        }

        public bool Project(FixedPoint point, out Vector2 local)
        {
            local = default;
            Camera camera = MapQuery.Camera;
            if (camera == null)
            {
                return false;
            }

            var enu = new Vector2(point.E / 256f, point.N / 256f);
            Vector3 screen = camera.WorldToScreenPoint(ZoneProjection.ToScene(enu, MapQuery.HeightAt(enu)));
            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return screen.z > 0 && RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, uiCamera, out local);
        }

        private void LateUpdate()
        {
            Camera camera = MapQuery.Camera;
            Matrix4x4 view = camera != null ? camera.projectionMatrix * camera.worldToCameraMatrix : Matrix4x4.zero;
            if (view != _view || rectTransform.rect != _rect)
            {
                _view = view;
                _rect = rectTransform.rect;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            foreach (LiveEntity entity in _entities)
            {
                Color tint = entity.EntityId == _selected ? Color.cyan : entity.Op == EditOp.NoteSet ? Color.white
                    : entity.Level >= 192 ? new Color(1, 0.25f, 0.2f) : new Color(1, 0.7f, 0.2f);
                if (entity.Op == EditOp.NoteSet)
                {
                    if (Project(entity.Payload.Point.Value, out Vector2 point))
                    {
                        Line(mesh, point + Vector2.left * 6, point + Vector2.right * 6, tint, 3);
                        Line(mesh, point + Vector2.down * 6, point + Vector2.up * 6, tint, 3);
                    }
                }
                else
                {
                    Ring(mesh, entity.Payload.Polygon, tint, true);
                }
            }

            Ring(mesh, _draft, Color.cyan, _draft.Count >= 3);
            foreach (FixedPoint vertex in _draft)
            {
                if (Project(vertex, out Vector2 point))
                {
                    Line(mesh, point + Vector2.left * 4, point + Vector2.right * 4, Color.white, 6);
                }
            }
        }

        private void Ring(VertexHelper mesh, IReadOnlyList<FixedPoint> ring, Color tint, bool closed)
        {
            int count = closed ? ring.Count : ring.Count - 1;
            for (int i = 0; i < count; i++)
            {
                if (Project(ring[i], out Vector2 a) && Project(ring[(i + 1) % ring.Count], out Vector2 b))
                {
                    Line(mesh, a, b, tint, 3);
                }
            }
        }

        private static void Line(VertexHelper mesh, Vector2 a, Vector2 b, Color tint, float width)
        {
            Vector2 offset = new Vector2(a.y - b.y, b.x - a.x).normalized * width * 0.5f;
            int start = mesh.currentVertCount;
            mesh.AddVert(a - offset, tint, Vector2.zero);
            mesh.AddVert(a + offset, tint, Vector2.zero);
            mesh.AddVert(b + offset, tint, Vector2.zero);
            mesh.AddVert(b - offset, tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
