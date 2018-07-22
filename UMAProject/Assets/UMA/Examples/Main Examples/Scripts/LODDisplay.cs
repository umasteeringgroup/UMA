using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UMA;

namespace UMA.Examples
{
    public class LODDisplay : MonoBehaviour
    {
        public TextMesh lodDisplay;

        private int _lastSetLevel = -1;
        private Transform _cameraTransform;
        private UMASimpleLOD _simpleLOD;
        private MeshRenderer _renderer;

        public void OnEnable()
        {
            //cache the camera transform for performance
            _cameraTransform = Camera.main.transform;
            _simpleLOD = GetComponent<UMASimpleLOD>();

            if (lodDisplay != null)
            {
                _renderer = lodDisplay.GetComponent<MeshRenderer>();
                if (_renderer != null)
                    _renderer.material.SetColor("_EmissionColor", Color.grey);
                else
                    Debug.LogError("Could not find renderer!");
            }
        }

        // Update is called once per frame
        void Update()
        {
            if (_simpleLOD == null)
                return;

            if (lodDisplay != null)
            {
                if (_lastSetLevel != _simpleLOD.CurrentLOD)
                {
                    _lastSetLevel = _simpleLOD.CurrentLOD;
                    lodDisplay.text = string.Format("LOD #{0}", _lastSetLevel);
                    if (_renderer != null)
                        _renderer.material.SetColor("_EmissionColor", Color.grey);
                }
                var delta = transform.position - _cameraTransform.position;
                delta.y = 0;
                lodDisplay.transform.rotation = Quaternion.LookRotation(delta, Vector3.up);
            }
        }

        public void CharacterUpdated(UMAData data)
        {
            if (lodDisplay != null)
                _renderer.material.SetColor("_EmissionColor", Color.white);
        }
    }
}
