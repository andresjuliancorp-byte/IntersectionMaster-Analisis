using UnityEngine;

namespace IntersectionMaster.Core
{
    /// <summary>Columna de humo decorativa: unas bocanadas grises que suben, crecen y se desvanecen en bucle.</summary>
    public class SmokeFx : MonoBehaviour
    {
        const int Puffs = 6;
        const float Life = 3.2f;

        SpriteRenderer[] puffs;
        float[] phase;

        void Awake()
        {
            puffs = new SpriteRenderer[Puffs];
            phase = new float[Puffs];
            for (int i = 0; i < Puffs; i++)
            {
                puffs[i] = SpriteFactory.Make("Puff", transform, SpriteFactory.Circle, new Color(0.8f, 0.8f, 0.82f, 0f), Vector2.zero, Vector2.one * 0.2f, 46);
                phase[i] = i / (float)Puffs;
            }
        }

        void Update()
        {
            for (int i = 0; i < Puffs; i++)
            {
                phase[i] += Time.deltaTime / Life;
                if (phase[i] >= 1f) phase[i] -= 1f;
                float t = phase[i];
                var tr = puffs[i].transform;
                float sway = Mathf.Sin((t * 5f) + i * 1.7f) * 0.12f * t;
                tr.localPosition = new Vector3(sway + 0.25f * t, 0.9f * t, 0f);
                float size = Mathf.Lerp(0.16f, 0.75f, t);
                tr.localScale = new Vector3(size, size, 1f);
                var c = puffs[i].color;
                c.a = Mathf.Sin(t * Mathf.PI) * 0.55f;
                puffs[i].color = c;
            }
        }
    }
}
