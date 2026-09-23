using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// Stores the user-defined order and favorite state of UMA documentation assets.
    /// Document GUIDs are used so renamed and moved documents retain their settings.
    /// </summary>
    [CreateAssetMenu(menuName = "UMA/Documentation Configuration", fileName = "UMADocumentConfiguration")]
    public class UMADocumentConfiguration : ScriptableObject
    {
        [Serializable]
        public class DocumentEntry
        {
            public string documentGuid;
            public bool favorite;
            public int order;
        }

        [SerializeField]
        private List<DocumentEntry> documents = new List<DocumentEntry>();

        public IReadOnlyList<DocumentEntry> Documents => documents;

        public DocumentEntry Find(string documentGuid)
        {
            if (string.IsNullOrEmpty(documentGuid))
                return null;

            for (int index = 0; index < documents.Count; index++)
            {
                DocumentEntry entry = documents[index];
                if (entry != null && string.Equals(entry.documentGuid, documentGuid, StringComparison.Ordinal))
                    return entry;
            }

            return null;
        }

        public DocumentEntry GetOrAdd(string documentGuid, int order)
        {
            DocumentEntry entry = Find(documentGuid);
            if (entry != null)
                return entry;

            entry = new DocumentEntry
            {
                documentGuid = documentGuid,
                order = order
            };
            documents.Add(entry);
            return entry;
        }
    }
}
