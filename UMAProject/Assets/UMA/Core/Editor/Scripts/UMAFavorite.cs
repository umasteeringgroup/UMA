using UnityEditor;
using UnityEngine;

namespace UMA
{
    [System.Serializable]
    public class UMAFavorite
    {
        public string path;
        public string GUID;
        public string name;
        public Object asset;
        public UMAFavoriteList favoriteList;
        public UMAFavorite(Object asset)
        {
            this.asset = asset;
            path = AssetDatabase.GetAssetPath(asset);
            GUID = AssetDatabase.AssetPathToGUID(path);
            name = asset.name;
        }
    }
}
