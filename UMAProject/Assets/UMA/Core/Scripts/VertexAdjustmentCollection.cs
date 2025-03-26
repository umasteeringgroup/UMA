using System;
using System.Collections;
using System.Collections.Generic;
using UMA;
using Unity.Collections;
using UnityEngine;

#if UMA_BURSTCOMPILE
using Unity.Burst;
#endif

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
    public enum VertexAdjustmentGizmo
    {
        Move, Rotate, Scale, None
    }

    #region vertexAdjustments
    [Serializable]
    public abstract class VertexAdjustment
    {
        public int vertexIndex;
        public float weight;
        public string _name;
 
        public abstract string Name { get; }
        public abstract VertexAdjustmentCollection VertexAdjustmentCollection { get; }

        public abstract VertexAdjustment ShallowCopy();

        public abstract void Apply(MeshDetails mesh, MeshDetails src);

        public abstract void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale);


        public VertexAdjustment()
        {
            weight = 1.0f;
            _name = Name;
#if UNITY_EDITOR
            active = false;
#endif
        }

#if UNITY_EDITOR
        public bool active;
        public string slotName;
        public abstract bool DoGUI();
        public abstract VertexAdjustmentGizmo Gizmo { get; }
        public abstract void Init(UMAMeshData meshData);
        public virtual void CopyFrom(VertexAdjustment other)
        {
            weight = other.weight;
        }        
        public abstract string ToJson();

#endif
        // List of all classes that inherit from VertexAdjustment
        private List<Type> vertexAdjustmentTypes = null;
        public List<Type> VertexAdjustmentTypes
        {
            get
            {
                if (vertexAdjustmentTypes == null)
                {
                    vertexAdjustmentTypes = GetVertexAdjustmentTypes();
                }
                return vertexAdjustmentTypes;
            }
        }

        public static List<Type> GetVertexAdjustmentTypes()
        {
            List<Type> types = new List<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsSubclassOf(typeof(VertexAdjustment)))
                    {
                        types.Add(type);
                    }
                }
            }
            return types;
        }

        public static VertexAdjustment CreateVertexAdjustment(Type type)
        {
            return Activator.CreateInstance(type) as VertexAdjustment;
        }

        private static List<VertexAdjustment>  adjustmentTypes = new List<VertexAdjustment>();

        public static List<VertexAdjustment> AdjustmentTypes
        {
            get
            {
                if (adjustmentTypes.Count == 0)
                {
                    foreach (var type in GetVertexAdjustmentTypes())
                    {
                        adjustmentTypes.Add(CreateVertexAdjustment(type));
                    }
                }
                return adjustmentTypes;
            }
        }

        //  This is a bit of a hack to allow us to create a new VertexAdjustment from a JSON string.
        public static VertexAdjustment FromJSON(string json)
        {
            var baseValue = JsonUtility.FromJson<VertexAdjustmentDummy>(json);

            if (baseValue == null)
            {
                Debug.LogError("Could not create base value from json : "+json);
                return null;
            }

            foreach (var vtype in AdjustmentTypes)
            {
                if (vtype.Name == baseValue.Name)
                {
                    VertexAdjustment value = (VertexAdjustment)JsonUtility.FromJson(json, vtype.GetType());
                    return value;
                }
            }
            Debug.LogError("Could not find type " + baseValue.Name);
            // ? How did we get here?
            // Dunno. Return null.
            return null;
        }
    }


    [Serializable]
    public class VertexAdjustmentDummy : VertexAdjustment
    {
        public override string Name
        {
            get
            {
                return _name;
            }
        }

        public override VertexAdjustmentCollection VertexAdjustmentCollection
        {
            get
            {
                return null;
            }
        }
#if UNITY_EDITOR
        public override VertexAdjustmentGizmo Gizmo
        {
            get { return VertexAdjustmentGizmo.None; }
        }
#endif
        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            throw new NotImplementedException();
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            throw new NotImplementedException();
        }

#if UNITY_EDITOR
        public override bool DoGUI()
        {
            return false;
        }

        public override void Init(UMAMeshData meshData)
        {
             
        }
#endif
        public override VertexAdjustment ShallowCopy()
        {
            return null;
        }
#if UNITY_EDITOR
        public override string ToJson()
        {
            return "";    
        }
#endif
    }



    [Serializable]
    public class VertexColorAdjustment : VertexAdjustment
    {
        public Color32 color;

        override public void Apply(MeshDetails mesh, MeshDetails src)
        {
            if (weight != 1.0f)
            {
                ApplyScaled(mesh, src, 1.0f);
            }
            else
            {
                mesh.colors32[vertexIndex] = color;
            }
        }

        override public void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            scale *= weight;
            Color startColor = mesh.colors32[vertexIndex];
            Color newColor = color;
            Color lerpColor = Color.Lerp(startColor, newColor, scale);
            mesh.colors32[vertexIndex] = lerpColor;
        }

#if UMA_BURSTCOMPILE
		[BurstCompile]
