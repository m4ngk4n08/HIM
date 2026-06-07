namespace HIM.AiService.Services.AI.Interface
{
    public interface IVectorSearchService
    {
        double CalculateCosineSimilarity(float[] vector1, float[] vector2);
    }
}
