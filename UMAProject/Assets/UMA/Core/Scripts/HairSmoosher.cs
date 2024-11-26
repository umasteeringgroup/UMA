using System.Collections.Generic;
using UMA;
using UMA.CharacterSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

#if false
public class HairSmoosher : MonoBehaviour
{
    // These are temporary until we can integrate this
    // into the workflow.
    private SlotDataAsset HairToSmoosh;
    private SlotDataAsset HairPlane;
    private SlotDataAsset HeadSlot;
    public bool invertX;
    public bool invertY;
    public bool invertZ;
    public bool invertDist;
#if UNITY_EDITOR
    [InspectorButton("OnButtonClicked")]
#endif
    public bool forceRebuild;
    public enum SmooshMode { ToCenter, Raycast, Physics };
    public float SmooshDistance = 0.01f;
    public float OverSmoosh = 0.02f;
    public bool enableCaching = false;
    public string LastSmoosh;
    public SmooshMode Mode;
    [Range(0, 5000)]
    public int ShowVertex = -1;
    public Vector3 theShownVert = new Vector3();

    public static Dictionary<string, Vector3[]> cachedSmooshes = new Dictionary<string, Vector3[]>();

    /// <summary>
    /// Forces the character to rebuild and re-smoosh the hair.
    /// Ignore the zero references. It really is referenced, in the attribute above.
    /// </summary>
    private void OnButtonClicked()
    {
        DynamicCharacterAvatar dca = this.gameObject.GetComponent<DynamicCharacterAvatar>();
        if (dca != null)
        {
            dca.GenerateNow();
        }
    }

    public System.Diagnostics.Stopwatch StartTimer()
    {
        Debug.Log("Timer started at " + Time.realtimeSinceStartup + " Sec");
        System.Diagnostics.Stopwatch st = new System.Diagnostics.Stopwatch();
        st.Start();

        return st;
    }

    public void StopTimer(System.Diagnostics.Stopwatch st, string Status)
    {
        st.Stop();
        LastSmoosh = Status + " Completed " + st.ElapsedMilliseconds + "ms";
        //Debug.Log(Status + " Completed " + st.ElapsedMilliseconds + "ms");
        return;
    }

    public void BeforeBuild(UMAData umaData)
    {
        // find the Hair slot in the SlotDataList.
        // process all verts in the SlotDataList.
        // Set override vertexes.
        Debug.Log("Smoosher called");

        SlotDataAsset hair = null;
        SlotDataAsset clip = HairPlane;

        int slotCount = umaData.GetSlotArraySize();

        for (int i = 0; i < slotCount; i++)
        {
            var slot = umaData.GetSlot(i);
            if (slot.HasTag("SmooshTarget"))
            {
                HeadSlot = slot.asset;
            }
            if (HairToSmoosh != null && slot != null && slot.slotName == HairToSmoosh.slotName)
            {
                hair = slot.asset;
            }
            else if (slot.HasTag("smooshable"))
            {
                hair = slot.asset;
            }
            else if (slot.HasTag("smooshclip"))
            {
                clip = slot.asset;
            }
        }

        if (hair != null)
        {
            HairToSmoosh = hair;
            Debug.Log("Smooshing selected slot: " + hair.slotName);
            var st = StartTimer();
            if (Mode == SmooshMode.Physics)
            {
                SmooshSlotPhysics(umaData, hair, clip, HeadSlot);
            }
            else
            {
                SmooshSlot(umaData, hair, clip, HeadSlot);
            }
            StopTimer(st, "Smooshing slot complete");
        }
    }

