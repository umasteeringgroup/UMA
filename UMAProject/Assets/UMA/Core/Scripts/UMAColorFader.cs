using UnityEngine;

namespace UMA.CharacterSystem
{
    public class UMAColorFader : MonoBehaviour
    {
        public enum FadeType
        {
            FadeIn,
            FadeOut
        };

        public DynamicCharacterAvatar DCA;
        private OverlayColorData Color = new OverlayColorData(3);

        public FadeType Fade = FadeType.FadeOut;
        public string ColorName = "Fade";
        public float time = 3.0f;

        // Start is called before the first frame update
        void Start()
        {
            DCA = GetComponent<DynamicCharacterAvatar>();
            if (DCA != null)
            {
                Color = DCA.GetColor(ColorName);
                if (Color == null)
                {
                    Color = new OverlayColorData(3);
                }

                DCA.SetColor(ColorName, Color, false);
            }
        }

        // Update is called once per frame
        void Update()
        {
            if (DCA == null)
            {
                return;
            }

            bool done = false;
            float FadeVal = Time.deltaTime / time;

            if (Fade == FadeType.FadeIn)
            {
                Color.channelMask[0].a += FadeVal;
                if (Color.channelMask[0].a >= 1.0f)
                {
                    done = true;
                }
                DCA.SetColor(ColorName,Color, true);
            }
            else
            {
                Color.channelMask[0].a -= FadeVal;
                if (Color.channelMask[0].a <= 0.0f)
                {
                    done = true;
                }
                DCA.SetColor(ColorName, Color, true);
            }
            if (done)
            {
                Destroy(this);
            }
        }
    }
}
