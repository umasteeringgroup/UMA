using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace UMA
{
	public class PiecesAvatar : UMAAvatarBase
	{
		public UMAPiece[] pieces;
	
		public UMALocationEntry[] locations;
		public RaceData race;
		
		[Serializable]
		public class UMALocationEntry
		{
			public UMALocation location;
			public enum Status
			{
				Undefined,
				Set,
				Suppressed
			}
			public Status status;
			public UMAPiece piece;
		}
		
		private UMALocation[] locationLookup;
		private int GetLocationIndex(UMALocation location)
		{
			return Array.IndexOf(locationLookup, location);
		}
		
		void PopulateLocationPieceStuff()
		{
			locations = new UMALocationEntry[pieces.Length];
			locationLookup = new UMALocation[pieces.Length];
			for (int i = 0; i < pieces.Length; i++) 
			{
				var piece = pieces[i];
				locationLookup[i] = piece.Location;
				locations[i] = new UMALocationEntry();
				locations[i].location = piece.Location;
				locations[i].status = UMALocationEntry.Status.Set;
				locations[i].piece = piece;
			}
		}
		
		public override void Start()
		{
			base.Start();

			if (animationController != null)
				umaData.animationController = animationController;

			PopulateLocationPieceStuff();

			foreach(var location in locations)
			{
				foreach( var block in location.piece.Blocks)
				{
					if (block.Condition.ConditionMet())
					{
						foreach(var suppress in block.SuppressLocations)
						{
							locations[GetLocationIndex(suppress)].status = UMALocationEntry.Status.Suppressed;
						}
					}
				}
			}

			var slots = new List<SlotData>();
			var namedMaterials = new List<UMANamedMaterial>();
			var namedMaterialSlot = new List<SlotData>();

			foreach(var location in locations)
			{
				if (location.status != UMALocationEntry.Status.Set)
					continue;
					
				foreach( var block in location.piece.Blocks)
				{
					if (block.Condition.ConditionMet())
					{
						foreach(var slot in block.Slots)
						{
							if ( slot.Operation != UMAPieceSlot.SlotOperation.Add)
								continue;
							var slotData = new SlotData(slot.Slot);
							slots.Add(slotData);
							var index = namedMaterials.IndexOf(slot.Slot.namedMaterial);
							if (index == -1)
							{
								namedMaterials.Add(slot.Slot.namedMaterial);
								namedMaterialSlot.Add(slotData);
							}
							else
							{
								slotData.SetOverlayList(namedMaterialSlot[index].GetOverlayList());
							}
						}
					}
				}
				foreach( var block in location.piece.Blocks)
				{
					if (block.Condition.ConditionMet())
					{
						foreach(var overlay in block.Overlays)
						{
							if ( overlay.Operation != UMAPieceOverlay.OverlayOperation.Add)
								continue;
							var overlayData = new OverlayData(overlay.Overlay);
							foreach(var property in overlay.MappedProperties)
							{
								if (property.Dest.propertyName == "Color")
								{
									overlayData.SetColor(0, (property.Source.data.GetValue() as ColorProperty).color);
									break;
								}
							}
							var index = namedMaterials.IndexOf(overlay.Overlay.namedMaterial);
							if (index != -1)
							{
								namedMaterialSlot[index].AddOverlay(overlayData);
							}
						}
					}
				}
				
				
				foreach( var block in location.piece.Blocks)
				{
					if (block.Condition.ConditionMet())
					{
						foreach(var slot in block.Slots)
						{
							if ( slot.Operation != UMAPieceSlot.SlotOperation.Remove)
								continue;
							
							for(int i = slots.Count-1; i >= 0; i--)
							{
								if (slots[i].asset == slot.Slot)
								{
									slots[i] = null;
								}
							}
						}

						foreach(var overlay in block.Overlays)
						{
							if ( overlay.Operation != UMAPieceOverlay.OverlayOperation.Remove)
								continue;
								
							var index = namedMaterials.IndexOf(overlay.Overlay.namedMaterial);
							if (index != -1)
							{
								var overlays = namedMaterialSlot[index].GetOverlayList();

								for(int i = overlays.Count-1; i >= 0; i--)
								{
									if (overlays[i].asset == overlay.Overlay)
									{
										overlays[i] = null;
									}
								}
							}
						}
					}
				}
			}
			umaData.umaRecipe.raceData = race;
			umaData.SetSlots(slots.ToArray());
			umaData.Dirty(true, true, true);
		}
	}
}