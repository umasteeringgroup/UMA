using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class HiddenSurfaceRemoval : MonoBehaviour {
	
	public SkinnedMeshRenderer skinnedMeshRenderer;
	public int angleSteps;
	public float maxAngle;
	public float range;
	public float rangeInnerMesh;
	public Transform rayCastBase;
	public Transform rayCastAxis;
	
	public bool[] isVertexOccluded;
	
	void Start () {
		
		Transform tempRayCastBase = Instantiate(rayCastBase,Vector3.zero,Quaternion.identity) as Transform;
		Transform tempRayCastAxis = Instantiate(rayCastAxis,Vector3.zero,Quaternion.identity) as Transform;
		tempRayCastAxis.parent = tempRayCastBase;
		int meshSize = skinnedMeshRenderer.sharedMesh.vertexCount;
		Vector3[] vertices = skinnedMeshRenderer.sharedMesh.vertices;
		Vector3[] normals = skinnedMeshRenderer.sharedMesh.normals;
		isVertexOccluded = new bool[vertices.Length];
		
		for(int vertexID = 0; vertexID < meshSize; vertexID++){
			
			tempRayCastBase.position = skinnedMeshRenderer.transform.position + skinnedMeshRenderer.transform.TransformPoint(vertices[vertexID]);
			tempRayCastBase.LookAt(tempRayCastBase.position + skinnedMeshRenderer.transform.TransformPoint(normals[vertexID]));
			
			bool occluded = true;
			RaycastHit hit;
				
			Debug.DrawLine(tempRayCastBase.position,tempRayCastBase.position + skinnedMeshRenderer.transform.TransformPoint(normals[vertexID])*0.01f);
				
			for(int X = 0; X <= angleSteps; X ++){
				for(int Y = 0; Y <= angleSteps; Y ++){	
					tempRayCastAxis.localEulerAngles = new Vector3(((maxAngle/angleSteps)*X) - maxAngle/2,((maxAngle/angleSteps)*Y) - maxAngle/2,tempRayCastAxis.localEulerAngles.z);
					
					if (Physics.Raycast(tempRayCastAxis.position + tempRayCastAxis.TransformDirection(Vector3.forward)*range,tempRayCastBase.position - (tempRayCastAxis.position + tempRayCastAxis.TransformDirection(Vector3.forward)*range),out hit,range + rangeInnerMesh)){
						Debug.DrawLine(tempRayCastAxis.position + tempRayCastAxis.TransformDirection(Vector3.forward)*range,hit.point,Color.red);
					}else{
						occluded = false;
						X = angleSteps;
						Y = angleSteps;
						Debug.DrawLine(tempRayCastAxis.position,tempRayCastAxis.position + tempRayCastAxis.TransformDirection(Vector3.forward)*range,Color.green);
					}
					tempRayCastBase.Rotate(Vector3.forward * angleSteps);
				}
			}
			isVertexOccluded[vertexID] = occluded;

		}
		
		int[] originalTriangleList = skinnedMeshRenderer.sharedMesh.triangles;
		List<int> newTriangleList = new List<int>();
	    
		
		for(var triangleIndex = 0; triangleIndex < originalTriangleList.Length; triangleIndex = triangleIndex + 3){
			if( (isVertexOccluded[originalTriangleList[triangleIndex]] == false) || (isVertexOccluded[originalTriangleList[triangleIndex + 1]] == false) || (isVertexOccluded[originalTriangleList[triangleIndex + 2]] == false)){
				newTriangleList.Add(originalTriangleList[triangleIndex]);
				newTriangleList.Add(originalTriangleList[triangleIndex + 1]);
				newTriangleList.Add(originalTriangleList[triangleIndex + 2]);
			}
		}
		
		skinnedMeshRenderer.sharedMesh.triangles = newTriangleList.ToArray();
		Debug.Log(originalTriangleList.Length);
		Debug.Log(newTriangleList.Count);
		Debug.Break();
	}
}
