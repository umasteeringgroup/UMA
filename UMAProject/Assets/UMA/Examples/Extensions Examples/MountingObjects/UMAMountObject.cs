using UnityEngine;
using System.Collections;
using UMA;

public class UMAMountObject : MonoBehaviour 
{
    public GameObject objPrefab;
    public string boneName;

    public Vector3 position = Vector3.zero;
    public Vector3 rotation = Vector3.zero;
    public Vector3 scale = Vector3.one;

    private UMAData _umaData;

    public void MountObject()
    {
        if( _umaData == null )
            _umaData = gameObject.GetComponent<UMAData>();

        if (_umaData == null)
            return;

        if (boneName == null)
            return;

        GameObject boneObj = _umaData.GetBoneGameObject(boneName);

        if (boneObj == null)
            return;

        Transform objTransform = boneObj.transform.FindChild(objPrefab.name);
        if (objTransform == null)
        {
            GameObject newObj = GameObject.Instantiate(objPrefab);
            newObj.name = objPrefab.name;
            newObj.transform.SetParent(boneObj.transform, false);
            newObj.transform.localPosition = position;
            newObj.transform.localRotation = Quaternion.Euler(rotation);
            newObj.transform.localScale = scale;
        }
        else
        {
            objTransform.gameObject.SetActive(true);
        }
    }

    public void UnMountObject()
    {
        if( _umaData == null )
            _umaData = gameObject.GetComponent<UMAData>();

        if (_umaData == null)
            return;

        if (boneName == null)
            return;

        GameObject boneObj = _umaData.GetBoneGameObject(boneName);

        if (boneObj == null)
            return;

        Transform objTransform = boneObj.transform.FindChild(objPrefab.name);
        if (objTransform != null)
        {
            objTransform.gameObject.SetActive(false);
        }
    }
}
