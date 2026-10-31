using System;
using System.Collections.Generic;
using UnityEngine;

namespace Withme.Character
{
    public static class JointDepth
    {
        // Double-sided intersections avoid depending on the global physics
        // backface flag. Disconnected/overlapping surfaces require a new view.
        public static void Intersections(Ray ray, Vector3[] vertices, int[] triangles, List<float> hits)
        {
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]]; var edge1 = vertices[triangles[i+1]] - a; var edge2 = vertices[triangles[i+2]] - a;
                var p = Vector3.Cross(ray.direction, edge2); var determinant = Vector3.Dot(edge1,p);
                if (Mathf.Abs(determinant) < 1e-8f) continue;
                var inverse = 1 / determinant; var offset = ray.origin-a; var u = Vector3.Dot(offset,p)*inverse;
                if (u < -1e-6f || u > 1+1e-6f) continue;
                var q = Vector3.Cross(offset,edge1); var v = Vector3.Dot(ray.direction,q)*inverse;
                if (v < -1e-6f || u+v > 1+1e-6f) continue;
                var distance = Vector3.Dot(edge2,q)*inverse;
                if (distance >= 0 && float.IsFinite(distance)) hits.Add(distance);
            }
        }
        public static bool Midpoint(Ray worldRay, List<float> hits, out Vector3 point)
        {
            point = default; hits.Sort(); var distinct = new List<float>();
            foreach(var hit in hits) if (distinct.Count == 0 || Math.Abs(hit-distinct[distinct.Count-1]) > .0001f) distinct.Add(hit);
            if (distinct.Count != 2 || distinct[1]-distinct[0] < .0001f) return false;
            point = worldRay.GetPoint((distinct[0]+distinct[1])*.5f); return true;
        }
    }
}
