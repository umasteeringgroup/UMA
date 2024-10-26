using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Globalization;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
    [Serializable]
    public abstract class UMAProperty 
    {
        public static string precision = "F4";
        public static string splitter =  ";" ;

        public static string vectorprecision = "{0:F4},{1:F4},{2:F4},{3:F4}";
        public static string transformprecision = "{0:F4},{1:F4},{2:F4},{3:F4},{4:F4}";
        public static char[] vectorsplitter = { ',' }; // needs to correspond to the format string above.
        public string stringRepresentation = "";

        public string name;
        public abstract void Apply(Material mpb, int overlayNumber);

        public abstract UMAProperty Clone();

        public string GetPropertyName(int overlayNumber)
        {
            if (overlayNumber < 0)
            {
                return name;
            }

            return $"{name}{overlayNumber}";
        }

        public static UMAProperty FromString(string serializedString)
        {
            char[] split = { splitter[0] };

            string[] str  = serializedString.Split(split,StringSplitOptions.RemoveEmptyEntries);

            if (str.Length < 3)
            {
                List<string> temp = new List<string>(str);
                temp.Add("0");
                temp.Add("0");
                str = temp.ToArray();
            }
            switch (str[0])
            {
                case "Float":
                    return new UMAFloatProperty() {Value = Convert.ToSingle(str[1], CultureInfo.InvariantCulture), name = str[2] };
                case "Int":
                    return new UMAIntProperty() { Value = Convert.ToInt32(str[1],CultureInfo.InvariantCulture), name = str[2] };
                case "Color":
                    Color c = Color.white;
                    ColorUtility.TryParseHtmlString(str[1], out c);

                    return new UMAColorProperty() { Value = c, name = str[2] };
                case "Vector":
                    string[] vector = str[1].Split(vectorsplitter);
                    float x = Convert.ToSingle(vector[0], CultureInfo.InvariantCulture);
                    float y = Convert.ToSingle(vector[1], CultureInfo.InvariantCulture);
                    float z = Convert.ToSingle(vector[2], CultureInfo.InvariantCulture);
                    float w = Convert.ToSingle(vector[3], CultureInfo.InvariantCulture);
                    return new UMAVectorProperty() { Value = new Vector4(x, y, z, w), name = str[2] };
                /// The rest of these are only programmable at runtime.
                case "VectorArray":
                    return new UMAVectorArrayProperty() { name = str[2] };
                case "Texture":
                    return new UMATextureProperty() { name = str[2] };
                case "FloatArray":
                    return new UMAFloatArrayProperty() { name = str[2] };
                case "Matrix":
                    return new UMAMatrixProperty() { name = str[2] };
                case "MatrixArray":
                    return new UMAMatrixArrayProperty() { name = str[2] };
                case "ComputeBuffer":
                    return new UMAComputeBufferProperty() { name = str[2] };
                case "ConstantComputeBuffer":
                    return new UMAConstantComputeBufferProperty() { name = str[2] };
                case "OverlayTransform":
                    string[] transform = str[1].Split(vectorsplitter);

                    float transformx = Convert.ToSingle(transform[0], CultureInfo.InvariantCulture);
                    float transformy = Convert.ToSingle(transform[1], CultureInfo.InvariantCulture);
                    float rotate = Convert.ToSingle(transform[2], CultureInfo.InvariantCulture);
                    float scalex = Convert.ToSingle(transform[3], CultureInfo.InvariantCulture);
                    float scaley = Convert.ToSingle(transform[4], CultureInfo.InvariantCulture);
                    return new UMAOverlayTransformProperty(new Vector2(transformx, transformy), rotate, new Vector2(scalex, scaley)) { name = str[2] };
                default:
                    Debug.LogError("Unknown Material Property type: " + str[0]);
                    break;
            }
            return null;
        }

        public override string ToString()
        {
            return "property" + splitter + name;
        }

#if UNITY_EDITOR
        public virtual bool OnGUI()
        {
            GUILayout.Label("Parameter Type: "+GetType().ToString());
            EditorGUILayout.BeginHorizontal();
            name = EditorGUILayout.DelayedTextField("Shader Property",name);
            if (GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
            {
                EditorGUILayout.EndHorizontal();
                return true;
            }
            EditorGUILayout.EndHorizontal();
            return false;
        }

        public void NoEdit()
        {
            EditorGUILayout.LabelField("Value", "Must be set programatically");
        }
#endif
    }

    [Serializable]
    public class UMAOverlayTransformProperty : UMAProperty
    {
        public Vector2 Translate;
        public float Rotate;
        public Vector2 Scale = new Vector2(1, 1);

        public UMAOverlayTransformProperty()
        {

        }

        public UMAOverlayTransformProperty(Vector2 translate, float rotate, Vector2 scale)
        {
            Translate = translate;
            Rotate = rotate;
            Scale = scale;
        }

        public override void Apply(Material mpb, int overlayNumber)
        {
            // Do nothing
        }
        public override UMAProperty Clone()
        {
            return new UMAOverlayTransformProperty(Translate, Rotate, Scale)
            {
                name = this.name
            };
        }
        public override string ToString()
        {
            return "OverlayTransform" + splitter + Translate.x.ToString(precision, CultureInfo.InvariantCulture) + "," + Translate.y.ToString(precision, CultureInfo.InvariantCulture) + "," + Rotate.ToString(precision, CultureInfo.InvariantCulture) + "," + Scale.x.ToString(precision, CultureInfo.InvariantCulture) + "," + Scale.y.ToString(precision, CultureInfo.InvariantCulture)+splitter+name;
        }

#if UNITY_EDITOR
        public override bool OnGUI()
        {
            bool retval = base.OnGUI();

            //EditorGUILayout.BeginHorizontal();
            ///Value = EditorGUILayout.FloatField("Float Value", Value);
            //EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox("Warning: translating, scaling or rotation could result in writing outside the bounds of the texture on the atlas. Be sure to use only in safe areas.", MessageType.Info);
            Rotate = EditorGUILayout.FloatField("Rotation", Rotate);
            Scale = EditorGUILayout.Vector2Field("Scale", Scale);
            EditorGUILayout.LabelField("Translation: ");
            Translate.x = EditorGUILayout.Slider("UV X Offset:", Translate.x * 100.0f, -100.0f, 100.0f) / 100.0f;
            Translate.y = EditorGUILayout.Slider("UV Y Offset:", Translate.y * 100.0f, -100.0f, 100.0f) / 100.0f;
            return retval;
        }
#endif
    }


    [Serializable]
    public class UMAFloatProperty : UMAProperty
    {
        public float Value;
        public override void Apply(Material mpb, int overlayNumber = -1)
        {
            string propName = GetPropertyName(overlayNumber);
            if (mpb.HasProperty(propName))
            {
                mpb.SetFloat(propName, Value);
            }
        }

        public override UMAProperty Clone()
        {
            return new UMAFloatProperty() { name = this.name, Value = this.Value };
        }

        public override string ToString()
        {
            return "Float" + splitter + Value.ToString(precision, CultureInfo.InvariantCulture) + splitter + name;
        }


#if UNITY_EDITOR
        public override bool OnGUI()
        {
            bool retval = base.OnGUI();
            EditorGUILayout.BeginHorizontal();
            Value = EditorGUILayout.FloatField("Float Value", Value);
            EditorGUILayout.EndHorizontal();
            return retval;
        }
#endif
    }

    [Serializable]
    public class UMAColorProperty : UMAProperty
    {
        public Color Value;
        public override void Apply(Material mpb, int overlayNumber = -1)
        {
            string propName = GetPropertyName(overlayNumber);
            if (mpb.HasProperty(propName))
            {
                mpb.SetColor(propName, Value);
            }
        }
        public override UMAProperty Clone()
        {
            return new UMAColorProperty() { name = this.name, Value = this.Value };
        }
        public override string ToString()
        {
            return "Color"+splitter+"#"+ColorUtility.ToHtmlStringRGBA(Value) + splitter + name;
        }
#if UNITY_EDITOR
        public override bool OnGUI()
        {
            bool retval = base.OnGUI();

            EditorGUILayout.BeginHorizontal();
            Value = EditorGUILayout.ColorField("Color Value", Value);
            EditorGUILayout.EndHorizontal();
            return retval;
        }
#endif

    }

    [Serializable]
    public class UMAVectorProperty : UMAProperty
    {
        public Vector4 Value;

        public void SetValue(Vector4 vector)
        {
            Value = vector;
        }

        public void SetValue(Vector3 vector)
        {
            Value.Set(vector.x, vector.y, vector.z, 0.0f);
        }

        public void SetValue(Vector2 vector)
        {
            Value.Set(vector.x, vector.y, 0.0f, 0.0f);
        }

        public override void Apply(Material mpb, int overlayNumber=-1)
        {
            string propName = GetPropertyName(overlayNumber);
            if (mpb.HasProperty(propName))
            {
                mpb.SetVector(propName, Value);
            }
        }
        public override UMAProperty Clone()
        {
            return new UMAVectorProperty() { name = this.name, Value = this.Value };
        }
        public override string ToString()
        {
            return "Vector" + splitter + string.Format(CultureInfo.InvariantCulture,vectorprecision, Value.x, Value.y, Value.z, Value.w) + ";" + name;
        }
#if UNITY_EDITOR
        public override bool OnGUI()
        {
            bool retval = base.OnGUI();
            EditorGUILayout.BeginHorizontal();
            Value = EditorGUILayout.Vector4Field("Vector Value", Value);
            EditorGUILayout.EndHorizontal();
            return retval;
        }
#endif
    }

    [Serializable]
    public class UMAVectorArrayProperty : UMAProperty
    {
        public Vector4[] Value;

        public override void Apply(Material mpb, int overlayNumber=-1)
        {
            string propName = GetPropertyName(overlayNumber);
            if (mpb.HasProperty(propName))
            {
                mpb.SetVectorArray(propName, Value);
            }
        }
        public override UMAProperty Clone()
        {
            UMAVectorArrayProperty UVAP = new UMAVectorArrayProperty();
            UVAP.name = name;
            UVAP.Value = Value.Clone() as Vector4[];

            return UVAP; ;
        }
        public override string ToString()
        {
            return "VectorArray" + splitter + name;
        }
#if UNITY_EDITOR
        public override bool OnGUI()
        {
            bool retval = base.OnGUI();

            NoEdit();
            return retval;
        }
#endif
    }

    [Serializable]
    public class UMATextureProperty : UMAProperty
    {
        public Texture Value;

        public override void Apply(Material mpb, int overlayNumber=-1)
        {
            string propName = GetPropertyName(overlayNumber);
            if (mpb.HasProperty(propName))
            {
                mpb.SetTexture(propName, Value);
            }
        }
        public override UMAProperty Clone()
        {
            return new UMATextureProperty() { name = this.name, Value = this.Value };
        }

        public override string ToString()
        {
            return "Texture" + splitter + name;
        }
#if UNITY_EDITOR
        public override bool OnGUI()
        {
            bool retval = base.OnGUI();
            NoEdit();
            return retval;
        }
#endif
    }

    /*  public class UMARenderTextureProperty : UMAProperty
      {
          public RenderTexture Value;

          public override void Apply(MaterialPropertyBlock mpb)
          {
              mpb.SetTexture(Name, Value);
          }
          public override UMAProperty Clone()
          {
              return new UMARenderTextureProperty() { Name = this.Name, Value = this.Value };
          }
      } */

    [Serializable]
    public class UMAFloatArrayProperty : UMAProperty
    {
        public float[] Value;

        public override void Apply(Material mpb, int overlayNumber = -1)
        {
            string propName = GetPropertyName(overlayNumber);
            if (mpb.HasProperty(propName))
        {
                mpb.SetFloatArray(propName, Value);
            }
        }
        public override UMAProperty Clone()
        {
            UMAFloatArrayProperty UVAP = new UMAFloatArrayProperty();
            UVAP.name = name;
            UVAP.Value = Value.Clone() as float[];

            return UVAP; ;
        }
        public override string ToString()
        {
            return "FloatArray" + splitter + name;
        }
#if UNITY_EDITOR
        public override bool OnGUI()
        {
            bool retval = base.OnGUI();
            NoEdit();
            return retval;
        }
#endif
    }

    [Serializable]
    public class UMAIntProperty : UMAProperty
    {
        public int Value;

        public override void Apply(Material mpb, int overlayNumber = -1)
        {
            string propName = GetPropertyName(overlayNumber);
            if (mpb.HasProperty(propName))
            {
                mpb.SetInt(propName, Value);
            }
        }
        public override UMAProperty Clone()
        {
            return new UMAIntProperty() { name = this.name, Value = this.Value };
        }

        public override string ToString()
        {
            return "Int"+splitter+Value.ToString(CultureInfo.InvariantCulture) + splitter + name+"***";
        }
#if UNITY_EDITOR
        public override bool OnGUI()
        {
            bool retval = base.OnGUI();
            EditorGUILayout.BeginHorizontal();
            Value = EditorGUILayout.IntField("Integer Value", Value);
            EditorGUILayout.EndHorizontal();
            return retval;
        }
#endif
    }

    [Serializable]
    public class UMAMatrixProperty : UMAProperty
    {
        public Matrix4x4 Value;

        public override void Apply(Material mpb, int overlayNumber = -1)
        {
            string propName = GetPropertyName(overlayNumber);
            if (mpb.HasProperty(propName))
        {
                mpb.SetMatrix(propName, Value);
            }
        }
        public override UMAProperty Clone()
        {
            return new UMAMatrixProperty() { name = this.name, Value = this.Value };
        }
        public override string ToString()
        {
            return "Matrix" + splitter + name;
        }
#if UNITY_EDITOR
        public override bool OnGUI()
        {
            bool retval = base.OnGUI();
            NoEdit();
            return retval;
        }
#endif
    }

    [Serializable]
    public class UMAMatrixArrayProperty : UMAProperty
    {
        public Matrix4x4[] Value;

        public override void Apply(Material mpb, int overlayNumber = -1 )
        {
            string propName = GetPropertyName(overlayNumber);
            if (mpb.HasProperty(propName))
        {
                mpb.SetMatrixArray(propName, Value);
            }
        }
        public override UMAProperty Clone()
        {
            UMAMatrixArrayProperty UVAP = new UMAMatrixArrayProperty();
            UVAP.name = name;
            UVAP.Value = Value.Clone() as Matrix4x4[];

            return UVAP; ;
        }
        public override string ToString()
        {
            return "MatrixArray" + splitter + name;
        }
#if UNITY_EDITOR
        public override bool OnGUI()
        {
            bool retval = base.OnGUI();
            NoEdit();
            return retval;
        }
#endif
    }

    [Serializable]
    public class UMAComputeBufferProperty : UMAProperty
    {
        public ComputeBuffer Value;

        public override void Apply(Material mpb, int overlayNumber = -1 )
        {
            string propName = GetPropertyName(overlayNumber);
            if (mpb.HasProperty(propName))
            {
                mpb.SetBuffer(propName, Value);
            }
        }
        public override UMAProperty Clone()
        {
            return new UMAComputeBufferProperty() { name = this.name, Value = this.Value };
        }
        public override string ToString()
        {
            return "ComputeBuffer" + splitter + name;
        }
#if UNITY_EDITOR
        public override bool OnGUI()
        {
            bool retval = base.OnGUI();
            NoEdit();
            return retval;
        }
#endif

    }

    [Serializable]
    public class UMAConstantComputeBufferProperty : UMAProperty
    {
        public ComputeBuffer Value;
        public int offset; 
        public int size;

        public override void Apply(Material mpb, int overlayNumber=-1)
        {
            string propName = GetPropertyName(overlayNumber);
            if (mpb.HasProperty(propName))
        {
                mpb.SetConstantBuffer(propName, Value,offset,size);
            }
        }
        public override UMAProperty Clone()
        {
            return new UMAConstantComputeBufferProperty() { name = this.name, Value = this.Value , offset = this.offset, size = this.size};
        }
        public override string ToString()
        {
            return "ConstantComputeBuffer" + splitter + name;
        }
#if UNITY_EDITOR
        public override bool OnGUI()
        {
            bool retval = base.OnGUI();
            NoEdit();
            return retval;
        }
#endif

    }

    /// <summary>
    /// due to serialization, we need a holder class
    /// </summary>
    [Serializable] 
    public class PropertyHolder
    {
        public UMAOverlayTransformProperty p12;
        public UMAFloatProperty p11;
        public UMAColorProperty p10;
        public UMAVectorProperty p9;
        public UMAVectorArrayProperty p8;
        public UMATextureProperty p7;
        public UMAFloatArrayProperty p6;
        public UMAIntProperty p5;
        public UMAMatrixProperty p4;
        public UMAMatrixArrayProperty p3;
        public UMAComputeBufferProperty p2;
        public UMAConstantComputeBufferProperty p1;
        public string propertType;


        public PropertyHolder(UMAProperty prop)
        {
            property = prop;
        }

        private UMAProperty Get()
        {
            if (propertType == "UMAConstantComputeBufferProperty")
            {
                return p1;
            }

            if (propertType == "UMAComputeBufferProperty")
            {
                return p2;
            }

            if (propertType == "UMAMatrixArrayProperty")
            {
                return p3;
            }

            if (propertType == "UMAMatrixProperty")
            {
                return p4;
            }

            if (propertType == "UMAIntProperty")
            {
                return p5;
            }

            if (propertType == "UMAFloatArrayProperty")
            {
                return p6;
            }

            if (propertType == "UMATextureProperty")
            {
                return p7;
            }

            if (propertType == "UMAVectorArrayProperty")
            {
                return p8;
            }

            if (propertType == "UMAVectorProperty")
            {
                return p9;
            }

            if (propertType == "UMAColorProperty")
            {
                return p10;
            }

            if (propertType == "UMAFloatProperty")
            {
                return p11;
            }

            if (propertType == "UMAOverlayTransformProperty")
            {
                return p12;
            }
            Debug.Log("Unknown property type: " + propertType);
            return null;
        }
        public UMAProperty property
        {
            get
            {
                return Get();
            }
            set
            {
                propertType = value.GetType().Name;
                if (value is UMAOverlayTransformProperty)
                {
                    p12 = value as UMAOverlayTransformProperty;
                }
                if (value is UMAFloatProperty)
                {
                    p11 = value as UMAFloatProperty;
                }

                if (value is UMAColorProperty)
                {
                    p10 = value as UMAColorProperty;
                }

                if (value is UMAVectorProperty)
                {
                    p9 = value as UMAVectorProperty;
                }

                if (value is UMAVectorArrayProperty)
                {
                    p8 = value as UMAVectorArrayProperty;
                }

                if (value is UMATextureProperty)
                {
                    p7 = value as UMATextureProperty;
                }

                if (value is UMAFloatArrayProperty)
                {
                    p6 = value as UMAFloatArrayProperty;
                }

                if (value is UMAIntProperty)
                {
                    p5 = value as UMAIntProperty;
                }

                if (value is UMAMatrixProperty)
                {
                    p4 = value as UMAMatrixProperty;
                }

                if (value is UMAMatrixArrayProperty)
                {
                    p3 = value as UMAMatrixArrayProperty;
                }

                if (value is UMAComputeBufferProperty)
                {
                    p2 = value as UMAComputeBufferProperty;
                }

                if (value is UMAConstantComputeBufferProperty)
                {
                    p1 = value as UMAConstantComputeBufferProperty;
                }
            }
        }
    }

    [Serializable]
    public class UMAMaterialPropertyBlock :  ISerializationCallbackReceiver 
    {
        // If this is checked, the color will always update the 
        public bool alwaysUpdate;
        public bool alwaysUpdateParms;
        public static string[] PropertyTypeStrings = new string[0];
        public static List<Type> availableTypes = new List<Type>();
        public string[] GetPropertyStrings()
        {
            List<string> strings = new List<string>();
            foreach(UMAProperty p in shaderProperties)
            {
                if (p != null)
                {
                    strings.Add(p.ToString());
                }
            }
            return strings.ToArray();
        }

        public void SetPropertyStrings(string[] strings)
        {
            shaderProperties = new List<UMAProperty>();
            foreach (string s in strings)
            {
                UMAProperty p = UMAProperty.FromString(s);
                if (p != null)
                {
                    shaderProperties.Add(p);
                }
            }
        }

        /// <summary>
        /// Make sure the class is initialized
        /// </summary>
        public static void CheckInitialize()
        {
            if (PropertyTypeStrings.Length == 0)
            {
                availableTypes = UMAMaterialPropertyBlock.GetPropertyTypes();
                PropertyTypeStrings = availableTypes.Select(i => i.ToString()).ToArray();
            }
        }

        public UMAMaterialPropertyBlock()
        {
            CheckInitialize();
        }

        // Returns a list of types to load 
   /*     public static List<Type> GetPropertyTypes()
        {
            return AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes())
                 .Where(x => typeof(UMAProperty).IsAssignableFrom(x) && !x.IsInterface && !x.IsAbstract)
                 .Select(x => x).ToList();
        }*/
        public static List<Type> GetPropertyTypes()
        {
            List<Type> theTypes = new List<Type>();

            var Assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (int i = 0; i < Assemblies.Length; i++)
            {
                System.Reflection.Assembly asm = Assemblies[i];
                try
                {
                    var Types = asm.GetTypes();
                    for (int i1 = 0; i1 < Types.Length; i1++)
                    {
                        Type t = Types[i1];
                        if (typeof(UMAProperty).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                        {
                            theTypes.Add(t);
                        }
                    }
                }
                catch (Exception)
                {
                    // This apparently blows up on some assemblies. 
                }
            }

            return theTypes;
        }

        public void Validate()
        {
            if (shaderProperties == null)
            {
                shaderProperties = new List<UMAProperty>();
            }
        }

        public void AddProperty(UMAProperty property)
        {
            if (shaderProperties == null)
            {
                shaderProperties = new List<UMAProperty>();
            }

            shaderProperties.Add(property);
        }


        public UMAProperty AddProperty(Type propertyType, string propertyName)
        {
            UMAProperty prop = Activator.CreateInstance(propertyType) as UMAProperty;
            prop.name = propertyName;
            AddProperty(prop);
            return prop;
        }


        public UMAProperty AddProperty<t>(string propertyName)
        {
            UMAProperty prop = Activator.CreateInstance<t>() as UMAProperty;
            prop.name = propertyName;
            AddProperty(prop);
            return prop;
        }

        ///
        /// Throw all properties into holders
        public void OnBeforeSerialize()
        {
            if (shaderProperties != null)
            {
                serializedProperties = new List<PropertyHolder>();
                for (int i = 0; i < shaderProperties.Count; i++)
                {
                    UMAProperty up = shaderProperties[i];
                    if (up != null)
                    {
                        up.stringRepresentation = up.ToString();
                        PropertyHolder ph = new PropertyHolder(up);
                        serializedProperties.Add(ph);
                    }
                }
            }
            
        }

        /// <summary>
        ///  Reload properties from holder.
        /// </summary>
        public void OnAfterDeserialize()
        {
            if (serializedProperties != null)
            {
                shaderProperties = new List<UMAProperty>();
                for (int i = 0; i < serializedProperties.Count; i++)
                {
                    if (serializedProperties[i] == null)
                    {
                        Debug.Log("Skipping null property");
                        continue;
                    }
                    //Debug.Log("Adding property to shaderProperties " + serializedProperties[i].property.name);
                    PropertyHolder p = serializedProperties[i];
                    AddProperty(p.property);
                }
            }
        }

        public List<PropertyHolder> serializedProperties;
        public List<UMAProperty> shaderProperties = new List<UMAProperty>();
    }
}
