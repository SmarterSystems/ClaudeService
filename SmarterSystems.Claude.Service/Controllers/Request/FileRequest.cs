namespace SmarterSystems.Claude.Service.Controllers.Request
{
    public class FileRequest
    {
        public byte[] File { get; set; }
        public string MediaType { get; set; }
        public string FileName { get; set; }
    }
}