#endif
        public static void Apply(MeshDetails mesh, MeshDetails original, List<VertexAdjustment> adjustments)
        {
            if (!mesh.colors32Modified)
            {
                if (original.colors32 == null || mesh.colors32.Length == 0)
                {
                    mesh.colors32 = new Color32[original.vertices.Length];
                }
                else
                {
                    mesh.colors32 = (Color32[])original.colors32.Clone();
                }
                mesh.colors32Modified = true;
            }
            for (int i = 0; i < adjustments.Count; i++)
            {
                adjustments[i].Apply(mesh, original);
                // mesh.colors32[adjustments[i].vertexIndex] = (adjustments[i] as VertexColorAdjustment).color;
            }
        }
#if UMA_BURSTCOMPILE
		[BurstCompile]
#endif
        public static void ApplyScaled(MeshDetails mesh, MeshDetails original, List<VertexAdjustment> adjustments, float scale)
        {    
            if (!mesh.colors32Modified)
            {
                if (original.colors32 == null || mesh.colors32.Length == 0)
                {
                    mesh.colors32 = new Color32[original.vertices.Length];
                }
                else
                {
                    mesh.colors32 = (Color32[])original.colors32.Clone();
                }
                mesh.colors32Modified = true;
            }
            for (int i = 0; i < adjustments.Count; i++)
            {
                adjustments[i].ApplyScaled(mesh, original, scale);
/*                int vertIndex = adjustments[i].vertexIndex;
                Color startColor = mesh.colors32[vertIndex];
                Color newColor = (adjustments[vertIndex] as VertexColorAdjustment).color;
                Color lerpColor = Color.Lerp(startColor, newColor, scale);
                mesh.colors32[vertIndex] = lerpColor; */
            }
        }

        public override string Name
        {
            get
            {
                return "Set Color";
            }
        }

        public override VertexAdjustmentCollection VertexAdjustmentCollection 
        { 
            get
            {
                return new VertexColorAdjustmentCollection();
            }
        }

        public override VertexAdjustment ShallowCopy()
        {
            return new VertexColorAdjustment()
            {
                vertexIndex = vertexIndex,
                weight = weight,
                color = color
#if UNITY_EDITOR
                ,
                slotName = slotName
#endif
            };
        }
#if UNITY_EDITOR
        public override bool DoGUI()
        {
            EditorGUI.BeginChangeCheck();
            weight = EditorGUILayout.Slider("Weight", weight, 0.0f, 1.0f);
            color = EditorGUILayout.ColorField("Color", color);
            return EditorGUI.EndChangeCheck();
        }

        public override string ToJson()
        {
            return JsonUtility.ToJson(this);
        }

        override public void CopyFrom(VertexAdjustment other)
        {
            base.CopyFrom(other);
            color = (other as VertexColorAdjustment).color;
            weight = other.weight;
        }

        public override VertexAdjustmentGizmo Gizmo
        {
            get
            {
                return VertexAdjustmentGizmo.None;
            }
        }

        public override void Init(UMAMeshData meshData)
        {
            if (meshData.colors32 != null)
            {
                if (meshData.colors32.Length > vertexIndex)
                {
                    color = meshData.colors32[vertexIndex];
                }
            }
        }

#endif
    }

    [Serializable]
    public class VertexDeltaAdjustment : VertexAdjustment
    {
        public Vector3 delta;

        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            if (weight != 1.0f)
            {
                ApplyScaled(mesh, src, 1.0f);
            }
            else
            {
                mesh.vertices[vertexIndex] += delta;
            }
        }

        override public void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            scale *= weight;
            mesh.vertices[vertexIndex] += (delta * scale);
        }

        public static void Apply(MeshDetails mesh, MeshDetails src, List<VertexAdjustment> adjustments)
        {
            if (!mesh.verticesModified)
            {
                mesh.vertices = (Vector3[])src.vertices.Clone();
                mesh.verticesModified = true;
            }
            for (int i = 0; i < adjustments.Count; i++)
            {
                adjustments[i].Apply(mesh, src);
                //mesh.vertices[adjustments[i].vertexIndex] += (adjustments[i] as VertexDeltaAdjustment).delta;
            }
        }
        public static void ApplyScaled(MeshDetails mesh, MeshDetails src, List<VertexAdjustment> adjustments, float scale)
        {
            if (!mesh.verticesModified)
            {
                mesh.vertices = (Vector3[])src.vertices.Clone();
                mesh.verticesModified = true;
            }
            for (int i = 0; i < adjustments.Count; i++)
            {
                adjustments[i].ApplyScaled(mesh, src, scale);
                // mesh.vertices[adjustments[i].vertexIndex] += ((adjustments[i] as VertexDeltaAdjustment).delta * scale);
            }
        }

        public override string Name
        {
            get
            {
                return "Delta Movement";
            }
        }

        public override VertexAdjustmentCollection VertexAdjustmentCollection
        {
            get
            {
                return new VertexDeltaAdjustmentCollection();
            }
        }

        public override VertexAdjustment ShallowCopy()
        {
            return new VertexDeltaAdjustment()
            {
                vertexIndex = vertexIndex,
                weight = weight,
                delta = delta
#if UNITY_EDITOR
                ,
                slotName = slotName
#endif
            };
        }

