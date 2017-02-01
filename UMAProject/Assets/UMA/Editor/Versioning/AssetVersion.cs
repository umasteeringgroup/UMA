using UnityEngine;
using System;
using System.Collections;
using System.Xml;

namespace kode80.Versioning
{
	public class AssetVersion
	{
		public string Name { get; private set; }
		public string Author { get; private set; }
		public SemanticVersion Version { get; private set; }
		public string Notes { get; private set; }
		public Uri packageURI { get; private set; }
		public Uri versionURI { get; private set; }

		public static AssetVersion ParseXML( string xmlString)
		{
			XmlDocument xml = new XmlDocument();

			try { xml.LoadXml( xmlString); }
			catch( XmlException) { return null; }

			XmlNode name = xml.SelectSingleNode( "asset/name");
			XmlNode author = xml.SelectSingleNode( "asset/author");
			XmlNode version = xml.SelectSingleNode( "asset/version");
			XmlNode notes = xml.SelectSingleNode( "asset/notes");
			XmlNode packageURI = xml.SelectSingleNode( "asset/package-uri");
			XmlNode versionURI = xml.SelectSingleNode( "asset/version-uri");

			if( name == null || 
				author == null || 
				version == null || 
				notes == null || 
				packageURI == null || 
				versionURI == null) 
			{
				Debug.Log( "Error parsing Asset Version XML");
				return null;
			}

			SemanticVersion semanticVersion = SemanticVersion.Parse( version.InnerText);
			if( semanticVersion == null) {
				Debug.Log( "Error parsing Semantic Version");
				return null;
			}

			AssetVersion assetVersion = new AssetVersion();
			assetVersion.Name = name.InnerText;
			assetVersion.Author = author.InnerText;
			assetVersion.Version = semanticVersion;
			assetVersion.Notes = notes.InnerText;
			assetVersion.packageURI = new Uri( packageURI.InnerText);
			assetVersion.versionURI = new Uri( versionURI.InnerText);

			return assetVersion;
		}

		public override string ToString()
		{
			return "Name: " + Name + "\n" +
				"Author: " + Author + "\n" +
				"Version: " + Version + "\n" +
				"Notes: " + Notes + "\n" +
				"PackageURI: " + packageURI + "\n" +
				"VersionURI: " + versionURI;
		}
	}
}
