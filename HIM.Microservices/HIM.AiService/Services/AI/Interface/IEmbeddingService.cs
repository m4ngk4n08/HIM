namespace HIM.AiService.Services.AI.Interface
{
    public interface IEmbeddingService
    {
        Task<float[]> GetEmbeddingAsync(string text);
    }
}