#if UNITY_EDITOR

        public override string ToJson()
        {
            return JsonUtility.ToJson(this);
        }
        public override bool DoGUI()
        {
            EditorGUI.BeginChangeCheck();
            weight = EditorGUILayout.Slider("Weight", weight, 0.0f, 1.0f);
            delta = EditorGUILayout.Vector3Field("Delta", delta);
            return EditorGUI.EndChangeCheck();
        }

        override public void CopyFrom(VertexAdjustment other)
        {
            base.CopyFrom(other);
            delta = (other as VertexDeltaAdjustment).delta;
            weight = other.weight;
        }

        public override VertexAdjustmentGizmo Gizmo
        {
            get
            {
                return VertexAdjustmentGizmo.Move;
            }
        }

        public override void Init(UMAMeshData meshData)
        {
            delta = Vector3.zero;
        }

#endif


    }

    [Serializable]
    public class VertexScaleAdjustment : VertexAdjustment
    {
        public float scale;
#if UNITY_EDITOR
        public Vector3 basePos;
        public bool basePosSet = false;
#endif
        override public void Apply(MeshDetails mesh, MeshDetails src)
        {
            if (weight != 1.0f)
            {
                ApplyScaled(mesh, src, 1.0f);
            }
            else
            {
                mesh.vertices[vertexIndex] += mesh.normals[vertexIndex] * scale;
            }
        }

        override public void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            scale *= weight;
            mesh.vertices[vertexIndex] += mesh.normals[vertexIndex] * (this.scale * scale);
        }

        public static void Apply(MeshDetails mesh, MeshDetails src, List<VertexAdjustment> adjustments)
        {
            if (!mesh.verticesModified)
            {
                mesh.vertices = (Vector3[])src.vertices.Clone();
                mesh.verticesModified = true;
            }
            for (int i = 0; i < adjustments.Count; i++)
            {
                adjustments[i].Apply(mesh, src);
                //int vertIndex = adjustments[i].vertexIndex;
                // mesh.vertices[vertIndex] += mesh.normals[vertIndex] * (adjustments[i] as VertexScaleAdjustment).scale;
            }
        }
        public static void ApplyScaled(MeshDetails mesh, MeshDetails src, List<VertexAdjustment> adjustments, float scale)
        {
            if (!mesh.verticesModified)
            {
                mesh.vertices = (Vector3[])src.vertices.Clone();
                mesh.verticesModified = true;
            }
            for (int i = 0; i < adjustments.Count; i++)
            {
                adjustments[i].ApplyScaled(mesh, src, scale);
//                int vertIndex = adjustments[i].vertexIndex;
//                mesh.vertices[vertIndex] += mesh.normals[vertIndex] * ((adjustments[i] as VertexScaleAdjustment).scale * scale);
            }
        }

        public override string Name
        {
            get
            {
                return "Scale Along Normal";
            }
        }

        public override VertexAdjustmentCollection VertexAdjustmentCollection
        {
            get
            {
                return new VertexScaleAdjustmentCollection();
            }
        }

        public override VertexAdjustment ShallowCopy()
        {
            return new VertexScaleAdjustment()
            {
                vertexIndex = vertexIndex,
                weight = weight,
                scale = scale
#if UNITY_EDITOR
                ,
                slotName = slotName
#endif
            };
        }

#if UNITY_EDITOR

        public override string ToJson()
        {
            return JsonUtility.ToJson(this);
        }
        public override bool DoGUI()
        {
            EditorGUI.BeginChangeCheck();
            weight = EditorGUILayout.Slider("Weight", weight, 0.0f, 1.0f);
            scale = EditorGUILayout.FloatField("Scale", scale);
            return EditorGUI.EndChangeCheck();
        }

        override public void CopyFrom(VertexAdjustment other)
        {
            base.CopyFrom(other);
            scale = (other as VertexScaleAdjustment).scale;
            weight = other.weight;
        }

        public override VertexAdjustmentGizmo Gizmo
        {
            get
            {
                return VertexAdjustmentGizmo.Scale;
            }
        }

        public override void Init(UMAMeshData meshData)
        {
            scale = 1.0f;
        }

