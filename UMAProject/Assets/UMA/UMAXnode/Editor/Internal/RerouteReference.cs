using UnityEngine;

namespace UMA.XNodeEditor.Internal {
	public struct RerouteReference {
		public UMA.XNode.NodePort port;
		public int connectionIndex;
		public int pointIndex;

		public RerouteReference(UMA.XNode.NodePort port, int connectionIndex, int pointIndex) {
			this.port = port;
			this.connectionIndex = connectionIndex;
			this.pointIndex = pointIndex;
		}

		public void InsertPoint(Vector2 pos) { port.GetReroutePoints(connectionIndex).Insert(pointIndex, pos); }
		public void SetPoint(Vector2 pos) { port.GetReroutePoints(connectionIndex) [pointIndex] = pos; }
		public void RemovePoint() { port.GetReroutePoints(connectionIndex).RemoveAt(pointIndex); }
		public Vector2 GetPoint() { return port.GetReroutePoints(connectionIndex) [pointIndex]; }
	}
}