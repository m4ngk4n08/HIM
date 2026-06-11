using HIM.AiService.Services.AI.Interface;
using System.Numerics;

namespace HIM.AiService.Services.AI
{
    public class VectorSearchService : IVectorSearchService
    {
        public float CalculateCosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        {
            if (a.Length != b.Length || a.Length == 0) return 0;

            float result = 0;
            int i = 0;
            int vCount = Vector<float>.Count;

            if(Vector.IsHardwareAccelerated && a.Length >= vCount)
            {
                for(; i<= a.Length - vCount; i += vCount)
                {
                    var va = new Vector<float>(a.Slice(i));
                    var vb = new Vector<float>(b.Slice(i));

                    result += Vector.Dot(va, vb);
                }
            }

            for(; i < a.Length; i++)
            {
                result += a[i] * b[i];
            }

            return result;

        }
    }
}