#endif


    }

    [Serializable]
    public class VertexNormalAdjustment : VertexAdjustment
    {
        public Vector3 normal;
        public Vector3 tangent;
#if UNITY_EDITOR
        public Vector3 bakedNormal;
        public bool bakedNormalSet = false;
#endif

        override public void Apply(MeshDetails mesh, MeshDetails src)
        {
            if (weight != 1.0f)
            {
                ApplyScaled(mesh, src, 1.0f);
            }
            else
            {
                mesh.normals[vertexIndex] = rotation * normal;
                mesh.tangents[vertexIndex] = rotation * tangent;
            }
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            scale *= weight;
            Vector3 startNormal = mesh.normals[vertexIndex];
            Vector3 newNormal = rotation * normal;
            Vector3 lerpNormal = Vector3.Lerp(startNormal, newNormal, scale);
            mesh.normals[vertexIndex] = lerpNormal;

            Vector3 startTangent = mesh.tangents[vertexIndex];
            Vector3 newTangent = rotation * tangent;
            Vector3 lerpTangent = Vector3.Lerp(startTangent, newTangent, scale);
            mesh.tangents[vertexIndex] = new Vector4(lerpTangent.x, lerpTangent.y, lerpTangent.z, 1);
        }

        public Quaternion rotation;
        public static void Apply(MeshDetails mesh, MeshDetails src, List<VertexAdjustment> adjustments)
        {
            if (!mesh.normalsModified)
            {
                mesh.normals = (Vector3[])src.normals.Clone();
                mesh.normalsModified = true;
            }
            if (!mesh.tangentsModified)
            {
                mesh.tangents = (Vector4[])src.tangents.Clone();
                mesh.tangentsModified = true;
            }
            for (int i = 0; i < adjustments.Count; i++)
            {
                adjustments[i].Apply(mesh, src);
                //int vertIndex = adjustments[i].vertexIndex;
                //VertexNormalAdjustment van = adjustments[i] as VertexNormalAdjustment;
                //mesh.normals[vertIndex] = van.rotation * van.normal;
                //mesh.tangents[vertIndex] = van.rotation * van.tangent;
            }
        }

        public static void ApplyScaled(MeshDetails mesh, MeshDetails src, List<VertexAdjustment> adjustments, float scale)
        {            
            if (!mesh.normalsModified)
            {
                mesh.normals = (Vector3[])src.normals.Clone();
                mesh.normalsModified = true;
            }
            if (!mesh.tangentsModified)
            {
                mesh.tangents = (Vector4[])src.tangents.Clone();
                mesh.tangentsModified = true;
            }

            for (int i = 0; i < adjustments.Count; i++)
            {
                adjustments[i].ApplyScaled(mesh, src, scale);
                /*VertexNormalAdjustment van = adjustments[i] as VertexNormalAdjustment;
                int vertIndex = adjustments[i].vertexIndex;
                Vector3 startNormal = mesh.normals[vertIndex];
                Vector3 newNormal = van.rotation * van.normal;
                Vector3 lerpNormal = Vector3.Lerp(startNormal, newNormal, scale);
                mesh.normals[vertIndex] = lerpNormal;
                Vector3 startTangent = mesh.tangents[vertIndex];
                Vector3 newTangent = van.rotation * van.tangent;
                Vector3 lerpTangent = Vector3.Lerp(startTangent, newTangent, scale);
                mesh.tangents[vertIndex] = new Vector4(lerpTangent.x, lerpTangent.y, lerpTangent.z, 1);*/
            }
        }

        public override string Name
        {
            get
            {
                return "Rotate Normal";
            }
        }

        public override VertexAdjustmentCollection VertexAdjustmentCollection
        {
            get
            {
                return new VertexNormalAdjustmentCollection();
            }
        }

        public override VertexAdjustment ShallowCopy()
        {
            return new VertexNormalAdjustment()
            {
                vertexIndex = vertexIndex,
                weight = weight,
                normal = normal,
                tangent = tangent
#if UNITY_EDITOR
                ,
                slotName = slotName
#endif
            };
        }

#if UNITY_EDITOR

        public override string ToJson()
        {
            return JsonUtility.ToJson(this);
        }
        public override bool DoGUI()
        {
            EditorGUI.BeginChangeCheck();
            weight = EditorGUILayout.Slider("Weight", weight, 0.0f, 1.0f);

           /* normal = EditorGUILayout.Vector3Field("Base Normal", normal);
            tangent = EditorGUILayout.Vector3Field("Base Tangent", tangent); */

            Vector3 orient = rotation.eulerAngles;
            orient  = EditorGUILayout.Vector3Field("Rotation", orient);
            rotation = Quaternion.Euler(orient);
            return EditorGUI.EndChangeCheck();
        }

        public override void CopyFrom(VertexAdjustment other)
        {
            base.CopyFrom(other);
            rotation = (other as VertexNormalAdjustment).rotation;
            weight = other.weight;
        }
        public override VertexAdjustmentGizmo Gizmo
        {
            get
            {
                return VertexAdjustmentGizmo.Rotate;
            }
        }

        public override void Init(UMAMeshData meshData)
        {
            normal = meshData.normals[vertexIndex];
            tangent = meshData.tangents[vertexIndex];
            // create a quaterion to rotate the normal
            rotation = Quaternion.identity;
        }

        public void SetRotation(Quaternion rot)
        {
            rotation = rot;
        }
#endif
    }

    [Serializable]
    public class VertexUVAdjustment : VertexAdjustment
    {
        public Vector2 uv;

        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            if (weight != 1.0f)
            {
                ApplyScaled(mesh, src, 1.0f);
            }
            else
            {
                mesh.uv[vertexIndex] = uv;
            }
        }

        override public void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            scale *= weight;
            Vector2 startUV = mesh.uv[vertexIndex];
            Vector2 newUV = uv;
            Vector2 lerpUV = Vector2.Lerp(startUV, newUV, scale);
            mesh.uv[vertexIndex] = lerpUV;
        }

        public static void Apply(MeshDetails mesh, MeshDetails src, List<VertexAdjustment> adjustments)
        {
            if (!mesh.uvModified)
            {
                mesh.uv = (Vector2[])src.uv.Clone();
                mesh.uvModified = true;
            }
            for (int i = 0; i < adjustments.Count; i++)
            {
                adjustments[i].Apply(mesh, src);
                //mesh.uv[adjustments[i].vertexIndex] = (adjustments[i] as VertexUVAdjustment).uv;
            }
        }
        public static void ApplyScaled(MeshDetails mesh, MeshDetails src, List<VertexAdjustment> adjustments, float scale)
        {
            if (!mesh.uvModified)
            {
                mesh.uv = (Vector2[])src.uv.Clone();
                mesh.uvModified = true;
            }
            for (int i = 0; i < adjustments.Count; i++)
            {
                adjustments[i].ApplyScaled(mesh, src, scale);
/*                int vertIndex = adjustments[i].vertexIndex;
                Vector2 startUV = mesh.uv[vertIndex];
                Vector2 newUV = (adjustments[vertIndex] as VertexUVAdjustment).uv;
                Vector2 lerpUV = Vector2.Lerp(startUV, newUV, scale);
                mesh.uv[vertIndex] = lerpUV; */
            }
        }

        public override string Name
        {
            get
            {
                return "Set UV";
            }
        }

        public override VertexAdjustmentCollection VertexAdjustmentCollection
        {
            get
            {
                return new VertexUVAdjustmentCollection();
            }
        }

        public override VertexAdjustment ShallowCopy()
        {
            return new VertexUVAdjustment()
            {
                vertexIndex = vertexIndex,
                weight = weight,
                uv = uv
#if UNITY_EDITOR
                ,
                slotName = slotName
#endif
            };
        }

