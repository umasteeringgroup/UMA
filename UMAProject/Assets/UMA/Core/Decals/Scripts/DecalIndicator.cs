using System;
using UnityEngine;

namespace UMA
{
    public class DecalIndicator : MonoBehaviour
    {
#if UNITY_EDITOR
        public Ray Ray;
        public Vector3 LocalEuler;
        public GameObject visualPlane;
        public GameObject visualCube;

        public Plane UVPlane;
        public GameObject U1;
        public GameObject U2;
        public GameObject V1;
        public GameObject V2;
        public GameObject Front;
        public GameObject Back;


        // Start is called before the first frame update
        void Start()
        {
            UVPlane = CalculatePlane(visualPlane);
        }

        // Update is called once per frame
        void Update()
        {

        }

        Plane CalculatePlane(GameObject go)
        {
            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf != null)
            {
                int vertcount = mf.sharedMesh.vertexCount;
                int size = Convert.ToInt32(Mathf.Sqrt(vertcount));

                Vector3 v1 = mf.sharedMesh.vertices[0];
                Vector3 v2 = mf.sharedMesh.vertices[1];
                Vector3 v3 = mf.sharedMesh.vertices[size + 1];

                v1 = gameObject.transform.localToWorldMatrix * v1;
                v2 = gameObject.transform.localToWorldMatrix * v2;
                v3 = gameObject.transform.localToWorldMatrix * v3;

                Plane p = new Plane(v1, v2, v3);
                return p;
            }
            throw new Exception("Unable to calc plane.");
        }
#endif
    }
}