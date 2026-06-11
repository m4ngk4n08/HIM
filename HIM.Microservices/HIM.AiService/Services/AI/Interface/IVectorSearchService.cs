namespace HIM.AiService.Services.AI.Interface
{
    public interface IVectorSearchService
    {
        float CalculateCosineSimilarity(ReadOnlySpan<float> vector1, ReadOnlySpan<float> vector2);
    }
}