#if UNITY_EDITOR

        public override string ToJson()
        {
            return JsonUtility.ToJson(this);
        }

        public override bool DoGUI()
        {
            EditorGUI.BeginChangeCheck();
            weight = EditorGUILayout.Slider("Weight", weight, 0.0f, 1.0f);
            uv = EditorGUILayout.Vector2Field("UV", uv);
            return EditorGUI.EndChangeCheck();
        }

        override public void CopyFrom(VertexAdjustment other)
        {
            base.CopyFrom(other);
            uv = (other as VertexUVAdjustment).uv;
            weight = other.weight;
        }

        public override VertexAdjustmentGizmo Gizmo
        {
            get
            {
                return VertexAdjustmentGizmo.None;
            }
        }

        public override void Init(UMAMeshData meshData)
        {
            uv = meshData.uv[vertexIndex];
        }
#endif
    }

    [Serializable]
    public class VertexBlendshapeAdjustment : VertexAdjustment
    {
        public Vector3 delta;
        public Vector3 normal;
        public Vector3 tangent;

        override public void Apply(MeshDetails mesh, MeshDetails src)
        {
            if (weight != 1.0f)
            {
                ApplyScaled(mesh, src, 1.0f);
            }
            else
            {
                mesh.vertices[vertexIndex] += delta;
                mesh.normals[vertexIndex] += normal;
                mesh.tangents[vertexIndex].x += tangent.x;
                mesh.tangents[vertexIndex].y += tangent.y;
                mesh.tangents[vertexIndex].z += tangent.z;
            }
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            scale *= weight;

            mesh.vertices[vertexIndex] += (delta * scale);
            if (mesh.normals != null)
            {
                mesh.normals[vertexIndex] = mesh.normals[vertexIndex] + (normal * scale);
            }
            if (mesh.tangents != null)
            {
                mesh.tangents[vertexIndex].x += tangent.x * scale;
                mesh.tangents[vertexIndex].y += tangent.y * scale;
                mesh.tangents[vertexIndex].z += tangent.z * scale;
            }
        }

        public static void Apply(MeshDetails mesh, MeshDetails src, List<VertexAdjustment> adjustments)
        {
            bool tangents = mesh.tangents != null;
            bool normals = mesh.normals != null;

            if (!mesh.verticesModified)
            {
                mesh.vertices = (Vector3[])src.vertices.Clone();
                mesh.verticesModified = true;
            }
            if (!mesh.normalsModified && normals)
            {
                mesh.normals = (Vector3[])src.normals.Clone();
                mesh.normalsModified = true;
            }
            if (!mesh.tangentsModified && tangents)
            {
                mesh.tangents = (Vector4[])src.tangents.Clone();
                mesh.tangentsModified = true;
            }
            for (int i = 0; i < adjustments.Count; i++)
            {
                adjustments[i].Apply(mesh, src);
            }
        }
        public static void ApplyScaled(MeshDetails mesh, MeshDetails src, List<VertexAdjustment> adjustments, float scale)
        {
            bool tangents = mesh.tangents != null;
            bool normals = mesh.normals != null;

            if (!mesh.verticesModified)
            {
                mesh.vertices = (Vector3[])src.vertices.Clone();
                mesh.verticesModified = true;
            }
            if (!mesh.normalsModified && normals)
            {
                mesh.normals = (Vector3[])src.normals.Clone();
                mesh.normalsModified = true;
            }
            if (!mesh.tangentsModified && tangents)
            {
                mesh.tangents = (Vector4[])src.tangents.Clone();
                mesh.tangentsModified = true;
            }
            for (int i = 0; i < adjustments.Count; i++)
            {
                adjustments[i].ApplyScaled(mesh, src, scale);
            }
        }

        public override string Name
        {
            get
            {
                return "Vertex Blendshape";
            }
        }

        public override VertexAdjustmentCollection VertexAdjustmentCollection
        {
            get
            {
                return new VertexBlendshapeAdjustmentCollection();
            }
        }

        public override VertexAdjustment ShallowCopy()
        {
            return new VertexBlendshapeAdjustment()
            {
                vertexIndex = vertexIndex,
                weight = weight,
                delta = delta,
                normal = normal,
                tangent = tangent
#if UNITY_EDITOR
                ,
                slotName = slotName
#endif
            };
        }
#if UNITY_EDITOR

        public override string ToJson()
        {
            return JsonUtility.ToJson(this);
        }
        public override bool DoGUI()
        {
            EditorGUI.BeginChangeCheck();
            weight = EditorGUILayout.Slider("Weight", weight, 0.0f, 1.0f);
            delta = EditorGUILayout.Vector3Field("Delta", delta);
            normal = EditorGUILayout.Vector3Field("Normal", normal);
            tangent = EditorGUILayout.Vector3Field("Tangent", tangent);
            return EditorGUI.EndChangeCheck();
        }

        override public void CopyFrom(VertexAdjustment other)
        {
            base.CopyFrom(other);
            VertexBlendshapeAdjustment vba = other as VertexBlendshapeAdjustment;
            delta = vba.delta;
            normal = vba.normal;
            tangent = vba.tangent;
            weight = other.weight;
        }

        public override VertexAdjustmentGizmo Gizmo
        {
            get
            {
                return VertexAdjustmentGizmo.None;
            }
        }

        public override void Init(UMAMeshData meshData)
        {
            delta = Vector3.zero;
            normal = meshData.normals[vertexIndex];
            tangent = meshData.tangents[vertexIndex];
        }
#endif
    }

    // This is a reset adjustment, it resets the vertex to its original position, but it can also be used to store a value.
    [Serializable]
    public class VertexResetAdjustment : VertexAdjustment
    {
        public int value;
        public Vector3 initialPosition;
        public Vector3 initialNormal;    // this can be used to reset the normal to a new orientation without rotation
        public Vector3 initialTangent;   // this can be used to reset the tangent to a new orientation without rotation

        override public void Apply(MeshDetails mesh, MeshDetails src)
        {
            if (weight != 1.0f)
            {
                ApplyScaled(mesh, src, 1.0f);
            }
            else
            {
                Vector3 Normal = mesh.normals[vertexIndex];
                mesh.vertices[vertexIndex] = initialPosition;
                mesh.normals[vertexIndex] = initialNormal;
                mesh.tangents[vertexIndex] = initialTangent;
                Quaternion qt = Quaternion.FromToRotation(Normal, mesh.normals[vertexIndex]);
                Vector3 rot = qt.eulerAngles;
                Debug.Log(rot.ToString());
            }
        }

        override public void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            scale *= weight;
            Vector3 start = mesh.vertices[vertexIndex];
            Vector3 lerp = Vector3.Lerp(start, initialPosition, scale);
            Vector3 newNormal = Vector3.Lerp(mesh.normals[vertexIndex], initialNormal, scale);
            Vector3 newTangent = Vector3.Lerp(mesh.tangents[vertexIndex], initialTangent, scale);
            mesh.vertices[vertexIndex] = lerp;
            mesh.normals[vertexIndex] = newNormal;
            mesh.tangents[vertexIndex] = newTangent;
        }

        public static void Apply(MeshDetails mesh, MeshDetails src, List<VertexAdjustment> adjustments)
        {
            if (!mesh.verticesModified)
            {
                mesh.vertices = (Vector3[])src.vertices.Clone();
                mesh.verticesModified = true;
            }
            if (!mesh.normalsModified)
            {
                mesh.normals = (Vector3[])src.normals.Clone();
                mesh.normalsModified = true;
            }
            if (!mesh.tangentsModified)
            {
                if (src.tangents == null)
                {
                    mesh.tangents = new Vector4[src.vertices.Length];
                }
                else
                {
                    mesh.tangents = (Vector4[])src.tangents.Clone();
                }
                mesh.tangentsModified = true;
            }
            for (int i = 0; i < adjustments.Count; i++)
            {
                adjustments[i].Apply(mesh, src);
            }
        }

        public static void ApplyScaled(MeshDetails mesh, MeshDetails src, List<VertexAdjustment> adjustments, float scale)
        {
            if (!mesh.verticesModified)
            {
                mesh.vertices = (Vector3[])src.vertices.Clone();
                mesh.verticesModified = true;
            }
            if (!mesh.normalsModified)
            {
                mesh.normals = (Vector3[])src.normals.Clone();
                mesh.normalsModified = true;
            }
            for (int i = 0; i < adjustments.Count; i++)
            {
                adjustments[i].ApplyScaled(mesh, src, scale);
            }
        }

        public override string Name
        {
            get
            {
                return "Position Reset";
            }
        }

        public override VertexAdjustmentCollection VertexAdjustmentCollection
        {
            get
            {
                return new VertexResetAdjustmentCollection();
            }
        }

        public override VertexAdjustment ShallowCopy()
        {
            VertexResetAdjustment vr = new VertexResetAdjustment();
            vr.vertexIndex = vertexIndex;
            vr.initialNormal = initialNormal;
            vr.initialPosition = initialPosition;
            vr.initialTangent = initialTangent;
            vr.weight = weight;
            vr.value = value;
#if UNITY_EDITOR

            vr.slotName = slotName;
#endif
            return vr;
        }