    private bool PointInTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 p)
    {
        Vector3 d, e;
        double w1, w2;
        d = b - a;
        e = c - a;

        if (Mathf.Approximately(e.y, 0))
        {
            e.y = 0.0001f;
        }

        w1 = ((e.x * (a.y - p.y)) + (e.y * (p.x - a.x))) / ((d.x * e.y) - (d.y * e.x));
        w2 = (p.y - a.y - (w1 * d.y)) / e.y;
        return (w1 >= 0f) && (w2 >= 0.0) && ((w1 + w2) <= 1.0);
    }

    public Vector3 GetDestVert(Vector3 vertex, Vector3 center, float PlaneDist, SlotDataAsset SmooshTarget)
    {
        Vector3 result = new Vector3();
        if (Mode == SmooshMode.ToCenter || HeadSlot == null)
        {
            return center;
        }

        result.Set(vertex.x, vertex.y, vertex.z);
        float distance = Vector3.Distance(center, vertex);

        SubMeshTriangles smt = SmooshTarget.meshData.submeshes[HeadSlot.subMeshIndex];

        int[] tris = smt.getBaseTriangles();
        Vector3[] verts = SmooshTarget.meshData.vertices;
        int tricount = tris.Length / 3;

        Plane p = new Plane();

        Vector3 direction = center - vertex;
        direction.Normalize();

        Ray r = new Ray(vertex, direction);

        for (int tri = 0; tri < tricount; tri++)
        {
            int baseTri = tri * 3;
            Vector3 v1 = verts[tris[baseTri]];
            Vector3 v2 = verts[tris[baseTri + 1]];
            Vector3 v3 = verts[tris[baseTri + 2]];

            try
            {
                p.Set3Points(v1, v2, v3);
            }
            catch (System.Exception ex)
            {
                Debug.Log("Exception: " + ex.Message);
            }
            //Initialise the enter variable
            float enter = 0.0f;

            if (p.Raycast(r, out enter))
            {
                //Get the point that is clicked
                Vector3 hitPoint = r.GetPoint(enter);
                if (PointInTriangle(v1, v2, v3, hitPoint))
                {
                    float vertdistance = (hitPoint - vertex).magnitude;
                    if (vertdistance < distance)
                    {
                        distance = vertdistance;
                        float newSmooshDistance = SmooshDistance;
                        // Smooth smooshing
                        if (distance > 0.0f)
                        {
                            newSmooshDistance = SmooshDistance + (SmooshDistance * (distance / OverSmoosh));
                        }
                        Vector3 newLocation = hitPoint + (direction * (0 - newSmooshDistance));
                        result.Set(newLocation.x, newLocation.y, newLocation.z);
                    }
                }
            }
#if false
            Vector3 v = p.ClosestPointOnPlane(vertex);
#endif

        }
        return result;
    }

    public Vector3 GetDestVertPhys(Vector3 vertex, Vector3 center, float PlaneDist, SlotDataAsset SmooshTarget, PhysicsScene ps, int vertindex)
    {
        Vector3 result = new Vector3();
        if (Mode == SmooshMode.ToCenter || HeadSlot == null)
        {
            return center;
        }

        Vector3 AlternateCenter = new Vector3(center.x, center.y + 0.001f, center.z);
        result.Set(vertex.x, vertex.y, vertex.z);
        float distance = Vector3.Distance(center, vertex);

        Vector3 direction = (center - vertex).normalized;
        if (ps.Raycast(vertex, direction, out RaycastHit hit, 5.0f, -5, QueryTriggerInteraction.Ignore))
        {
            // todo: offset by smoosh distance
            float vertdistance = (hit.point - vertex).magnitude;
            if (vertdistance < distance)
            {
                distance = vertdistance;
                float newSmooshDistance = SmooshDistance;
                // Smooth smooshing
                if (distance > 0.0f)
                {
                    newSmooshDistance = SmooshDistance + (SmooshDistance * (distance / OverSmoosh));
                }
                Vector3 newLocation = hit.point + (direction * (0 - newSmooshDistance));
                result.Set(newLocation.x, newLocation.y, newLocation.z);
            }
            else
            {
                result.Set(hit.point.x, hit.point.y, hit.point.z);
            }
        }
        else
        {
            result.Set(center.x, center.y, center.z);
        }

        return result;
    }

    public void SmooshSlotPhysics(UMAData umaData, SlotDataAsset SmooshMe, SlotDataAsset SmooshPlane, SlotDataAsset SmooshTarget)
    {
        if (SmooshMe == null || SmooshPlane == null || SmooshTarget == null)
        {
            return;
        }
        Mesh m = new Mesh();

        if (umaData.VertexOverrides.ContainsKey(SmooshTarget.slotName))
        {
            m.SetVertices(umaData.VertexOverrides[SmooshTarget.slotName]);
        }
        else
        {
            m.SetVertices(SmooshTarget.meshData.GetVertices());
        }
        m.SetTriangles(SmooshTarget.meshData.submeshes[0].getBaseTriangles(), 0);
        CreateSceneParameters csp = new CreateSceneParameters(LocalPhysicsMode.Physics3D);
        Scene S = SceneManager.CreateScene("SmooshScene", csp);
        GameObject go = new GameObject();
        try
        {
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = m;
            SceneManager.MoveGameObjectToScene(go, S);
            PhysicsScene physicsScene = S.GetPhysicsScene();

            var a = SmooshPlane.meshData.vertices[0];
            var b = SmooshPlane.meshData.vertices[1];
            var c = SmooshPlane.meshData.vertices[2];

            if (invertY)
            {
                a.y = -a.y;
                b.y = -b.y;
                c.y = -c.y;
            }
            if (invertX)
            {
                a.x = -a.x;
                b.x = -b.x;
                c.x = -c.x;
            }

            Plane p = new Plane(a, b, c);
            Vector3 center = (a + b + c) / 3;

            Vector3[] newVerts = new Vector3[SmooshMe.meshData.vertices.Length];

            Vector3[] sourceVertexes = SmooshMe.meshData.vertices;

            if (umaData.VertexOverrides.ContainsKey(SmooshMe.slotName))
            {
                sourceVertexes = umaData.VertexOverrides[SmooshMe.slotName];
            }

            for (int i = 0; i < newVerts.Length; i++)
            {
                Vector3 currentVert = sourceVertexes[i];

                float dist = p.GetDistanceToPoint(currentVert);
                if (invertDist)
                {
                    dist *= -1;
                }

                if (dist > -OverSmoosh)
                {
                    newVerts[i] = GetDestVertPhys(currentVert, center, dist, SmooshTarget, physicsScene, i);
                }
                else
                {
                    newVerts[i] = currentVert;
                }
            }
            umaData.AddVertexOverride(SmooshMe, newVerts);
        }
        finally
        {
            //  Cleanup
            SceneManager.UnloadSceneAsync(S);
            GameObject.Destroy(m);
        }
    }

    public void SmooshSlot(UMAData umaData, SlotDataAsset SmooshMe, SlotDataAsset SmooshPlane, SlotDataAsset SmooshTarget)
    {

        string key = SmooshMe.slotName + "*" + SmooshPlane.slotName;
        if (enableCaching)
        {
            if (cachedSmooshes.ContainsKey(key))
            {
                Debug.Log("Used cached smoosh");
                umaData.AddVertexOverride(SmooshMe, cachedSmooshes[key]);
                return;
            }
        }

        var a = SmooshPlane.meshData.vertices[0];
        var b = SmooshPlane.meshData.vertices[1];
        var c = SmooshPlane.meshData.vertices[2];

        Vector3[] newVerts = new Vector3[SmooshMe.meshData.vertices.Length];

        if (invertY)
        {
            a.y = -a.y;
            b.y = -b.y;
            c.y = -c.y;
        }
        if (invertX)
        {
            a.x = -a.x;
            b.x = -b.x;
            c.x = -c.x;
        }

        Plane p = new Plane(a, b, c);
        Vector3 center = (a + b + c) / 3;


        for (int i = 0; i < newVerts.Length; i++)
        {
            Vector3 currentVert = SmooshMe.meshData.vertices[i];
            //  if (invertX) currentVert.x = -currentVert.x;
            //  if (invertY) currentVert.y = -currentVert.y;
            //  if (invertZ) currentVert.z = -currentVert.z;

            float dist = p.GetDistanceToPoint(currentVert);
            if (invertDist)
            {
                dist *= -1;
            }

            if (dist > -OverSmoosh)
            {
                newVerts[i] = GetDestVert(currentVert, center, dist, SmooshTarget);
            }
            else
            {
                newVerts[i] = currentVert;
            }
        }
        umaData.AddVertexOverride(SmooshMe, newVerts);
        if (enableCaching)
        {
            if (cachedSmooshes.ContainsKey(key) == false)
            {
                cachedSmooshes.Add(key, newVerts);
            }
            else
            {
                cachedSmooshes[key] = newVerts;
            }
        }
    }
}
#endif