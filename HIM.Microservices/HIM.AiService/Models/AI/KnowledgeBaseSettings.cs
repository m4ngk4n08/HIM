namespace HIM.AiService.Models.AI
{
    public class KnowledgeBaseSettings
    {
        public string FilePath { get; set; } = string.Empty;
        public string MemoryCollectionName { get; set; } = string.Empty;

        public string CacheFile { get; set; } = string.Empty;
    }
}