#if UNITY_EDITOR
        public override string ToJson()
        {
            return JsonUtility.ToJson(this);
        }

        public override bool DoGUI()
        {
            EditorGUI.BeginChangeCheck();
            weight = EditorGUILayout.Slider("Weight", weight, 0.0f, 1.0f);
            value = EditorGUILayout.IntField("Value", value);
            return EditorGUI.EndChangeCheck();
        }

        override public void CopyFrom(VertexAdjustment other)
        {
            base.CopyFrom(other);
            VertexResetAdjustment vra = other as VertexResetAdjustment;
            value = vra.value;
            weight = other.weight;
        }

        public override VertexAdjustmentGizmo Gizmo
        {
            get
            {
                return VertexAdjustmentGizmo.None;
            }
        }

        public override void Init(UMAMeshData meshData)
        {
            initialPosition = meshData.vertices[vertexIndex];
            initialNormal = meshData.normals[vertexIndex];
            initialTangent = meshData.tangents[vertexIndex];
        }
#endif

    }
    #endregion

    #region VertexGroups
    public struct VertexGroupMember
    {
        public int vertexIndex;
        public float weight;
    }

    public struct VertexGroup
    {
        string Name;
        public VertexGroupMember[] members;
    }
    #endregion

    [Serializable]
    public abstract class VertexAdjustmentCollection
    {
        public virtual bool SupportWeightedAdjustments
        {
            get { return false; }
        }

        public List<VertexAdjustment> vertexAdjustments = new List<VertexAdjustment>();
        // when the asset is saved, a collection for each slot is added.
        // When the recipe is loaded, after the slotdatas are merged, 
        // the adjustments for the specific slot are added to the SlotData.
        // When the MeshCombiner builds the CombineInstances, it will process the adjustments for each slot. 
        public abstract void Apply(MeshDetails mesh, MeshDetails src);
        public abstract void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale);

        public void Add(VertexAdjustment adjustment)
        {
            vertexAdjustments.Add(adjustment);
        }

        public int Count()
        {
            return vertexAdjustments.Count;
        }

