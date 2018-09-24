using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UMA;
using UMA.CharacterSystem;

namespace UMA.Examples
{
    [RequireComponent(typeof(UMASimpleLOD))]
    public class LODDisplay : MonoBehaviour
    {
        public GameObject LODDisplayPrefab;
        private TextMesh _lodDisplay;

        private int _lastSetLevel = -1;
        private Transform _cameraTransform;
        private UMASimpleLOD _simpleLOD;

        public void Start()
        {
            //cache the camera transform for performance
            _cameraTransform = Camera.main.transform;
            _simpleLOD = GetComponent<UMASimpleLOD>();

            // Add the display prefab
            if (LODDisplayPrefab != null)
            {
                GameObject tm = (GameObject)GameObject.Instantiate(LODDisplayPrefab, transform.position, transform.rotation);
                tm.transform.SetParent(transform);
                tm.transform.localPosition = new Vector3(0, 2f, 0f);
                tm.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                _lodDisplay = tm.GetComponent<TextMesh>();
            }
            else
            {
                if (Debug.isDebugBuild)
                    Debug.LogWarning("No LOD Display prefab set on " + gameObject.name);
            }
        }

        // Update is called once per frame
        void Update()
        {
            if (_simpleLOD != null && _lodDisplay != null)
            {
                if (_lastSetLevel != _simpleLOD.CurrentLOD)
                {
                    _lastSetLevel = _simpleLOD.CurrentLOD;
                    if (_lastSetLevel < 0)
                    {
                        _lodDisplay.text = string.Format("LOD #0/{0}", _lastSetLevel);
                    }
                    else
                    {
                        _lodDisplay.text = string.Format("LOD #{0}", _lastSetLevel);
                    }
                }
                var delta = transform.position - _cameraTransform.position;
                delta.y = 0;
                _lodDisplay.transform.rotation = Quaternion.LookRotation(delta, Vector3.up);
            }
        }
    }
}
