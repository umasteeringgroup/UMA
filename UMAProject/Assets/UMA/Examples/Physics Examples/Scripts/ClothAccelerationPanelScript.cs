using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UMA.CharacterSystem;

// @cond doxygen ignore
namespace UMA.Examples
{
	public class ClothAccelerationPanelScript : MonoBehaviour 
	{
	    public DynamicCharacterAvatar avatar;
	    public Slider xSlider;
	    public Slider ySlider;
	    public Slider zSlider;

	    private Cloth m_Cloth;
	    private Vector3 acceleration;

	    public void UpdateClothAcceleration()
	    {
	        if (avatar == null)
	            return;

	        if (xSlider == null || ySlider == null || zSlider == null)
	            return;

	        if (m_Cloth == null)
	        {
	            m_Cloth = avatar.GetComponentInChildren<Cloth>();
	            if (m_Cloth == null)
	                return;
	        }

	        acceleration.x = xSlider.value;
	        acceleration.y = ySlider.value;
	        acceleration.z = zSlider.value;

	        m_Cloth.externalAcceleration = acceleration;
	    }
	}
}
// @endcond