#if UNITY_EDITOR
        public abstract VertexAdjustment Create();
        public abstract string Name { get; }
        public abstract Type AdjustmentType { get; }
        public abstract string Help { get; }

        public VertexAdjustmentCollection GetCollection()
        {
            return Activator.CreateInstance(AdjustmentType) as VertexAdjustmentCollection;
        }
#endif
    }

    [Serializable]
    public class VertexColorAdjustmentCollection: VertexAdjustmentCollection
    {
        public override bool SupportWeightedAdjustments
        {
            get { return true; }
        }

        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            VertexColorAdjustment.Apply(mesh, src, vertexAdjustments);
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            VertexColorAdjustment.ApplyScaled(mesh, src, vertexAdjustments, scale);
        }

#if UNITY_EDITOR


        public override VertexAdjustment Create()
        {
            return new VertexColorAdjustment();
        }

        public override string Name
        {
            get
            {
                return "Vertex Color";
            }
        }

        public override Type AdjustmentType
        {
            get
            {
                return typeof(VertexColorAdjustment);
            }
        }

        public override string Help
        {
            get
            {
                return "This adjustment allows you to change the color of a vertex.";
            }
        }
#endif
    }

    [Serializable]
    public class VertexDeltaAdjustmentCollection : VertexAdjustmentCollection
    {

        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            if (mesh.colors32 == null)
            {
                mesh.colors32 = new Color32[src.vertices.Length];
            }

            VertexDeltaAdjustment.Apply(mesh, src, vertexAdjustments);
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            VertexDeltaAdjustment.ApplyScaled(mesh, src, vertexAdjustments, scale);
        }
    
