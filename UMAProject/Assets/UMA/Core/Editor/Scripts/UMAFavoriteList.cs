using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    public class UMAFavoriteList : ScriptableObject
    {
        public List<UMAFavorite> Favorites = new List<UMAFavorite>();
        public bool exPanded = true;
        public void AddAsset(Object asset)
        {
            if (Favorites == null)
            {
                Favorites = new List<UMAFavorite>();
            }
            UMAFavorite favorite = new UMAFavorite(asset);
            favorite.favoriteList = this;
            Favorites.Add(favorite);
        }
        public void RemoveAsset(Object asset)
        {
            Favorites.Remove(new UMAFavorite(asset));
        }
        public void RemoveAsset(UMAFavorite asset)
        {
            Favorites.Remove(asset);
        }
    }
}