#if UNITY_EDITOR
        public override VertexAdjustment Create()
        {
            return new VertexDeltaAdjustment();
        }

        public override string Name
        {
            get
            {
                return "Vertex Delta";
            }
        }

        public override Type AdjustmentType
        {
            get
            {
                return typeof(VertexDeltaAdjustment);
            }
        }

        public override string Help
        {
            get
            {
                return "This adjustment allows you to move the base vertex position by a delta.";
            }
        }
#endif
    }

    [Serializable]
    public class VertexScaleAdjustmentCollection : VertexAdjustmentCollection
    {
        override public bool SupportWeightedAdjustments
        {
            get { return true; }
        }


        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            VertexScaleAdjustment.Apply(mesh, src, vertexAdjustments);
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            VertexScaleAdjustment.ApplyScaled(mesh, src, vertexAdjustments, scale);
        }

#if UNITY_EDITOR

        public override VertexAdjustment Create()
        {
            return new VertexScaleAdjustment();
        }

        public override string Name
        {
            get
            {
                return "Vertex Scale";
            }
        }

        public override Type AdjustmentType
        {
            get
            {
                return typeof(VertexScaleAdjustment);
            }
        }

        public override string Help
        {
            get
            {
                return "This adjustment allows you to scale the base vertex position along the normal.";
            }
        }

#endif

    }

    [Serializable]
    public class VertexNormalAdjustmentCollection : VertexAdjustmentCollection
    {
        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            VertexNormalAdjustment.Apply(mesh, src, vertexAdjustments);
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            VertexNormalAdjustment.ApplyScaled(mesh, src, vertexAdjustments, scale);
        }
#if UNITY_EDITOR


        public override VertexAdjustment Create()
        {
            return new VertexNormalAdjustment();
        }

        public override string Name
        {
            get
            {
                return "Vertex Normal";
            }
        }

        public override Type AdjustmentType
        {
            get
            {
                return typeof(VertexNormalAdjustment);
            }
        }

        public override string Help
        {
            get
            {
                return "This adjustment allows you to change the normal and tangent of a vertex.";
            }
        }
#endif
    }

    [Serializable]
    public class VertexUVAdjustmentCollection : VertexAdjustmentCollection
    {

        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            VertexUVAdjustment.Apply(mesh, src, vertexAdjustments);
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            VertexUVAdjustment.ApplyScaled(mesh, src, vertexAdjustments, scale);
        }
#if UNITY_EDITOR


        public override VertexAdjustment Create()
        {
            return new VertexUVAdjustment();
        }

        public override string Name
        {
            get
            {
                return "Vertex UV";
            }
        }

        public override Type AdjustmentType
        {
            get
            {
                return typeof(VertexUVAdjustment);
            }
        }

        public override string Help
        {
            get
            {
                return "This adjustment allows you to change the UV of a vertex.";
            }
        }
#endif
    }

    [Serializable]
    public class VertexBlendshapeAdjustmentCollection : VertexAdjustmentCollection
    {
        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            VertexBlendshapeAdjustment.Apply(mesh, src, vertexAdjustments);
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            VertexBlendshapeAdjustment.ApplyScaled(mesh, src, vertexAdjustments, scale);
        }
#if UNITY_EDITOR
        public override VertexAdjustment Create()
        {
            return new VertexBlendshapeAdjustment();
        }

        public override string Name
        {
            get
            {
                return "Vertex Blendshape";
            }
        }

        public override Type AdjustmentType
        {
            get
            {
                return typeof(VertexBlendshapeAdjustment);
            }
        }

        public override string Help
        {
            get
            {
                return "This adjustment allows you to change the vertex position, normal and tangent of a vertex.";
            }
        }
#endif
    }

    [Serializable]
    public class VertexResetAdjustmentCollection : VertexAdjustmentCollection
    {
        public override void Apply(MeshDetails mesh, MeshDetails src)
        {
            VertexResetAdjustment.Apply(mesh, src, vertexAdjustments);
        }

        public override void ApplyScaled(MeshDetails mesh, MeshDetails src, float scale)
        {
            VertexResetAdjustment.ApplyScaled(mesh, src, vertexAdjustments, scale);
        }
#if UNITY_EDITOR

        public override VertexAdjustment Create()
        {
            return new VertexResetAdjustment();
        }

        public override string Name
        {
            get
            {
                return "Vertex User";
            }
        }

        public override Type AdjustmentType
        {
            get
            {
                return typeof(VertexResetAdjustment);
            }
        }

        public override string Help
        {
            get
            {
                return "This adjustment resets the position of a vertex to the default position based on the weight. You can also assign a generic user value to the vertex";
            }
        }
#endif
    }
}